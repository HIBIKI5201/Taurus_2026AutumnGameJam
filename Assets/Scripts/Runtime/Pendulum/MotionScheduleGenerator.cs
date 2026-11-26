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

        // 他の玉（親玉・他振り子）が真上通過する予定の時刻スロットでは、同時発光シンクロを抑えるため回転候補の重みを減衰させる
        private const double OVERLAP_TOP_CROSSING_WEIGHT_FACTOR = 0.12;

        /// <summary>
        /// WaveConfig の分割区間・量制約 [Q_min, Q_max]・終了ルールに従い、
        /// 後ろ向きDP（w'_i = w_i * can_reach[t + t_i, q + q_i]）で制約を守りつつ決定論的スケジュールを生成する。
        /// avoidInitialAngles を渡すと初期位置・初動方向を分散し、
        /// avoidActiveSlots を渡すと他玉と同じ時刻スロットで同時発光する頻度を抑制する。
        /// </summary>
        public static MotionSchedule Generate(
            uint seed,
            WaveConfig waveConfig,
            IReadOnlyList<double> avoidInitialAngles = null,
            IReadOnlyList<int> avoidActiveSlots = null)
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

            // Wave全体（親区間）の入れ子制約用 後ろ向きDPテーブルを事前計算
            bool useWaveConstraint = waveConfig.UseWaveQuantityConstraint;
            bool[,] waveCanReach = null;
            if (useWaveConstraint)
            {
                waveCanReach = StageDifficultyEvaluator.BuildWaveBackwardReachability(
                    dpResults,
                    waveConfig.WaveMinQuantity,
                    waveConfig.WaveMaxQuantity);
                if (waveCanReach.GetLength(0) == 0 || waveCanReach.GetLength(1) == 0 || !waveCanReach[0, 0])
                {
                    useWaveConstraint = false;
                }
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
                    useWaveConstraint,
                    waveCanReach,
                    avoidInitialAngles,
                    avoidActiveSlots,
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
            bool useWaveConstraint,
            bool[,] waveCanReach,
            IReadOnlyList<double> avoidInitialAngles,
            IReadOnlyList<int> avoidActiveSlots,
            out MotionSchedule schedule)
        {
            uint state = seed;
            List<MotionSegment> segments = new List<MotionSegment>(16);

            double rotation = SampleDistinctInitialRotation(ref state, avoidInitialAngles);
            int direction = DetermineGravityInitialDirection(rotation, ref state);

            double elapsedCursor = 0.0;
            int totalCount = 0;
            float stepSec = Mathf.Clamp(timeStepSeconds > 0f ? timeStepSeconds : 1.0f, 0.1f, 2.0f);
            int waveMaxTrackedQ = waveCanReach != null ? waveCanReach.GetLength(1) - 1 : 0;
            int globalTimeStep = 0;

            for (int k = 0; k < intervals.Count; k++)
            {
                StageIntervalConfig interval = intervals[k];
                IntervalDpResult dp = dpResults[k];
                IReadOnlyList<WeightedCountChoice> choices = interval.Choices;
                if (choices == null || choices.Count == 0)
                {
                    continue;
                }

                // 区間制約とWave全体制約の積集合に対する有効ゴール・到達可能性テーブルを構築
                bool enforceConstraint = interval.UseQuantityConstraint && dp.HasValidPath;
                bool[,] activeCanReach = dp.CanReachTable;
                bool[] validGoalQ = null;
                int maxT = dp.TimeSteps;
                int maxAllowedQ = dp.MaxQuantity;

                if (useWaveConstraint && waveCanReach != null)
                {
                    int remainingMaxQ = Math.Max(0, waveMaxTrackedQ - totalCount);
                    int goalLen = Math.Min(dp.MaxTrackedQuantity, remainingMaxQ) + 1;
                    bool[] nestedGoalQ = new bool[goalLen];
                    bool anyValidGoal = false;

                    for (int q = 0; q < goalLen; q++)
                    {
                        bool intervalOk = !interval.UseQuantityConstraint || (q >= interval.MinQuantity && q <= interval.MaxQuantity);
                        bool waveOk = totalCount + q <= waveMaxTrackedQ && waveCanReach[k + 1, totalCount + q];
                        if (intervalOk && waveOk)
                        {
                            nestedGoalQ[q] = true;
                            anyValidGoal = true;
                        }
                    }

                    if (anyValidGoal)
                    {
                        bool[,] nestedCanReach = StageDifficultyEvaluator.BuildIntervalReachabilityForGoals(
                            interval,
                            stopRule,
                            stepSec,
                            nestedGoalQ,
                            out int nestedTimeSteps,
                            out int nestedMaxQ);
                        if (nestedCanReach[0, 0])
                        {
                            enforceConstraint = true;
                            activeCanReach = nestedCanReach;
                            validGoalQ = nestedGoalQ;
                            maxT = nestedTimeSteps;
                            maxAllowedQ = nestedMaxQ;
                        }
                    }
                }

                int currentT = 0;
                int currentQ = 0;

                while (currentT < maxT)
                {
                    int overlapCount = (avoidActiveSlots != null && globalTimeStep >= 0 && globalTimeStep < avoidActiveSlots.Count)
                        ? avoidActiveSlots[globalTimeStep]
                        : 0;

                    if (!TryPickChoiceWithBackwardDp(
                        ref state,
                        choices,
                        dp,
                        activeCanReach,
                        validGoalQ,
                        maxAllowedQ,
                        currentT,
                        currentQ,
                        maxT,
                        stepSec,
                        stopRule,
                        enforceConstraint,
                        overlapCount,
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
                    globalTimeStep += choiceSteps;
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
            bool[,] canReachTable,
            bool[] validGoalQ,
            int maxAllowedQ,
            int currentT,
            int currentQ,
            int maxT,
            float stepSec,
            DrawStopRule stopRule,
            bool enforceConstraint,
            int overlapCount,
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
            int reachRows = canReachTable != null ? canReachTable.GetLength(0) : 0;
            int reachCols = canReachTable != null ? canReachTable.GetLength(1) : 0;

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
                        bool canStopHere = validGoalQ != null
                            ? (currentQ >= 0 && currentQ < validGoalQ.Length && validGoalQ[currentQ])
                            : (currentQ >= dp.MinQuantity && currentQ <= dp.MaxQuantity);
                        adjustedWeight = canStopHere ? c.Weight : 0.0;
                    }
                    else
                    {
                        // w'_i = w_i * can_reach[t + t_i, q + q_i]
                        bool reachable = nextQ <= maxAllowedQ
                            && nextT >= 0 && nextT < reachRows
                            && nextQ >= 0 && nextQ < reachCols
                            && canReachTable[nextT, nextQ];
                        adjustedWeight = reachable ? c.Weight : 0.0;
                    }
                }

                // 同時発光シンクロ抑制：既に他玉が真上通過する予定のスロットでは、回転候補（TopCount > 0）の重みを減衰させる
                if (adjustedWeight > 0.0 && !isOverflow && overlapCount > 0 && c.TopCount > 0)
                {
                    adjustedWeight *= Math.Pow(OVERLAP_TOP_CROSSING_WEIGHT_FACTOR, overlapCount);
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

            double rotation = SampleInitialRotation(ref state);
            int direction = DetermineGravityInitialDirection(rotation, ref state);

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

        /// <summary>
        /// 初手の回転方向を重力方向に整合させる。
        /// θ=0 が真下のため、sinθ &gt; 0（右側）なら -1、sinθ &lt; 0（左側）なら +1 として必ず最下点側へ落ち始める。
        /// </summary>
        private static int DetermineGravityInitialDirection(double initialRotation, ref uint state)
        {
            double sinTheta = Math.Sin(initialRotation);
            if (Math.Abs(sinTheta) < 1e-6)
            {
                return NextRandom01(ref state) < 0.5 ? 1 : -1;
            }

            return sinTheta > 0.0 ? -1 : 1;
        }

        /// <summary>
        /// 既出の初期角度群（他振り子・親玉等）と初期位置・左右方向が被らないように複数候補から最も離れた角度を選ぶ。
        /// 複数振り子時に同じ動きへシンクロするのを防ぐ。
        /// </summary>
        private static double SampleDistinctInitialRotation(ref uint state, IReadOnlyList<double> avoidAngles)
        {
            if (avoidAngles == null || avoidAngles.Count == 0)
            {
                return SampleInitialRotation(ref state);
            }

            const int CANDIDATE_COUNT = 16;
            double lastAngle = avoidAngles[avoidAngles.Count - 1];
            double lastSin = Math.Sin(lastAngle);
            double lastCos = Math.Cos(lastAngle);

            double bestAngle = SampleInitialRotation(ref state);
            double bestScore = double.NegativeInfinity;

            for (int c = 0; c < CANDIDATE_COUNT; c++)
            {
                double candidate = c == 0 ? bestAngle : SampleInitialRotation(ref state);
                double candSin = Math.Sin(candidate);
                double candCos = Math.Cos(candidate);

                // 既出角度すべてとの最小円周距離
                double minAngularDist = Math.PI;
                for (int i = 0; i < avoidAngles.Count; i++)
                {
                    double diff = Math.Abs(WrapToPi(candidate - avoidAngles[i]));
                    if (diff < minAngularDist)
                    {
                        minAngularDist = diff;
                    }
                }

                // 直前の玉と左右反対側（初動方向が逆）であることを強く優先し、上下（cos符号）の違いも加点する
                double oppositeSideBonus = (candSin * lastSin < -0.02) ? 2.5 : 0.0;
                double oppositeHalfBonus = (candCos * lastCos < 0.0) ? 0.45 : 0.0;
                // 真下・真上付近（sin≈0）を避けて左右差を明確にする
                double sideClarity = Math.Abs(candSin) * 0.35;

                double score = minAngularDist + oppositeSideBonus + oppositeHalfBonus + sideClarity;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestAngle = candidate;
                }
            }

            return bestAngle;
        }

        private static double WrapToPi(double angleRad)
        {
            double wrapped = (angleRad + Math.PI) % TWO_PI;
            if (wrapped < 0.0)
            {
                wrapped += TWO_PI;
            }

            return wrapped - Math.PI;
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
