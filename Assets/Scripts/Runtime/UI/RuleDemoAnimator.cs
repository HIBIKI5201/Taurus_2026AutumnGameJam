using Taurus.Runtime.Pendulum;
using UnityEngine;
using UnityEngine.UI;

namespace Taurus.Runtime.UI
{
    /// <summary>
    /// ルール説明画面の3種視覚デモ（(1)真上通過と光、(2)0回区間のフェイント、(3)区間ごとの反転）を
    /// AnimationClip を使わず軽量にループ再生するスクリプト。
    /// HTML参照実装の CSS @keyframes と同じキーフレーム比率で角度と発光を制御している。
    /// </summary>
    public class RuleDemoAnimator : MonoBehaviour
    {
        private const float SPIN_CYCLE = 2.4f;
        private const float FEINT_CYCLE = 2.3f;
        private const float REVERSE_CYCLE = 2.7f;

        [SerializeField] private RectTransform _spinArm;
        [SerializeField] private Image _spinBob;
        [SerializeField] private RectTransform _feintArm;
        [SerializeField] private RectTransform _reverseArm;
        [SerializeField] private Image _reverseBob;

        /// <summary> エディタ構築ツール（ProjectSceneSetupTool）からデモUI参照を設定する。 </summary>
        public void ConfigureDemos(
            RectTransform spinArm,
            Image spinBob,
            RectTransform feintArm,
            RectTransform reverseArm,
            Image reverseBob)
        {
            _spinArm = spinArm;
            _spinBob = spinBob;
            _feintArm = feintArm;
            _reverseArm = reverseArm;
            _reverseBob = reverseBob;
        }

        private void Update()
        {
            float t = Time.unscaledTime;
            UpdateSpinDemo((t % SPIN_CYCLE) / SPIN_CYCLE);
            UpdateFeintDemo((t % FEINT_CYCLE) / FEINT_CYCLE);
            UpdateReverseDemo((t % REVERSE_CYCLE) / REVERSE_CYCLE);
        }

        /// <summary> デモ1：180度（真上）を通過した瞬間に光る動きを再生する。 </summary>
        private void UpdateSpinDemo(float u)
        {
            float angle = EvaluatePiecewise(u,
                0.00f, 0f,
                0.46f, 160f,
                0.50f, 180f,
                0.54f, 200f,
                1.00f, 360f);

            if (_spinArm != null)
            {
                _spinArm.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            if (_spinBob != null)
            {
                bool isFlash = u >= 0.48f && u <= 0.54f;
                _spinBob.color = isFlash ? PendulumFlashPalette.ParentFlashColor : PendulumFlashPalette.ParentBaseColor;
                _spinBob.rectTransform.localScale = Vector3.one * (isFlash ? 1.35f : 1f);
            }
        }

        /// <summary> デモ2：真上手前（158度）で引き返す0回フェイントの動きを再生する。 </summary>
        private void UpdateFeintDemo(float u)
        {
            float angle = EvaluatePiecewise(u,
                0.00f, 0f,
                0.35f, 105f,
                0.52f, 158f,
                0.70f, 125f,
                0.86f, 25f,
                1.00f, 0f);

            if (_feintArm != null)
            {
                _feintArm.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        /// <summary> デモ3：真上（180度）を越えて190度まで到達したあと、逆方向へ折り返す動きを再生する。 </summary>
        private void UpdateReverseDemo(float u)
        {
            float angle = EvaluatePiecewise(u,
                0.00f, 0f,
                0.40f, 0f,
                0.52f, 150f,
                0.68f, 190f,
                0.82f, 130f,
                1.00f, 0f);

            if (_reverseArm != null)
            {
                _reverseArm.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            if (_reverseBob != null)
            {
                bool isFlash = u >= 0.60f && u <= 0.67f;
                _reverseBob.color = isFlash ? PendulumFlashPalette.ParentFlashColor : PendulumFlashPalette.ParentBaseColor;
                _reverseBob.rectTransform.localScale = Vector3.one * (isFlash ? 1.35f : 1f);
            }
        }

        /// <summary> (時刻, 角度) ペアの配列を SmoothStep で区間補間するヘルパー。 </summary>
        private static float EvaluatePiecewise(float u, params float[] keyframes)
        {
            int count = keyframes.Length / 2;
            if (count <= 0) return 0f;
            if (u <= keyframes[0]) return keyframes[1];

            for (int i = 0; i < count - 1; i++)
            {
                float t0 = keyframes[i * 2];
                float v0 = keyframes[i * 2 + 1];
                float t1 = keyframes[(i + 1) * 2];
                float v1 = keyframes[(i + 1) * 2 + 1];

                if (u <= t1)
                {
                    float localU = Mathf.Clamp01((u - t0) / Mathf.Max(0.0001f, t1 - t0));
                    float smooth = localU * localU * (3f - 2f * localU);
                    return Mathf.Lerp(v0, v1, smooth);
                }
            }

            return keyframes[keyframes.Length - 1];
        }
    }
}
