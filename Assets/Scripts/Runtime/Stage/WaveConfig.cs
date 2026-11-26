using System;
using System.Collections.Generic;
using UnityEngine;

namespace Taurus.Runtime.Stage
{
    /// <summary>
    /// 1つのWaveにおける振り子構成（単振り子／二重振り子の本数）と区間抽選重みを保持する。
    /// GameController が振り子の生成数を決め、MotionScheduleGenerator が重み抽選に参照する。
    /// </summary>
    [Serializable]
    public class WaveConfig
    {
        // 単振り子（1重：1玉）の配置本数
        [SerializeField] private int _singlePendulumCount;

        // 二重振り子（2重：親玉＋子玉の2玉）の配置本数
        [SerializeField] private int _doublePendulumCount;

        // 0〜8秒の各1秒区間で独立抽選される通過回数の候補リスト
        [SerializeField] private WeightedCountChoice[] _choices;

        /// <summary> 単振り子の本数。 </summary>
        public int SinglePendulumCount => _singlePendulumCount;

        /// <summary> 二重振り子の本数。 </summary>
        public int DoublePendulumCount => _doublePendulumCount;

        /// <summary> 振り子の合計本数（支点の数）。 </summary>
        public int TotalPendulumCount => _singlePendulumCount + _doublePendulumCount;

        /// <summary> 区間抽選候補の一覧。 </summary>
        public IReadOnlyList<WeightedCountChoice> Choices => _choices;

        /// <summary> 構成と重み候補を指定して初期化する。 </summary>
        public WaveConfig(int singlePendulumCount, int doublePendulumCount, WeightedCountChoice[] choices)
        {
            _singlePendulumCount = singlePendulumCount;
            _doublePendulumCount = doublePendulumCount;
            _choices = choices;
        }
    }
}
