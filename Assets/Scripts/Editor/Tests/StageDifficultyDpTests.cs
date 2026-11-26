using System;
using NUnit.Framework;
using Taurus.Runtime.Game;
using Taurus.Runtime.Pendulum;
using Taurus.Runtime.Stage;
using Taurus.Runtime.UI;

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

        [Test]
        public void SplitPoints_AddMoveRemove_PreservesTotalPeriodContinuity()
        {
            WaveConfig wave = new WaveConfig(1, 0, new[]
            {
                new WeightedCountChoice(60, 0, 1f),
                new WeightedCountChoice(40, 1, 1f),
            });

            // 初期 [0, 8] を中央で分割 → 2区間 [0, 4], [4, 8]
            Assert.IsTrue(wave.SplitLongestInterval());
            float[] splits = wave.GetSplitPoints();
            Assert.AreEqual(1, splits.Length);
            Assert.AreEqual(4f, splits[0], 1e-4f);

            // 分割点 p_1 を 3.0s へ移動 → [0, 3], [3, 8]（合計 8s 維持）
            Assert.IsTrue(wave.MoveSplitPoint(0, 3f));
            splits = wave.GetSplitPoints();
            Assert.AreEqual(3f, splits[0], 1e-4f);
            Assert.AreEqual(8f, wave.EndTimeSeconds, 1e-4f);

            // さらに最長区間 [3, 8] を分割 → 3区間、合計 8s 維持
            Assert.IsTrue(wave.SplitLongestInterval());
            Assert.AreEqual(2, wave.GetSplitPoints().Length);
            Assert.AreEqual(8f, wave.EndTimeSeconds, 1e-4f);

            // 分割点削除で 2 区間に戻り、合計 8s 維持
            Assert.IsTrue(wave.RemoveSplitPoint(0));
            Assert.AreEqual(1, wave.GetSplitPoints().Length);
            Assert.AreEqual(8f, wave.EndTimeSeconds, 1e-4f);
        }

        [Test]
        public void NestedConstraints_WaveAndIntervals_SatisfiedSimultaneouslyByBackwardDp()
        {
            // 前半4秒は区間制約 [1, 3]、後半4秒は区間制約 [2, 4]、さらにWave全体制約 [4, 5] を課す（入れ子区間制約）
            WeightedCountChoice[] choices =
            {
                new WeightedCountChoice(50, 0, 1f),
                new WeightedCountChoice(35, 1, 1f),
                new WeightedCountChoice(15, 2, 1f),
            };

            WaveConfig nestedWave = new WaveConfig(1, 0, new[]
            {
                new StageIntervalConfig("前半", 4f, choices, useQuantityConstraint: true, minQuantity: 1, maxQuantity: 3),
                new StageIntervalConfig("後半", 4f, choices, useQuantityConstraint: true, minQuantity: 2, maxQuantity: 4),
            });
            nestedWave.SetWaveQuantityConstraint(true, 4, 5);

            WaveDifficultyReport report = StageDifficultyEvaluator.EvaluateWave(nestedWave);
            Assert.IsTrue(report.AllConstraintsFeasible);
            Assert.IsTrue(report.WaveConstraintFeasible);
            Assert.Greater(report.WaveConstraintSatisfactionProbability, 0.0);

            // 50シードすべてで前半 [1,3]、後半 [2,4]、かつ Wave合計 [4,5] を厳密に満たすことを検証
            for (uint i = 0; i < 50u; i++)
            {
                MotionSchedule schedule = MotionScheduleGenerator.Generate(2000u + i * 7919u, nestedWave);
                Assert.GreaterOrEqual(schedule.TotalExpectedTopCount, 4);
                Assert.LessOrEqual(schedule.TotalExpectedTopCount, 5);

                int firstHalfCount = 0;
                int secondHalfCount = 0;
                foreach (MotionSegment seg in schedule.Segments)
                {
                    if (seg.StartTime < 4.0 - 1e-6)
                    {
                        firstHalfCount += seg.ExpectedTopCount;
                    }
                    else
                    {
                        secondHalfCount += seg.ExpectedTopCount;
                    }
                }

                Assert.GreaterOrEqual(firstHalfCount, 1);
                Assert.LessOrEqual(firstHalfCount, 3);
                Assert.GreaterOrEqual(secondHalfCount, 2);
                Assert.LessOrEqual(secondHalfCount, 4);
            }
        }

        [Test]
        public void NestedConstraints_ContradictoryWaveConstraint_IsDetectedAsInfeasible()
        {
            // 各区間は [1, 2] で到達可能だが、合計は最大 4 なのに Wave全体で [6, 8] を要求 → 入れ子矛盾を検出
            WeightedCountChoice[] choices = { new WeightedCountChoice(50, 0, 1f), new WeightedCountChoice(50, 1, 1f) };
            WaveConfig wave = new WaveConfig(1, 0, new[]
            {
                new StageIntervalConfig("前半", 4f, choices, useQuantityConstraint: true, minQuantity: 1, maxQuantity: 2),
                new StageIntervalConfig("後半", 4f, choices, useQuantityConstraint: true, minQuantity: 1, maxQuantity: 2),
            });
            wave.SetWaveQuantityConstraint(true, 6, 8);

            WaveDifficultyReport report = StageDifficultyEvaluator.EvaluateWave(wave);
            Assert.IsTrue(report.Intervals[0].HasValidPath);
            Assert.IsTrue(report.Intervals[1].HasValidPath);
            Assert.IsFalse(report.WaveConstraintFeasible);
            Assert.IsFalse(report.AllConstraintsFeasible);
        }

        [Test]
        public void InitialDirection_AlwaysMovesTowardBottomByGravity()
        {
            WaveConfig wave1 = StageDefinition.GetDefaultWaveConfig(1);
            for (uint i = 0; i < 100u; i++)
            {
                MotionSchedule schedule = MotionScheduleGenerator.Generate(3000u + i * 7919u, wave1);
                Assert.Greater(schedule.Segments.Count, 0);
                MotionSegment first = schedule.Segments[0];
                double sinTheta = Math.Sin(first.StartAngle);
                if (Math.Abs(sinTheta) > 1e-4)
                {
                    int expectedDir = sinTheta > 0.0 ? -1 : 1;
                    Assert.AreEqual(expectedDir, first.Direction, $"seed={3000u + i * 7919u}, angle={first.StartAngle}");
                }
            }
        }

        [Test]
        public void TriplePendulum_StructureDifficulty_OrdersHigherThanDouble()
        {
            // 全玉合計期待量をすべて 12.0 に揃えて、3重×1 が 2重×1 より高く、3重×2 が 2重×2 より高いことを検証
            WaveConfig double1 = new WaveConfig(0, 1, new[] { new StageIntervalConfig("d1", 8f, new[] { new WeightedCountChoice(25, 0), new WeightedCountChoice(75, 1) }) });
            WaveConfig triple1 = new WaveConfig(0, 0, new[] { new StageIntervalConfig("t1", 8f, new[] { new WeightedCountChoice(50, 0), new WeightedCountChoice(50, 1) }) }, triplePendulumCount: 1);
            WaveConfig double2 = new WaveConfig(0, 2, new[] { new StageIntervalConfig("d2", 8f, new[] { new WeightedCountChoice(62, 0), new WeightedCountChoice(38, 1) }) });
            WaveConfig triple2 = new WaveConfig(0, 0, new[] { new StageIntervalConfig("t2", 8f, new[] { new WeightedCountChoice(75, 0), new WeightedCountChoice(25, 1) }) }, triplePendulumCount: 2);

            WaveDifficultyReport rD1 = StageDifficultyEvaluator.EvaluateWave(double1);
            WaveDifficultyReport rT1 = StageDifficultyEvaluator.EvaluateWave(triple1);
            WaveDifficultyReport rD2 = StageDifficultyEvaluator.EvaluateWave(double2);
            WaveDifficultyReport rT2 = StageDifficultyEvaluator.EvaluateWave(triple2);

            Assert.Greater(rT1.StructureDifficultyMultiplier, rD1.StructureDifficultyMultiplier);
            Assert.Greater(rT2.StructureDifficultyMultiplier, rD2.StructureDifficultyMultiplier);
            Assert.Greater(rT1.BlendedDifficultyScore, rD1.BlendedDifficultyScore);
            Assert.Greater(rT2.BlendedDifficultyScore, rD2.BlendedDifficultyScore);
        }

        [Test]
        public void Optimal15WavePreset_AllWavesFeasibleAndSatisfyCurveProfiles()
        {
            WaveConfig[] waves = StageDefinition.CreateOptimal15Waves();
            Assert.AreEqual(15, waves.Length);

            for (int i = 0; i < waves.Length; i++)
            {
                WaveDifficultyReport report = StageDifficultyEvaluator.EvaluateWave(waves[i]);
                Assert.IsTrue(report.AllConstraintsFeasible, $"Wave {i + 1} is infeasible");
                Assert.IsTrue(report.UseWaveQuantityConstraint, $"Wave {i + 1} should use wave constraint");
                Assert.GreaterOrEqual(report.WaveMinQuantity, 1, $"Wave {i + 1} must guarantee Q_min >= 1");

                // ランタイム生成でも親区間制約を 100% 満たすことを複数シードで検証
                for (uint s = 0; s < 10u; s++)
                {
                    MotionSchedule schedule = MotionScheduleGenerator.Generate(5000u + (uint)(i * 100) + s * 7919u, waves[i]);
                    Assert.GreaterOrEqual(schedule.TotalExpectedTopCount, report.WaveMinQuantity);
                    Assert.LessOrEqual(schedule.TotalExpectedTopCount, report.WaveMaxQuantity);
                }
            }

            // 二次曲線型（W6）は L2 RMS 密度が台形平均密度を大きく上回り、平坦型（W4）はほぼ一致する
            WaveDifficultyReport w4Flat = StageDifficultyEvaluator.EvaluateWave(waves[3]);
            WaveDifficultyReport w6Quad = StageDifficultyEvaluator.EvaluateWave(waves[5]);
            Assert.Less(w4Flat.L2RmsDensity - w4Flat.TrapezoidalAverageDensity, 0.01);
            Assert.Greater(w6Quad.L2RmsDensity - w6Quad.TrapezoidalAverageDensity, 0.05);
        }

        [Test]
        public void AudioSilenceTrimAndResultRank_WorkAsExpected()
        {
            // 先頭 4 フレーム無音、5 フレーム目（index 4）で発音開始する2chサンプル
            float[] samples = new float[10 * 2];
            samples[4 * 2] = 0.25f;
            Assert.AreEqual(4, GameAudioHooks.FindFirstAudibleFrame(samples, 2, 0.015f));

            StringAssert.Contains("S RANK", HUDController.FormatResultRankLabel(15, true));
            StringAssert.Contains("NEW RECORD", HUDController.FormatResultRankLabel(15, true));
            StringAssert.Contains("A RANK", HUDController.FormatResultRankLabel(10, false));
            StringAssert.Contains("B RANK", HUDController.FormatResultRankLabel(4, false));
            StringAssert.Contains("TRY AGAIN", HUDController.FormatResultRankLabel(0, false));
        }

        [Test]
        public void ProjectStageDefinitionAsset_AllWavesGenerateFull8SecondsAndNeverZero()
        {
            StageDefinition asset = UnityEditor.AssetDatabase.LoadAssetAtPath<StageDefinition>("Assets/Settings/StageDefinition.asset");
            Assert.IsNotNull(asset, "Assets/Settings/StageDefinition.asset が存在すること");
            Assert.GreaterOrEqual(asset.Waves.Count, 8);

            for (int w = 1; w <= asset.Waves.Count; w++)
            {
                WaveConfig config = asset.GetWaveConfig(w);
                WaveDifficultyReport report = asset.EvaluateWave(w);
                Assert.AreEqual(8f, report.TotalDuration, 1e-3f, $"Wave {w} の期間が 8 秒であること");
                Assert.IsTrue(report.AllConstraintsFeasible, $"Wave {w} の制約が到達可能であること");

                int nonZeroCount = 0;
                for (uint s = 0; s < 20u; s++)
                {
                    MotionSchedule schedule = MotionScheduleGenerator.Generate(9000u + (uint)(w * 100) + s * 7919u, config);
                    Assert.AreEqual(8, schedule.Segments.Count, $"Wave {w} は 8 秒分のセグメントを生成すること");
                    if (schedule.TotalExpectedTopCount > 0)
                    {
                        nonZeroCount++;
                    }
                }

                Assert.AreEqual(20, nonZeroCount, $"Wave {w} は全シードで 0 回にならないこと（最低1回保証）");
            }
        }
    }
}
