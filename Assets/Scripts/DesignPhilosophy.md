# 設計方針

SymphonyKillChord の設計思想を、ゲームジャム向けに軽くしたもの。
「短期間で作り切る」「数人で同時に触っても壊れにくい」を優先し、レイヤー分けは最小限にする。

## フォルダと名前空間

| フォルダ | 名前空間 | 内容 |
| --- | --- | --- |
| `Assets/Scripts/Runtime` | `Taurus.Runtime.*` | ゲーム本体。エディタ専用の API を使わない |
| `Assets/Scripts/Editor` | `Taurus.Editor.*` | エディタ拡張・ツール |

名前空間はフォルダ構成をそのまま反映する（例: `Assets/Scripts/Runtime/Player/PlayerMover.cs` → `Taurus.Runtime.Player`）。

## 役割の分け方

機能ごとにフォルダを切り、その中で必要に応じて次の役割にクラスを分ける。小さな機能は無理に分けなくてよい。

- **Data**: パラメータや状態を持つクラス・ScriptableObject。ゲームロジックを持たない。
- **Logic**: ゲームのルールを処理するピュアクラス。`MonoBehaviour` を継承しない（`UnityEngine` の型を使うのはよい）。
- **View**: `MonoBehaviour`。入力を受け取り、表示・演出・GameObject の操作を行う。ルールの判断は Logic に任せる。
- **Composition**: シーンの初期化役。各クラスを生成してつなぐ。依存は SymphonyFramework の ServiceLocator に登録するか、直接渡す。

## 基本の考え

- KISS: 迷ったら単純な方を選ぶ。将来の拡張のための抽象化はしない。
- 参照は `[SerializeField]` か Composition から渡す。シーンを走査して探さない。
- 値の変化の通知は R3、非同期は UniTask、トゥイーンは LitMotion を使う。
