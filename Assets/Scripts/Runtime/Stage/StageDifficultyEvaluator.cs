using System;
using System.Collections.Generic;
using UnityEngine;

namespace Taurus.Runtime.Stage
{
    /// <summary>
    /// 前向きDPによる厳密確率・期待値・分散評価、後ろ向きDPによる到達可能性テーブル構築、
    /// および区分線形補間＋台形積分＋L2ピーク評価による難易度算出を行う純C#評価エンジン。
    /// </summary>
    public static class StageDifficultyEvaluator
    {
        private const int MAX_TIME_STEPS = 200;
        private const int MAX_TRACKED_QUANTITY = 256;

        /// <summary> WaveConfig 全体の難易度曲線・平均難易度・L2ピーク評価・構成負荷・各区間DP結果を算出する。 </summary>
        public static WaveDifficultyReport EvaluateWave(
            WaveConfig config,
            float peakWeight = 0.35f,
            float multiPendulumPenalty = 0.25f,
            float doublePendulumPenalty = 0.50f,
            float triplePendulumPenalty = 1.10f)
        {
            if (config == null)
            {
                return new WaveDifficultyReport(0f, 8f, 0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, true, Array.Empty<IntervalDpResult>(), Array.Empty<Vector2>());
            }

            IReadOnlyList<StageIntervalConfig> intervals = config.GetEffectiveIntervals();
            IntervalDpResult[] results = new IntervalDpResult[intervals.Count];

            float cursorTime = config.StartTimeSeconds;
            double totalExpectedPerBob = 0.0;
            double peakDensity = 0.0;
            bool allFeasible = true;

            for (int i = 0; i < intervals.Count; i++)
            {
                IntervalDpResult intervalResult = EvaluateInterval(
                    intervals[i],
                    cursorTime,
                    config.StopRule,
                    config.TimeQuantizationStep);

                results[i] = intervalResult;
                cursorTime = intervalResult.EndTime;
                totalExpectedPerBob += intervalResult.EffectiveExpectedQuantity;
                if (intervalResult.Density > peakDensity)
                {
                    peakDensity = intervalResult.Density;
                }

                if (!intervalResult.HasValidPath)
                {
                    allFeasible = false;
                }
            }

            float startTime = config.StartTimeSeconds;
            float endTime = Math.Max(startTime + 0.1f, cursorTime);
            Vector2[] curvePoints = BuildPiecewiseLinearCurve(results, startTime, endTime);

            // 台形公式による平均密度と、区分線形区間上の二乗厳密積分による L2（RMS）密度を算出
            double totalDuration = endTime - startTime;
            double linearIntegral = 0.0;
            double squaredIntegral = 0.0;

            for (int i = 0; i < curvePoints.Length - 1; i++)
            {
                double dx = curvePoints[i + 1].x - curvePoints[i].x;
                if (dx <= 0.0)
                {
                    continue;
                }

                double y0 = curvePoints[i].y;
                double y1 = curvePoints[i + 1].y;
                linearIntegral += 0.5 * (y0 + y1) * dx;
                // 区分線形関数 y(t) の二乗積分: ∫ (y0 + (y1-y0)u)^2 dx = dx/3 * (y0^2 + y0*y1 + y1^2)
                squaredIntegral += (dx / 3.0) * (y0 * y0 + y0 * y1 + y1 * y1);
            }

            double avgDensity = totalDuration > 0.0 ? linearIntegral / totalDuration : 0.0;
            double l2RmsDensity = totalDuration > 0.0 ? Math.Sqrt(Math.Max(0.0, squaredIntegral / totalDuration)) : 0.0;

            double clampedPeakWeight = Mathf.Clamp01(peakWeight);
            double singleBobScore = (1.0 - clampedPeakWeight) * avgDensity + clampedPeakWeight * l2RmsDensity;

            // 構成負荷倍率：同じ総量でも 1重×1 < 1重×2 < 2重×1 < 2重×2 < 3重×1 < 3重×2 となるよう複数本・多段振り子の認知負荷を反映
            int extraPendulums = Math.Max(0, config.TotalPendulumCount - 1);
            int doubleCount = Math.Max(0, config.DoublePendulumCount);
            int tripleCount = Math.Max(0, config.TriplePendulumCount);
            double multiFactor = 1.0 + Math.Max(0.0, multiPendulumPenalty) * extraPendulums;
            double chainFactor = 1.0 + Math.Max(0.0, doublePendulumPenalty) * doubleCount + Math.Max(0.0, triplePendulumPenalty) * tripleCount;
            double structureMultiplier = multiFactor * chainFactor;

            double blendedScore = singleBobScore * Math.Max(1, config.TotalBobCount) * structureMultiplier;

            // Wave全体（親区間）の合計量分布を畳み込み、入れ子制約 [W_min, W_max] の達成確率・到達可能性を算出
            double[] waveTotalProbs = ConvolveWaveDistributions(results);
            bool useWaveConstraint = config.UseWaveQuantityConstraint;
            int waveMinQ = config.WaveMinQuantity;
            int waveMaxQ = config.WaveMaxQuantity;
            double waveConstraintProb = 1.0;
            bool waveConstraintFeasible = true;

            if (useWaveConstraint)
            {
                waveConstraintProb = 0.0;
                for (int q = waveMinQ; q <= waveMaxQ && q < waveTotalProbs.Length; q++)
                {
                    waveConstraintProb += waveTotalProbs[q];
                }

                bool[,] waveCanReach = BuildWaveBackwardReachability(results, waveMinQ, waveMaxQ);
                waveConstraintFeasible = allFeasible && waveCanReach.GetLength(0) > 0 && waveCanReach.GetLength(1) > 0 && waveCanReach[0, 0];
            }

            bool combinedFeasible = allFeasible && waveConstraintFeasible;

            return new WaveDifficultyReport(
                startTime,
                endTime,
                config.TotalBobCount,
                totalExpectedPerBob,
                avgDensity,
                l2RmsDensity,
                peakDensity,
                structureMultiplier,
                blendedScore,
                combinedFeasible,
                results,
                curvePoints,
                useWaveConstraint,
                waveMinQ,
                waveMaxQ,
                waveConstraintFeasible,
                waveConstraintProb,
                waveTotalProbs);
        }

        /// <summary> 1つの分割区間に対して前向きDP（厳密評価）と後ろ向きDP（到達可能性）を実行する。 </summary>
        public static IntervalDpResult EvaluateInterval(
            StageIntervalConfig interval,
            float startTime,
            DrawStopRule stopRule,
            float timeStepSeconds)
        {
            float duration = interval != null ? interval.DurationSeconds : 1f;
            float endTime = startTime + duration;
            string label = interval != null ? interval.Label : "区間";
            bool useConstraint = interval != null && interval.UseQuantityConstraint;
            int minQ = interval != null ? interval.MinQuantity : 0;
            int maxQ = interval != null ? interval.MaxQuantity : 99;
            IReadOnlyList<WeightedCountChoice> rawChoices = interval != null ? interval.Choices : null;

            float stepSec = Mathf.Clamp(timeStepSeconds > 0f ? timeStepSeconds : 1.0f, 0.1f, 2.0f);
            int timeSteps = Mathf.Clamp(Mathf.RoundToInt(duration / stepSec), 1, MAX_TIME_STEPS);

            // 有効な候補（重み > 0）を量子化ステップへ変換
            List<QuantizedChoice> choices = QuantizeChoices(rawChoices, stepSec, out int maxChoiceQ);
            if (choices.Count == 0)
            {
                bool feasibleEmpty = !useConstraint || (0 >= minQ && 0 <= maxQ);
                bool[,] emptyReach = new bool[timeSteps + 1, 1];
                emptyReach[0, 0] = feasibleEmpty;
                return new IntervalDpResult(
                    label, startTime, endTime, duration, useConstraint, minQ, maxQ,
                    0.0, 0.0, 0.0, 0.0, feasibleEmpty ? 1.0 : 0.0, feasibleEmpty,
                    new[] { 1.0 }, emptyReach, timeSteps, 0, stepSec);
            }

            // 状態空間の上限 Q_maxTrack を決定（制約なしでも計算量を抑える）
            int minStep = int.MaxValue;
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i].TimeSteps < minStep)
                {
                    minStep = choices[i].TimeSteps;
                }
            }

            int maxDraws = Math.Max(1, timeSteps / Math.Max(1, minStep));
            int naturalMaxQ = maxDraws * Math.Max(1, maxChoiceQ);
            int maxTrackedQ = useConstraint
                ? Math.Min(MAX_TRACKED_QUANTITY, Math.Max(maxQ + maxChoiceQ, minQ))
                : Math.Min(MAX_TRACKED_QUANTITY, Math.Max(1, naturalMaxQ));

            // 1. 前向きDP：dp[t, q]（時刻ステップ t、累積量 q への到達確率）
            double[,] dp = new double[timeSteps + 1, maxTrackedQ + 1];
            double[] terminalProb = new double[maxTrackedQ + 1];
            dp[0, 0] = 1.0;

            double allChoicesTotalWeight = 0.0;
            for (int i = 0; i < choices.Count; i++)
            {
                allChoicesTotalWeight += choices[i].Weight;
            }

            for (int t = 0; t <= timeSteps; t++)
            {
                // StopWhenNoFit 用：時刻 t から収まる候補の合計重み
                double fittingWeight = 0.0;
                for (int i = 0; i < choices.Count; i++)
                {
                    if (t + choices[i].TimeSteps <= timeSteps)
                    {
                        fittingWeight += choices[i].Weight;
                    }
                }

                for (int q = 0; q <= maxTrackedQ; q++)
                {
                    double prob = dp[t, q];
                    if (prob <= 0.0)
                    {
                        continue;
                    }

                    if (stopRule == DrawStopRule.StopWhenNoFit)
                    {
                        if (fittingWeight <= 0.0)
                        {
                            terminalProb[q] += prob;
                            continue;
                        }

                        for (int i = 0; i < choices.Count; i++)
                        {
                            QuantizedChoice c = choices[i];
                            if (t + c.TimeSteps > timeSteps)
                            {
                                continue;
                            }

                            int nextQ = Math.Min(maxTrackedQ, q + c.Quantity);
                            dp[t + c.TimeSteps, nextQ] += prob * (c.Weight / fittingWeight);
                        }
                    }
                    else
                    {
                        // StopOnFirstOverflow：全候補から抽選し、制限時間を超えたら現在の q で終了
                        for (int i = 0; i < choices.Count; i++)
                        {
                            QuantizedChoice c = choices[i];
                            double p = c.Weight / allChoicesTotalWeight;
                            if (t + c.TimeSteps > timeSteps)
                            {
                                terminalProb[q] += prob * p;
                            }
                            else
                            {
                                int nextQ = Math.Min(maxTrackedQ, q + c.Quantity);
                                dp[t + c.TimeSteps, nextQ] += prob * p;
                            }
                        }
                    }
                }
            }

            // 前向きDPの停止分布から期待量・二乗平均・分散・制約達成確率・条件付き期待量を厳密算出
            double expectedQ = 0.0;
            double expectedQ2 = 0.0;
            double constrainedProb = 0.0;
            double constrainedWeightedQ = 0.0;

            for (int q = 0; q <= maxTrackedQ; q++)
            {
                double p = terminalProb[q];
                if (p <= 0.0)
                {
                    continue;
                }

                expectedQ += q * p;
                expectedQ2 += (double)q * q * p;

                if (!useConstraint || (q >= minQ && q <= maxQ))
                {
                    constrainedProb += p;
                    constrainedWeightedQ += q * p;
                }
            }

            double variance = Math.Max(0.0, expectedQ2 - expectedQ * expectedQ);
            double rmsQ = Math.Sqrt(Math.Max(0.0, expectedQ2));
            double conditionalExpectedQ = constrainedProb > 1e-12 ? constrainedWeightedQ / constrainedProb : expectedQ;

            // 2. 後ろ向きDP：can_reach[t, q]（ゴール条件 t <= T かつ Q_min <= q <= Q_max へ到達可能か）
            bool[,] canReach = BuildBackwardReachability(timeSteps, maxTrackedQ, useConstraint, minQ, maxQ, stopRule, choices);
            bool hasValidPath = canReach[0, 0];

            return new IntervalDpResult(
                label,
                startTime,
                endTime,
                duration,
                useConstraint,
                minQ,
                maxQ,
                expectedQ,
                conditionalExpectedQ,
                variance,
                rmsQ,
                constrainedProb,
                hasValidPath,
                terminalProb,
                canReach,
                timeSteps,
                maxTrackedQ,
                stepSec);
        }

        private static bool[,] BuildBackwardReachability(
            int timeSteps,
            int maxTrackedQ,
            bool useConstraint,
            int minQ,
            int maxQ,
            DrawStopRule stopRule,
            List<QuantizedChoice> choices)
        {
            bool[,] canReach = new bool[timeSteps + 1, maxTrackedQ + 1];

            if (!useConstraint)
            {
                for (int t = 0; t <= timeSteps; t++)
                {
                    for (int q = 0; q <= maxTrackedQ; q++)
                    {
                        canReach[t, q] = true;
                    }
                }

                return canReach;
            }

            for (int t = timeSteps; t >= 0; t--)
            {
                bool hasFittingChoice = false;
                bool hasOverflowChoice = false;
                for (int i = 0; i < choices.Count; i++)
                {
                    if (t + choices[i].TimeSteps <= timeSteps)
                    {
                        hasFittingChoice = true;
                    }
                    else
                    {
                        hasOverflowChoice = true;
                    }
                }

                for (int q = 0; q <= maxTrackedQ; q++)
                {
                    // 最大値超過した状態からは減らせないため到達不能
                    if (q > maxQ)
                    {
                        canReach[t, q] = false;
                        continue;
                    }

                    bool isGoalRange = q >= minQ && q <= maxQ;

                    // 停止状態（もう収まる候補がない）なら、ゴール範囲内かどうかで確定
                    if (!hasFittingChoice)
                    {
                        canReach[t, q] = isGoalRange;
                        continue;
                    }

                    // StopOnFirstOverflow では、超過候補を引いて即終了する経路もゴール範囲内なら到達可能
                    if (stopRule == DrawStopRule.StopOnFirstOverflow && hasOverflowChoice && isGoalRange)
                    {
                        canReach[t, q] = true;
                        continue;
                    }

                    // 1つでも有効遷移先が到達可能であれば can_reach[t, q] = true
                    bool reachable = false;
                    for (int i = 0; i < choices.Count; i++)
                    {
                        QuantizedChoice c = choices[i];
                        int nextT = t + c.TimeSteps;
                        int nextQ = q + c.Quantity;
                        if (nextT <= timeSteps && nextQ <= maxQ && nextQ <= maxTrackedQ && canReach[nextT, nextQ])
                        {
                            reachable = true;
                            break;
                        }
                    }

                    canReach[t, q] = reachable;
                }
            }

            return canReach;
        }

        /// <summary>
        /// Wave全体（親区間）の制約 [waveMinQ, waveMaxQ] と各分割区間の制約を同時に満たすための
        /// 区間横断後ろ向きDPテーブル waveCanReach[intervalIndex, accumulatedWaveQ] を構築する。
        /// </summary>
        public static bool[,] BuildWaveBackwardReachability(
            IReadOnlyList<IntervalDpResult> intervals,
            int waveMinQ,
            int waveMaxQ)
        {
            int count = intervals != null ? intervals.Count : 0;
            int clampedMin = Math.Max(0, waveMinQ);
            int clampedMax = Math.Max(clampedMin, Math.Min(MAX_TRACKED_QUANTITY, waveMaxQ));
            bool[,] waveCanReach = new bool[count + 1, clampedMax + 1];

            for (int q = clampedMin; q <= clampedMax; q++)
            {
                waveCanReach[count, q] = true;
            }

            for (int k = count - 1; k >= 0; k--)
            {
                IntervalDpResult iv = intervals[k];
                if (iv == null || !iv.HasValidPath)
                {
                    continue;
                }

                IReadOnlyList<double> probs = iv.TerminalQuantityProbabilities;
                List<int> achievableQ = new List<int>();
                for (int q = 0; q < probs.Count; q++)
                {
                    if (probs[q] <= 0.0)
                    {
                        continue;
                    }

                    if (iv.UseQuantityConstraint && (q < iv.MinQuantity || q > iv.MaxQuantity))
                    {
                        continue;
                    }

                    achievableQ.Add(q);
                }

                for (int accQ = 0; accQ <= clampedMax; accQ++)
                {
                    bool reachable = false;
                    for (int i = 0; i < achievableQ.Count; i++)
                    {
                        int nextAcc = accQ + achievableQ[i];
                        if (nextAcc <= clampedMax && waveCanReach[k + 1, nextAcc])
                        {
                            reachable = true;
                            break;
                        }
                    }

                    waveCanReach[k, accQ] = reachable;
                }
            }

            return waveCanReach;
        }

        /// <summary>
        /// 任意の有効ゴール集合 validGoalQ[q] に対する1区間内の後ろ向きDP到達可能性テーブル can_reach[t, q] を構築する。
        /// 入れ子制約（各区間制約 ∩ Wave全体制約）のランタイム抽選重み補正で使用する。
        /// </summary>
        public static bool[,] BuildIntervalReachabilityForGoals(
            StageIntervalConfig interval,
            DrawStopRule stopRule,
            float timeStepSeconds,
            bool[] validGoalQ,
            out int timeSteps,
            out int maxTrackedQ)
        {
            float duration = interval != null ? interval.DurationSeconds : 1f;
            float stepSec = Mathf.Clamp(timeStepSeconds > 0f ? timeStepSeconds : 1.0f, 0.1f, 2.0f);
            timeSteps = Mathf.Clamp(Mathf.RoundToInt(duration / stepSec), 1, MAX_TIME_STEPS);

            List<QuantizedChoice> choices = QuantizeChoices(interval != null ? interval.Choices : null, stepSec, out _);
            maxTrackedQ = validGoalQ != null && validGoalQ.Length > 0 ? validGoalQ.Length - 1 : 0;

            bool[,] canReach = new bool[timeSteps + 1, maxTrackedQ + 1];
            if (choices.Count == 0)
            {
                canReach[0, 0] = validGoalQ != null && validGoalQ.Length > 0 && validGoalQ[0];
                return canReach;
            }

            for (int t = timeSteps; t >= 0; t--)
            {
                bool hasFittingChoice = false;
                bool hasOverflowChoice = false;
                for (int i = 0; i < choices.Count; i++)
                {
                    if (t + choices[i].TimeSteps <= timeSteps)
                    {
                        hasFittingChoice = true;
                    }
                    else
                    {
                        hasOverflowChoice = true;
                    }
                }

                for (int q = 0; q <= maxTrackedQ; q++)
                {
                    bool isGoal = validGoalQ != null && q < validGoalQ.Length && validGoalQ[q];

                    if (!hasFittingChoice)
                    {
                        canReach[t, q] = isGoal;
                        continue;
                    }

                    if (stopRule == DrawStopRule.StopOnFirstOverflow && hasOverflowChoice && isGoal)
                    {
                        canReach[t, q] = true;
                        continue;
                    }

                    bool reachable = false;
                    for (int i = 0; i < choices.Count; i++)
                    {
                        QuantizedChoice c = choices[i];
                        int nextT = t + c.TimeSteps;
                        int nextQ = q + c.Quantity;
                        if (nextT <= timeSteps && nextQ <= maxTrackedQ && canReach[nextT, nextQ])
                        {
                            reachable = true;
                            break;
                        }
                    }

                    canReach[t, q] = reachable;
                }
            }

            return canReach;
        }

        private static double[] ConvolveWaveDistributions(IntervalDpResult[] intervals)
        {
            if (intervals == null || intervals.Length == 0)
            {
                return new[] { 1.0 };
            }

            double[] acc = new[] { 1.0 };
            for (int k = 0; k < intervals.Length; k++)
            {
                IntervalDpResult iv = intervals[k];
                IReadOnlyList<double> raw = iv != null ? iv.TerminalQuantityProbabilities : null;
                if (raw == null || raw.Count == 0)
                {
                    continue;
                }

                // 区間制約が有効かつ達成可能なら、制約内条件付き分布を畳み込む
                double[] dist = new double[raw.Count];
                double sum = 0.0;
                for (int q = 0; q < raw.Count; q++)
                {
                    if (iv.UseQuantityConstraint && iv.HasValidPath && (q < iv.MinQuantity || q > iv.MaxQuantity))
                    {
                        continue;
                    }

                    dist[q] = raw[q];
                    sum += raw[q];
                }

                if (sum <= 1e-12)
                {
                    for (int q = 0; q < raw.Count; q++)
                    {
                        dist[q] = raw[q];
                        sum += raw[q];
                    }
                }

                if (sum > 0.0)
                {
                    for (int q = 0; q < dist.Length; q++)
                    {
                        dist[q] /= sum;
                    }
                }

                int nextLen = Math.Min(MAX_TRACKED_QUANTITY + 1, acc.Length + dist.Length - 1);
                double[] next = new double[nextLen];
                for (int i = 0; i < acc.Length; i++)
                {
                    if (acc[i] <= 0.0)
                    {
                        continue;
                    }

                    for (int j = 0; j < dist.Length; j++)
                    {
                        if (dist[j] <= 0.0)
                        {
                            continue;
                        }

                        int idx = Math.Min(nextLen - 1, i + j);
                        next[idx] += acc[i] * dist[j];
                    }
                }

                acc = next;
            }

            return acc;
        }

        private static Vector2[] BuildPiecewiseLinearCurve(IntervalDpResult[] intervals, float startTime, float endTime)
        {
            if (intervals == null || intervals.Length == 0)
            {
                return new[] { new Vector2(startTime, 0f), new Vector2(endTime, 0f) };
            }

            if (intervals.Length == 1)
            {
                float d = (float)intervals[0].Density;
                return new[] { new Vector2(startTime, d), new Vector2(endTime, d) };
            }

            // 始点 s + 各区間中点 (s_k + e_k)/2 + 終点 e を結ぶ区分線形関数 f(x)
            List<Vector2> pts = new List<Vector2>(intervals.Length + 2);
            pts.Add(new Vector2(startTime, (float)intervals[0].Density));

            for (int i = 0; i < intervals.Length; i++)
            {
                float mid = 0.5f * (intervals[i].StartTime + intervals[i].EndTime);
                pts.Add(new Vector2(mid, (float)intervals[i].Density));
            }

            pts.Add(new Vector2(endTime, (float)intervals[intervals.Length - 1].Density));
            return pts.ToArray();
        }

        private static List<QuantizedChoice> QuantizeChoices(
            IReadOnlyList<WeightedCountChoice> rawChoices,
            float stepSec,
            out int maxChoiceQ)
        {
            List<QuantizedChoice> list = new List<QuantizedChoice>();
            maxChoiceQ = 0;
            if (rawChoices == null)
            {
                return list;
            }

            for (int i = 0; i < rawChoices.Count; i++)
            {
                WeightedCountChoice c = rawChoices[i];
                if (c.Weight <= 0)
                {
                    continue;
                }

                int q = Math.Max(0, c.TopCount);
                int tSteps = Math.Max(1, Mathf.RoundToInt(c.DurationSeconds / stepSec));
                if (q > maxChoiceQ)
                {
                    maxChoiceQ = q;
                }

                list.Add(new QuantizedChoice(c.Weight, q, tSteps));
            }

            return list;
        }

        private readonly struct QuantizedChoice
        {
            public readonly int Weight;
            public readonly int Quantity;
            public readonly int TimeSteps;

            public QuantizedChoice(int weight, int quantity, int timeSteps)
            {
                Weight = weight;
                Quantity = quantity;
                TimeSteps = timeSteps;
            }
        }
    }
}
