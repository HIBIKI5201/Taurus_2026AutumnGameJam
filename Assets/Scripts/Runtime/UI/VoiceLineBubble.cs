using LitMotion;
using Taurus.Runtime.Game;
using TMPro;
using UnityEngine;

namespace Taurus.Runtime.UI
{
    /// <summary>
    ///     ボイスが鳴ったとき、そのボイスに対応するセリフを吹き出しに出す。
    ///     画面（パネル）ごとに置き、表示中の画面の吹き出しだけが反応する。セリフが空のボイスでは出さない。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class VoiceLineBubble : MonoBehaviour
    {
        [SerializeField] private TMP_Text _lineText;

        // ボイスが終わってからも少し残しておく秒数
        [SerializeField, Min(0f)] private float _extraHoldSeconds = 0.8f;
        [SerializeField, Min(0.01f)] private float _fadeSeconds = 0.15f;

        private CanvasGroup _group;
        private Vector3 _baseScale;
        private float _hideAt;
        private bool _isShowing;
        private MotionHandle _showHandle;
        private MotionHandle _hideHandle;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            _baseScale = transform.localScale;
            _group.alpha = 0f;
        }

        private void OnEnable()
        {
            GameAudioHooks.OnVoiceLinePlayed += HandleVoiceLinePlayed;
        }

        private void OnDisable()
        {
            GameAudioHooks.OnVoiceLinePlayed -= HandleVoiceLinePlayed;
            CancelMotions();
            _isShowing = false;
            if (_group != null)
            {
                _group.alpha = 0f;
            }
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
            _hideAt = Time.unscaledTime + voiceSeconds + _extraHoldSeconds;
            _isShowing = true;

            // ぽんと出す：少し小さい所から弾ませ、同時にフェードイン
            CancelMotions();
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

        private void Hide()
        {
            _isShowing = false;
            CancelMotions();
            _hideHandle = LMotion.Create(_group.alpha, 0f, _fadeSeconds)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(_group, static (a, g) => g.alpha = a);
        }

        private void CancelMotions()
        {
            if (_showHandle.IsActive()) _showHandle.Cancel();
            if (_hideHandle.IsActive()) _hideHandle.Cancel();
        }
    }
}
