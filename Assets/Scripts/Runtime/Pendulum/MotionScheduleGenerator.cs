using System;
using System.Collections.Generic;
using Taurus.Runtime.Stage;
using UnityEngine;

namespace Taurus.Runtime.Pendulum
{
    /// <summary>
    /// 32bit 乱数シードから決定論的振り子スケジュールを生成・検算する純C#モジュール。
    /// WaveConfig（分割区間・量制約 [Q_min, Q_max]・終了ルール）を渡した場合は、
    /// 後ろ向きDP（can_reach[t, q]）で抽選重みを補正し、制約逸脱を防ぎながら生成する。
    /// </summary>
    public static class MotionScheduleGenerator
    {
        private const int DEFAULT_PLANNED_SECONDS = 8;
        private const int MAX_STEP_SEARCH = 12;
        private const int MAX_RETRY_COUNT = 16;
        private const double TWO_PI = Math.PI * 2.0;

        // 終点高さが 0 や sin(√3) ぴったりになって境界判定が不安定になるのを避けるマージン
        private const double HEIGHT_MARGIN = 0.0001;
        private const double BOTTOM_AFTER_TOLERANCE = 1e-8;

        // 終点高さの上限 sin(√3) ≈ 0.987（真上 h=1 の少し手前で折り返す）
        private static readonly double MAX_TURN_HEIGHT = Math.Sin(Math.Sqrt(3.0));

        // Wave開始時の初期角度のsinθ上限：sin(π/√3) ≈ 0.971（水平付近の極端な初期姿勢を避ける）
        private static readonly double INITIAL_SIN_LIMIT = Math.Sin(Math.PI / Math.Sqrt(3.0));

        /// <summary>
        /// WaveConfig の分割区間・量制約 [Q_min, Q_max]・終了ルールに従い、
        /// 後ろ向きDP（w'_i = w_i * can_reach[t + t_i, q + q_i]）で制約を守りつつ決定論的スケジュールを生成する。
        /// </summary>
        public static MotionSchedule Generate(uint seed, WaveConfig waveConfig)
        {
            if (waveConfig == null)
            {
                throw new ArgumentNullException(nameof(waveConfig));
            }

            IReadOnlyList<StageIntervalConfig> intervals = waveConfig.GetEffectiveIntervals();
            if (intervals == null || intervals.Count == 0)
            {
                throw new ArgumentException("有効な区間設定がありません。", nameof(waveConfig));
            }

            // 後ろ向きDPテーブルを区間ごとに事前計算
            IntervalDpResult[] dpResults = new IntervalDpResult[intervals.Count];
            float cursor = waveConfig.StartTimeSeconds;
            for (int i = 0; i < intervals.Count; i++)
            {
                dpResults[i] = StageDifficultyEvaluator.EvaluateInterval(
                    intervals[i],
                    cursor,
                    waveConfig.StopRule,
                    waveConfig.TimeQuantizationStep);
                cursor = dpResults[i].EndTime;
            }

            for (int retry = 0; retry < MAX_RETRY_COUNT; retry++)
            {
                uint attemptSeed = unchecked(seed + (uint)(retry * 2654435761u));
                if (TryBuildScheduleFromIntervals(
                    attemptSeed,
                    intervals,
                    dpResults,
                    waveConfig.StopRule,
                    waveConfig.TimeQuantizationStep,
                    out MotionSchedule schedule))
                {
                    return schedule;
                }
            }

            throw new InvalidOperationException($"[{nameof(MotionScheduleGenerator)}] 制約を満たすスケジュールを生成できませんでした (seed={seed})。");
        }

        /// <summary>
        /// 指定シードと単一区間重み候補から決定論的スケジュールを生成する（従来互換API）。
        /// 同じシード・同じ重みを渡せば必ず同一の軌道が再現される。
        /// </summary>
        public static MotionSchedule Generate(uint seed, IReadOnlyList<WeightedCountChoice> choices, int plannedSeconds = DEFAULT_PLANNED_SECONDS)
        {
            if (choices == null || choices.Count == 0)
            {
                throw new ArgumentException("抽選候補が空です。", nameof(choices));
            }

            for (int retry = 0; retry < MAX_RETRY_COUNT; retry++)
            {
                uint attemptSeed = unchecked(seed + (uint)(retry * 2654435761u));
                if (TryBuildSchedule(attemptSeed, choices, plannedSeconds, out MotionSchedule schedule))
                {
                    return schedule;
                }
            }

            throw new InvalidOperationException($"[{nameof(MotionScheduleGenerator)}] 制約を満たすスケジュールを生成できませんでした (seed={seed})。");
        }

        private static bool TryBuildScheduleFromIntervals(
            uint seed,
            IReadOnlyList<StageIntervalConfig> intervals,
            IntervalDpResult[] dpResults,
            DrawStopRule stopRule,
            float timeStepSeconds,
            out MotionSchedule schedule)
        {
            uint state = seed;
            List<MotionSegment> segments = new List<MotionSegment>(16);

            int direction = NextRandom01(ref state) < 0.5 ? 1 : -1;
            double rotation = SampleInitialRotation(ref state);

            double elapsedCursor = 0.0;
            int totalCount = 0;
            float stepSec = Mathf.Clamp(timeStepSeconds > 0f ? timeStepSeconds : 1.0f, 0.1f, 2.0f);

            for (int k = 0; k < intervals.Count; k++)
            {
                StageIntervalConfig interval = intervals[k];
                IntervalDpResult dp = dpResults[k];
                IReadOnlyList<WeightedCountChoice> choices = interval.Choices;
                if (choices == null || choices.Count == 0)
                {
                    continue;
                }

                bool enforceConstraint = interval.UseQuantityConstraint && dp.HasValidPath;
                int currentT = 0;
                int currentQ = 0;
                int maxT = dp.TimeSteps;

                while (currentT < maxT)
                {
                    if (!TryPickChoiceWithBackwardDp(
                        ref state,
                        choices,
                        dp,
                        currentT,
                        currentQ,
                        maxT,
                        stepSec,
                        stopRule,
                        enforceConstraint,
                        out WeightedCountChoice choice,
                        out bool shouldStop))
                    {
                        break;
                    }

                    if (shouldStop)
                    {
                        break;
                    }

                    int choiceSteps = Math.Max(1, Mathf.RoundToInt(choice.DurationSeconds / stepSec));
                    double segDuration = choice.DurationSeconds;

                    double alpha = NextTurnAlpha(ref state);
                    double endPhase = direction > 0
                        ? Math.PI * 0.5 + alpha
                        : Math.PI * 1.5 - alpha;

                    if (!TryChooseEndpoint(rotation, direction, choice.TopCount, endPhase, out double endAngle))
                    {
                        schedule = null;
                        return false;
                    }

                    int verifiedTops = TopCrossingDetector.CountTopCrossings(rotation, endAngle);
                    int verifiedBottoms = TopCrossingDetector.CountBoundaryCrossings(rotation, endAngle, 0.0);
                    if (verifiedTops != choice.TopCount || verifiedBottoms < 1)
                    {
                        schedule = null;
                        return false;
                    }

                    segments.Add(new MotionSegment(
                        elapsedCursor,
                        segDuration,
                        choice.TopCount,
                        direction,
                        rotation,
                        endAngle,
                        Math.Sin(alpha)));

                    elapsedCursor += segDuration;
                    totalCount += choice.TopCount;
                    currentT += choiceSteps;
                    currentQ += Math.Max(0, choice.TopCount);
                    rotation = endAngle;
                    direction = -direction;
                }
            }

            schedule = new MotionSchedule(segments.ToArray(), totalCount, rotation, seed);
            return true;
        }

        /// <summary>
        /// 後ろ向きDPの到達可能性テーブル can_reach[t, q] を使って重みを
        /// w'_i = w_i * can_reach[t + t_i, q + q_i] に補正し、制約逸脱を防ぎながら候補を抽選する。
        /// </summary>
        private static bool TryPickChoiceWithBackwardDp(
            ref uint state,
            IReadOnlyList<WeightedCountChoice> choices,
            IntervalDpResult dp,
            int currentT,
            int currentQ,
            int maxT,
            float stepSec,
            DrawStopRule stopRule,
            bool enforceConstraint,
            out WeightedCountChoice picked,
            out bool shouldStop)
        {
            picked = default;
            shouldStop = false;

            double totalAdjustedWeight = 0.0;
            bool anyFitsTime = false;

            // 各候補の補正重みを計算
            double[] weights = new double[choices.Count];
            bool[] overflows = new bool[choices.Count];

            for (int i = 0; i < choices.Count; i++)
            {
                WeightedCountChoice c = choices[i];
                if (c.Weight <= 0)
                {
                    continue;
                }

                int tSteps = Math.Max(1, Mathf.RoundToInt(c.DurationSeconds / stepSec));
                int nextT = currentT + tSteps;
                int nextQ = currentQ + Math.Max(0, c.TopCount);
                bool isOverflow = nextT > maxT;
                overflows[i] = isOverflow;

                if (!isOverflow)
                {
                    anyFitsTime = true;
                }

                if (stopRule == DrawStopRule.StopWhenNoFit && isOverflow)
                {
                    continue;
                }

                double adjustedWeight = c.Weight;
                if (enforceConstraint)
                {
                    if (isOverflow)
                    {
                        // StopOnFirstOverflow で停止する場合、現在量 currentQ がゴール範囲内なら許可
                        bool canStopHere = currentQ >= dp.MinQuantity && currentQ <= dp.MaxQuantity;
                        adjustedWeight = canStopHere ? c.Weight : 0.0;
                    }
                    else
                    {
                        // w'_i = w_i * can_reach[t + t_i, q + q_i]
                        bool reachable = nextQ <= dp.MaxQuantity && dp.CanReach(nextT, nextQ);
                        adjustedWeight = reachable ? c.Weight : 0.0;
                    }
                }

                weights[i] = adjustedWeight;
                totalAdjustedWeight += adjustedWeight;
            }

            if (stopRule == DrawStopRule.StopWhenNoFit && !anyFitsTime)
            {
                shouldStop = true;
                return false;
            }

            if (totalAdjustedWeight <= 0.0)
            {
                shouldStop = true;
                return false;
            }

            double roll = NextRandom01(ref state) * totalAdjustedWeight;
            int selectedIndex = -1;
            for (int i = 0; i < choices.Count; i++)
            {
                if (weights[i] <= 0.0)
                {
                    continue;
                }

                selectedIndex = i;
                roll -= weights[i];
                if (roll < 0.0)
                {
                    break;
                }
            }

            if (selectedIndex < 0)
            {
                shouldStop = true;
                return false;
            }

            if (overflows[selectedIndex])
            {
                shouldStop = true;
                return true;
            }

            picked = choices[selectedIndex];
            return true;
        }

        private static bool TryBuildSchedule(uint seed, IReadOnlyList<WeightedCountChoice> choices, int plannedSeconds, out MotionSchedule schedule)
        {
            uint state = seed;
            List<MotionSegment> segments = new List<MotionSegment>(plannedSeconds);
            int remaining = plannedSeconds;

            int direction = NextRandom01(ref state) < 0.5 ? 1 : -1;
            double rotation = SampleInitialRotation(ref state);

            int totalCount = 0;

            while (remaining > 0)
            {
                WeightedCountChoice choice = PickChoice(ref state, choices, remaining);
                int durationInt = Math.Max(1, (int)Math.Round(choice.DurationSeconds));

                double alpha = NextTurnAlpha(ref state);
                double endPhase = direction > 0
                    ? Math.PI * 0.5 + alpha
                    : Math.PI * 1.5 - alpha;

                if (!TryChooseEndpoint(rotation, direction, choice.TopCount, endPhase, out double endAngle))
                {
                    schedule = null;
                    return false;
                }

                int verifiedTops = TopCrossingDetector.CountTopCrossings(rotation, endAngle);
                int verifiedBottoms = TopCrossingDetector.CountBoundaryCrossings(rotation, endAngle, 0.0);
                if (verifiedTops != choice.TopCount || verifiedBottoms < 1)
                {
                    schedule = null;
                    return false;
                }

                double startTime = plannedSeconds - remaining;
                segments.Add(new MotionSegment(
                    startTime,
                    choice.DurationSeconds,
                    choice.TopCount,
                    direction,
                    rotation,
                    endAngle,
                    Math.Sin(alpha)));

                totalCount += choice.TopCount;
                rotation = endAngle;
                direction = -direction;
                remaining -= durationInt;
            }

            schedule = new MotionSchedule(segments.ToArray(), totalCount, rotation, seed);
            return true;
        }

        private static double SampleInitialRotation(ref uint state)
        {
            // Wave開始時の理不尽回避：sin(π/√3) > sinθ > sin(-π/√3) の範囲でsinθをランダムに決め、
            // cosθの符号（支点より下半分/上半分）も独立にランダムに選ぶ
            double initialSin = (NextRandom01(ref state) * 2.0 - 1.0) * INITIAL_SIN_LIMIT;
            double initialBaseAngle = Math.Asin(initialSin);
            bool startsAboveHorizontal = NextRandom01(ref state) < 0.5;
            return startsAboveHorizontal ? Math.PI - initialBaseAngle : initialBaseAngle;
        }

        private static bool TryChooseEndpoint(double startAngle, int direction, int targetTopCount, double endPhase, out double endAngle)
        {
            double baseCycle = Math.Floor(startAngle / TWO_PI);

            for (int step = 1; step <= MAX_STEP_SEARCH; step++)
            {
                double candidate = endPhase + TWO_PI * (baseCycle + direction * step);
                if (direction * (candidate - startAngle) <= 0.0)
                {
                    continue;
                }

                int tops = TopCrossingDetector.CountBoundaryCrossings(startAngle, candidate, Math.PI);
                int bottoms = TopCrossingDetector.CountBoundaryCrossings(startAngle, candidate, 0.0);
                if (tops != targetTopCount || bottoms < 1)
                {
                    continue;
                }

                if (targetTopCount == 0)
                {
                    endAngle = candidate;
                    return true;
                }

                double lastTop = direction > 0
                    ? Math.PI + TWO_PI * (Math.Floor((startAngle - Math.PI) / TWO_PI) + tops)
                    : Math.PI + TWO_PI * (Math.Ceiling((startAngle - Math.PI) / TWO_PI) - tops + 1);

                double bottomAfter = direction > 0 ? lastTop + Math.PI : lastTop - Math.PI;
                if (direction * (candidate - bottomAfter) >= -BOTTOM_AFTER_TOLERANCE)
                {
                    endAngle = candidate;
                    return true;
                }
            }

            endAngle = 0.0;
            return false;
        }

        private static WeightedCountChoice PickChoice(ref uint state, IReadOnlyList<WeightedCountChoice> choices, int remainingSeconds)
        {
            int totalWeight = 0;
            WeightedCountChoice fallback = choices[choices.Count - 1];

            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i].DurationSeconds <= remainingSeconds)
                {
                    totalWeight += choices[i].Weight;
                    fallback = choices[i];
                }
            }

            double pick = NextRandom01(ref state) * totalWeight;
            for (int i = 0; i < choices.Count; i++)
            {
                WeightedCountChoice candidate = choices[i];
                if (candidate.DurationSeconds > remainingSeconds)
                {
                    continue;
                }

                pick -= candidate.Weight;
                if (pick < 0.0)
                {
                    return candidate;
                }
            }

            return fallback;
        }

        private static double NextTurnAlpha(ref uint state)
        {
            double h = HEIGHT_MARGIN + NextRandom01(ref state) * (MAX_TURN_HEIGHT - HEIGHT_MARGIN * 2.0);
            return Math.Asin(h);
        }

        /// <summary> Mulberry32 擬似乱数生成器（HTML参照実装とビット単位で同一）。 </summary>
        private static double NextRandom01(ref uint state)
        {
            state = unchecked(state + 0x6D2B79F5u);
            uint n = state;
            n = unchecked((n ^ (n >> 15)) * (n | 1u));
            n ^= unchecked(n + unchecked((n ^ (n >> 7)) * (n | 61u)));
            return (n ^ (n >> 14)) / 4294967296.0;
        }
    }
}
