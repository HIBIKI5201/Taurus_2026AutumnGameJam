using LitMotion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Taurus.Runtime.UI
{
    /// <summary>
    ///     ボタンの押下演出。
    ///     マウスを乗せると少し大きくなり、押している間は沈んだまま止まり、離した瞬間（Play）に弾んで
    ///     ボタン画像の形に沿って光る板（Taurus/UI/PressGlow シェーダー）を重ねる。
    ///     キー入力・クリックのどちらでも HUDController から Play が呼ばれる。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ButtonPressEffect : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private static readonly int SOURCE_TEX_ID = Shader.PropertyToID("_SourceTex");
        private static readonly int SOURCE_UV_RECT_ID = Shader.PropertyToID("_SourceUVRect");
        private static readonly int PADDING_ID = Shader.PropertyToID("_Padding");
        private static readonly int GLOW_COLOR_ID = Shader.PropertyToID("_GlowColor");
        private static readonly int PROGRESS_ID = Shader.PropertyToID("_Progress");

        // 押す・乗せるで大きさが変わるまでの時間（秒）
        private const float STATE_SCALE_DURATION = 0.08f;

        // 光らせる形の元になるボタン画像（未設定なら同じ GameObject の Image）
        [SerializeField] private Image _sourceImage;

        // Taurus/UI/PressGlow を使うマテリアル。実行時に複製して使う
        [SerializeField] private Material _glowMaterial;

        [SerializeField, ColorUsage(true, true)] private Color _glowColor = new Color(0.3f, 0.85f, 1f, 1f);

        // 光の板をボタンより何倍大きくするか（外側に光と輪郭を広げる余白）
        [SerializeField, Min(1f)] private float _glowAreaScale = 1.5f;

        [SerializeField, Min(0.05f)] private float _glowDuration = 0.55f;

        // 押している間の大きさ（1 = 変化なし）
        [SerializeField, Range(0.5f, 1f)] private float _pressedScale = 0.86f;

        // マウスを乗せている間の大きさ（1 = 変化なし）
        [SerializeField, Range(1f, 1.3f)] private float _hoverScale = 1.05f;

        [SerializeField, Min(0.05f)] private float _bounceDuration = 0.35f;

        private RectTransform _glowRect;
        private Material _glowInstance;
        private Vector3 _baseScale = Vector3.one;
        private float _currentScale = 1f;
        private bool _isPointerOver;
        private bool _isPointerDown;
        private MotionHandle _glowHandle;
        private MotionHandle _scaleHandle;

        /// <summary> 離した瞬間の演出（沈んだ状態から弾み、光る）を最初から再生する。連打したときは前の演出を打ち切る。 </summary>
        public void Play()
        {
            CancelScaleMotion();
            float from = Mathf.Min(_currentScale, _pressedScale);
            _scaleHandle = LMotion.Create(from, GetRestingScale(), _bounceDuration)
                .WithEase(Ease.OutBack)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(this, static (value, self) => self.SetScale(value));

            if (!EnsureGlowOverlay())
            {
                return;
            }

            UpdateSourceParameters();
            if (_glowHandle.IsActive())
            {
                _glowHandle.Cancel();
            }

            _glowRect.gameObject.SetActive(true);
            _glowHandle = LMotion.Create(0f, 1f, _glowDuration)
                .WithEase(Ease.OutCubic)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(() => _glowRect.gameObject.SetActive(false))
                .Bind(this, static (value, self) => self._glowInstance.SetFloat(PROGRESS_ID, value));
        }

        /// <summary> マウスで押し込んだ。離すまで沈んだまま止める。 </summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            _isPointerDown = true;
            AnimateToScale(_pressedScale);
        }

        /// <summary> 離した。ボタンの上で離したときの弾む演出は、クリック処理から Play が呼ばれて始まる。 </summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            _isPointerDown = false;
            if (!_isPointerOver)
            {
                AnimateToScale(GetRestingScale());
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isPointerOver = true;
            if (!_isPointerDown)
            {
                AnimateToScale(GetRestingScale());
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isPointerOver = false;
            AnimateToScale(_isPointerDown ? _pressedScale : GetRestingScale());
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
            CancelScaleMotion();
            if (_glowHandle.IsActive())
            {
                _glowHandle.Cancel();
            }

            _isPointerOver = false;
            _isPointerDown = false;
            SetScale(1f);
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

        private float GetRestingScale()
        {
            return _isPointerOver ? _hoverScale : 1f;
        }

        private void SetScale(float scale)
        {
            _currentScale = scale;
            transform.localScale = _baseScale * scale;
        }

        private void AnimateToScale(float target)
        {
            CancelScaleMotion();
            _scaleHandle = LMotion.Create(_currentScale, target, STATE_SCALE_DURATION)
                .WithEase(Ease.OutQuad)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(this, static (value, self) => self.SetScale(value));
        }

        private void CancelScaleMotion()
        {
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
                Debug.LogWarning($"[{nameof(ButtonPressEffect)}] 発光マテリアルかボタン画像が未設定です。", this);
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
