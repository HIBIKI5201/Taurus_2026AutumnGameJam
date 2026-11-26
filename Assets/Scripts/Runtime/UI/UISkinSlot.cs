namespace Taurus.Runtime.UI
{
    /// <summary> UIスキンで差し替えられる画像の種類。シーン上の Image は、どの種類を表示するかだけを持つ。 </summary>
    /// <remarks> 並びはシリアライズされた値になるため、途中に挿入せず末尾に足す。 </remarks>
    public enum UISkinSlot
    {
        None = 0,

        // タイトル・ルール説明
        TitleLogo = 10,
        StartButton = 11,
        RulesIllustration = 12,
        RulesCard = 13,

        // インゲームHUD
        HudTopFrame = 20,
        WaveFrame = 21,
        TimerFrame = 22,
        CounterFrame = 23,
        PlusButton = 24,
        MinusButton = 25,
        SpaceKeyLabel = 26,
        CtrlKeyLabel = 27,

        // リザルト
        GameOverHeading = 30,
        ResultClearedFrame = 31,
        ResultRecordsFrame = 32,
        RetryIcon = 33,
        ReturnToTitleIcon = 34,
    }
}
