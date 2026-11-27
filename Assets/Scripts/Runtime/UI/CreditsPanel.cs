using System;
using System.Text;
using LitMotion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Taurus.Runtime.UI
{
    /// <summary>
    ///     タイトル画面から開くクレジット画面。CreditsData から役職と名前を2列に並べて表示する。
    ///     閉じるボタン・Space・Esc で閉じる。開いている間は GameController のキー入力を止める（HUDController が中継する）。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class CreditsPanel : MonoBehaviour
    {
        private const float FADE_SECONDS = 0.2f;

        [SerializeField] private CreditsData _data;
        [SerializeField] private Text _leftColumnText;
        [SerializeField] private Text _rightColumnText;
        [SerializeField] private Button _closeButton;

        // 役職名の文字色（リッチテキストの color 指定に使う）
        [SerializeField] private Color _roleColor = new Color(0.29f, 0.63f, 0.76f, 1f);

        private CanvasGroup _group;
        private MotionHandle _fadeHandle;
        private bool _isOpen;

        /// <summary> 開いたとき true、閉じ終わったとき false で発行される。 </summary>
        public event Action<bool> OnVisibilityChanged;

        public bool IsOpen => _isOpen;

        /// <summary> クレジット画面を開く。 </summary>
        public void Open()
        {
            if (_isOpen)
            {
                return;
            }

            _isOpen = true;
            gameObject.SetActive(true);
            _group.blocksRaycasts = true;
            Fade(0f, 1f, null);
            OnVisibilityChanged?.Invoke(true);
        }

        /// <summary> クレジット画面を閉じる。 </summary>
        public void Close()
        {
            if (!_isOpen)
            {
                return;
            }

            _isOpen = false;
            _group.blocksRaycasts = false;
            Fade(_group.alpha, 0f, () => gameObject.SetActive(false));
            OnVisibilityChanged?.Invoke(false);
        }

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(Close);
            }

            BuildColumns();
        }

        private void Update()
        {
            if (!_isOpen || Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                Close();
            }
        }

        private void OnDisable()
        {
            if (_fadeHandle.IsActive())
            {
                _fadeHandle.Cancel();
            }
        }

        private void Fade(float from, float to, Action onComplete)
        {
            if (_fadeHandle.IsActive())
            {
                _fadeHandle.Cancel();
            }

            _group.alpha = from;
            _fadeHandle = LMotion.Create(from, to, FADE_SECONDS)
                .WithEase(Ease.OutQuad)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(() => onComplete?.Invoke())
                .Bind(_group, static (alpha, group) => group.alpha = alpha);
        }

        // 役職ごとのまとまりを崩さないよう、行数がなるべく均等になる位置で左右の列に分ける
        private void BuildColumns()
        {
            if (_data == null || _data.Sections.Count == 0)
            {
                return;
            }

            int totalLines = 0;
            foreach (CreditsData.Section section in _data.Sections)
            {
                totalLines += GetLineCount(section);
            }

            StringBuilder left = new StringBuilder();
            StringBuilder right = new StringBuilder();
            int leftLines = 0;
            string roleColor = ColorUtility.ToHtmlStringRGB(_roleColor);
            foreach (CreditsData.Section section in _data.Sections)
            {
                bool isLeft = right.Length == 0 && leftLines < totalLines / 2;
                StringBuilder target = isLeft ? left : right;
                if (target.Length > 0)
                {
                    target.Append('\n');
                }

                target.Append($"<color=#{roleColor}><size={_leftColumnText.fontSize * 3 / 4}>{section.Role}</size></color>\n");
                foreach (string memberName in section.Names)
                {
                    target.Append(memberName).Append('\n');
                }

                if (isLeft)
                {
                    leftLines += GetLineCount(section);
                }
            }

            if (_leftColumnText != null)
            {
                _leftColumnText.text = left.ToString().TrimEnd('\n');
            }

            if (_rightColumnText != null)
            {
                _rightColumnText.text = right.ToString().TrimEnd('\n');
            }
        }

        // 役職名1行・名前・区切りの空行1行
        private static int GetLineCount(CreditsData.Section section)
        {
            return section.Names.Count + 2;
        }
    }
}
