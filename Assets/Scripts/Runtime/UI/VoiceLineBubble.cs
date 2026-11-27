using LitMotion;
using Taurus.Runtime.Game;
using TMPro;
using UnityEngine;

namespace Taurus.Runtime.UI
{
    /// <summary>
    ///     ボイスが鳴ったときだけ吹き出しを表示し、文字数とボイス長に応じた時間のあいだセリフを出す。
    ///     SpeechBubble 自身に付け、親パネルや他 UI には一切影響しない。セリフが空のボイスでは出さない。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class VoiceLineBubble : MonoBehaviour
    {
        [SerializeField] private TMP_Text _lineText;

        // 文字数に応じた表示時間の計算用（基準秒数 + 1文字あたりの秒数）
        [SerializeField, Min(0f)] private float _baseDisplaySeconds = 1.0f;
        [SerializeField, Min(0f)] private float _secondsPerCharacter = 0.08f;
        [SerializeField, Min(0f)] private float _minDisplaySeconds = 1.5f;
        [SerializeField, Min(0f)] private float _maxDisplaySeconds = 6.0f;

        // ボイスが終わってからも少し残しておく秒数（文字数計算よりボイスの方が長いときの下限）
        [SerializeField, Min(0f)] private float _extraHoldSeconds = 0.4f;
        [SerializeField, Min(0.01f)] private float _fadeSeconds = 0.15f;

        // 9-slice 吹き出しを文字数・テキストサイズに合わせて自動リサイズする設定
        [SerializeField] private bool _autoResizeToText = true;
        [SerializeField, Min(0f)] private float _horizontalPadding = 84f;
        [SerializeField, Min(0f)] private float _verticalPadding = 28f;
        [SerializeField, Min(0f)] private float _minWidth = 180f;
        [SerializeField, Min(0f)] private float _maxWidth = 760f;
        [SerializeField, Min(0f)] private float _minHeight = 88f;
        [SerializeField, Min(0f)] private float _maxHeight = 168f;

        private RectTransform _rectTransform;
        private CanvasGroup _group;
        private Vector3 _baseScale;
        private float _hideAt;
        private bool _isShowing;
        private MotionHandle _showHandle;
        private MotionHandle _hideHandle;

        private void Awake()
        {
            _rectTransform = transform as RectTransform;
            _group = GetComponent<CanvasGroup>();
            _baseScale = transform.localScale;
            HideImmediate();
        }

        private void OnEnable()
        {
            if (_group == null)
            {
                _group = GetComponent<CanvasGroup>();
            }

            HideImmediate();
            GameAudioHooks.OnVoiceLinePlayed += HandleVoiceLinePlayed;
        }

        private void OnDisable()
        {
            GameAudioHooks.OnVoiceLinePlayed -= HandleVoiceLinePlayed;
            HideImmediate();
        }

        private void Update()
        {
            if (_isShowing && Time.unscaledTime >= _hideAt)
            {
                Hide();
            }
        }

        private void HandleVoiceLinePlayed(string lineKey, float voiceSeconds)
        {
            if (string.IsNullOrEmpty(lineKey) || _lineText == null)
            {
                return;
            }

            string line = L10n.Get(lineKey);
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            _lineText.text = line;
            if (_autoResizeToText)
            {
                ResizeToFitText(line);
            }

            float displaySeconds = CalculateDisplaySeconds(line, voiceSeconds);
            _hideAt = Time.unscaledTime + displaySeconds;
            _isShowing = true;

            // ぽんと出す：吹き出し自身だけを少し小さい所から弾ませ、同時にフェードイン
            CancelMotions();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            transform.localScale = _baseScale * 0.85f;
            _showHandle = LMotion.Create(0f, 1f, 0.22f)
                .WithEase(Ease.OutBack)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(this, static (t, self) =>
                {
                    self.transform.localScale = self._baseScale * Mathf.LerpUnclamped(0.85f, 1f, t);
                    self._group.alpha = Mathf.Clamp01(t * 2f);
                });
        }

        private void ResizeToFitText(string line)
        {
            if (_rectTransform == null)
            {
                _rectTransform = transform as RectTransform;
                if (_rectTransform == null)
                {
                    return;
                }
            }

            float maxWidth = Mathf.Max(_minWidth, _maxWidth);
            float maxHeight = Mathf.Max(_minHeight, _maxHeight);
            float maxTextWidth = Mathf.Max(40f, maxWidth - _horizontalPadding);

            if (_lineText.enableAutoSizing)
            {
                _lineText.fontSize = _lineText.fontSizeMax;
            }

            Vector2 preferred = _lineText.GetPreferredValues(line, maxTextWidth, Mathf.Infinity);
            float targetWidth = Mathf.Clamp(preferred.x + _horizontalPadding, _minWidth, maxWidth);
            float targetHeight = Mathf.Clamp(preferred.y + _verticalPadding, _minHeight, maxHeight);
            _rectTransform.sizeDelta = new Vector2(targetWidth, targetHeight);
        }

        private float CalculateDisplaySeconds(string line, float voiceSeconds)
        {
            int charCount = string.IsNullOrEmpty(line) ? 0 : line.Trim().Length;
            float byLength = _baseDisplaySeconds + charCount * _secondsPerCharacter;
            float byVoice = Mathf.Max(0f, voiceSeconds) + _extraHoldSeconds;
            float duration = Mathf.Max(byLength, byVoice);
            float maxSeconds = Mathf.Max(_minDisplaySeconds, _maxDisplaySeconds);
            return Mathf.Clamp(duration, _minDisplaySeconds, maxSeconds);
        }

        private void Hide()
        {
            _isShowing = false;
            CancelMotions();
            _hideHandle = LMotion.Create(_group.alpha, 0f, _fadeSeconds)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(() =>
                {
                    if (_group != null)
                    {
                        _group.alpha = 0f;
                    }
                })
                .Bind(_group, static (a, g) => g.alpha = a);
        }

        private void HideImmediate()
        {
            CancelMotions();
            _isShowing = false;
            if (_group != null)
            {
                _group.alpha = 0f;
                _group.blocksRaycasts = false;
                _group.interactable = false;
            }

            transform.localScale = _baseScale;
        }

        private void CancelMotions()
        {
            if (_showHandle.IsActive()) _showHandle.Cancel();
            if (_hideHandle.IsActive()) _hideHandle.Cancel();
        }
    }
}
