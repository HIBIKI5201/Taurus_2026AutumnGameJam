using System;
using System.Collections.Generic;
using UnityEngine;

namespace Taurus.Runtime.UI
{
    /// <summary>
    ///     UIの各スロットに表示するスプライトをまとめたアセット。
    ///     本番素材に入れ替えるときは、このアセットのスプライトを差し替えるか、別のUISkinを作って UISkinApplier に設定する。
    /// </summary>
    [CreateAssetMenu(fileName = "UISkin", menuName = "Taurus/UI Skin")]
    public class UISkin : ScriptableObject
    {
        [SerializeField] private List<Entry> _entries = new List<Entry>();

        /// <summary> スロットに対応するスプライトを返す。未設定なら false。 </summary>
        public bool TryGetSprite(UISkinSlot slot, out Sprite sprite)
        {
            foreach (Entry entry in _entries)
            {
                if (entry.Slot == slot && entry.Sprite != null)
                {
                    sprite = entry.Sprite;
                    return true;
                }
            }

            sprite = null;
            return false;
        }

        /// <summary> スロットのスプライトを設定する（主にエディタ構築ツール用）。 </summary>
        public void SetSprite(UISkinSlot slot, Sprite sprite)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Slot == slot)
                {
                    _entries[i] = new Entry(slot, sprite);
                    return;
                }
            }

            _entries.Add(new Entry(slot, sprite));
        }

        /// <summary> スロットとスプライトの組。 </summary>
        [Serializable]
        private struct Entry
        {
            [SerializeField] private UISkinSlot _slot;
            [SerializeField] private Sprite _sprite;

            public Entry(UISkinSlot slot, Sprite sprite)
            {
                _slot = slot;
                _sprite = sprite;
            }

            public UISkinSlot Slot => _slot;

            public Sprite Sprite => _sprite;
        }
    }
}
