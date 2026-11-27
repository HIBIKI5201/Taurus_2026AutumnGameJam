using LitMotion;
using UnityEngine;
using UnityEngine.UI;

namespace Taurus.Runtime.UI
{
    /// <summary>
    ///     カウントの数字が変わったときの控えめな演出。
    ///     数字を少しだけ弾ませ、+1 は水色・-1 は赤みに一瞬染めて戻し、横に小さな「+1 / -1」を浮かせて消す。
    ///     目立ちすぎず、見れば増減が分かる程度にとどめる。
    /// </summary>
    [RequireComponent(typeof(Text))]
    public class CountTextEffect : MonoBehaviour
    {
        [SerializeField] private Color _plusColor = new Color(0.45f, 0.95f, 1f, 1f);
        [SerializeField] private Color _minusColor = new Color(1f, 0.45f, 0.4f, 1f);

        // 弾ませる大きさ（1 = 変化なし）
        [SerializeField, Range(1f, 1.5f)] private float _punchScale = 1.2f;
        [SerializeField, Min(0.05f)] private float _punchSeconds = 0.22f;
        [SerializeField, Min(0.05f)] private float _tintSeconds = 0.3f;

        // 浮かせる「+1 / -1」の文字。数字の右横（枠の外）から上へ流して消す
        [SerializeField] private Vector2 _floatOffset = new Vector2(215f, -30f);
        [SerializeField] private float _floatDistance = 36f;
        [SerializeField, Min(0.05f)] private float _floatSeconds = 0.45f;

        private Text _text;
        private Color _baseColor;
        private Vector3 _baseScale;
        private Text _floatText;
        private MotionHandle _punchHandle;
        private MotionHandle _tintHandle;
        private MotionHandle _floatHandle;

        /// <summary> 増減 delta に合わせて演出を最初から再生する。0 なら何もしない。 </summary>
        public void Play(int delta)
        {
            if (delta == 0)
            {
                return;
            }

            EnsureInitialized();
            Cancel();
            Color accent = delta > 0 ? _plusColor : _minusColor;

            transform.localScale = _baseScale * _punchScale;
            _punchHandle = LMotion.Create(_punchScale, 1f, _punchSeconds)
                .WithEase(Ease.OutBack)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(this, static (scale, self) => self.transform.localScale = self._baseScale * scale);

            _tintHandle = LMotion.Create(0f, 1f, _tintSeconds)
                .WithEase(Ease.OutQuad)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind((this, accent), static (t, state) => state.Item1._text.color = Color.Lerp(state.accent, state.Item1._baseColor, t));

            PlayFloatText(delta > 0 ? "+1" : "-1", accent);
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnDisable()
        {
            if (_text == null)
            {
                return;
            }

            Cancel();
            transform.localScale = _baseScale;
            _text.color = _baseColor;
            if (_floatText != null)
            {
                _floatText.gameObject.SetActive(false);
            }
        }

        // ゲーム画面が非表示のまま Awake 前に呼ばれることがあるため、初回の呼び出しで参照を用意する
        private void EnsureInitialized()
        {
            if (_text != null)
            {
                return;
            }

            _text = GetComponent<Text>();
            _baseColor = _text.color;
            _baseScale = transform.localScale;
        }

        private void PlayFloatText(string label, Color color)
        {
            if (_floatText == null)
            {
                GameObject go = new GameObject("CountDeltaText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                go.layer = gameObject.layer;
                RectTransform rect = (RectTransform)go.transform;
                rect.SetParent(transform.parent, false);
                rect.sizeDelta = new Vector2(120f, 50f);
                _floatText = go.GetComponent<Text>();
                _floatText.font = _text.font;
                _floatText.fontStyle = FontStyle.Bold;
                _floatText.fontSize = Mathf.Max(24, _text.fontSize / 3);
                Shadow shadow = go.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
                shadow.effectDistance = new Vector2(0f, -2f);
                _floatText.alignment = TextAnchor.MiddleCenter;
                _floatText.raycastTarget = false;
            }

            RectTransform floatRect = (RectTransform)_floatText.transform;
            Vector2 start = ((RectTransform)transform).anchoredPosition + _floatOffset;
            _floatText.text = label;
            _floatText.color = color;
            _floatText.gameObject.SetActive(true);
            floatRect.anchoredPosition = start;

            _floatHandle = LMotion.Create(0f, 1f, _floatSeconds)
                .WithEase(Ease.OutCubic)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(() => _floatText.gameObject.SetActive(false))
                .Bind((this, start, color), static (t, state) =>
                {
                    RectTransform rect = (RectTransform)state.Item1._floatText.transform;
                    rect.anchoredPosition = state.start + Vector2.up * (state.Item1._floatDistance * t);
                    Color c = state.color;
                    c.a = 1f - t;
                    state.Item1._floatText.color = c;
                });
        }

        private void Cancel()
        {
            if (_punchHandle.IsActive()) _punchHandle.Cancel();
            if (_tintHandle.IsActive()) _tintHandle.Cancel();
            if (_floatHandle.IsActive()) _floatHandle.Cancel();
        }
    }
}
