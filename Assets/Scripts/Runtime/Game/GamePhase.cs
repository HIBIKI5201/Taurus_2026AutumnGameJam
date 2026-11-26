namespace Taurus.Runtime.Game
{
    /// <summary>
    /// ゲーム全体の画面・進行状態を表す列挙型。
    /// 遷移フロー：Title →(Space)→ Rules →(Space/開始ボタン)→ Playing →(10秒判定)→ Success(1.1秒後次Wave) / Result(Spaceでリトライ、Ctrlでタイトルへ戻る)
    /// </summary>
    public enum GamePhase
    {
        /// <summary> タイトル画面（Spaceまたは開始ボタンでルール説明画面へ進む）。 </summary>
        Title,

        /// <summary> ルール説明画面（3種デモを表示し、Spaceまたは開始ボタンでWave 1へ進む）。 </summary>
        Rules,

        /// <summary> Waveプレイ中（10秒間：0〜8秒計画モーション＋8〜10秒減衰揺動）。 </summary>
        Playing,

        /// <summary> 正解表示中（約1.1秒間カウント入力を無効化し、その後自動で次Waveを開始）。 </summary>
        Success,

        /// <summary> リザルト画面（クリアWave数・トップ3記録。Spaceでリトライ、Ctrlまたはボタンでタイトルへ戻る）。 </summary>
        Result,
    }
}
