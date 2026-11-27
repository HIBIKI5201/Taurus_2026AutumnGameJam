using UnityEngine;
using UnityEngine.UI;

namespace Taurus.Runtime.UI
{
    /// <summary>
    ///     インゲームの少女の立ち絵。カウントのボタン（キー・クリック）が押されるたびに、
    ///     「カチッ」と押した差分へ一瞬で切り替え、少し下へずらしたまま止めてから元に戻す（補間しないコマ送り）。
    ///     Wave 成功時は喜びの差分を出し、次の Wave 開始で通常に戻る。
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class CharacterPortrait : MonoBehaviour
    {
        [SerializeField] private Sprite _idleSprite;
        [SerializeField] private Sprite _clickSprite;
        [SerializeField] private Sprite _clearSprite;

        // 押した差分を出しておく秒数
        [SerializeField, Min(0.02f)] private float _clickHoldSeconds = 0.12f;

        // 押した差分の間だけ下へずらす量（UI 座標）。補間せずに一段で動かす
        [SerializeField] private float _clickDropPixels = 8f;

        private Image _image;
        private RectTransform _rect;
        private Vector2 _basePosition;
        private float _clickRemaining;
        private bool _isClear;

        /// <summary> ボタンを押した差分を最初から出し直す。 </summary>
        public void PlayClick()
        {
            if (_isClear || _clickSprite == null)
            {
                return;
            }

            _clickRemaining = _clickHoldSeconds;
            _image.sprite = _clickSprite;
            _rect.anchoredPosition = _basePosition + Vector2.down * _clickDropPixels;
        }

        /// <summary> Wave 成功の差分を出す。ShowIdle まで押下の差分は出さない。 </summary>
        public void ShowClear()
        {
            _isClear = true;
            _clickRemaining = 0f;
            _rect.anchoredPosition = _basePosition;
            if (_clearSprite != null)
            {
                _image.sprite = _clearSprite;
            }
        }

        /// <summary> 通常の立ち絵に戻す。 </summary>
        public void ShowIdle()
        {
            _isClear = false;
            _clickRemaining = 0f;
            _rect.anchoredPosition = _basePosition;
            if (_idleSprite != null)
            {
                _image.sprite = _idleSprite;
            }
        }

        private void Awake()
        {
            _image = GetComponent<Image>();
            _rect = (RectTransform)transform;
            _basePosition = _rect.anchoredPosition;
        }

        private void OnEnable()
        {
            ShowIdle();
        }

        private void Update()
        {
            if (_clickRemaining <= 0f)
            {
                return;
            }

            _clickRemaining -= Time.unscaledDeltaTime;
            if (_clickRemaining <= 0f && !_isClear)
            {
                ShowIdle();
            }
        }
    }
}
