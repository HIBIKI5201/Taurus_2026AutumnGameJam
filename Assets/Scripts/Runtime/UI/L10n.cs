using UnityEngine.Localization.Settings;

namespace Taurus.Runtime.UI
{
    /// <summary>
    ///     コードで組み立てる文言を、Localization の文字列テーブル（UIStrings）から取り出す窓口。
    ///     シーン上の固定の文言は LocalizeStringEvent で直接テーブルに結び付けているので、ここはコード側だけで使う。
    /// </summary>
    public static class L10n
    {
        /// <summary> 文字列テーブルの名前。 </summary>
        public const string TABLE = "UIStrings";

        /// <summary> 今の言語での文言を返す。 </summary>
        public static string Get(string key)
        {
            return LocalizationSettings.StringDatabase.GetLocalizedString(TABLE, key);
        }

        /// <summary> 今の言語での文言に、string.Format で値を差し込んで返す（{0}, {1:00} など）。 </summary>
        public static string Format(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }
    }
}
