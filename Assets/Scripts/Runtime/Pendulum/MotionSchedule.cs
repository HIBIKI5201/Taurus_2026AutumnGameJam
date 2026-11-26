using System;
using System.Collections.Generic;

namespace Taurus.Runtime.Pendulum
{
    /// <summary>
    /// 0〜8秒の事前生成済み軌道スケジュールを保持し、任意の経過時刻に対する連続角度を評価する。
    /// ゲームの正解カウントは描画やフレームレートに依存させず、このクラスの TotalExpectedTopCount を正本とする。
    /// </summary>
    public class MotionSchedule
    {
        private readonly MotionSegment[] _segments;
        private readonly double _totalDuration;

        /// <summary> 構成区間の一覧（通常は1秒×8区間）。 </summary>
        public IReadOnlyList<MotionSegment> Segments => _segments;

        /// <summary> 全区間の予定真上通過回数の合計（この玉の正解数）。 </summary>
        public int TotalExpectedTopCount { get; }

        /// <summary> 計画区間終了時（t=8秒）の最終連続角度（ラジアン）。8〜10秒減衰運動の初期角度になる。 </summary>
        public double EndAngle { get; }

        /// <summary> 生成に使用した32bitシード値（再現デバッグ用）。 </summary>
        public uint Seed { get; }

        /// <summary> スケジュールを初期化する。 </summary>
        public MotionSchedule(MotionSegment[] segments, int totalExpectedTopCount, double endAngle, uint seed)
        {
            _segments = segments ?? Array.Empty<MotionSegment>();
            TotalExpectedTopCount = totalExpectedTopCount;
            EndAngle = endAngle;
            Seed = seed;

            double sum = 0.0;
            for (int i = 0; i < _segments.Length; i++)
            {
                sum += _segments[i].Duration;
            }

            _totalDuration = sum;
        }

        /// <summary>
        /// 指定経過時刻（秒）の連続角度（ラジアン）を SmoothStep 補間で評価する。
        /// 区間端（u=0, u=1）で微分値が0になるため、速度0で反転する自然な振り子軌道になる。
        /// </summary>
        public double EvaluateAngle(double elapsedSeconds)
        {
            if (_segments.Length == 0)
            {
                return 0.0;
            }

            // 8秒以降は計画区間の最終角度を返す（減衰運動は PendulumMotion 側が引き継ぐ）
            if (elapsedSeconds >= _totalDuration)
            {
                return EndAngle;
            }

            if (elapsedSeconds <= 0.0)
            {
                return _segments[0].StartAngle;
            }

            MotionSegment segment = _segments[0];
            for (int i = 0; i < _segments.Length; i++)
            {
                MotionSegment candidate = _segments[i];
                if (elapsedSeconds >= candidate.StartTime && elapsedSeconds < candidate.StartTime + candidate.Duration)
                {
                    segment = candidate;
                    break;
                }
            }

            // SmoothStep: e(u) = u^2 * (3 - 2u)
            double u = (elapsedSeconds - segment.StartTime) / segment.Duration;
            if (u < 0.0) u = 0.0;
            if (u > 1.0) u = 1.0;

            double ease = u * u * (3.0 - 2.0 * u);
            return segment.StartAngle + (segment.EndAngle - segment.StartAngle) * ease;
        }

        /// <summary> 指定時刻が属する区間インデックス（0始まり）を返す（デバッグ表示用）。 </summary>
        public int GetSegmentIndex(double elapsedSeconds)
        {
            if (_segments.Length == 0)
            {
                return -1;
            }

            if (elapsedSeconds >= _totalDuration)
            {
                return _segments.Length - 1;
            }

            for (int i = 0; i < _segments.Length; i++)
            {
                MotionSegment candidate = _segments[i];
                if (elapsedSeconds >= candidate.StartTime && elapsedSeconds < candidate.StartTime + candidate.Duration)
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
