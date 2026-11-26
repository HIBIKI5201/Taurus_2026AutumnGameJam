using System;
using System.Collections.Generic;
using NUnit.Framework;
using Taurus.Runtime.Game;
using Taurus.Runtime.Pendulum;
using Taurus.Runtime.Stage;

namespace Taurus.Editor.Tests
{
    /// <summary>
    /// 決定論的モーション生成・真上通過検知・Wave設定・トップ3記録の EditMode 単体テスト。
    /// Unity の Window &gt; General &gt; Test Runner（EditModeタブ）から実行できる。
    /// ロジック変更後はこのテストが全件パスすることを確認すること。
    /// </summary>
    public class PendulumCoreTests
    {
        [Test]
        public void StageDefinition_Wave1To10_MatchesSpecCounts()
        {
            Assert.AreEqual(1, StageDefinition.GetDefaultWaveConfig(1).SinglePendulumCount);
            Assert.AreEqual(0, StageDefinition.GetDefaultWaveConfig(1).DoublePendulumCount);

            Assert.AreEqual(2, StageDefinition.GetDefaultWaveConfig(3).SinglePendulumCount);
            Assert.AreEqual(0, StageDefinition.GetDefaultWaveConfig(3).DoublePendulumCount);

            Assert.AreEqual(0, StageDefinition.GetDefaultWaveConfig(5).SinglePendulumCount);
            Assert.AreEqual(1, StageDefinition.GetDefaultWaveConfig(5).DoublePendulumCount);

            Assert.AreEqual(0, StageDefinition.GetDefaultWaveConfig(7).SinglePendulumCount);
            Assert.AreEqual(2, StageDefinition.GetDefaultWaveConfig(7).DoublePendulumCount);

            // Wave 8以降はWave 8と同一設定
            WaveConfig wave8 = StageDefinition.GetDefaultWaveConfig(8);
            WaveConfig wave10 = StageDefinition.GetDefaultWaveConfig(10);
            Assert.AreEqual(wave8.SinglePendulumCount, wave10.SinglePendulumCount);
            Assert.AreEqual(wave8.DoublePendulumCount, wave10.DoublePendulumCount);
            Assert.AreEqual(wave8.Choices.Count, wave10.Choices.Count);
        }

        [Test]
        public void MotionScheduleGenerator_FixedSeed_ProducesIdenticalSchedule()
        {
            WaveConfig wave4 = StageDefinition.GetDefaultWaveConfig(4);
            MotionSchedule a = MotionScheduleGenerator.Generate(987654321u, wave4.Choices);
            MotionSchedule b = MotionScheduleGenerator.Generate(987654321u, wave4.Choices);

            Assert.AreEqual(a.TotalExpectedTopCount, b.TotalExpectedTopCount);
            Assert.AreEqual(a.EndAngle, b.EndAngle, 1e-12);
            Assert.AreEqual(a.Segments.Count, b.Segments.Count);

            for (int i = 0; i < a.Segments.Count; i++)
            {
                Assert.AreEqual(a.Segments[i].ExpectedTopCount, b.Segments[i].ExpectedTopCount);
                Assert.AreEqual(a.Segments[i].StartAngle, b.Segments[i].StartAngle, 1e-12);
                Assert.AreEqual(a.Segments[i].EndAngle, b.Segments[i].EndAngle, 1e-12);
                Assert.AreEqual(a.Segments[i].Direction, b.Segments[i].Direction);
            }
        }

        [Test]
        public void MotionScheduleGenerator_AllWaves_SatisfyConstraintsAndMatchObservedCrossings()
        {
            double maxHeight = Math.Sin(Math.Sqrt(3.0));

            for (int wave = 1; wave <= 8; wave++)
            {
                WaveConfig config = StageDefinition.GetDefaultWaveConfig(wave);
                for (uint seed = 100u; seed < 120u; seed++)
                {
                    MotionSchedule schedule = MotionScheduleGenerator.Generate(seed + (uint)(wave * 7919), config.Choices);
                    Assert.AreEqual(8, schedule.Segments.Count);

                    for (int i = 0; i < schedule.Segments.Count; i++)
                    {
                        MotionSegment seg = schedule.Segments[i];
                        Assert.Greater(seg.TurnHeight, 0.0);
                        Assert.Less(seg.TurnHeight, maxHeight);

                        if (i > 0)
                        {
                            // 区間間の連続性と毎区間の方向反転
                            Assert.AreEqual(schedule.Segments[i - 1].EndAngle, seg.StartAngle, 1e-9);
                            Assert.AreEqual(-schedule.Segments[i - 1].Direction, seg.Direction);
                        }
                    }

                    // 60FPS と 低FPS(15FPS) の両方で実測 == 予定を検証
                    AssertObservedMatchesExpected(schedule, 1.0 / 60.0);
                    AssertObservedMatchesExpected(schedule, 1.0 / 15.0);
                }
            }
        }

        [Test]
        public void RecordStore_MergeAndParse_HandlesDuplicatesAndCorruption()
        {
            List<int> r = new List<int>();
            r = RecordStore.MergeRecords(r, 0);
            Assert.AreEqual(0, r.Count);

            r = RecordStore.MergeRecords(r, 3);
            r = RecordStore.MergeRecords(r, 7);
            r = RecordStore.MergeRecords(r, 3);
            r = RecordStore.MergeRecords(r, 12);
            r = RecordStore.MergeRecords(r, 5);

            CollectionAssert.AreEqual(new[] { 12, 7, 5 }, r);

            string serialized = RecordStore.SerializeRecords(r);
            List<int> roundTrip = RecordStore.ParseRecords(serialized);
            CollectionAssert.AreEqual(new[] { 12, 7, 5 }, roundTrip);

            // 破損データは空リストへフォールバック
            Assert.AreEqual(0, RecordStore.ParseRecords("{broken json").Count);
            Assert.AreEqual(0, RecordStore.ParseRecords(null).Count);
        }

        private static void AssertObservedMatchesExpected(MotionSchedule schedule, double stepSeconds)
        {
            TopCrossingDetector detector = new TopCrossingDetector();
            detector.Evaluate(schedule.EvaluateAngle(0.0), 0f);

            for (double t = stepSeconds; t < 8.0; t += stepSeconds)
            {
                detector.Evaluate(schedule.EvaluateAngle(t), (float)t);
            }

            detector.Evaluate(schedule.EvaluateAngle(8.0), 8f);
            Assert.AreEqual(schedule.TotalExpectedTopCount, detector.ObservedCount);
        }
    }
}
