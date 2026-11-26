namespace Taurus.Runtime.Stage
{
    /// <summary>
    /// 定義済み Wave 数を超えた Wave 番号が要求されたときの振る舞い。
    /// </summary>
    public enum WaveOverflowBehavior
    {
        /// <summary> 末尾の Wave 設定を繰り返し使用する（従来互換）。 </summary>
        ClampToLast = 0,

        /// <summary> Wave 1 から先頭へ戻ってループする。 </summary>
        LoopFromStart = 1,

        /// <summary> 末尾の数 Wave（LoopTailCount）をループする。 </summary>
        LoopLastSection = 2,
    }
}
