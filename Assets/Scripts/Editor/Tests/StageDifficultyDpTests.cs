using NUnit.Framework;
using Taurus.Runtime.Pendulum;
using Taurus.Runtime.Stage;

namespace Taurus.Editor.Tests
{
    /// <summary>
    /// 数理モデル（密度・台形積分・L2ピーク評価）および前向き/後ろ向きDPの EditMode 単体テスト。
    /// </summary>
    public class StageDifficultyDpTests
    {
        [Test]
        public void ForwardDp_Wave1_MatchesExactAnalyticalExpectation()
        {
            // Wave 1: 70:0, 20:1, 10:2 (1秒あたり期待値 0.40) × 8秒 = 3.20
            WaveConfig wave1 = StageDefinition.GetDefaultWaveConfig(1);
            WaveDifficultyReport report = StageDifficultyEvaluator.EvaluateWave(wave1, 0.35f);

            Assert.IsTrue(report.AllConstraintsFeasible);
            Assert.AreEqual(3.20, report.TotalExpectedQuantityPerBob, 1e-6);
            Assert.AreEqual(0.40, report.TrapezoidalAverageDensity, 1e-6);
            Assert.AreEqual(0.40, report.L2RmsDensity, 1e-6);
        }

        [Test]
        public void L2PeakMetric_RatesPeakedWaveHigherThanFlatWaveWithSameAverage()
        {
            // 平坦構成：前後半とも密度 1.0/s
            WaveConfig flatWave = new WaveConfig(1, 0, new[]
            {
                new StageIntervalConfig("前半", 4f, new[] { new WeightedCountChoice(100, 1, 1f) }),
                new StageIntervalConfig("後半", 4f, new[] { new WeightedCountChoice(100, 1, 1f) }),
            });

            // 山場あり構成：前半 0.0/s、後半 2.0/s（平均は同じく 1.0/s）
            WaveConfig peakedWave = new WaveConfig(1, 0, new[]
            {
                new StageIntervalConfig("静", 4f, new[] { new WeightedCountChoice(100, 0, 1f) }),
                new StageIntervalConfig("山場", 4f, new[] { new WeightedCountChoice(100, 2, 1f) }),
            });

            WaveDifficultyReport flatReport = StageDifficultyEvaluator.EvaluateWave(flatWave, 0.5f);
            WaveDifficultyReport peakedReport = StageDifficultyEvaluator.EvaluateWave(peakedWave, 0.5f);

            Assert.AreEqual(flatReport.TrapezoidalAverageDensity, peakedReport.TrapezoidalAverageDensity, 1e-6);
            Assert.Greater(peakedReport.L2RmsDensity, flatReport.L2RmsDensity);
            Assert.Greater(peakedReport.BlendedDifficultyScore, flatReport.BlendedDifficultyScore);
        }

        [Test]
        public void BackwardDp_RuntimeSampling_StrictlySatisfiesQuantityBounds()
        {
            // 1秒あたり 0(60%), 1(30%), 2(10%) で 8 秒間、量制約 [3, 5] を強制
            WaveConfig constrainedWave = new WaveConfig(1, 0, new[]
            {
                new StageIntervalConfig(
                    "制約テスト",
                    8f,
                    new[]
                    {
                        new WeightedCountChoice(60, 0, 1f),
                        new WeightedCountChoice(30, 1, 1f),
                        new WeightedCountChoice(10, 2, 1f),
                    },
                    useQuantityConstraint: true,
                    minQuantity: 3,
                    maxQuantity: 5),
            });

            WaveDifficultyReport report = StageDifficultyEvaluator.EvaluateWave(constrainedWave);
            Assert.IsTrue(report.AllConstraintsFeasible);
            Assert.Greater(report.Intervals[0].ConstraintSatisfactionProbability, 0.0);

            // 50シードすべてで後ろ向きDPが [3, 5] を厳密に満たすことを検証
            for (uint i = 0; i < 50u; i++)
            {
                MotionSchedule schedule = MotionScheduleGenerator.Generate(1000u + i * 7919u, constrainedWave);
                Assert.GreaterOrEqual(schedule.TotalExpectedTopCount, 3);
                Assert.LessOrEqual(schedule.TotalExpectedTopCount, 5);
            }
        }

        [Test]
        public void BackwardDp_InfeasibleConstraint_IsDetected()
        {
            // 4秒間で最大 1回/秒 なのに Q_min = 6 を要求 → 到達不能
            WaveConfig impossibleWave = new WaveConfig(1, 0, new[]
            {
                new StageIntervalConfig(
                    "到達不能",
                    4f,
                    new[] { new WeightedCountChoice(100, 1, 1f) },
                    useQuantityConstraint: true,
                    minQuantity: 6,
                    maxQuantity: 8),
            });

            WaveDifficultyReport report = StageDifficultyEvaluator.EvaluateWave(impossibleWave);
            Assert.IsFalse(report.AllConstraintsFeasible);
            Assert.IsFalse(report.Intervals[0].HasValidPath);
            Assert.AreEqual(0.0, report.Intervals[0].ConstraintSatisfactionProbability, 1e-12);
        }

        [Test]
        public void StopRule_NoFitVsFirstOverflow_ProducesDistinctExactDistributions()
        {
            // 区間長 4 秒、候補は (重み50, 量1, 時間2秒) と (重み50, 量2, 時間3秒)
            WeightedCountChoice[] choices =
            {
                new WeightedCountChoice(50, 1, 2f),
                new WeightedCountChoice(50, 2, 3f),
            };

            WaveConfig noFitWave = new WaveConfig(1, 0, new[] { new StageIntervalConfig("NoFit", 4f, choices) }, DrawStopRule.StopWhenNoFit, 1f);
            WaveConfig overflowWave = new WaveConfig(1, 0, new[] { new StageIntervalConfig("Overflow", 4f, choices) }, DrawStopRule.StopOnFirstOverflow, 1f);

            WaveDifficultyReport noFit = StageDifficultyEvaluator.EvaluateWave(noFitWave);
            WaveDifficultyReport overflow = StageDifficultyEvaluator.EvaluateWave(overflowWave);

            // StopWhenNoFit: t=2 で 2s しか収まらないため必ず 2 回目も 2s を引き、全経路が量 2（確率100%）になる
            Assert.AreEqual(2.0, noFit.TotalExpectedQuantityPerBob, 1e-6);
            // StopOnFirstOverflow: t=2 で 3s を引くと即終了（量1、確率0.25）になるため期待値は 1.75
            Assert.AreEqual(1.75, overflow.TotalExpectedQuantityPerBob, 1e-6);
        }

        [Test]
        public void StructureDifficulty_SameTotalQuantity_OrdersSingle1LessThanSingle2LessThanDouble1LessThanDouble2()
        {
            // 全玉合計の総量がすべて 8.0（8秒で合計8回）になるよう 1玉あたりの量を揃えて比較
            WaveConfig single1 = new WaveConfig(1, 0, new[] { new WeightedCountChoice(100, 1, 1f) }); // 1玉 × 8 = 8
            WaveConfig single2 = new WaveConfig(2, 0, new[] { new WeightedCountChoice(50, 0, 1f), new WeightedCountChoice(50, 1, 1f) }); // 2玉 × 4 = 8
            WaveConfig double1 = new WaveConfig(0, 1, new[] { new WeightedCountChoice(50, 0, 1f), new WeightedCountChoice(50, 1, 1f) }); // 2玉 × 4 = 8
            WaveConfig double2 = new WaveConfig(0, 2, new[] { new WeightedCountChoice(75, 0, 1f), new WeightedCountChoice(25, 1, 1f) }); // 4玉 × 2 = 8

            WaveDifficultyReport rSingle1 = StageDifficultyEvaluator.EvaluateWave(single1);
            WaveDifficultyReport rSingle2 = StageDifficultyEvaluator.EvaluateWave(single2);
            WaveDifficultyReport rDouble1 = StageDifficultyEvaluator.EvaluateWave(double1);
            WaveDifficultyReport rDouble2 = StageDifficultyEvaluator.EvaluateWave(double2);

            // 全玉合計期待量はすべて 8.0 で同一
            Assert.AreEqual(8.0, rSingle1.TotalExpectedQuantityAllBobs, 1e-6);
            Assert.AreEqual(8.0, rSingle2.TotalExpectedQuantityAllBobs, 1e-6);
            Assert.AreEqual(8.0, rDouble1.TotalExpectedQuantityAllBobs, 1e-6);
            Assert.AreEqual(8.0, rDouble2.TotalExpectedQuantityAllBobs, 1e-6);

            // 同じ総量でも 1重×1 < 1重×2 < 2重×1 < 2重×2 の難易度序列になることを検証
            Assert.Less(rSingle1.BlendedDifficultyScore, rSingle2.BlendedDifficultyScore);
            Assert.Less(rSingle2.BlendedDifficultyScore, rDouble1.BlendedDifficultyScore);
            Assert.Less(rDouble1.BlendedDifficultyScore, rDouble2.BlendedDifficultyScore);
        }
    }
}
