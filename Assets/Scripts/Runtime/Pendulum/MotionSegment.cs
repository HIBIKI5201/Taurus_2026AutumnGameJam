namespace Taurus.Runtime.Pendulum
{
    /// <summary>
    /// 1区間分（基本1秒）の決定論的振り子軌道データを保持する不変構造体。
    /// MotionScheduleGenerator が事前生成し、MotionSchedule が毎フレーム SmoothStep で補間評価する。
    /// </summary>
    public readonly struct MotionSegment
    {
        /// <summary> 区間の開始時刻（秒、0〜7）。 </summary>
        public double StartTime { get; }

        /// <summary> 区間の長さ（秒、基本1.0）。 </summary>
        public double Duration { get; }

        /// <summary> この区間の予定真上通過回数（0回の場合はフェイント区間）。 </summary>
        public int ExpectedTopCount { get; }

        /// <summary> 回転方向（+1 または -1）。隣接する区間とは必ず逆符号になる。 </summary>
        public int Direction { get; }

        /// <summary> 区間開始時の連続角度（ラジアン、-π..πに折り返さない累積値）。 </summary>
        public double StartAngle { get; }

        /// <summary> 区間終了時の連続角度（ラジアン）。次区間の StartAngle と一致する。 </summary>
        public double EndAngle { get; }

        /// <summary> 区間終端の支点基準高さ h = -cos(θ)。0 &lt; h &lt; sin(√3) の範囲に収まる。 </summary>
        public double TurnHeight { get; }

        /// <summary> 区間データを初期化する。 </summary>
        public MotionSegment(
            double startTime,
            double duration,
            int expectedTopCount,
            int direction,
            double startAngle,
            double endAngle,
            double turnHeight)
        {
            StartTime = startTime;
            Duration = duration;
            ExpectedTopCount = expectedTopCount;
            Direction = direction;
            StartAngle = startAngle;
            EndAngle = endAngle;
            TurnHeight = turnHeight;
        }
    }
}
