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

        // 親棒のピボットTransform（Z軸回転させる対象）
        [SerializeField] private Transform _parentArm;

        // 二重振り子時の子棒ピボットTransform（親棒先端の子として配置。単振り子ではnull）
        [SerializeField] private Transform _childArm;

        // 二重振り子として扱うかどうかのフラグ
        [SerializeField] private bool _isDoublePendulum;

        // 描画に使う角度と全く同じ値を渡してフレームズレを防ぐための検出器
        private readonly TopCrossingDetector _parentDetector = new TopCrossingDetector();
        private readonly TopCrossingDetector _childDetector = new TopCrossingDetector();

        private int _pendulumIndex;
        private MotionSchedule _parentSchedule;
        private MotionSchedule _childSchedule;

        private double _parentAngle;
        private double _childWorldAngle;
        private double _childLocalAngle;
        private int _currentSegmentIndex;

        // 8〜10秒減衰運動用の状態変数
        private bool _isTailStarted;
        private double _tailParentAngle;
        private double _tailParentVelocity;
        private double _tailChildAngle;
        private double _tailChildVelocity;

        /// <summary> 真上を通過した際に発行される（振り子本体、子玉ならtrue、通過回数）。視覚発光やSEフックが購読する。 </summary>
        public event Action<PendulumMotion, bool, int> OnTopCrossed;

        /// <summary> 振り子番号（0始まり）。 </summary>
        public int PendulumIndex => _pendulumIndex;

        /// <summary> 二重振り子として稼働しているかどうか。 </summary>
        public bool IsDoublePendulum => _isDoublePendulum && _childArm != null && _childSchedule != null;

        /// <summary> 親玉のスケジュール。 </summary>
        public MotionSchedule ParentSchedule => _parentSchedule;

        /// <summary> 子玉のスケジュール（単振り子ならnull）。 </summary>
        public MotionSchedule ChildSchedule => _childSchedule;

        /// <summary> 親玉の現在連続角度（ラジアン）。 </summary>
        public double ParentAngleRad => _parentAngle;

        /// <summary> 子玉の現在ワールド連続角度（ラジアン）。 </summary>
        public double ChildWorldAngleRad => _childWorldAngle;

        /// <summary> 親玉の現在の高さ h = -cos(θ)（+1=真上, -1=真下）。 </summary>
        public double ParentHeight => -Math.Cos(_parentAngle);

        /// <summary> 子玉の現在の高さ h = -cos(θ)（+1=真上, -1=真下）。 </summary>
        public double ChildHeight => -Math.Cos(_childWorldAngle);

        /// <summary> 親玉の実測真上通過回数（デバッグ検証用）。 </summary>
        public int ParentObservedCount => _parentDetector.ObservedCount;

        /// <summary> 子玉の実測真上通過回数（デバッグ検証用）。 </summary>
        public int ChildObservedCount => _childDetector.ObservedCount;

        /// <summary> 親玉の直近真上通過時刻（unscaledTime）。 </summary>
        public float ParentLastTopTime => _parentDetector.LastTopTime;

        /// <summary> 子玉の直近真上通過時刻（unscaledTime）。 </summary>
        public float ChildLastTopTime => _childDetector.LastTopTime;

        /// <summary> 現在の区間インデックス（0〜7、8秒以降の減衰区間では8）。 </summary>
        public int CurrentSegmentIndex => _currentSegmentIndex;

        /// <summary> この振り子の予定通過数合計（親＋子）。GameController が正解値算出に使う。 </summary>
        public int TotalExpectedTopCount =>
            (_parentSchedule != null ? _parentSchedule.TotalExpectedTopCount : 0) +
            (_childSchedule != null ? _childSchedule.TotalExpectedTopCount : 0);

        private void Awake()
        {
            _parentDetector.OnTopCrossed += HandleParentTopCrossed;
            _childDetector.OnTopCrossed += HandleChildTopCrossed;
        }

        private void OnDestroy()
        {
            _parentDetector.OnTopCrossed -= HandleParentTopCrossed;
            _childDetector.OnTopCrossed -= HandleChildTopCrossed;
        }

        /// <summary> エディタ構築ツール（ProjectSceneSetupTool）からアーム参照を設定する。 </summary>
        public void ConfigureArms(Transform parentArm, Transform childArm, bool isDouble)
        {
            _parentArm = parentArm;
            _childArm = childArm;
            _isDoublePendulum = isDouble;
        }

        /// <summary> Wave開始時にスケジュールをセットし、t=0 の初期姿勢へ反映する。 </summary>
        public void Initialize(int pendulumIndex, MotionSchedule parentSchedule, MotionSchedule childSchedule, float unscaledTime)
        {
            _pendulumIndex = pendulumIndex;
            _parentSchedule = parentSchedule;
            _childSchedule = childSchedule;
            _isDoublePendulum = childSchedule != null;

            _parentDetector.Reset();
            _childDetector.Reset();

            _isTailStarted = false;
            _tailParentVelocity = 0.0;
            _tailChildVelocity = 0.0;
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
                }
            }

            _parentAngle = _tailParentAngle;
            ApplyArmRotation(_parentArm, _parentAngle);

            if (IsDoublePendulum)
            {
                _childWorldAngle = _tailChildAngle;
                _childLocalAngle = _tailChildAngle - _tailParentAngle;
                ApplyArmRotation(_childArm, _childLocalAngle);
            }
        }

        private static void ApplyArmRotation(Transform arm, double angleRad)
        {
            float degrees = (float)(angleRad * Mathf.Rad2Deg);
            arm.localRotation = Quaternion.Euler(0f, 0f, degrees);
        }

        private void HandleParentTopCrossed(int crossedCount)
        {
            OnTopCrossed?.Invoke(this, false, crossedCount);
        }

        private void HandleChildTopCrossed(int crossedCount)
        {
            OnTopCrossed?.Invoke(this, true, crossedCount);
        }
    }
}
