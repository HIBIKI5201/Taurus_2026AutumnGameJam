using UnityEngine;

namespace Taurus.Runtime.Pendulum
{
    /// <summary> 真上通過時に親玉・子玉・孫玉の HDR Emission カラーとスケールを演出する。 </summary>
    [RequireComponent(typeof(PendulumMotion))]
    public class PendulumVisualFeedback : MonoBehaviour
    {
        // 仕様書第9章準拠：真上通過時の発光持続時間（秒）
        private const float FLASH_DURATION = 0.48f;
        private const float PEAK_SCALE_MULTIPLIER = 1.45f;
        private const float DEFAULT_FLASH_INTENSITY = 6.0f;

        private static readonly int EMISSION_COLOR_ID = Shader.PropertyToID("_EmissionColor");

        [Header("Parent Bob（親玉）")]
        [SerializeField] private Transform _parentBob;
        [SerializeField] private Renderer _parentBobMeshRenderer;
        [SerializeField] private SpriteRenderer _parentBobRenderer;
        [SerializeField, ColorUsage(false, true)] private Color _parentIdleEmissionColor = Color.black;
        [SerializeField, ColorUsage(false, true)] private Color _parentFlashEmissionColor = PendulumFlashPalette.ParentFlashColor.linear * DEFAULT_FLASH_INTENSITY;

        [Header("Child Bob（子玉：二重・三重振り子）")]
        [SerializeField] private Transform _childBob;
        [SerializeField] private Renderer _childBobMeshRenderer;
        [SerializeField] private SpriteRenderer _childBobRenderer;
        [SerializeField, ColorUsage(false, true)] private Color _childIdleEmissionColor = Color.black;
        [SerializeField, ColorUsage(false, true)] private Color _childFlashEmissionColor = PendulumFlashPalette.ChildFlashColor.linear * DEFAULT_FLASH_INTENSITY;

        [Header("Grandchild Bob（孫玉：三重振り子のみ）")]
        [SerializeField] private Transform _grandchildBob;
        [SerializeField] private Renderer _grandchildBobMeshRenderer;
        [SerializeField] private SpriteRenderer _grandchildBobRenderer;
        [SerializeField, ColorUsage(false, true)] private Color _grandchildIdleEmissionColor = Color.black;
        [SerializeField, ColorUsage(false, true)] private Color _grandchildFlashEmissionColor = PendulumFlashPalette.GrandchildFlashColor.linear * DEFAULT_FLASH_INTENSITY;

        private PendulumMotion _motion;
        private MaterialPropertyBlock _parentPropBlock;
        private MaterialPropertyBlock _childPropBlock;
        private MaterialPropertyBlock _grandchildPropBlock;
        private Vector3 _parentBaseScale = Vector3.one;
        private Vector3 _childBaseScale = Vector3.one;
        private Vector3 _grandchildBaseScale = Vector3.one;
        private float _parentFlashTimer;
        private float _childFlashTimer;
        private float _grandchildFlashTimer;

        private void Awake()
        {
            _motion = GetComponent<PendulumMotion>();
            _parentPropBlock = new MaterialPropertyBlock();
            _childPropBlock = new MaterialPropertyBlock();
            _grandchildPropBlock = new MaterialPropertyBlock();

            if (_parentBob != null)
            {
                _parentBaseScale = _parentBob.localScale;
            }

            if (_childBob != null)
            {
                _childBaseScale = _childBob.localScale;
            }

            if (_grandchildBob != null)
            {
                _grandchildBaseScale = _grandchildBob.localScale;
            }

            // URP Lit 等はマテリアル側で _EMISSION キーワードが有効でないと _EmissionColor を無視するため確実に有効化する
            EnableEmissionKeyword(_parentBobMeshRenderer);
            EnableEmissionKeyword(_childBobMeshRenderer);
            EnableEmissionKeyword(_grandchildBobMeshRenderer);

            // 起動直後から通常時エミッションカラーを反映する
            ApplyFlashVisual(
                _parentBob,
                _parentBobMeshRenderer,
                _parentBobRenderer,
                _parentPropBlock,
                _parentBaseScale,
                _parentIdleEmissionColor,
                _parentFlashEmissionColor,
                0f);
            ApplyFlashVisual(
                _childBob,
                _childBobMeshRenderer,
                _childBobRenderer,
                _childPropBlock,
                _childBaseScale,
                _childIdleEmissionColor,
                _childFlashEmissionColor,
                0f);
            ApplyFlashVisual(
                _grandchildBob,
                _grandchildBobMeshRenderer,
                _grandchildBobRenderer,
                _grandchildPropBlock,
                _grandchildBaseScale,
                _grandchildIdleEmissionColor,
                _grandchildFlashEmissionColor,
                0f);

            if (_motion != null)
            {
                _motion.OnBobTopCrossed += HandleBobTopCrossed;
            }
        }

        private void OnDestroy()
        {
            if (_motion != null)
            {
                _motion.OnBobTopCrossed -= HandleBobTopCrossed;
            }
        }

        /// <summary> ランタイム拡張で生成された三重振り子・孫玉の参照を登録する。 </summary>
        public void ConfigureGrandchildMeshBob(Transform grandchildBob, Renderer grandchildMeshRenderer)
        {
            _grandchildBob = grandchildBob;
            _grandchildBobMeshRenderer = grandchildMeshRenderer;
            if (_grandchildBob != null)
            {
                _grandchildBaseScale = _grandchildBob.localScale;
            }

            _grandchildPropBlock ??= new MaterialPropertyBlock();
            EnableEmissionKeyword(_grandchildBobMeshRenderer);
            ApplyFlashVisual(
                _grandchildBob,
                _grandchildBobMeshRenderer,
                _grandchildBobRenderer,
                _grandchildPropBlock,
                _grandchildBaseScale,
                _grandchildIdleEmissionColor,
                _grandchildFlashEmissionColor,
                0f);
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
                    _parentIdleEmissionColor,
                    _parentFlashEmissionColor,
                    _parentFlashTimer / FLASH_DURATION);
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
                    _childIdleEmissionColor,
                    _childFlashEmissionColor,
                    _childFlashTimer / FLASH_DURATION);
            }

            if (_grandchildFlashTimer > 0f)
            {
                _grandchildFlashTimer = Mathf.Max(0f, _grandchildFlashTimer - dt);
                ApplyFlashVisual(
                    _grandchildBob,
                    _grandchildBobMeshRenderer,
                    _grandchildBobRenderer,
                    _grandchildPropBlock,
                    _grandchildBaseScale,
                    _grandchildIdleEmissionColor,
                    _grandchildFlashEmissionColor,
                    _grandchildFlashTimer / FLASH_DURATION);
            }
        }

        private void HandleBobTopCrossed(PendulumMotion pendulum, int bobTier, int crossedCount)
        {
            if (bobTier == 2)
            {
                _grandchildFlashTimer = FLASH_DURATION;
                ApplyFlashVisual(
                    _grandchildBob,
                    _grandchildBobMeshRenderer,
                    _grandchildBobRenderer,
                    _grandchildPropBlock,
                    _grandchildBaseScale,
                    _grandchildIdleEmissionColor,
                    _grandchildFlashEmissionColor,
                    1f);
            }
            else if (bobTier == 1)
            {
                _childFlashTimer = FLASH_DURATION;
                ApplyFlashVisual(
                    _childBob,
                    _childBobMeshRenderer,
                    _childBobRenderer,
                    _childPropBlock,
                    _childBaseScale,
                    _childIdleEmissionColor,
                    _childFlashEmissionColor,
                    1f);
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
                    _parentIdleEmissionColor,
                    _parentFlashEmissionColor,
                    1f);
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
            Color idleEmissionColor,
            Color flashEmissionColor,
            float normalizedRemaining)
        {
            // 二次カーブで素早く光って滑らかに通常色へ戻す
            float ease = normalizedRemaining * normalizedRemaining;
            if (bob != null)
            {
                float scaleFactor = Mathf.Lerp(1f, PEAK_SCALE_MULTIPLIER, ease);
                bob.localScale = baseScale * scaleFactor;
            }

            Color currentEmission = Color.Lerp(idleEmissionColor, flashEmissionColor, ease);

            // 3D MeshRenderer（URP Lit）は MaterialPropertyBlock で HDR _EmissionColor のみを補間更新
            if (meshRenderer != null && propBlock != null)
            {
                meshRenderer.GetPropertyBlock(propBlock);
                propBlock.SetColor(EMISSION_COLOR_ID, currentEmission);
                meshRenderer.SetPropertyBlock(propBlock);
            }

            // 2D SpriteRenderer 互換
            if (spriteRenderer != null)
            {
                spriteRenderer.color = currentEmission;
            }
        }
    }
}
