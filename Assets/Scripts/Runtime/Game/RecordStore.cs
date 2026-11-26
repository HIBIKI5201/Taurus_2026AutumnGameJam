using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Taurus.Runtime.Game
{
    /// <summary>
    /// クリアWave数のトップ3を端末ローカル（PlayerPrefs）へ永続化するストア。
    /// HTML参照版の localStorage キー（pendulum-count-records）と同名のキーを使用する。
    ///
    /// 【保存ルール】
    /// - クリア数 0 以下のランは記録に追加しない。
    /// - 同一のクリア数は重複を1件にまとめ、降順で上位3件だけを保持する。
    /// - 初回起動やデータ破損時は例外を投げず空リストへフォールバックする。
    /// </summary>
    public class RecordStore
    {
        /// <summary> PlayerPrefs の保存キー名（仕様書第10章準拠）。 </summary>
        public const string PREFS_KEY = "pendulum-count-records";

        /// <summary> 保持する最大ランキング件数。 </summary>
        public const int MAX_RECORDS = 3;

        private readonly List<int> _records = new List<int>(MAX_RECORDS);

        /// <summary> 現在のトップ3記録（降順、最大3件）。不足分はUI側で「—」を表示する。 </summary>
        public IReadOnlyList<int> Records => _records;

        /// <summary> PlayerPrefsから記録を読み込む。破損時は空リストへ安全にフォールバックする。 </summary>
        public IReadOnlyList<int> Load()
        {
            _records.Clear();
            string raw = PlayerPrefs.GetString(PREFS_KEY, string.Empty);
            List<int> parsed = ParseRecords(raw);
            _records.AddRange(parsed);
            return _records;
        }

        /// <summary> クリアWave数を追加し、重複排除・降順上位3件で保存する。0以下は無視する。 </summary>
        public IReadOnlyList<int> RecordScore(int clearedWaves)
        {
            if (clearedWaves <= 0)
            {
                return _records;
            }

            List<int> merged = MergeRecords(_records, clearedWaves);
            _records.Clear();
            _records.AddRange(merged);
            Save();
            return _records;
        }

        /// <summary> 現在の記録をPlayerPrefsへ書き込む。 </summary>
        public void Save()
        {
            string serialized = SerializeRecords(_records);
            PlayerPrefs.SetString(PREFS_KEY, serialized);
            PlayerPrefs.Save();
        }

        /// <summary> 既存記録に新しいスコアを統合し、重複排除・降順上位3件を返す純粋関数（単体テスト対象）。 </summary>
        public static List<int> MergeRecords(IReadOnlyList<int> existing, int newScore)
        {
            HashSet<int> unique = new HashSet<int>();
            if (existing != null)
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    if (existing[i] > 0)
                    {
                        unique.Add(existing[i]);
                    }
                }
            }

            if (newScore > 0)
            {
                unique.Add(newScore);
            }

            List<int> sorted = new List<int>(unique);
            sorted.Sort((a, b) => b.CompareTo(a));

            if (sorted.Count > MAX_RECORDS)
            {
                sorted.RemoveRange(MAX_RECORDS, sorted.Count - MAX_RECORDS);
            }

            return sorted;
        }

        /// <summary> JSON配列形式（例: [10,5,2]）の文字列を安全にパースする。破損文字列は空リストを返す。 </summary>
        public static List<int> ParseRecords(string raw)
        {
            List<int> result = new List<int>(MAX_RECORDS);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return result;
            }

            string trimmed = raw.Trim();
            if (!trimmed.StartsWith("[") || !trimmed.EndsWith("]"))
            {
                return result;
            }

            string inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
            if (string.IsNullOrEmpty(inner))
            {
                return result;
            }

            string[] tokens = inner.Split(',');
            HashSet<int> unique = new HashSet<int>();
            for (int i = 0; i < tokens.Length; i++)
            {
                if (int.TryParse(tokens[i].Trim(), out int value) && value > 0)
                {
                    unique.Add(value);
                }
            }

            result.AddRange(unique);
            result.Sort((a, b) => b.CompareTo(a));
            if (result.Count > MAX_RECORDS)
            {
                result.RemoveRange(MAX_RECORDS, result.Count - MAX_RECORDS);
            }

            return result;
        }

        /// <summary> 記録リストをJSON配列文字列（例: [10,5,2]）へシリアライズする。 </summary>
        public static string SerializeRecords(IReadOnlyList<int> records)
        {
            if (records == null || records.Count == 0)
            {
                return "[]";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append('[');
            int count = Math.Min(records.Count, MAX_RECORDS);
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(records[i]);
            }

            sb.Append(']');
            return sb.ToString();
        }
    }
}
