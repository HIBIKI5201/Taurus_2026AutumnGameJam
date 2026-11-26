using UnityEngine;

namespace Taurus.Runtime.Pendulum
{
    /// <summary>
    /// 真上通過時に親玉（ライム #d5ff58）と子玉（コーラル #ff735b）を約 0.48 秒発光・拡大演出させるコンポーネント。
    /// PendulumMotion と同じ Prefab ルートにアタッチして使用する。
    ///
    /// 【3D / 2D 両対応と引き継ぎポイント】
    /// - 3D モデル（MeshRenderer）では Awake 時にマテリアルの _EMISSION キーワードを有効化したうえで、
    ///   MaterialPropertyBlock で _BaseColor と HDR の _EmissionColor を更新する（Bloom 連携対応）。
    /// - 2D SpriteRenderer が設定されている場合も互換動作する。
    /// - 本番パーティクル等を追加したい場合は、HandleTopCrossed 内で再生処理を追加する。
    /// </summary>
    [RequireComponent(typeof(PendulumMotion))]
    public class PendulumVisualFeedback : MonoBehaviour
    {
        // 仕様書第9章準拠：真上通過時の発光持続時間（秒）
        private const float FLASH_DURATION = 0.48f;
        private const float PEAK_SCALE_MULTIPLIER = 1.45f;

        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");
        private static readonly int COLOR_ID = Shader.PropertyToID("_Color");
        private static readonly int EMISSION_COLOR_ID = Shader.PropertyToID("_EmissionColor");

        [Header("Emission Settings（Bloomと連携するHDR発光強度）")]
        [SerializeField, Min(0f)] private float _emissionIntensity = 6.0f;

        [Header("Parent Bob（親玉）")]
        [SerializeField] private Transform _parentBob;
        [SerializeField] private Renderer _parentBobMeshRenderer;
        [SerializeField] private SpriteRenderer _parentBobRenderer;

        [Header("Child Bob（子玉：二重振り子のみ）")]
        [SerializeField] private Transform _childBob;
        [SerializeField] private Renderer _childBobMeshRenderer;
        [SerializeField] private SpriteRenderer _childBobRenderer;

        private PendulumMotion _motion;
        private MaterialPropertyBlock _parentPropBlock;
        private MaterialPropertyBlock _childPropBlock;
        private Vector3 _parentBaseScale = Vector3.one;
        private Vector3 _childBaseScale = Vector3.one;
        private float _parentFlashTimer;
        private float _childFlashTimer;

        private void Awake()
        {
            _motion = GetComponent<PendulumMotion>();
            _parentPropBlock = new MaterialPropertyBlock();
            _childPropBlock = new MaterialPropertyBlock();

            if (_parentBob != null)
            {
                _parentBaseScale = _parentBob.localScale;
            }

            if (_childBob != null)
            {
                _childBaseScale = _childBob.localScale;
            }

            // URP Lit 等はマテリアル側で _EMISSION キーワードが有効でないと _EmissionColor を無視するため確実に有効化する
            EnableEmissionKeyword(_parentBobMeshRenderer);
            EnableEmissionKeyword(_childBobMeshRenderer);

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

        /// <summary> エディタ構築ツール（ProjectSceneSetupTool）から2D玉の参照を設定する。 </summary>
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

        /// <summary> エディタ構築ツール（ProjectSceneSetupTool）から3D玉の参照を設定する。 </summary>
        public void ConfigureMeshBobs(
            Transform parentBob,
            Renderer parentMeshRenderer,
            Transform childBob,
            Renderer childMeshRenderer)
        {
            _parentBob = parentBob;
            _parentBobMeshRenderer = parentMeshRenderer;
            _childBob = childBob;
            _childBobMeshRenderer = childMeshRenderer;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (_parentFlashTimer > 0f)
            {
                _parentFlashTimer = Mathf.Max(0f, _parentFlashTimer - dt);
                ApplyFlashVisual(
                    _parentBob,
                    _parentBobMeshRenderer,
                    _parentBobRenderer,
                    _parentPropBlock,
                    _parentBaseScale,
                    PendulumFlashPalette.ParentBaseColor,
                    PendulumFlashPalette.ParentFlashColor,
                    _parentFlashTimer / FLASH_DURATION,
                    _emissionIntensity);
            }

            if (_childFlashTimer > 0f)
            {
                _childFlashTimer = Mathf.Max(0f, _childFlashTimer - dt);
                ApplyFlashVisual(
                    _childBob,
                    _childBobMeshRenderer,
                    _childBobRenderer,
                    _childPropBlock,
                    _childBaseScale,
                    PendulumFlashPalette.ChildBaseColor,
                    PendulumFlashPalette.ChildFlashColor,
                    _childFlashTimer / FLASH_DURATION,
                    _emissionIntensity);
            }
        }

        private void HandleTopCrossed(PendulumMotion pendulum, bool isChild, int crossedCount)
        {
            if (isChild)
            {
                _childFlashTimer = FLASH_DURATION;
                ApplyFlashVisual(
                    _childBob,
                    _childBobMeshRenderer,
                    _childBobRenderer,
                    _childPropBlock,
                    _childBaseScale,
                    PendulumFlashPalette.ChildBaseColor,
                    PendulumFlashPalette.ChildFlashColor,
                    1f,
                    _emissionIntensity);
            }
            else
            {
                _parentFlashTimer = FLASH_DURATION;
                ApplyFlashVisual(
                    _parentBob,
                    _parentBobMeshRenderer,
                    _parentBobRenderer,
                    _parentPropBlock,
                    _parentBaseScale,
                    PendulumFlashPalette.ParentBaseColor,
                    PendulumFlashPalette.ParentFlashColor,
                    1f,
                    _emissionIntensity);
            }
        }

        private static void EnableEmissionKeyword(Renderer meshRenderer)
        {
            if (meshRenderer == null)
            {
                return;
            }

            Material[] materials = meshRenderer.materials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null)
                {
                    continue;
                }

                materials[i].EnableKeyword("_EMISSION");
                materials[i].globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }

        private static void ApplyFlashVisual(
            Transform bob,
            Renderer meshRenderer,
            SpriteRenderer spriteRenderer,
            MaterialPropertyBlock propBlock,
            Vector3 baseScale,
            Color baseColor,
            Color flashColor,
            float normalizedRemaining,
            float emissionIntensity)
        {
            // 二次カーブで素早く光って滑らかに元へ戻す
            float ease = normalizedRemaining * normalizedRemaining;
            if (bob != null)
            {
                float scaleFactor = Mathf.Lerp(1f, PEAK_SCALE_MULTIPLIER, ease);
                bob.localScale = baseScale * scaleFactor;
            }

            Color currentColor = Color.Lerp(baseColor, flashColor, ease);

            // 3D MeshRenderer（URP Lit）は MaterialPropertyBlock で色と HDR Linear Emission を更新
            if (meshRenderer != null && propBlock != null)
            {
                meshRenderer.GetPropertyBlock(propBlock);
                propBlock.SetColor(BASE_COLOR_ID, currentColor);
                propBlock.SetColor(COLOR_ID, currentColor);
                Color hdrEmission = flashColor.linear * (ease * emissionIntensity);
                propBlock.SetColor(EMISSION_COLOR_ID, hdrEmission);
                meshRenderer.SetPropertyBlock(propBlock);
            }

            // 2D SpriteRenderer 互換
            if (spriteRenderer != null)
            {
                spriteRenderer.color = currentColor;
            }
        }
    }
}
