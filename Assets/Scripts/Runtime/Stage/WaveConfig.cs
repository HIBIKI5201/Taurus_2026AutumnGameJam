using System;
using System.Collections.Generic;
using UnityEngine;

namespace Taurus.Runtime.Stage
{
    /// <summary>
    /// 1つのWaveにおける振り子構成、期間 [s, e]、分割区間（長さ L_k・量制約 [Q_min, Q_max]）、
    /// およびDP評価用の離散化・終了ルール設定を保持する。
    ///
    /// 【後方互換性】
    /// - _intervals が空の場合は、従来の _choices を期間 [s, e] の単一区間として自動解釈するため、
    ///   既存のアセット・テストを壊さずに段階的に複数区間・制約設計へ移行できる。
    /// </summary>
    [Serializable]
    public class WaveConfig
    {
        // 単振り子（1重：1玉）の配置本数
        [SerializeField, Min(0)] private int _singlePendulumCount;

        // 二重振り子（2重：親玉＋子玉の2玉）の配置本数
        [SerializeField, Min(0)] private int _doublePendulumCount;

        [Header("期間・DP設定")]
        // 評価・抽選期間の開始時刻 s（秒）
        [SerializeField, Min(0f)] private float _startTimeSeconds = 0f;

        // 評価・抽選期間の終了時刻 e（秒）。複数区間指定時は区間長合計が優先される
        [SerializeField, Min(0.1f)] private float _endTimeSeconds = 8f;

        // 抽選終了ルール（残り時間に収まる候補があれば継続 / 初回超過で停止）
        [SerializeField] private DrawStopRule _stopRule = DrawStopRule.StopWhenNoFit;

        // DPの時間離散化ステップ Δt（秒）。1秒単位の候補なら 1.0 または 0.5 を推奨
        [SerializeField, Range(0.1f, 2.0f)] private float _timeQuantizationStep = 1.0f;

        [Header("分割区間（空の場合は下の単一テーブルを使用）")]
        // 期間 [s, e] を分割点 p1..pn で分けた区間設定の配列
        [SerializeField] private StageIntervalConfig[] _intervals;

        [Header("単一区間フォールバック用（従来互換）")]
        // _intervals が未設定のときに使われる抽選候補リスト
        [SerializeField] private WeightedCountChoice[] _choices;

        /// <summary> 単振り子の本数。 </summary>
        public int SinglePendulumCount => _singlePendulumCount;

        /// <summary> 二重振り子の本数。 </summary>
        public int DoublePendulumCount => _doublePendulumCount;

        /// <summary> 振り子の合計本数（支点の数）。 </summary>
        public int TotalPendulumCount => _singlePendulumCount + _doublePendulumCount;

        /// <summary> カウント対象となる玉の合計数（単振り子1玉＋二重振り子2玉）。 </summary>
        public int TotalBobCount => _singlePendulumCount + _doublePendulumCount * 2;

        /// <summary> 期間開始時刻 s（秒）。 </summary>
        public float StartTimeSeconds => Mathf.Max(0f, _startTimeSeconds);

        /// <summary> 期間終了時刻 e（秒）。 </summary>
        public float EndTimeSeconds => Mathf.Max(StartTimeSeconds + 0.1f, _endTimeSeconds);

        /// <summary> 抽選終了ルール。 </summary>
        public DrawStopRule StopRule => _stopRule;

        /// <summary> DPの時間離散化ステップ Δt（秒）。 </summary>
        public float TimeQuantizationStep => Mathf.Clamp(_timeQuantizationStep > 0f ? _timeQuantizationStep : 1.0f, 0.1f, 2.0f);

        /// <summary> 従来互換の単一抽選候補一覧。複数区間が設定されている場合は先頭区間の候補を返す。 </summary>
        public IReadOnlyList<WeightedCountChoice> Choices
        {
            get
            {
                if (_choices != null && _choices.Length > 0)
                {
                    return _choices;
                }

                if (_intervals != null && _intervals.Length > 0 && _intervals[0] != null)
                {
                    return _intervals[0].Choices;
                }

                return Array.Empty<WeightedCountChoice>();
            }
        }

        /// <summary> 実際に評価・ランタイム生成に使用する有効な分割区間リストを返す。 </summary>
        public IReadOnlyList<StageIntervalConfig> GetEffectiveIntervals()
        {
            if (_intervals != null && _intervals.Length > 0)
            {
                List<StageIntervalConfig> valid = new List<StageIntervalConfig>(_intervals.Length);
                for (int i = 0; i < _intervals.Length; i++)
                {
                    if (_intervals[i] != null && _intervals[i].Choices != null && _intervals[i].Choices.Count > 0)
                    {
                        valid.Add(_intervals[i]);
                    }
                }

                if (valid.Count > 0)
                {
                    return valid;
                }
            }

            float length = Mathf.Max(1f, EndTimeSeconds - StartTimeSeconds);
            return new[]
            {
                new StageIntervalConfig("全区間", length, _choices ?? Array.Empty<WeightedCountChoice>(), false, 0, 99),
            };
        }

        /// <summary> 従来互換コンストラクタ（単一区間 0〜8 秒として初期化）。 </summary>
        public WaveConfig(int singlePendulumCount, int doublePendulumCount, WeightedCountChoice[] choices)
        {
            _singlePendulumCount = singlePendulumCount;
            _doublePendulumCount = doublePendulumCount;
            _choices = choices;
            _startTimeSeconds = 0f;
            _endTimeSeconds = 8f;
            _stopRule = DrawStopRule.StopWhenNoFit;
            _timeQuantizationStep = 1.0f;
            _intervals = Array.Empty<StageIntervalConfig>();
        }

        /// <summary> 分割区間・制約・終了ルールを指定して初期化する。 </summary>
        public WaveConfig(
            int singlePendulumCount,
            int doublePendulumCount,
            StageIntervalConfig[] intervals,
            DrawStopRule stopRule = DrawStopRule.StopWhenNoFit,
            float timeQuantizationStep = 1.0f,
            float startTimeSeconds = 0f)
        {
            _singlePendulumCount = singlePendulumCount;
            _doublePendulumCount = doublePendulumCount;
            _intervals = intervals;
            _stopRule = stopRule;
            _timeQuantizationStep = timeQuantizationStep;
            _startTimeSeconds = startTimeSeconds;

            float totalLen = 0f;
            if (intervals != null)
            {
                for (int i = 0; i < intervals.Length; i++)
                {
                    if (intervals[i] != null)
                    {
                        totalLen += intervals[i].DurationSeconds;
                    }
                }
            }

            _endTimeSeconds = startTimeSeconds + Mathf.Max(1f, totalLen);
            _choices = (intervals != null && intervals.Length > 0 && intervals[0]?.Choices is WeightedCountChoice[] arr)
                ? arr
                : Array.Empty<WeightedCountChoice>();
        }
    }
}
