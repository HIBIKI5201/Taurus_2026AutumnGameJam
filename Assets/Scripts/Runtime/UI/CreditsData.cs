using System;
using System.Collections.Generic;
using UnityEngine;

namespace Taurus.Runtime.UI
{
    /// <summary>
    ///     クレジット画面に載せる役職と名前の一覧。
    ///     Notion の「開発メンバー」データベースの内容を写したもの。メンバーが変わったらこのアセットを直す。
    /// </summary>
    [CreateAssetMenu(fileName = "CreditsData", menuName = "Taurus/Credits Data")]
    public class CreditsData : ScriptableObject
    {
        [SerializeField] private List<Section> _sections = new List<Section>();

        /// <summary> 表示順に並んだ役職ごとの一覧。 </summary>
        public IReadOnlyList<Section> Sections => _sections;

        /// <summary> 役職ごとの一覧を置き換える（主にエディタ構築ツール用）。 </summary>
        public void SetSections(List<Section> sections)
        {
            _sections = sections;
        }

        /// <summary> 1つの役職と、その役職のメンバー名。 </summary>
        [Serializable]
        public class Section
        {
            [SerializeField] private string _role;
            [SerializeField] private List<string> _names = new List<string>();

            public Section(string role, List<string> names)
            {
                _role = role;
                _names = names;
            }

            public string Role => _role;

            public IReadOnlyList<string> Names => _names;
        }
    }
}
