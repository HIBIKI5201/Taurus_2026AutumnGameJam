using LitMotion;
using UnityEngine;
using UnityEngine.UI;

namespace Taurus.Runtime.UI
{
    /// <summary>
    ///     カウンターボタン（+1 / -1）の押下演出。
    ///     ボタンを一瞬沈ませて弾ませ、ボタン画像の形に沿って光る板（Taurus/UI/PressGlow シェーダー）を重ねる。
    ///     キー入力・クリックのどちらでも HUDController から Play が呼ばれる。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class CounterButtonPressEffect : MonoBehaviour
    {
        private static readonly int SOURCE_TEX_ID = Shader.PropertyToID("_SourceTex");
        private static readonly int SOURCE_UV_RECT_ID = Shader.PropertyToID("_SourceUVRect");
        private static readonly int PADDING_ID = Shader.PropertyToID("_Padding");
        private static readonly int GLOW_COLOR_ID = Shader.PropertyToID("_GlowColor");
        private static readonly int PROGRESS_ID = Shader.PropertyToID("_Progress");

        // 光らせる形の元になるボタン画像（未設定なら同じ GameObject の Image）
        [SerializeField] private Image _sourceImage;

        // Taurus/UI/PressGlow を使うマテリアル。実行時に複製して使う
        [SerializeField] private Material _glowMaterial;

        [SerializeField, ColorUsage(true, true)] private Color _glowColor = new Color(0.3f, 0.85f, 1f, 1f);

        // 光の板をボタンより何倍大きくするか（外側に光と輪郭を広げる余白）
        [SerializeField, Min(1f)] private float _glowAreaScale = 1.5f;

        [SerializeField, Min(0.05f)] private float _glowDuration = 0.55f;

        // 押した瞬間に沈む大きさ（1 = 変化なし）
        [SerializeField, Range(0.5f, 1f)] private float _pressedScale = 0.86f;

        [SerializeField, Min(0.05f)] private float _bounceDuration = 0.35f;

        private RectTransform _glowRect;
        private Material _glowInstance;
        private Vector3 _baseScale = Vector3.one;
        private MotionHandle _glowHandle;
        private MotionHandle _scaleHandle;

        /// <summary> 押下演出を最初から再生する。連打したときは前の演出を打ち切って再生し直す。 </summary>
        public void Play()
        {
            if (!EnsureGlowOverlay())
            {
                return;
            }

            UpdateSourceParameters();
            CancelMotions();

            _glowRect.gameObject.SetActive(true);
            _glowHandle = LMotion.Create(0f, 1f, _glowDuration)
                .WithEase(Ease.OutCubic)
                .WithOnComplete(() => _glowRect.gameObject.SetActive(false))
                .Bind(this, static (value, self) => self._glowInstance.SetFloat(PROGRESS_ID, value));

            transform.localScale = _baseScale * _pressedScale;
            _scaleHandle = LMotion.Create(_pressedScale, 1f, _bounceDuration)
                .WithEase(Ease.OutBack)
                .Bind(this, static (value, self) => self.transform.localScale = self._baseScale * value);
        }

        private void Awake()
        {
            if (_sourceImage == null)
            {
                _sourceImage = GetComponent<Image>();
            }

            _baseScale = transform.localScale;
        }

        private void OnDisable()
        {
            CancelMotions();
            transform.localScale = _baseScale;
            if (_glowRect != null)
            {
                _glowRect.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_glowInstance != null)
            {
                Destroy(_glowInstance);
            }
        }

        private void CancelMotions()
        {
            if (_glowHandle.IsActive())
            {
                _glowHandle.Cancel();
            }

            if (_scaleHandle.IsActive())
            {
                _scaleHandle.Cancel();
            }
        }

        // 光の板（子オブジェクト）を初回だけ作る。ボタンより手前に描き、クリック判定は持たせない
        private bool EnsureGlowOverlay()
        {
            if (_glowRect != null)
            {
                return true;
            }

            if (_glowMaterial == null || _sourceImage == null)
            {
                Debug.LogWarning($"[{nameof(CounterButtonPressEffect)}] 発光マテリアルかボタン画像が未設定です。", this);
                return false;
            }

            GameObject glowObject = new GameObject("PressGlow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _glowRect = (RectTransform)glowObject.transform;
            _glowRect.SetParent(_sourceImage.rectTransform, false);
            _glowRect.anchorMin = new Vector2(0.5f, 0.5f);
            _glowRect.anchorMax = new Vector2(0.5f, 0.5f);
            _glowRect.pivot = new Vector2(0.5f, 0.5f);
            _glowRect.anchoredPosition = Vector2.zero;

            _glowInstance = new Material(_glowMaterial);
            Image glowImage = glowObject.GetComponent<Image>();
            glowImage.material = _glowInstance;
            glowImage.raycastTarget = false;
            glowObject.SetActive(false);
            return true;
        }

        // スキン差し替えでボタン画像が変わっても追従できるよう、再生のたびに画像の範囲と板の大きさを合わせ直す
        private void UpdateSourceParameters()
        {
            Sprite sprite = _sourceImage.sprite;
            if (sprite == null)
            {
                return;
            }

            Texture texture = sprite.texture;
            Rect textureRect = sprite.textureRect;
            _glowInstance.SetTexture(SOURCE_TEX_ID, texture);
            _glowInstance.SetVector(SOURCE_UV_RECT_ID, new Vector4(
                textureRect.x / texture.width,
                textureRect.y / texture.height,
                textureRect.width / texture.width,
                textureRect.height / texture.height));
            _glowInstance.SetColor(GLOW_COLOR_ID, _glowColor);
            _glowInstance.SetFloat(PROGRESS_ID, 0f);

            // preserveAspect で実際に描かれている大きさに合わせ、その外周に余白を足す
            Vector2 drawnSize = GetDrawnSize(_sourceImage, sprite);
            float margin = Mathf.Min(drawnSize.x, drawnSize.y) * (_glowAreaScale - 1f) * 0.5f;
            Vector2 glowSize = drawnSize + Vector2.one * (margin * 2f);
            _glowRect.sizeDelta = glowSize;
            _glowInstance.SetVector(PADDING_ID, new Vector4(margin / glowSize.x, margin / glowSize.y, 0f, 0f));
        }

        private static Vector2 GetDrawnSize(Image image, Sprite sprite)
        {
            Vector2 rectSize = image.rectTransform.rect.size;
            if (!image.preserveAspect)
            {
                return rectSize;
            }

            float spriteAspect = sprite.rect.width / sprite.rect.height;
            float rectAspect = rectSize.x / rectSize.y;
            return spriteAspect > rectAspect
                ? new Vector2(rectSize.x, rectSize.x / spriteAspect)
                : new Vector2(rectSize.y * spriteAspect, rectSize.y);
        }
    }
}
