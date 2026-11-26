using System;
using System.Collections.Generic;
using UnityEngine;

namespace Taurus.Runtime.Stage
{
    /// <summary>
    /// 期間 [s, e] を分割点 p1..pn で分けた 1 区間（長さ L_k）の抽選テーブルと量制約 [Q_min, Q_max] を保持する。
    /// </summary>
    [Serializable]
    public class StageIntervalConfig
    {
        // エディタ上での識別ラベル（例: "序盤", "山場", "終盤"）
        [SerializeField] private string _label = "区間";

        // この区間の長さ L_k（秒）
        [SerializeField, Min(0.1f)] private float _durationSeconds = 8f;

        // true のとき、累積量 q が [Q_min, Q_max] に収まるよう前向きDP評価・後ろ向きDP抽選制御を適用する
        [SerializeField] private bool _useQuantityConstraint;

        // 量の最小値制約 Q_min
        [SerializeField, Min(0)] private int _minQuantity;

        // 量の最大値制約 Q_max
        [SerializeField, Min(0)] private int _maxQuantity = 16;

        // この区間の抽選テーブル（量 q_i・重み w_i・時間 t_i）
        [SerializeField] private WeightedCountChoice[] _choices;

        /// <summary> 区間ラベル。 </summary>
        public string Label => string.IsNullOrWhiteSpace(_label) ? "区間" : _label;

        /// <summary> 区間長 L_k（秒）。 </summary>
        public float DurationSeconds => Mathf.Max(0.1f, _durationSeconds);

        /// <summary> 量制約 [Q_min, Q_max] を有効にするか。 </summary>
        public bool UseQuantityConstraint => _useQuantityConstraint;

        /// <summary> 最小値制約 Q_min。 </summary>
        public int MinQuantity => Mathf.Max(0, _minQuantity);

        /// <summary> 最大値制約 Q_max（Q_min 未満にならないよう補正）。 </summary>
        public int MaxQuantity => Mathf.Max(MinQuantity, _maxQuantity);

        /// <summary> この区間の抽選候補一覧。 </summary>
        public IReadOnlyList<WeightedCountChoice> Choices => _choices;

        /// <summary> 区間設定を初期化する。 </summary>
        public StageIntervalConfig(
            string label,
            float durationSeconds,
            WeightedCountChoice[] choices,
            bool useQuantityConstraint = false,
            int minQuantity = 0,
            int maxQuantity = 16)
        {
            _label = label;
            _durationSeconds = durationSeconds;
            _choices = choices;
            _useQuantityConstraint = useQuantityConstraint;
            _minQuantity = minQuantity;
            _maxQuantity = maxQuantity;
        }
    }
}
