using System;
using System.Collections.Generic;

namespace Taurus.Runtime.Stage
{
    /// <summary>
    /// 1つの分割区間に対する前向きDP（厳密確率分布・期待値・分散）および
    /// 後ろ向きDP（到達可能性テーブル can_reach[t, q]）の評価結果を保持する。
    /// </summary>
    public class IntervalDpResult
    {
        /// <summary> 区間ラベル。 </summary>
        public string Label { get; }

        /// <summary> 区間開始時刻（秒）。 </summary>
        public float StartTime { get; }

        /// <summary> 区間終了時刻（秒）。 </summary>
        public float EndTime { get; }

        /// <summary> 区間長 L_k（秒）。 </summary>
        public float DurationSeconds { get; }

        /// <summary> 量制約 [Q_min, Q_max] を使用しているか。 </summary>
        public bool UseQuantityConstraint { get; }

        /// <summary> 最小値制約 Q_min。 </summary>
        public int MinQuantity { get; }

        /// <summary> 最大値制約 Q_max。 </summary>
        public int MaxQuantity { get; }

        /// <summary> 制約なしの前向きDP期待量 E[Q_k]。 </summary>
        public double UnconstrainedExpectedQuantity { get; }

        /// <summary> 制約 [Q_min, Q_max] 内での条件付き期待量 E[Q_k | Q_min <= Q_k <= Q_max]。 </summary>
        public double ConstrainedExpectedQuantity { get; }

        /// <summary> 難易度評価に採用する代表量（制約有効時は条件付き期待量、無効時は制約なし期待量）。 </summary>
        public double EffectiveExpectedQuantity => UseQuantityConstraint && HasValidPath
            ? ConstrainedExpectedQuantity
            : UnconstrainedExpectedQuantity;

        /// <summary> 区間密度 D_k = Q_k / L_k（時間あたりの負荷）。 </summary>
        public double Density => DurationSeconds > 0f ? EffectiveExpectedQuantity / DurationSeconds : 0.0;

        /// <summary> 累積量の分散 Var(Q_k)。 </summary>
        public double QuantityVariance { get; }

        /// <summary> 二乗平均平方根（L2評価） sqrt(E[Q_k^2])。 </summary>
        public double RmsQuantity { get; }

        /// <summary> 前向きDPで算出した厳密な制約達成確率 P(Q_min <= Q_k <= Q_max)。 </summary>
        public double ConstraintSatisfactionProbability { get; }

        /// <summary> 後ろ向きDPで開始状態 (t=0, q=0) から制約達成が可能かどうか（can_reach[0, 0]）。 </summary>
        public bool HasValidPath { get; }

        /// <summary> 最終累積量 q ごとの厳密確率分布（インデックス = 量 q）。 </summary>
        public IReadOnlyList<double> TerminalQuantityProbabilities { get; }

        /// <summary> 後ろ向きDPの到達可能性テーブル can_reach[tStep, q]（ランタイム重み補正用）。 </summary>
        public bool[,] CanReachTable { get; }

        /// <summary> 時間量子化ステップ数 T。 </summary>
        public int TimeSteps { get; }

        /// <summary> 追跡した最大量インデックス。 </summary>
        public int MaxTrackedQuantity { get; }

        /// <summary> 時間量子化ステップ幅 Δt（秒）。 </summary>
        public float TimeStepSeconds { get; }

        /// <summary> 区間DP評価結果を初期化する。 </summary>
        public IntervalDpResult(
            string label,
            float startTime,
            float endTime,
            float durationSeconds,
            bool useQuantityConstraint,
            int minQuantity,
            int maxQuantity,
            double unconstrainedExpectedQuantity,
            double constrainedExpectedQuantity,
            double quantityVariance,
            double rmsQuantity,
            double constraintSatisfactionProbability,
            bool hasValidPath,
            double[] terminalQuantityProbabilities,
            bool[,] canReachTable,
            int timeSteps,
            int maxTrackedQuantity,
            float timeStepSeconds)
        {
            Label = label;
            StartTime = startTime;
            EndTime = endTime;
            DurationSeconds = durationSeconds;
            UseQuantityConstraint = useQuantityConstraint;
            MinQuantity = minQuantity;
            MaxQuantity = maxQuantity;
            UnconstrainedExpectedQuantity = unconstrainedExpectedQuantity;
            ConstrainedExpectedQuantity = constrainedExpectedQuantity;
            QuantityVariance = quantityVariance;
            RmsQuantity = rmsQuantity;
            ConstraintSatisfactionProbability = constraintSatisfactionProbability;
            HasValidPath = hasValidPath;
            TerminalQuantityProbabilities = terminalQuantityProbabilities ?? Array.Empty<double>();
            CanReachTable = canReachTable;
            TimeSteps = timeSteps;
            MaxTrackedQuantity = maxTrackedQuantity;
            TimeStepSeconds = timeStepSeconds;
        }

        /// <summary> 指定した離散時刻・累積量からゴール制約へ到達可能か返す。 </summary>
        public bool CanReach(int timeStep, int quantity)
        {
            if (!UseQuantityConstraint)
            {
                return true;
            }

            if (CanReachTable == null || timeStep < 0 || timeStep > TimeSteps || quantity < 0 || quantity > MaxTrackedQuantity)
            {
                return false;
            }

            return CanReachTable[timeStep, quantity];
        }
    }
}
