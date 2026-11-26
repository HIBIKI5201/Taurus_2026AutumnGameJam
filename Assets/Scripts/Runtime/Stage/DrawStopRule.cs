namespace Taurus.Runtime.Stage
{
    /// <summary>
    /// 区間内の抽選をいつ終了するかを表すルール。
    /// 前向きDPの確率遷移および後ろ向きDPの到達可能性計算、ランタイム抽選で共通して使用する。
    /// </summary>
    public enum DrawStopRule
    {
        /// <summary> 残り時間に収まる候補が1つでもあれば継続し、収まる候補が無くなったら終了する。 </summary>
        StopWhenNoFit = 0,

        /// <summary> 次の抽選で制限時間を超える候補を引いた時点で即座に終了する。 </summary>
        StopOnFirstOverflow = 1,
    }
}
