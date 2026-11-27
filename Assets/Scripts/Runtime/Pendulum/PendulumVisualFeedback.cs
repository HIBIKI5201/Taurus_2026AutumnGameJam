using UnityEngine;

namespace Taurus.Runtime.Pendulum
{
    /// <summary>
    ///     真上通過時に親玉・子玉・孫玉の HDR Emission カラーとスケールを演出する。
    ///     WebGL 環境では HDR / Bloom がブラウザ依存で効かない場合があるため、
    ///     ラジアル発光ハロー（2層ビルボード）＋ポイントライトによる疑似 HDR グローを自動生成する。
    /// </summary>
    [RequireComponent(typeof(PendulumMotion))]
    public class PendulumVisualFeedback : MonoBehaviour
    {
        // 仕様書第9章準拠：真上通過時の発光持続時間（秒）
        private const float FLASH_DURATION = 0.48f;
        private const float PEAK_SCALE_MULTIPLIER = 1.45f;
        private const float DEFAULT_FLASH_INTENSITY = 6.0f;

        private static readonly int EMISSION_COLOR_ID = Shader.PropertyToID("_EmissionColor");
        private static Sprite _sharedGlowSprite;

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

        [Header("WebGL 疑似 HDR グロー（HDR / Bloom 非対応ブラウザ向けフォールバック）")]
        [SerializeField] private bool _usePseudoGlowOnWebGl = true;
        [SerializeField] private bool _previewPseudoGlowInEditor;
        [SerializeField, Min(0.1f)] private float _pseudoGlowIdleScale = 2.4f;
        [SerializeField, Min(0.1f)] private float _pseudoGlowFlashScale = 5.4f;
        [SerializeField, Range(0f, 1f)] private float _pseudoGlowIdleAlpha = 0.38f;
        [SerializeField, Range(0f, 1f)] private float _pseudoGlowFlashAlpha = 0.92f;
        [SerializeField, Min(0f)] private float _pseudoLightIdleIntensity = 1.6f;
        [SerializeField, Min(0f)] private float _pseudoLightFlashIntensity = 12f;
        [SerializeField, Min(0.5f)] private float _pseudoLightRange = 7f;

        private sealed class PseudoGlowRig
        {
            public Transform Root;
            public SpriteRenderer OuterHalo;
            public SpriteRenderer InnerCore;
            public Light PointLight;
            public float BaseRadius;
        }

        private PendulumMotion _motion;
        private MaterialPropertyBlock _parentPropBlock;
        private MaterialPropertyBlock _childPropBlock;
        private MaterialPropertyBlock _grandchildPropBlock;
        private PseudoGlowRig _parentGlow;
        private PseudoGlowRig _childGlow;
        private PseudoGlowRig _grandchildGlow;
        private Vector3 _parentBaseScale = Vector3.one;
        private Vector3 _childBaseScale = Vector3.one;
        private Vector3 _grandchildBaseScale = Vector3.one;
        private float _parentFlashTimer;
        private float _childFlashTimer;
        private float _grandchildFlashTimer;

        private bool ShouldUsePseudoGlow
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return _usePseudoGlowOnWebGl;
#else
                return _previewPseudoGlowInEditor;
#endif
            }
        }

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

            if (ShouldUsePseudoGlow)
            {
                _parentGlow = CreatePseudoGlowRig("WebGLGlow_Parent", _parentBob, _parentBobMeshRenderer);
                _childGlow = CreatePseudoGlowRig("WebGLGlow_Child", _childBob, _childBobMeshRenderer);
                _grandchildGlow = CreatePseudoGlowRig("WebGLGlow_Grandchild", _grandchildBob, _grandchildBobMeshRenderer);
            }

            // 起動直後から通常時エミッションカラーを反映する
            ApplyFlashVisual(
                _parentBob,
                _parentBobMeshRenderer,
                _parentBobRenderer,
                _parentPropBlock,
                _parentGlow,
                _parentBaseScale,
                _parentIdleEmissionColor,
                _parentFlashEmissionColor,
                0f);
            ApplyFlashVisual(
                _childBob,
                _childBobMeshRenderer,
                _childBobRenderer,
                _childPropBlock,
                _childGlow,
                _childBaseScale,
                _childIdleEmissionColor,
                _childFlashEmissionColor,
                0f);
            ApplyFlashVisual(
                _grandchildBob,
                _grandchildBobMeshRenderer,
                _grandchildBobRenderer,
                _grandchildPropBlock,
                _grandchildGlow,
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

            DestroyPseudoGlowRig(ref _parentGlow);
            DestroyPseudoGlowRig(ref _childGlow);
            DestroyPseudoGlowRig(ref _grandchildGlow);
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

            if (ShouldUsePseudoGlow && _grandchildGlow == null)
            {
                _grandchildGlow = CreatePseudoGlowRig("WebGLGlow_Grandchild", _grandchildBob, _grandchildBobMeshRenderer);
            }

            ApplyFlashVisual(
                _grandchildBob,
                _grandchildBobMeshRenderer,
                _grandchildBobRenderer,
                _grandchildPropBlock,
                _grandchildGlow,
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
                    _parentGlow,
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
                    _childGlow,
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
                    _grandchildGlow,
                    _grandchildBaseScale,
                    _grandchildIdleEmissionColor,
                    _grandchildFlashEmissionColor,
                    _grandchildFlashTimer / FLASH_DURATION);
            }
        }

        private void LateUpdate()
        {
            if (!ShouldUsePseudoGlow)
            {
                return;
            }

            UnityEngine.Camera cam = UnityEngine.Camera.main;
            UpdatePseudoGlowTransform(_parentGlow, _parentBob, _parentBobMeshRenderer, cam);
            UpdatePseudoGlowTransform(_childGlow, _childBob, _childBobMeshRenderer, cam);
            UpdatePseudoGlowTransform(_grandchildGlow, _grandchildBob, _grandchildBobMeshRenderer, cam);
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
                    _grandchildGlow,
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
                    _childGlow,
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
                    _parentGlow,
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

        private void ApplyFlashVisual(
            Transform bob,
            Renderer meshRenderer,
            SpriteRenderer spriteRenderer,
            MaterialPropertyBlock propBlock,
            PseudoGlowRig glowRig,
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
                if (ShouldUsePseudoGlow)
                {
                    // LDR 環境でも玉自体がくすまず白飛び発光に見えるよう色味を持ち上げる
                    Color vivid = NormalizeToVividColor(currentEmission);
                    Color boosted = Color.Lerp(vivid * 1.4f, Color.white * 2.2f, ease * 0.65f);
                    propBlock.SetColor(EMISSION_COLOR_ID, boosted);
                }
                else
                {
                    propBlock.SetColor(EMISSION_COLOR_ID, currentEmission);
                }

                meshRenderer.SetPropertyBlock(propBlock);
            }

            // WebGL 疑似 HDR グロー（2層ラジアルハロー＋ポイントライト）を更新
            if (glowRig != null)
            {
                UpdatePseudoGlowVisual(glowRig, idleEmissionColor, flashEmissionColor, ease);
            }

            // 2D SpriteRenderer 互換
            if (spriteRenderer != null)
            {
                spriteRenderer.color = currentEmission;
            }
        }

        private PseudoGlowRig CreatePseudoGlowRig(string rigName, Transform bob, Renderer meshRenderer)
        {
            if (bob == null && meshRenderer == null)
            {
                return null;
            }

            Sprite glowSprite = GetOrCreateGlowSprite();
            GameObject rootGo = new GameObject(rigName);
            rootGo.transform.SetParent(transform, false);

            GameObject outerGo = new GameObject("OuterHalo");
            outerGo.transform.SetParent(rootGo.transform, false);
            SpriteRenderer outerSr = outerGo.AddComponent<SpriteRenderer>();
            outerSr.sprite = glowSprite;
            outerSr.sortingOrder = 10;

            GameObject innerGo = new GameObject("InnerCore");
            innerGo.transform.SetParent(rootGo.transform, false);
            SpriteRenderer innerSr = innerGo.AddComponent<SpriteRenderer>();
            innerSr.sprite = glowSprite;
            innerSr.sortingOrder = 11;

            Light pointLight = rootGo.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.shadows = LightShadows.None;
            pointLight.range = _pseudoLightRange;
            pointLight.intensity = _pseudoLightIdleIntensity;

            float radius = EstimateBobRadius(bob, meshRenderer);
            return new PseudoGlowRig
            {
                Root = rootGo.transform,
                OuterHalo = outerSr,
                InnerCore = innerSr,
                PointLight = pointLight,
                BaseRadius = radius
            };
        }

        private void UpdatePseudoGlowVisual(PseudoGlowRig rig, Color idleEmission, Color flashEmission, float ease)
        {
            Color idleVivid = NormalizeToVividColor(idleEmission);
            Color flashVivid = NormalizeToVividColor(flashEmission);
            Color haloColor = Color.Lerp(idleVivid, flashVivid, ease);
            Color coreColor = Color.Lerp(haloColor, Color.white, Mathf.Lerp(0.45f, 0.88f, ease));

            float outerScale = rig.BaseRadius * Mathf.Lerp(_pseudoGlowIdleScale, _pseudoGlowFlashScale, ease);
            float innerScale = outerScale * Mathf.Lerp(0.48f, 0.56f, ease);
            float outerAlpha = Mathf.Lerp(_pseudoGlowIdleAlpha, _pseudoGlowFlashAlpha, ease);
            float innerAlpha = Mathf.Clamp01(outerAlpha * 1.15f);

            if (rig.OuterHalo != null)
            {
                rig.OuterHalo.transform.localScale = Vector3.one * outerScale;
                rig.OuterHalo.color = new Color(haloColor.r, haloColor.g, haloColor.b, outerAlpha);
            }

            if (rig.InnerCore != null)
            {
                rig.InnerCore.transform.localScale = Vector3.one * innerScale;
                rig.InnerCore.color = new Color(coreColor.r, coreColor.g, coreColor.b, innerAlpha);
            }

            if (rig.PointLight != null)
            {
                rig.PointLight.color = haloColor;
                rig.PointLight.intensity = Mathf.Lerp(_pseudoLightIdleIntensity, _pseudoLightFlashIntensity, ease);
                rig.PointLight.range = _pseudoLightRange;
            }
        }

        private static void UpdatePseudoGlowTransform(PseudoGlowRig rig, Transform bob, Renderer meshRenderer, UnityEngine.Camera cam)
        {
            if (rig == null || rig.Root == null)
            {
                return;
            }

            Vector3 worldPos;
            if (meshRenderer != null)
            {
                worldPos = meshRenderer.bounds.center;
            }
            else if (bob != null)
            {
                worldPos = bob.position;
            }
            else
            {
                return;
            }

            if (cam != null)
            {
                // 玉のメッシュに埋もれないようカメラ側へわずかに寄せてビルボード化する
                Vector3 toCam = (cam.transform.position - worldPos).normalized;
                rig.Root.position = worldPos + toCam * (rig.BaseRadius * 0.65f);
                rig.Root.rotation = cam.transform.rotation;
            }
            else
            {
                rig.Root.position = worldPos;
            }
        }

        private static float EstimateBobRadius(Transform bob, Renderer meshRenderer)
        {
            if (meshRenderer != null)
            {
                Vector3 ext = meshRenderer.bounds.extents;
                float maxAxis = Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z));
                if (maxAxis > 0.05f && maxAxis < 5f)
                {
                    return maxAxis;
                }
            }

            if (bob != null)
            {
                return Mathf.Clamp(bob.lossyScale.magnitude * 0.35f, 0.2f, 1.5f);
            }

            return 0.45f;
        }

        private static Color NormalizeToVividColor(Color hdrColor)
        {
            float maxChannel = Mathf.Max(hdrColor.r, Mathf.Max(hdrColor.g, hdrColor.b));
            if (maxChannel <= 0.0001f)
            {
                return Color.white;
            }

            return new Color(
                Mathf.Clamp01(hdrColor.r / maxChannel),
                Mathf.Clamp01(hdrColor.g / maxChannel),
                Mathf.Clamp01(hdrColor.b / maxChannel),
                1f);
        }

        private static Sprite GetOrCreateGlowSprite()
        {
            if (_sharedGlowSprite != null)
            {
                return _sharedGlowSprite;
            }

            const int SIZE = 128;
            Texture2D tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "PendulumPseudoGlowTex"
            };

            Color[] pixels = new Color[SIZE * SIZE];
            float half = (SIZE - 1) * 0.5f;
            for (int y = 0; y < SIZE; y++)
            {
                float dy = (y - half) / half;
                for (int x = 0; x < SIZE; x++)
                {
                    float dx = (x - half) / half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float falloff = Mathf.Clamp01(1f - dist);
                    // 柔らかい外周ブルーム＋中心の強い発光コアを合成
                    float alpha = Mathf.Clamp01(Mathf.Pow(falloff, 2.4f) * 0.85f + Mathf.Pow(falloff, 7.0f) * 0.65f);
                    pixels[y * SIZE + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);

            _sharedGlowSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, SIZE, SIZE),
                new Vector2(0.5f, 0.5f),
                SIZE);
            return _sharedGlowSprite;
        }

        private static void DestroyPseudoGlowRig(ref PseudoGlowRig rig)
        {
            if (rig != null && rig.Root != null)
            {
                Destroy(rig.Root.gameObject);
            }

            rig = null;
        }
    }
}
