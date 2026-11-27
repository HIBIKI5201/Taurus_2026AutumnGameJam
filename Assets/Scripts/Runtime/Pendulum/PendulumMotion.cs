using System;
using UnityEngine;

namespace Taurus.Runtime.Pendulum
{
    /// <summary>
    /// 経過時刻に応じて振り子の 0〜8 秒 SmoothStep 揺動と 8〜10 秒物理減衰を評価し、Transform へ反映する。
    /// PendulumSingle / PendulumDouble の Prefab ルートにアタッチする。
    ///
    /// 【角度・座標規約】
    /// - θ = 0 が真下（-Y方向）、θ = ±π が真上（+Y方向）、高さ h = -cos(θ)。
    /// - 親棒・子棒ともに「ピボットが上端（支点）、棒と玉が -Y 方向へ伸びる」構造を前提とする。
    /// - 二重振り子の子玉は「ワールド角 θchildWorld」で真上判定を行い、描画用の子棒のみローカル角 (θchildWorld - θparentWorld) を適用する。
    /// </summary>
    public class PendulumMotion : MonoBehaviour
    {
        // 0〜8秒は決定論的軌道、8〜10秒は見た目用の減衰揺動（カウント対象外）
        private const double PLANNED_DURATION = 8.0;

        // Wave開始直後の理不尽回避：最初の1秒間は初期姿勢のまま回転させない
        private const double START_FREEZE_DURATION = 1.0;

        // 減衰運動の数値積分が不安定にならないよう1フレームのΔtとサブステップ幅を制限する
        private const double MAX_FRAME_DT = 0.05;
        private const double MAX_SUBSTEP_DT = 1.0 / 120.0;
        private const double GRAVITY = 9.81;
        private const double DAMPING = 1.35;

        // 親棒のピボットTransform（Prefab ルートの Z 軸回りに回転させる対象。FBX のボーンも可）
        [SerializeField] private Transform _parentArm;

        // 二重振り子時の子棒ピボットTransform（親棒先端の子として配置。単振り子ではnull）
        [SerializeField] private Transform _childArm;

        // 三重振り子時の孫棒ピボットTransform（子棒先端の子として配置。単・二重ではnull）
        [SerializeField] private Transform _grandchildArm;

        // 二重振り子として扱うかどうかのフラグ
        [SerializeField] private bool _isDoublePendulum;

        // 三重振り子として扱うかどうかのフラグ
        [SerializeField] private bool _isTriplePendulum;

        // 描画に使う角度と全く同じ値を渡してフレームズレを防ぐための検出器
        private readonly TopCrossingDetector _parentDetector = new TopCrossingDetector();
        private readonly TopCrossingDetector _childDetector = new TopCrossingDetector();
        private readonly TopCrossingDetector _grandchildDetector = new TopCrossingDetector();

        private int _pendulumIndex;
        private MotionSchedule _parentSchedule;
        private MotionSchedule _childSchedule;
        private MotionSchedule _grandchildSchedule;

        private double _parentAngle;
        private double _childWorldAngle;
        private double _childLocalAngle;
        private double _grandchildWorldAngle;
        private double _grandchildLocalAngle;
        private int _currentSegmentIndex;

        // 8〜10秒減衰運動用の状態変数
        private bool _isTailStarted;
        private double _tailParentAngle;
        private double _tailParentVelocity;
        private double _tailChildAngle;
        private double _tailChildVelocity;
        private double _tailGrandchildAngle;
        private double _tailGrandchildVelocity;

        /// <summary> 真上を通過した際に発行される（振り子本体、子玉・孫玉ならtrue、通過回数）。従来互換イベント。 </summary>
        public event Action<PendulumMotion, bool, int> OnTopCrossed;

        /// <summary> 真上を通過した際に階層付きで発行される（振り子本体、階層 0=親/1=子/2=孫、通過回数）。 </summary>
        public event Action<PendulumMotion, int, int> OnBobTopCrossed;

        /// <summary> 振り子番号（0始まり）。 </summary>
        public int PendulumIndex => _pendulumIndex;

        /// <summary> 二重振り子として稼働しているかどうか（三重振り子も子玉を持つためtrue）。 </summary>
        public bool IsDoublePendulum => (_isDoublePendulum || _isTriplePendulum) && _childArm != null && _childSchedule != null;

        /// <summary> 三重振り子として稼働しているかどうか。 </summary>
        /// <summary> プレハブ側に孫の腕（3段目）が用意されているか。 </summary>
        public bool HasGrandchildArm => _grandchildArm != null;

        public bool IsTriplePendulum => _isTriplePendulum && _grandchildArm != null && _grandchildSchedule != null;

        /// <summary> 親玉のスケジュール。 </summary>
        public MotionSchedule ParentSchedule => _parentSchedule;

        /// <summary> 子玉のスケジュール（単振り子ならnull）。 </summary>
        public MotionSchedule ChildSchedule => _childSchedule;

        /// <summary> 孫玉のスケジュール（単・二重振り子ならnull）。 </summary>
        public MotionSchedule GrandchildSchedule => _grandchildSchedule;

        /// <summary> 親玉の現在連続角度（ラジアン）。 </summary>
        public double ParentAngleRad => _parentAngle;

        /// <summary> 子玉の現在ワールド連続角度（ラジアン）。 </summary>
        public double ChildWorldAngleRad => _childWorldAngle;

        /// <summary> 孫玉の現在ワールド連続角度（ラジアン）。 </summary>
        public double GrandchildWorldAngleRad => _grandchildWorldAngle;

        /// <summary> 親玉の現在の高さ h = -cos(θ)（+1=真上, -1=真下）。 </summary>
        public double ParentHeight => -Math.Cos(_parentAngle);

        /// <summary> 子玉の現在の高さ h = -cos(θ)（+1=真上, -1=真下）。 </summary>
        public double ChildHeight => -Math.Cos(_childWorldAngle);

        /// <summary> 孫玉の現在の高さ h = -cos(θ)（+1=真上, -1=真下）。 </summary>
        public double GrandchildHeight => -Math.Cos(_grandchildWorldAngle);

        /// <summary> 親玉の実測真上通過回数（デバッグ検証用）。 </summary>
        public int ParentObservedCount => _parentDetector.ObservedCount;

        /// <summary> 子玉の実測真上通過回数（デバッグ検証用）。 </summary>
        public int ChildObservedCount => _childDetector.ObservedCount;

        /// <summary> 孫玉の実測真上通過回数（デバッグ検証用）。 </summary>
        public int GrandchildObservedCount => _grandchildDetector.ObservedCount;

        /// <summary> 親玉の直近真上通過時刻（unscaledTime）。 </summary>
        public float ParentLastTopTime => _parentDetector.LastTopTime;

        /// <summary> 子玉の直近真上通過時刻（unscaledTime）。 </summary>
        public float ChildLastTopTime => _childDetector.LastTopTime;

        /// <summary> 孫玉の直近真上通過時刻（unscaledTime）。 </summary>
        public float GrandchildLastTopTime => _grandchildDetector.LastTopTime;

        /// <summary> 現在の区間インデックス（0〜7、8秒以降の減衰区間では8）。 </summary>
        public int CurrentSegmentIndex => _currentSegmentIndex;

        /// <summary> この振り子の予定通過数合計（親＋子＋孫）。GameController が正解値算出に使う。 </summary>
        public int TotalExpectedTopCount =>
            (_parentSchedule != null ? _parentSchedule.TotalExpectedTopCount : 0) +
            (_childSchedule != null ? _childSchedule.TotalExpectedTopCount : 0) +
            (_grandchildSchedule != null ? _grandchildSchedule.TotalExpectedTopCount : 0);

        // アームの初期姿勢（ローカル回転）と、親空間で見た揺動軸。
        // FBX のボーンは初期姿勢に回転が入っているため、角度は「初期姿勢に揺動軸回りの回転を足す」形で反映する。
        private bool _hasRestPose;
        private Quaternion _parentRestLocalRotation = Quaternion.identity;
        private Quaternion _childRestLocalRotation = Quaternion.identity;
        private Quaternion _grandchildRestLocalRotation = Quaternion.identity;
        private Vector3 _parentSwingAxisLocal = Vector3.forward;
        private Vector3 _childSwingAxisLocal = Vector3.forward;
        private Vector3 _grandchildSwingAxisLocal = Vector3.forward;

        private void Awake()
        {
            _parentDetector.OnTopCrossed += HandleParentTopCrossed;
            _childDetector.OnTopCrossed += HandleChildTopCrossed;
            _grandchildDetector.OnTopCrossed += HandleGrandchildTopCrossed;
            CaptureRestPose();
        }

        private void OnDestroy()
        {
            _parentDetector.OnTopCrossed -= HandleParentTopCrossed;
            _childDetector.OnTopCrossed -= HandleChildTopCrossed;
            _grandchildDetector.OnTopCrossed -= HandleGrandchildTopCrossed;
        }

        /// <summary> エディタ構築ツール（ProjectSceneSetupTool）からアーム参照を設定する。 </summary>
        public void ConfigureArms(Transform parentArm, Transform childArm, bool isDouble, Transform grandchildArm = null, bool isTriple = false)
        {
            _parentArm = parentArm;
            _childArm = childArm;
            _isDoublePendulum = isDouble || isTriple;
            _grandchildArm = grandchildArm;
            _isTriplePendulum = isTriple;
            CaptureRestPose();
        }

        /// <summary>
        /// 専用三重振り子Prefabが未設定のとき、既存の二重振り子Prefab（_childArm）の先端に
        /// 3段目アームと孫玉（MeshRenderer付き）をランタイムで自動生成する。
        /// </summary>
        public bool EnsureGrandchildRig(out Transform grandchildBob, out Renderer grandchildMeshRenderer)
        {
            grandchildBob = null;
            grandchildMeshRenderer = null;

            if (_grandchildArm != null)
            {
                grandchildMeshRenderer = _grandchildArm.GetComponentInChildren<Renderer>();
                grandchildBob = grandchildMeshRenderer != null ? grandchildMeshRenderer.transform : _grandchildArm;
                return true;
            }

            if (_childArm == null)
            {
                return false;
            }

            // 子棒の先端（最も遠い子メッシュ位置、なければ -Y 方向 1.2m）を孫ピボット位置にする
            Vector3 pivotLocalPos = new Vector3(0f, -1.2f, 0f);
            Renderer childRenderer = _childArm.GetComponentInChildren<Renderer>();
            if (childRenderer != null && childRenderer.transform != _childArm)
            {
                pivotLocalPos = childRenderer.transform.localPosition;
            }

            GameObject armObj = new GameObject("GrandchildArm");
            armObj.transform.SetParent(_childArm, false);
            armObj.transform.localPosition = pivotLocalPos;
            armObj.transform.localRotation = Quaternion.identity;
            armObj.transform.localScale = Vector3.one;

            // 孫棒（細い円柱）
            GameObject rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rod.name = "GrandchildRod";
            rod.transform.SetParent(armObj.transform, false);
            rod.transform.localPosition = new Vector3(0f, -0.45f, 0f);
            rod.transform.localScale = new Vector3(0.06f, 0.45f, 0.06f);
            Collider rodCol = rod.GetComponent<Collider>();
            if (rodCol != null) Destroy(rodCol);

            // 孫玉（球）
            GameObject bob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bob.name = "GrandchildBob";
            bob.transform.SetParent(armObj.transform, false);
            bob.transform.localPosition = new Vector3(0f, -0.9f, 0f);
            bob.transform.localScale = Vector3.one * 0.34f;
            Collider bobCol = bob.GetComponent<Collider>();
            if (bobCol != null) Destroy(bobCol);

            Renderer bobRenderer = bob.GetComponent<Renderer>();
            if (bobRenderer != null && childRenderer != null && childRenderer.sharedMaterial != null)
            {
                bobRenderer.sharedMaterial = childRenderer.sharedMaterial;
            }

            _grandchildArm = armObj.transform;
            _isTriplePendulum = true;
            CaptureRestPose();

            grandchildBob = bob.transform;
            grandchildMeshRenderer = bobRenderer;
            return true;
        }

        /// <summary>
        ///     現在のアーム姿勢を「θ = 0（真下）」の初期姿勢として記録する。
        ///     揺動軸は Prefab ルートの Z 軸（画面奥行き方向）で、各アームの親空間に直して保持する。
        /// </summary>
        private void CaptureRestPose()
        {
            Vector3 swingAxisWorld = transform.forward;
            if (_parentArm != null)
            {
                _parentRestLocalRotation = _parentArm.localRotation;
                _parentSwingAxisLocal = ToParentSpace(_parentArm, swingAxisWorld);
            }

            if (_childArm != null)
            {
                _childRestLocalRotation = _childArm.localRotation;
                _childSwingAxisLocal = ToParentSpace(_childArm, swingAxisWorld);
            }

            if (_grandchildArm != null)
            {
                _grandchildRestLocalRotation = _grandchildArm.localRotation;
                _grandchildSwingAxisLocal = ToParentSpace(_grandchildArm, swingAxisWorld);
            }

            _hasRestPose = true;
        }

        private static Vector3 ToParentSpace(Transform arm, Vector3 worldDirection)
        {
            return arm.parent != null
                ? Quaternion.Inverse(arm.parent.rotation) * worldDirection
                : worldDirection;
        }

        /// <summary> Wave開始時にスケジュールをセットし、t=0 の初期姿勢へ反映する。 </summary>
        public void Initialize(
            int pendulumIndex,
            MotionSchedule parentSchedule,
            MotionSchedule childSchedule,
            float unscaledTime,
            MotionSchedule grandchildSchedule = null)
        {
            _pendulumIndex = pendulumIndex;
            _parentSchedule = parentSchedule;
            _childSchedule = childSchedule;
            _grandchildSchedule = grandchildSchedule;
            _isDoublePendulum = childSchedule != null;
            _isTriplePendulum = grandchildSchedule != null;

            _parentDetector.Reset();
            _childDetector.Reset();
            _grandchildDetector.Reset();

            _isTailStarted = false;
            _tailParentVelocity = 0.0;
            _tailChildVelocity = 0.0;
            _tailGrandchildVelocity = 0.0;
            _currentSegmentIndex = 0;

            if (_parentArm == null)
            {
                Debug.LogError($"[{nameof(PendulumMotion)}] ParentArm が未設定です。", this);
                return;
            }

            if (_isDoublePendulum && _childArm == null)
            {
                Debug.LogError($"[{nameof(PendulumMotion)}] 二重振り子ですが ChildArm が未設定です。", this);
            }

            if (_isTriplePendulum && _grandchildArm == null)
            {
                Debug.LogError($"[{nameof(PendulumMotion)}] 三重振り子ですが GrandchildArm が未設定です。", this);
            }

            // t = 0 の初期角度を適用し、検出器に基準角度を登録しておく
            EvaluateAt(0.0, 0.0, unscaledTime);
        }

        /// <summary>
        /// 経過時刻とフレームデルタ時間から角度を評価し、Transform と真上通過検知を同時更新する。
        /// GameController.Update() から毎フレーム呼び出される。
        /// </summary>
        public void EvaluateAt(double elapsedSeconds, double unscaledDeltaTime, float unscaledTime)
        {
            if (_parentSchedule == null || _parentArm == null)
            {
                return;
            }

            double scheduleElapsed = elapsedSeconds - START_FREEZE_DURATION;

            // Wave開始から最初の1秒間：初期姿勢のまま固定し、回転も真上通過判定も行わない
            if (scheduleElapsed < 0.0)
            {
                _currentSegmentIndex = 0;
                double frozenParent = _parentSchedule.EvaluateAngle(0.0);
                _parentAngle = frozenParent;
                ApplyArmRotation(_parentArm, _parentAngle);

                if (IsDoublePendulum)
                {
                    double frozenChildWorld = _childSchedule.EvaluateAngle(0.0);
                    _childWorldAngle = frozenChildWorld;
                    _childLocalAngle = frozenChildWorld - frozenParent;
                    ApplyArmRotation(_childArm, _childLocalAngle);

                    if (IsTriplePendulum)
                    {
                        double frozenGrandWorld = _grandchildSchedule.EvaluateAngle(0.0);
                        _grandchildWorldAngle = frozenGrandWorld;
                        _grandchildLocalAngle = frozenGrandWorld - frozenChildWorld;
                        ApplyArmRotation(_grandchildArm, _grandchildLocalAngle);
                    }
                }

                return;
            }

            // 固定1秒を終えた後の8秒間：事前生成スケジュールに沿った決定論的モーション
            if (scheduleElapsed < PLANNED_DURATION)
            {
                _currentSegmentIndex = _parentSchedule.GetSegmentIndex(scheduleElapsed);
                double nextParent = _parentSchedule.EvaluateAngle(scheduleElapsed);
                _parentAngle = nextParent;
                ApplyArmRotation(_parentArm, _parentAngle);
                _parentDetector.Evaluate(nextParent, unscaledTime);

                if (IsDoublePendulum)
                {
                    // 子玉は支点基準のワールド角で真上通過判定し、子棒の見た目だけローカル角へ変換する
                    double nextChildWorld = _childSchedule.EvaluateAngle(scheduleElapsed);
                    _childWorldAngle = nextChildWorld;
                    _childLocalAngle = nextChildWorld - nextParent;
                    ApplyArmRotation(_childArm, _childLocalAngle);
                    _childDetector.Evaluate(nextChildWorld, unscaledTime);

                    if (IsTriplePendulum)
                    {
                        // 孫玉もワールド角で真上通過判定し、孫棒の見た目だけ子棒との差分ローカル角へ変換する
                        double nextGrandWorld = _grandchildSchedule.EvaluateAngle(scheduleElapsed);
                        _grandchildWorldAngle = nextGrandWorld;
                        _grandchildLocalAngle = nextGrandWorld - nextChildWorld;
                        ApplyArmRotation(_grandchildArm, _grandchildLocalAngle);
                        _grandchildDetector.Evaluate(nextGrandWorld, unscaledTime);
                    }
                }

                return;
            }

            // 8〜10秒：見た目用の物理減衰運動（演出区間であり、追加カウントは一切行わない）
            _currentSegmentIndex = 8;
            if (!_isTailStarted)
            {
                // t=8秒の最終計画角度から角速度0で連続的に移行する（位置ジャンプさせない）
                _isTailStarted = true;
                _tailParentAngle = _parentSchedule.EndAngle;
                _tailParentVelocity = 0.0;
                _tailChildAngle = _childSchedule != null ? _childSchedule.EndAngle : 0.0;
                _tailChildVelocity = 0.0;
                _tailGrandchildAngle = _grandchildSchedule != null ? _grandchildSchedule.EndAngle : 0.0;
                _tailGrandchildVelocity = 0.0;
            }

            double clampedDt = Math.Min(MAX_FRAME_DT, Math.Max(0.0, unscaledDeltaTime));
            int steps = Math.Max(1, (int)Math.Ceiling(clampedDt / MAX_SUBSTEP_DT));
            double dt = clampedDt / steps;

            // 仕様式のオイラー小刻み積分：α = -9.81 sin(θ) - 1.35 ω
            for (int i = 0; i < steps; i++)
            {
                double parentAlpha = -GRAVITY * Math.Sin(_tailParentAngle) - DAMPING * _tailParentVelocity;
                _tailParentVelocity += parentAlpha * dt;
                _tailParentAngle += _tailParentVelocity * dt;

                if (IsDoublePendulum)
                {
                    double childAlpha = -GRAVITY * Math.Sin(_tailChildAngle) - DAMPING * _tailChildVelocity;
                    _tailChildVelocity += childAlpha * dt;
                    _tailChildAngle += _tailChildVelocity * dt;

                    if (IsTriplePendulum)
                    {
                        double grandAlpha = -GRAVITY * Math.Sin(_tailGrandchildAngle) - DAMPING * _tailGrandchildVelocity;
                        _tailGrandchildVelocity += grandAlpha * dt;
                        _tailGrandchildAngle += _tailGrandchildVelocity * dt;
                    }
                }
            }

            _parentAngle = _tailParentAngle;
            ApplyArmRotation(_parentArm, _parentAngle);

            if (IsDoublePendulum)
            {
                _childWorldAngle = _tailChildAngle;
                _childLocalAngle = _tailChildAngle - _tailParentAngle;
                ApplyArmRotation(_childArm, _childLocalAngle);

                if (IsTriplePendulum)
                {
                    _grandchildWorldAngle = _tailGrandchildAngle;
                    _grandchildLocalAngle = _tailGrandchildAngle - _tailChildAngle;
                    ApplyArmRotation(_grandchildArm, _grandchildLocalAngle);
                }
            }
        }

        private void ApplyArmRotation(Transform arm, double angleRad)
        {
            if (!_hasRestPose)
            {
                CaptureRestPose();
            }

            float degrees = (float)(angleRad * Mathf.Rad2Deg);
            Vector3 axis;
            Quaternion rest;
            if (arm == _grandchildArm)
            {
                axis = _grandchildSwingAxisLocal;
                rest = _grandchildRestLocalRotation;
            }
            else if (arm == _childArm)
            {
                axis = _childSwingAxisLocal;
                rest = _childRestLocalRotation;
            }
            else
            {
                axis = _parentSwingAxisLocal;
                rest = _parentRestLocalRotation;
            }

            // 初期姿勢が無回転・軸がZのグレーボックスでは、従来の Quaternion.Euler(0, 0, θ) と同じ結果になる
            arm.localRotation = Quaternion.AngleAxis(degrees, axis) * rest;
        }

        private void HandleParentTopCrossed(int crossedCount)
        {
            OnTopCrossed?.Invoke(this, false, crossedCount);
            OnBobTopCrossed?.Invoke(this, 0, crossedCount);
        }

        private void HandleChildTopCrossed(int crossedCount)
        {
            OnTopCrossed?.Invoke(this, true, crossedCount);
            OnBobTopCrossed?.Invoke(this, 1, crossedCount);
        }

        private void HandleGrandchildTopCrossed(int crossedCount)
        {
            OnTopCrossed?.Invoke(this, true, crossedCount);
            OnBobTopCrossed?.Invoke(this, 2, crossedCount);
        }
    }
}
