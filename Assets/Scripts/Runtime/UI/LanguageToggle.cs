using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace Taurus.Runtime.UI
{
    /// <summary>
    ///     タイトル画面の言語切り替え（日本語 / English）。
    ///     押したボタンの言語を LocalizationSettings.SelectedLocale に設定し、表示の切り替えは Localization パッケージに任せる。
    ///     選んだ言語は Localization 設定の PlayerPrefs セレクターで次回起動時にも引き継がれる。
    /// </summary>
    public class LanguageToggle : MonoBehaviour
    {
        [SerializeField] private Button _japaneseButton;
        [SerializeField] private Button _englishButton;

        // 選ばれている側・選ばれていない側のボタンの色
        [SerializeField] private Color _selectedColor = new Color(0.988f, 0.945f, 0.902f, 1f);
        [SerializeField] private Color _unselectedColor = new Color(0.482f, 0.333f, 0.310f, 1f);
        [SerializeField] private Color _selectedTextColor = new Color(0.482f, 0.333f, 0.310f, 1f);
        [SerializeField] private Color _unselectedTextColor = new Color(0.988f, 0.945f, 0.902f, 1f);

        private const string JAPANESE_CODE = "ja";
        private const string ENGLISH_CODE = "en";

        private void Awake()
        {
            if (_japaneseButton != null)
            {
                _japaneseButton.onClick.AddListener(SelectJapanese);
            }

            if (_englishButton != null)
            {
                _englishButton.onClick.AddListener(SelectEnglish);
            }
        }

        private void OnEnable()
        {
            LocalizationSettings.SelectedLocaleChanged += HandleSelectedLocaleChanged;
            RefreshVisuals(LocalizationSettings.SelectedLocale);
        }

        private void OnDisable()
        {
            LocalizationSettings.SelectedLocaleChanged -= HandleSelectedLocaleChanged;
        }

        private void SelectJapanese()
        {
            Select(JAPANESE_CODE);
        }

        private void SelectEnglish()
        {
            Select(ENGLISH_CODE);
        }

        private static void Select(string code)
        {
            Locale locale = LocalizationSettings.AvailableLocales.GetLocale(code);
            if (locale != null)
            {
                LocalizationSettings.SelectedLocale = locale;
            }
        }

        private void HandleSelectedLocaleChanged(Locale locale)
        {
            RefreshVisuals(locale);
        }

        // 選ばれている言語のボタンを明るい色にする
        private void RefreshVisuals(Locale locale)
        {
            bool isEnglish = locale != null && locale.Identifier.Code == ENGLISH_CODE;
            Paint(_japaneseButton, !isEnglish);
            Paint(_englishButton, isEnglish);
        }

        private void Paint(Button button, bool isSelected)
        {
            if (button == null)
            {
                return;
            }

            if (button.targetGraphic != null)
            {
                button.targetGraphic.color = isSelected ? _selectedColor : _unselectedColor;
            }

            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.color = isSelected ? _selectedTextColor : _unselectedTextColor;
            }
        }
    }
}
