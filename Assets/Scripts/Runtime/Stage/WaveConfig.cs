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
        // 単振り子（1重：1玉）の配置本数（StageAnchorの支点は最大2本）
        [SerializeField, Range(0, 2)] private int _singlePendulumCount;

        // 二重振り子（2重：親玉＋子玉の2玉）の配置本数（StageAnchorの支点は最大2本）
        [SerializeField, Range(0, 2)] private int _doublePendulumCount;

        [Header("期間・DP設定")]
        // 評価・抽選期間の開始時刻 s（秒）
        [SerializeField, Min(0f)] private float _startTimeSeconds = 0f;

        // 評価・抽選期間の終了時刻 e（秒）。複数区間指定時は区間長合計が優先される
        [SerializeField, Min(0.1f)] private float _endTimeSeconds = 8f;

        // 抽選終了ルール（残り時間に収まる候補があれば継続 / 初回超過で停止）
        [SerializeField] private DrawStopRule _stopRule = DrawStopRule.StopWhenNoFit;

        // DPの時間離散化ステップ Δt（秒）。1秒単位の候補なら 1.0 または 0.5 を推奨
        [SerializeField, Range(0.1f, 2.0f)] private float _timeQuantizationStep = 1.0f;

        [Header("Wave全体（親区間）の量制約 [W_min, W_max]")]
        // true のとき、全分割区間の合計量（1玉あたり）が [W_min, W_max] に収まるよう入れ子DP制御を適用する
        [SerializeField] private bool _useWaveQuantityConstraint;

        // Wave全体の最小値保証 W_min（例: 1 にすると「0回（見てるだけ）」を確実に防げる）
        [SerializeField, Min(0)] private int _waveMinQuantity = 1;

        // Wave全体の最大値保証 W_max（例: 序盤Waveでの理不尽な多回転を防ぐ）
        [SerializeField, Min(0)] private int _waveMaxQuantity = 16;

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

        /// <summary> Wave全体（親区間）の量制約 [W_min, W_max] を有効にするか。 </summary>
        public bool UseWaveQuantityConstraint => _useWaveQuantityConstraint;

        /// <summary> Wave全体の最小値保証 W_min（1玉あたり）。 </summary>
        public int WaveMinQuantity => Mathf.Max(0, _waveMinQuantity);

        /// <summary> Wave全体の最大値保証 W_max（1玉あたり、W_min 未満にならないよう補正）。 </summary>
        public int WaveMaxQuantity => Mathf.Max(WaveMinQuantity, _waveMaxQuantity);

        /// <summary> 明示的な分割区間配列が構成されているか。 </summary>
        public bool HasExplicitIntervals => _intervals != null && _intervals.Length > 0;

        /// <summary> 現在の分割区間から分割点 p_1, p_2, ..., p_n（秒）を算出して返す。 </summary>
        public float[] GetSplitPoints()
        {
            IReadOnlyList<StageIntervalConfig> effective = GetEffectiveIntervals();
            if (effective.Count <= 1)
            {
                return Array.Empty<float>();
            }

            float[] points = new float[effective.Count - 1];
            float cursor = StartTimeSeconds;
            for (int i = 0; i < effective.Count - 1; i++)
            {
                cursor += effective[i].DurationSeconds;
                points[i] = cursor;
            }

            return points;
        }

        /// <summary> 未分割（単一テーブル）状態であれば、現在の [s, e] を1区間として明示配列へ変換する。 </summary>
        public void EnsureIntervalsInitialized()
        {
            if (_intervals != null && _intervals.Length > 0)
            {
                SyncEndTimeFromIntervals();
                return;
            }

            float length = Mathf.Max(0.2f, EndTimeSeconds - StartTimeSeconds);
            WeightedCountChoice[] choicesCopy = _choices != null ? new WeightedCountChoice[_choices.Length] : Array.Empty<WeightedCountChoice>();
            if (_choices != null)
            {
                Array.Copy(_choices, choicesCopy, _choices.Length);
            }

            _intervals = new[]
            {
                new StageIntervalConfig("区間1", length, choicesCopy, false, 0, 16),
            };
            SyncEndTimeFromIntervals();
        }

        /// <summary>
        /// 指定時刻 splitTime（秒）に分割点 p を追加し、該当区間を2つに分割する。
        /// 区間長合計 L_1 + ... + L_m は元の期間長 e - s と厳密に一致する。
        /// </summary>
        public bool AddSplitPoint(float splitTime)
        {
            EnsureIntervalsInitialized();

            float start = StartTimeSeconds;
            float minGap = Mathf.Max(0.2f, TimeQuantizationStep);
            float cursor = start;

            for (int i = 0; i < _intervals.Length; i++)
            {
                StageIntervalConfig target = _intervals[i];
                float ivStart = cursor;
                float ivEnd = cursor + target.DurationSeconds;
                cursor = ivEnd;

                if (splitTime <= ivStart + minGap * 0.5f || splitTime >= ivEnd - minGap * 0.5f)
                {
                    continue;
                }

                float leftLen = SnapDuration(splitTime - ivStart);
                float rightLen = SnapDuration(ivEnd - (ivStart + leftLen));
                if (leftLen < 0.1f || rightLen < 0.1f)
                {
                    return false;
                }

                StageIntervalConfig left = target.Clone(target.Label, leftLen);
                StageIntervalConfig right = target.Clone($"区間{_intervals.Length + 1}", rightLen);

                List<StageIntervalConfig> updated = new List<StageIntervalConfig>(_intervals.Length + 1);
                for (int j = 0; j < _intervals.Length; j++)
                {
                    if (j == i)
                    {
                        updated.Add(left);
                        updated.Add(right);
                    }
                    else
                    {
                        updated.Add(_intervals[j]);
                    }
                }

                _intervals = updated.ToArray();
                SyncEndTimeFromIntervals();
                return true;
            }

            return false;
        }

        /// <summary> 最も長い区間の中央に分割点を追加する。 </summary>
        public bool SplitLongestInterval()
        {
            EnsureIntervalsInitialized();
            if (_intervals == null || _intervals.Length == 0)
            {
                return false;
            }

            int longestIndex = 0;
            float longestDuration = _intervals[0].DurationSeconds;
            float cursor = StartTimeSeconds;
            float longestStart = cursor;

            for (int i = 0; i < _intervals.Length; i++)
            {
                float d = _intervals[i].DurationSeconds;
                if (d > longestDuration)
                {
                    longestDuration = d;
                    longestIndex = i;
                    longestStart = cursor;
                }

                cursor += d;
            }

            if (longestDuration < 0.4f)
            {
                return false;
            }

            return AddSplitPoint(longestStart + _intervals[longestIndex].DurationSeconds * 0.5f);
        }

        /// <summary>
        /// 指定した分割点 p_{splitIndex} を新しい時刻へ移動し、左右の区間長を連動更新する。
        /// 期間全体 [s, e] の長さは不変に保たれる。
        /// </summary>
        public bool MoveSplitPoint(int splitIndex, float newSplitTime)
        {
            if (_intervals == null || splitIndex < 0 || splitIndex >= _intervals.Length - 1)
            {
                return false;
            }

            float leftStart = StartTimeSeconds;
            for (int i = 0; i < splitIndex; i++)
            {
                leftStart += _intervals[i].DurationSeconds;
            }

            float combined = _intervals[splitIndex].DurationSeconds + _intervals[splitIndex + 1].DurationSeconds;
            float minGap = Mathf.Min(Mathf.Max(0.2f, TimeQuantizationStep), combined * 0.45f);
            float clampedTime = Mathf.Clamp(newSplitTime, leftStart + minGap, leftStart + combined - minGap);

            float newLeftLen = SnapDuration(clampedTime - leftStart);
            newLeftLen = Mathf.Clamp(newLeftLen, 0.1f, combined - 0.1f);
            float newRightLen = Mathf.Max(0.1f, combined - newLeftLen);

            _intervals[splitIndex].SetDurationSeconds(newLeftLen);
            _intervals[splitIndex + 1].SetDurationSeconds(newRightLen);
            SyncEndTimeFromIntervals();
            return true;
        }

        /// <summary> 指定した分割点を削除し、左右の2区間を1区間に統合する（長さは合算）。 </summary>
        public bool RemoveSplitPoint(int splitIndex)
        {
            if (_intervals == null || splitIndex < 0 || splitIndex >= _intervals.Length - 1)
            {
                return false;
            }

            float mergedDuration = _intervals[splitIndex].DurationSeconds + _intervals[splitIndex + 1].DurationSeconds;
            _intervals[splitIndex].SetDurationSeconds(mergedDuration);

            List<StageIntervalConfig> updated = new List<StageIntervalConfig>(_intervals.Length - 1);
            for (int i = 0; i < _intervals.Length; i++)
            {
                if (i == splitIndex + 1)
                {
                    continue;
                }

                updated.Add(_intervals[i]);
            }

            _intervals = updated.ToArray();
            SyncEndTimeFromIntervals();
            return true;
        }

        /// <summary> 期間 [s, e] を更新し、明示区間がある場合は比率を維持して区間長合計を e - s に同期する。 </summary>
        public void SetPeriod(float startTime, float endTime)
        {
            _startTimeSeconds = Mathf.Max(0f, startTime);
            float targetDuration = Mathf.Max(0.2f, endTime - _startTimeSeconds);
            _endTimeSeconds = _startTimeSeconds + targetDuration;

            if (_intervals == null || _intervals.Length == 0)
            {
                return;
            }

            float oldTotal = 0f;
            for (int i = 0; i < _intervals.Length; i++)
            {
                oldTotal += _intervals[i].DurationSeconds;
            }

            if (oldTotal <= 0.0001f)
            {
                float equalLen = targetDuration / _intervals.Length;
                for (int i = 0; i < _intervals.Length; i++)
                {
                    _intervals[i].SetDurationSeconds(equalLen);
                }

                return;
            }

            float allocated = 0f;
            for (int i = 0; i < _intervals.Length; i++)
            {
                if (i == _intervals.Length - 1)
                {
                    _intervals[i].SetDurationSeconds(Mathf.Max(0.1f, targetDuration - allocated));
                }
                else
                {
                    float scaled = SnapDuration(targetDuration * (_intervals[i].DurationSeconds / oldTotal));
                    scaled = Mathf.Clamp(scaled, 0.1f, Math.Max(0.1f, targetDuration - allocated - 0.1f * (_intervals.Length - 1 - i)));
                    _intervals[i].SetDurationSeconds(scaled);
                    allocated += scaled;
                }
            }

            SyncEndTimeFromIntervals();
        }

        /// <summary> Wave全体の量制約 [W_min, W_max] を更新する。 </summary>
        public void SetWaveQuantityConstraint(bool useConstraint, int minQuantity, int maxQuantity)
        {
            _useWaveQuantityConstraint = useConstraint;
            _waveMinQuantity = Mathf.Max(0, minQuantity);
            _waveMaxQuantity = Mathf.Max(_waveMinQuantity, maxQuantity);
        }

        /// <summary> 選択Waveを「前半（導入）＋後半（山場）」の2区間に分割するプリセットを適用する。 </summary>
        public void ApplyDefaultTwoIntervalSplit()
        {
            EnsureIntervalsInitialized();
            if (_intervals.Length == 1)
            {
                SplitLongestInterval();
            }

            if (_intervals.Length >= 2)
            {
                _intervals[0].SetLabel("前半(導入)");
                _intervals[1].SetLabel("後半(山場)");
            }
        }

        private void SyncEndTimeFromIntervals()
        {
            if (_intervals == null || _intervals.Length == 0)
            {
                return;
            }

            float total = 0f;
            for (int i = 0; i < _intervals.Length; i++)
            {
                if (_intervals[i] != null)
                {
                    total += _intervals[i].DurationSeconds;
                }
            }

            _endTimeSeconds = StartTimeSeconds + Mathf.Max(0.1f, total);
        }

        private float SnapDuration(float raw)
        {
            float step = Mathf.Clamp(TimeQuantizationStep, 0.1f, 2.0f);
            float snapped = Mathf.Round(raw / step) * step;
            return Mathf.Max(0.1f, snapped);
        }

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
