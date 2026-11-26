# 設計方針

## フォルダ

| フォルダ | 内容 |
| --- | --- |
| `Assets/Scripts/Runtime` | ゲーム本体のコード。機能ごとにフォルダを切る（例: `Runtime/Player`） |
| `Assets/Scripts/Develop` | デバッグ用のコード。製品に入れたくないもの |
| `Assets/Scripts/Editor` | エディタ拡張 |
| `Assets/Scripts/Shaders` | シェーダー |

名前空間は `Taurus.<Runtime / Develop / Editor>` の後ろに機能フォルダ名を付ける。

| フォルダ | 名前空間の例 |
| --- | --- |
| `Runtime/Player` | `Taurus.Runtime.Player` |
| `Develop/Cheat` | `Taurus.Develop.Cheat` |
| `Editor/LevelTool` | `Taurus.Editor.LevelTool` |

## 書き方

- MonoBehaviour で素直に書いてよい。レイヤー分け・インターフェース・抽象化は、必要になるまで作らない。
- 1 クラスが大きくなって触りにくくなったら分ける。最初から分けない。
- 調整したい数値は `[SerializeField]` にして、インスペクターで触れるようにする。
- 他のオブジェクトへの参照は `[SerializeField]` でつなぐ。

## チームで壊さないために

- 1 つのシーンを複数人で同時に編集しない。自分の作業は Prefab か自分用のシーンで行い、メインシーンへの配置は担当者が行う。
- 他の人が作ったクラスを大きく書き換えるときは、一声かける。
- 迷ったら単純な方を選ぶ。
