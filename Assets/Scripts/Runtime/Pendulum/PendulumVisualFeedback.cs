using UnityEngine;

namespace Taurus.Runtime.Pendulum
{
    /// <summary>
    /// 真上通過時に親玉（ライム #d5ff58）と子玉（コーラル #ff735b）を約 0.48 秒発光・拡大演出させるコンポーネント。
    /// PendulumMotion と同じ Prefab ルートにアタッチして使用する。
    ///
    /// 【本番素材差し替え時の引き継ぎ】
    /// - 現在は仮の SpriteRenderer の色変更と Transform スケール拡大で演出している。
    /// - 本番 3D モデルやパーティクルへ差し替える場合は、HandleTopCrossed 内でパーティクル再生やマテリアル Emission 制御を追加する。
    /// </summary>
    [RequireComponent(typeof(PendulumMotion))]
    public class PendulumVisualFeedback : MonoBehaviour
    {
        // 仕様書第9章準拠：真上通過時の発光持続時間（秒）
        private const float FLASH_DURATION = 0.48f;
        private const float PEAK_SCALE_MULTIPLIER = 1.45f;

        private static readonly Color PARENT_BASE_COLOR = new Color(0.17f, 0.27f, 0.25f, 1f);
        private static readonly Color PARENT_FLASH_COLOR = new Color(0.835f, 1.0f, 0.345f, 1f); // #d5ff58（ライム）
        private static readonly Color CHILD_BASE_COLOR = new Color(0.29f, 0.23f, 0.20f, 1f);
        private static readonly Color CHILD_FLASH_COLOR = new Color(1.0f, 0.451f, 0.357f, 1f);  // #ff735b（コーラル）

        [SerializeField] private Transform _parentBob;
        [SerializeField] private SpriteRenderer _parentBobRenderer;
        [SerializeField] private Transform _childBob;
        [SerializeField] private SpriteRenderer _childBobRenderer;

        private PendulumMotion _motion;
        private Vector3 _parentBaseScale = Vector3.one;
        private Vector3 _childBaseScale = Vector3.one;
        private float _parentFlashTimer;
        private float _childFlashTimer;

        private void Awake()
        {
            _motion = GetComponent<PendulumMotion>();
            if (_parentBob != null)
            {
                _parentBaseScale = _parentBob.localScale;
            }

            if (_childBob != null)
            {
                _childBaseScale = _childBob.localScale;
            }

            if (_motion != null)
            {
                _motion.OnTopCrossed += HandleTopCrossed;
            }
        }

        private void OnDestroy()
        {
            if (_motion != null)
            {
                _motion.OnTopCrossed -= HandleTopCrossed;
            }
        }

        /// <summary> エディタ構築ツール（ProjectSceneSetupTool）から玉のTransformとRendererを設定する。 </summary>
        public void ConfigureBobs(
            Transform parentBob,
            SpriteRenderer parentRenderer,
            Transform childBob,
            SpriteRenderer childRenderer)
        {
            _parentBob = parentBob;
            _parentBobRenderer = parentRenderer;
            _childBob = childBob;
            _childBobRenderer = childRenderer;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (_parentFlashTimer > 0f)
            {
                _parentFlashTimer = Mathf.Max(0f, _parentFlashTimer - dt);
                ApplyFlashVisual(_parentBob, _parentBobRenderer, _parentBaseScale, PARENT_BASE_COLOR, PARENT_FLASH_COLOR, _parentFlashTimer / FLASH_DURATION);
            }

            if (_childFlashTimer > 0f)
            {
                _childFlashTimer = Mathf.Max(0f, _childFlashTimer - dt);
                ApplyFlashVisual(_childBob, _childBobRenderer, _childBaseScale, CHILD_BASE_COLOR, CHILD_FLASH_COLOR, _childFlashTimer / FLASH_DURATION);
            }
        }

        private void HandleTopCrossed(PendulumMotion pendulum, bool isChild, int crossedCount)
        {
            if (isChild)
            {
                _childFlashTimer = FLASH_DURATION;
                ApplyFlashVisual(_childBob, _childBobRenderer, _childBaseScale, CHILD_BASE_COLOR, CHILD_FLASH_COLOR, 1f);
            }
            else
            {
                _parentFlashTimer = FLASH_DURATION;
                ApplyFlashVisual(_parentBob, _parentBobRenderer, _parentBaseScale, PARENT_BASE_COLOR, PARENT_FLASH_COLOR, 1f);
            }
        }

        private static void ApplyFlashVisual(
            Transform bob,
            SpriteRenderer renderer,
            Vector3 baseScale,
            Color baseColor,
            Color flashColor,
            float normalizedRemaining)
        {
            // 二次カーブで素早く光って滑らかに元へ戻す
            float ease = normalizedRemaining * normalizedRemaining;
            if (bob != null)
            {
                float scaleFactor = Mathf.Lerp(1f, PEAK_SCALE_MULTIPLIER, ease);
                bob.localScale = baseScale * scaleFactor;
            }

            if (renderer != null)
            {
                renderer.color = Color.Lerp(baseColor, flashColor, ease);
            }
        }
    }
}
