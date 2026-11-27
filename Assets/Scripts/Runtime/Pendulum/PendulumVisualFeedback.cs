using UnityEngine;

namespace Taurus.Runtime.Pendulum
{
    /// <summary>
    ///     真上通過時に親玉・子玉・孫玉の HDR Emission カラーとスケールを演出する。
    ///     WebGL 環境では HDR / Bloom がブラウザ依存で弱くなるため、
    ///     加算シェーダー（Taurus/PendulumPseudoBloom）のビルボード＋ポイントライトで
    ///     PC 版と同等の白飛びコアと広域カラーブルームを疑似再現する。
    /// </summary>
    [RequireComponent(typeof(PendulumMotion))]
    public class PendulumVisualFeedback : MonoBehaviour
    {
        // 仕様書第9章準拠：真上通過時の発光持続時間（秒）
        private const float FLASH_DURATION = 0.48f;
        private const float PEAK_SCALE_MULTIPLIER = 1.45f;
        private const float DEFAULT_FLASH_INTENSITY = 6.0f;

        private static readonly int EMISSION_COLOR_ID = Shader.PropertyToID("_EmissionColor");
        private static readonly int USE_EMISSION_ID = Shader.PropertyToID("_UseEmission");
        private static readonly int EMISSION_BLEND_ID = Shader.PropertyToID("_EmissionBlend");
        private static readonly int CORE_COLOR_ID = Shader.PropertyToID("_CoreColor");
        private static readonly int HALO_COLOR_ID = Shader.PropertyToID("_HaloColor");
        private static readonly int CORE_INTENSITY_ID = Shader.PropertyToID("_CoreIntensity");
        private static readonly int INNER_INTENSITY_ID = Shader.PropertyToID("_InnerIntensity");
        private static readonly int MID_INTENSITY_ID = Shader.PropertyToID("_MidIntensity");
        private static readonly int OUTER_INTENSITY_ID = Shader.PropertyToID("_OuterIntensity");
        private static readonly int DISC_RADIUS_ID = Shader.PropertyToID("_DiscRadius");

        private static Mesh _sharedQuadMesh;

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

        [Header("WebGL 疑似 HDR ブルーム（PC 版と同等の強発光を再現）")]
        [SerializeField] private Material _pseudoBloomMaterial;
        [SerializeField] private bool _usePseudoGlowOnWebGl = true;
        [SerializeField] private bool _previewPseudoGlowInEditor;
        [SerializeField, Min(0.5f)] private float _pseudoBloomIdleScale = 3.8f;
        [SerializeField, Min(0.5f)] private float _pseudoBloomFlashScale = 15.5f;
        [SerializeField, Min(0f)] private float _pseudoLightIdleIntensity = 2.4f;
        [SerializeField, Min(0f)] private float _pseudoLightFlashIntensity = 18.0f;
        [SerializeField, Min(0.5f)] private float _pseudoLightRange = 10.0f;

        private sealed class PseudoGlowRig
        {
            public Transform Root;
            public MeshRenderer QuadRenderer;
            public Material InstanceMaterial;
            public Light PointLight;
            public float EstimatedRadius;
        }

        private PendulumMotion _motion;
        private MaterialPropertyBlock _parentPropBlock;
        private MaterialPropertyBlock _childPropBlock;
        private MaterialPropertyBlock _grandchildPropBlock;
        private Material[] _parentMaterials;
        private Material[] _childMaterials;
        private Material[] _grandchildMaterials;
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

            // URP Lit / lilToon ともに発光が確実に反映されるようインスタンスマテリアルを準備する
            _parentMaterials = PrepareBobRenderer(_parentBobMeshRenderer);
            _childMaterials = PrepareBobRenderer(_childBobMeshRenderer);
            _grandchildMaterials = PrepareBobRenderer(_grandchildBobMeshRenderer);

            if (ShouldUsePseudoGlow)
            {
                _parentGlow = CreatePseudoGlowRig("WebGLBloom_Parent", _parentBob, _parentBobMeshRenderer);
                _childGlow = CreatePseudoGlowRig("WebGLBloom_Child", _childBob, _childBobMeshRenderer);
                _grandchildGlow = CreatePseudoGlowRig("WebGLBloom_Grandchild", _grandchildBob, _grandchildBobMeshRenderer);
            }

            // 起動直後から通常時エミッションカラーを反映する
            ApplyFlashVisual(
                _parentBob,
                _parentBobMeshRenderer,
                _parentMaterials,
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
                _childMaterials,
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
                _grandchildMaterials,
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
            _grandchildMaterials = PrepareBobRenderer(_grandchildBobMeshRenderer);

            if (ShouldUsePseudoGlow && _grandchildGlow == null)
            {
                _grandchildGlow = CreatePseudoGlowRig("WebGLBloom_Grandchild", _grandchildBob, _grandchildBobMeshRenderer);
            }

            ApplyFlashVisual(
                _grandchildBob,
                _grandchildBobMeshRenderer,
                _grandchildMaterials,
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
                    _parentMaterials,
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
                    _childMaterials,
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
                    _grandchildMaterials,
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
            UpdatePseudoGlowTransform(_parentGlow, _parentBob, _parentBobMeshRenderer, cam, _parentFlashTimer / FLASH_DURATION);
            UpdatePseudoGlowTransform(_childGlow, _childBob, _childBobMeshRenderer, cam, _childFlashTimer / FLASH_DURATION);
            UpdatePseudoGlowTransform(_grandchildGlow, _grandchildBob, _grandchildBobMeshRenderer, cam, _grandchildFlashTimer / FLASH_DURATION);
        }

        private void HandleBobTopCrossed(PendulumMotion pendulum, int bobTier, int crossedCount)
        {
            if (bobTier == 2)
            {
                _grandchildFlashTimer = FLASH_DURATION;
                ApplyFlashVisual(
                    _grandchildBob,
                    _grandchildBobMeshRenderer,
                    _grandchildMaterials,
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
                    _childMaterials,
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
                    _parentMaterials,
                    _parentBobRenderer,
                    _parentPropBlock,
                    _parentGlow,
                    _parentBaseScale,
                    _parentIdleEmissionColor,
                    _parentFlashEmissionColor,
                    1f);
            }
        }

        private Material[] PrepareBobRenderer(Renderer meshRenderer)
        {
            if (meshRenderer == null)
            {
                return null;
            }

            // SkinnedMeshRenderer は updateWhenOffscreen を有効にしないと回転ボーンに bounds.center が追従しない
            if (meshRenderer is SkinnedMeshRenderer skinned && ShouldUsePseudoGlow)
            {
                skinned.updateWhenOffscreen = true;
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
                if (materials[i].HasProperty(USE_EMISSION_ID))
                {
                    materials[i].SetFloat(USE_EMISSION_ID, 1f);
                }

                if (materials[i].HasProperty(EMISSION_BLEND_ID))
                {
                    materials[i].SetFloat(EMISSION_BLEND_ID, 1f);
                }
            }

            return materials;
        }

        private void ApplyFlashVisual(
            Transform bob,
            Renderer meshRenderer,
            Material[] instanceMaterials,
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
            Color appliedEmission = currentEmission;
            if (ShouldUsePseudoGlow)
            {
                // WebGL（LDR / lilToon）でも玉本体がくすまずクリーム白〜純白発光になるよう持ち上げる
                float hasGlow = Mathf.Max(GetMaxChannel(idleEmissionColor), ease);
                if (hasGlow > 0.001f)
                {
                    Color vivid = NormalizeToVividColor(currentEmission);
                    Color warmIdle = Color.Lerp(vivid, new Color(1f, 0.99f, 0.85f, 1f), 0.55f) * 1.8f;
                    Color whiteFlash = Color.Lerp(vivid, Color.white, 0.85f) * 3.5f;
                    appliedEmission = Color.Lerp(warmIdle, whiteFlash, ease);
                    appliedEmission.a = 1f;
                }
            }

            // URP Lit / lilToon 両対応：MaterialPropertyBlock とインスタンスマテリアル双方へ反映
            if (meshRenderer != null && propBlock != null)
            {
                meshRenderer.GetPropertyBlock(propBlock);
                propBlock.SetColor(EMISSION_COLOR_ID, appliedEmission);
                meshRenderer.SetPropertyBlock(propBlock);
            }

            if (instanceMaterials != null)
            {
                for (int i = 0; i < instanceMaterials.Length; i++)
                {
                    if (instanceMaterials[i] != null && instanceMaterials[i].HasProperty(EMISSION_COLOR_ID))
                    {
                        instanceMaterials[i].SetColor(EMISSION_COLOR_ID, appliedEmission);
                    }
                }
            }

            // WebGL 疑似 HDR ブルームを更新
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

            Material bloomMat = CreatePseudoBloomMaterialInstance();
            if (bloomMat == null)
            {
                return null;
            }

            GameObject rootGo = new GameObject(rigName);
            rootGo.transform.SetParent(transform, false);

            MeshFilter mf = rootGo.AddComponent<MeshFilter>();
            mf.sharedMesh = GetOrCreateQuadMesh();

            MeshRenderer mr = rootGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = bloomMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            Light pointLight = rootGo.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.shadows = LightShadows.None;
            pointLight.range = _pseudoLightRange;
            pointLight.intensity = _pseudoLightIdleIntensity;

            return new PseudoGlowRig
            {
                Root = rootGo.transform,
                QuadRenderer = mr,
                InstanceMaterial = bloomMat,
                PointLight = pointLight,
                EstimatedRadius = 0.45f
            };
        }

        private Material CreatePseudoBloomMaterialInstance()
        {
            if (_pseudoBloomMaterial != null)
            {
                return new Material(_pseudoBloomMaterial);
            }

            Shader shader = Shader.Find("Taurus/PendulumPseudoBloom");
            if (shader != null)
            {
                return new Material(shader);
            }

            Debug.LogWarning($"[{nameof(PendulumVisualFeedback)}] 疑似ブルーム用マテリアル／シェーダーが見つかりません。", this);
            return null;
        }

        private void UpdatePseudoGlowVisual(PseudoGlowRig rig, Color idleEmission, Color flashEmission, float ease)
        {
            float idleStrength = Mathf.Clamp01(GetMaxChannel(idleEmission));
            float activeWeight = Mathf.Max(idleStrength, ease);

            if (rig.QuadRenderer != null)
            {
                rig.QuadRenderer.enabled = activeWeight > 0.001f;
            }

            if (rig.InstanceMaterial != null)
            {
                Color idleVivid = idleStrength > 0.001f ? NormalizeToVividColor(idleEmission) : NormalizeToVividColor(flashEmission);
                Color flashVivid = NormalizeToVividColor(flashEmission);
                Color haloColor = Color.Lerp(idleVivid, flashVivid, ease);
                Color coreColor = Color.Lerp(
                    Color.Lerp(idleVivid, new Color(1f, 0.99f, 0.88f, 1f), 0.72f),
                    Color.white,
                    ease);

                // 通常時（画像1相当）：玉を明るくクリーム白に光らせつつ柔らかな光輪
                // フラッシュ時（画像2相当）：純白の強い芯＋画面へ大きく広がる鮮やかカラーブルーム
                float coreIntensity = Mathf.Lerp(0.85f * idleStrength, 2.6f, ease);
                float innerIntensity = Mathf.Lerp(0.55f * idleStrength, 2.2f, ease);
                float midIntensity = Mathf.Lerp(0.32f * idleStrength, 1.85f, ease);
                float outerIntensity = Mathf.Lerp(0.14f * idleStrength, 0.95f, ease);
                float discRadius = Mathf.Lerp(0.24f, 0.11f, ease);

                rig.InstanceMaterial.SetColor(CORE_COLOR_ID, coreColor);
                rig.InstanceMaterial.SetColor(HALO_COLOR_ID, haloColor);
                rig.InstanceMaterial.SetFloat(CORE_INTENSITY_ID, coreIntensity);
                rig.InstanceMaterial.SetFloat(INNER_INTENSITY_ID, innerIntensity);
                rig.InstanceMaterial.SetFloat(MID_INTENSITY_ID, midIntensity);
                rig.InstanceMaterial.SetFloat(OUTER_INTENSITY_ID, outerIntensity);
                rig.InstanceMaterial.SetFloat(DISC_RADIUS_ID, discRadius);
            }

            if (rig.PointLight != null)
            {
                Color lightColor = Color.Lerp(
                    idleStrength > 0.001f ? NormalizeToVividColor(idleEmission) : NormalizeToVividColor(flashEmission),
                    NormalizeToVividColor(flashEmission),
                    ease);
                rig.PointLight.color = lightColor;
                rig.PointLight.intensity = Mathf.Lerp(_pseudoLightIdleIntensity * idleStrength, _pseudoLightFlashIntensity, ease);
                rig.PointLight.range = _pseudoLightRange;
            }
        }

        private void UpdatePseudoGlowTransform(
            PseudoGlowRig rig,
            Transform bob,
            Renderer meshRenderer,
            UnityEngine.Camera cam,
            float normalizedRemaining)
        {
            if (rig == null || rig.Root == null)
            {
                return;
            }

            Vector3 worldPos;
            if (meshRenderer != null)
            {
                Bounds bounds = meshRenderer.bounds;
                worldPos = bounds.center;
                Vector3 ext = bounds.extents;
                float avgExt = (ext.x + ext.y + ext.z) / 3f;
                if (avgExt > 0.1f && avgExt < 2.0f)
                {
                    rig.EstimatedRadius = Mathf.Clamp(avgExt, 0.35f, 0.85f);
                }
            }
            else if (bob != null)
            {
                worldPos = bob.position;
            }
            else
            {
                return;
            }

            float ease = Mathf.Clamp01(normalizedRemaining) * Mathf.Clamp01(normalizedRemaining);
            float quadDiameter = rig.EstimatedRadius * Mathf.Lerp(_pseudoBloomIdleScale, _pseudoBloomFlashScale, ease);
            rig.Root.localScale = Vector3.one * quadDiameter;

            if (cam != null)
            {
                Vector3 toCam = (cam.transform.position - worldPos).normalized;
                rig.Root.position = worldPos + toCam * (rig.EstimatedRadius * 0.35f);
                rig.Root.rotation = cam.transform.rotation;
            }
            else
            {
                rig.Root.position = worldPos;
            }
        }

        private static float GetMaxChannel(Color color)
        {
            return Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        }

        private static Color NormalizeToVividColor(Color hdrColor)
        {
            float maxChannel = GetMaxChannel(hdrColor);
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

        private static Mesh GetOrCreateQuadMesh()
        {
            if (_sharedQuadMesh != null)
            {
                return _sharedQuadMesh;
            }

            Mesh mesh = new Mesh { name = "PendulumPseudoBloomQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            _sharedQuadMesh = mesh;
            return _sharedQuadMesh;
        }

        private static void DestroyPseudoGlowRig(ref PseudoGlowRig rig)
        {
            if (rig == null)
            {
                return;
            }

            if (rig.InstanceMaterial != null)
            {
                Destroy(rig.InstanceMaterial);
            }

            if (rig.Root != null)
            {
                Destroy(rig.Root.gameObject);
            }

            rig = null;
        }
    }
}
