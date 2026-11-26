using System;
using System.Collections.Generic;
using SymphonyFrameWork.System.SaveSystem;
using UnityEngine;

namespace Taurus.Runtime.Game
{
    /// <summary> SymphonyFramework の SaveStore で永続化するトップ3クリア記録データ。 </summary>
    [Serializable]
    public sealed class PendulumRecordsSaveData : SaveDataContent
    {
        [SerializeField] private List<int> _records = new List<int>();

        /// <summary> 保存されているトップ3記録リスト。 </summary>
        public IReadOnlyList<int> Records => _records;

        /// <summary> 記録リストの内容を更新する。 </summary>
        public void SetRecords(IReadOnlyList<int> records)
        {
            _records ??= new List<int>();
            _records.Clear();

            if (records == null)
            {
                return;
            }

            for (int i = 0; i < records.Count; i++)
            {
                _records.Add(records[i]);
            }
        }
    }
}
