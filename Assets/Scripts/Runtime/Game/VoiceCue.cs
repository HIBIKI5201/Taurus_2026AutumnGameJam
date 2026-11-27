using System;
using System.Collections.Generic;
using UnityEngine;

namespace Taurus.Runtime.Game
{
    /// <summary>
    ///     ひとつの場面（ゲーム開始・Wave クリアなど）で鳴らすボイスの候補と、それぞれに対応するセリフ。
    ///     候補が複数あれば毎回ランダムに選び（直前と同じものは避ける）、選んだボイスのセリフを吹き出しに出す。
    ///     Audio Random Container は鳴らしたクリップを外から知れないため、セリフと声を合わせるためにここで選ぶ。
    /// </summary>
    [CreateAssetMenu(fileName = "VoiceCue", menuName = "Taurus/Voice Cue")]
    public class VoiceCue : ScriptableObject
    {
        [SerializeField] private List<Entry> _entries = new List<Entry>();

        // 音量のばらつき（± の幅）。同じ声が続いても単調に聞こえないようにする
        [SerializeField, Range(0f, 0.3f)] private float _volumeVariance = 0.05f;

        // ピッチのばらつき（± の幅）。大きくすると声質が変わるので控えめに
        [SerializeField, Range(0f, 0.1f)] private float _pitchVariance = 0.02f;

        private int _lastIndex = -1;

        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary> 候補からひとつ選ぶ。候補が無ければ false。 </summary>
        public bool TryPick(out Entry entry, out float volume, out float pitch)
        {
            entry = default;
            volume = 1f;
            pitch = 1f;
            if (_entries.Count == 0)
            {
                return false;
            }

            int index = UnityEngine.Random.Range(0, _entries.Count);
            if (_entries.Count > 1 && index == _lastIndex)
            {
                index = (index + 1 + UnityEngine.Random.Range(0, _entries.Count - 1)) % _entries.Count;
            }

            _lastIndex = index;
            entry = _entries[index];
            volume = 1f + UnityEngine.Random.Range(-_volumeVariance, _volumeVariance);
            pitch = 1f + UnityEngine.Random.Range(-_pitchVariance, _pitchVariance);
            return entry.Clip != null;
        }

        /// <summary> 候補を置き換える（主にエディタ構築ツール用）。 </summary>
        public void SetEntries(List<Entry> entries)
        {
            _entries = entries;
        }

        /// <summary> ボイス1つと、そのセリフの文字列テーブル（UIStrings）のキー。 </summary>
        [Serializable]
        public struct Entry
        {
            [SerializeField] private AudioClip _clip;

            // 空ならセリフを出さない
            [SerializeField] private string _lineKey;

            public Entry(AudioClip clip, string lineKey)
            {
                _clip = clip;
                _lineKey = lineKey;
            }

            public AudioClip Clip => _clip;

            public string LineKey => _lineKey;
        }
    }
}
