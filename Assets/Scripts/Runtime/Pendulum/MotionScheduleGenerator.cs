using System;
using System.Collections.Generic;
using Taurus.Runtime.Stage;

namespace Taurus.Runtime.Pendulum
{
    /// <summary>
    /// 32bit 乱数シードから 0〜8 秒（1秒×8区間）の決定論的振り子スケジュールを生成・検算する純C#モジュール。
    /// 仕様書（pendulum-count-spec.md 第5章）および pendulum-count.html の生成アルゴリズムと完全一致させている。
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
        /// 指定シードと区間重み候補から決定論的スケジュールを生成する。
        /// 同じシード・同じ重みを渡せば必ず同一の軌道が再現される。
        /// </summary>
        public static MotionSchedule Generate(uint seed, IReadOnlyList<WeightedCountChoice> choices, int plannedSeconds = DEFAULT_PLANNED_SECONDS)
        {
            if (choices == null || choices.Count == 0)
            {
                throw new ArgumentException("抽選候補が空です。", nameof(choices));
            }

            // 万一制約を満たす終点が見つからない場合でも、シードをずらして再生成し誤ったスケジュールを出さない
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

        private static bool TryBuildSchedule(uint seed, IReadOnlyList<WeightedCountChoice> choices, int plannedSeconds, out MotionSchedule schedule)
        {
            uint state = seed;
            List<MotionSegment> segments = new List<MotionSegment>(plannedSeconds);
            int remaining = plannedSeconds;

            // 最初の進行方向はランダム（+1 または -1）、以降は毎区間必ず反転させる
            int direction = NextRandom01(ref state) < 0.5 ? 1 : -1;

            // Wave開始時の理不尽回避：sin(π/√3) > sinθ > sin(-π/√3) の範囲でsinθをランダムに決め、
            // cosθの符号（支点より下半分/上半分）も独立にランダムに選ぶ
            double initialSin = (NextRandom01(ref state) * 2.0 - 1.0) * INITIAL_SIN_LIMIT;
            double initialBaseAngle = Math.Asin(initialSin); // cosθ > 0（支点より下半分）側の principal 値
            bool startsAboveHorizontal = NextRandom01(ref state) < 0.5; // true: cosθ < 0（上半分）を選ぶ
            double rotation = startsAboveHorizontal ? Math.PI - initialBaseAngle : initialBaseAngle;

            int totalCount = 0;

            while (remaining > 0)
            {
                WeightedCountChoice choice = PickChoice(ref state, choices, remaining);
                int durationInt = Math.Max(1, (int)Math.Round(choice.DurationSeconds));

                // 次区間の終端高さと位相を決定（進行方向に応じて支点より上側の反対側位相を選ぶ）
                double alpha = NextTurnAlpha(ref state);
                double endPhase = direction > 0
                    ? Math.PI * 0.5 + alpha
                    : Math.PI * 1.5 - alpha;

                if (!TryChooseEndpoint(rotation, direction, choice.TopCount, endPhase, out double endAngle))
                {
                    schedule = null;
                    return false;
                }

                // 事前検算：真上通過数が抽選値と一致し、かつ真下を最低1回通っていることを最終保証する
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
                direction = -direction; // 仕様制約5: 次区間は必ず逆方向へ移る
                remaining -= durationInt;
            }

            schedule = new MotionSchedule(segments.ToArray(), totalCount, rotation, seed);
            return true;
        }

        /// <summary>
        /// 開始角度・進行方向・予定真上通過数・終端位相から、
        /// 「真上通過=N」「真下通過>=1」「最後の真上の後に真下を通過」を満たす終端連続角度を探索する。
        /// </summary>
        private static bool TryChooseEndpoint(double startAngle, int direction, int targetTopCount, double endPhase, out double endAngle)
        {
            double baseCycle = Math.Floor(startAngle / TWO_PI);

            for (int step = 1; step <= MAX_STEP_SEARCH; step++)
            {
                double candidate = endPhase + TWO_PI * (baseCycle + direction * step);

                // 進行方向と逆向きに進む候補は除外
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

                // N=0（フェイント区間）は真上を横切らず真下を通って反対側上端に着けば条件成立
                if (targetTopCount == 0)
                {
                    endAngle = candidate;
                    return true;
                }

                // N>0 の場合、最後の真上通過後に真下（lastTop ± π）を越えているか確認する
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

        /// <summary> 残り秒数に収まる候補から重み付き抽選で1つを選ぶ。 </summary>
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

        /// <summary> 終端高さ h ∈ (0, sin(√3)) を抽選し、対応する角度オフセット alpha = asin(h) を返す。 </summary>
        private static double NextTurnAlpha(ref uint state)
        {
            double h = HEIGHT_MARGIN + NextRandom01(ref state) * (MAX_TURN_HEIGHT - HEIGHT_MARGIN * 2.0);
            return Math.Asin(h);
        }

        /// <summary>
        /// Mulberry32 擬似乱数生成器（HTML参照実装とビット単位で同一）。
        /// UnityEngine.Random に依存しないため、プラットフォーム差なくシード再現できる。
        /// </summary>
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
