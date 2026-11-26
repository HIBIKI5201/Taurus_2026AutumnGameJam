using System;
using System.Collections.Generic;
using UnityEngine;

namespace Taurus.Runtime.Stage
{
    /// <summary>
    /// 1つのWave全体に対する難易度評価結果（区分線形曲線・台形積分平均・L2ピーク評価・全区間DP結果）を保持する。
    /// </summary>
    public class WaveDifficultyReport
    {
        /// <summary> 期間開始時刻 s（秒）。 </summary>
        public float StartTime { get; }

        /// <summary> 期間終了時刻 e（秒）。 </summary>
        public float EndTime { get; }

        /// <summary> 全体時間 e - s（秒）。 </summary>
        public float TotalDuration { get; }

        /// <summary> 玉の総数（単振り子1玉＋二重振り子2玉）。 </summary>
        public int TotalBobCount { get; }

        /// <summary> 玉1個あたりの総期待量 Σ Q_k。 </summary>
        public double TotalExpectedQuantityPerBob { get; }

        /// <summary> Wave全体（全玉合計）の総期待量。 </summary>
        public double TotalExpectedQuantityAllBobs => TotalExpectedQuantityPerBob * TotalBobCount;

        /// <summary> 区分線形関数 f(x) の台形積分を全体時間で割った平均密度（玉1個あたり）。 </summary>
        public double TrapezoidalAverageDensity { get; }

        /// <summary> 区分線形関数 f(x) の二乗積分に基づく L2 ノルム（RMS密度、ピーク重視評価）。 </summary>
        public double L2RmsDensity { get; }

        /// <summary> 区間密度の最大値 max_k D_k。 </summary>
        public double PeakDensity { get; }

        /// <summary> 複数本・二重振り子による認知負荷倍率（1重×1 &lt; 1重×2 &lt; 2重×1 &lt; 2重×2）。 </summary>
        public double StructureDifficultyMultiplier { get; }

        /// <summary> 平均密度と L2 ピーク評価をブレンドし、玉数と構成負荷倍率を掛けた総合難易度スコア。 </summary>
        public double BlendedDifficultyScore { get; }

        /// <summary> すべての区間で量制約 [Q_min, Q_max] が到達可能（実行可能）かどうか。 </summary>
        public bool AllConstraintsFeasible { get; }

        /// <summary> 各分割区間のDP評価結果一覧。 </summary>
        public IReadOnlyList<IntervalDpResult> Intervals { get; }

        /// <summary> 区分線形難易度曲線 f(x) の頂点列（x=時刻, y=密度 D）。 </summary>
        public IReadOnlyList<Vector2> CurvePoints { get; }

        /// <summary> Wave難易度評価レポートを初期化する。 </summary>
        public WaveDifficultyReport(
            float startTime,
            float endTime,
            int totalBobCount,
            double totalExpectedQuantityPerBob,
            double trapezoidalAverageDensity,
            double l2RmsDensity,
            double peakDensity,
            double structureDifficultyMultiplier,
            double blendedDifficultyScore,
            bool allConstraintsFeasible,
            IntervalDpResult[] intervals,
            Vector2[] curvePoints)
        {
            StartTime = startTime;
            EndTime = endTime;
            TotalDuration = Math.Max(0.0001f, endTime - startTime);
            TotalBobCount = totalBobCount;
            TotalExpectedQuantityPerBob = totalExpectedQuantityPerBob;
            TrapezoidalAverageDensity = trapezoidalAverageDensity;
            L2RmsDensity = l2RmsDensity;
            PeakDensity = peakDensity;
            StructureDifficultyMultiplier = structureDifficultyMultiplier;
            BlendedDifficultyScore = blendedDifficultyScore;
            AllConstraintsFeasible = allConstraintsFeasible;
            Intervals = intervals ?? Array.Empty<IntervalDpResult>();
            CurvePoints = curvePoints ?? Array.Empty<Vector2>();
        }
    }
}
