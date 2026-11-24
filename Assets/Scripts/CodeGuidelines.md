# コード規約

## 必須

- CG-01 ファイルは UTF-8 で保存する
- CG-02 1 ファイルに 1 クラス。ファイル名とクラス名を揃える（Unity が MonoBehaviour を見つけられなくなるため）
- CG-03 フィールドは `private` にし、インスペクターに出すものは `[SerializeField]` を付ける。`public` フィールドは使わない
- CG-04 名前空間 `Taurus.Runtime.<機能フォルダ>` を付ける（[設計方針](DesignPhilosophy.md#フォルダ)）

## 命名

- CG-10 クラス・メソッド・プロパティは `PascalCase`
- CG-11 フィールドは `_camelCase`、引数とローカル変数は `camelCase`
- CG-12 定数は `UPPER_SNAKE_CASE`
- CG-13 bool は `Is` / `Has` / `Can` で始める
- CG-14 イベントは `On〇〇`、それを受けるメソッドは `Handle〇〇`

## コメント

- CG-20 コメントは日本語で、分かりにくい所にだけ書く
- CG-21 クラスには何をするものか一行のサマリーを付ける

```csharp
/// <summary> プレイヤーの移動を行う。 </summary>
public class PlayerMover : MonoBehaviour
```

## ログ

- CG-30 エラー・警告ログはクラス名を先頭に付け、MonoBehaviour では `this` を渡す（Console からオブジェクトに飛べる）

```csharp
Debug.LogError($"[{nameof(PlayerMover)}] Rigidbody がありません。", this);
```

## 非同期

- CG-40 非同期処理は UniTask を使う。`async void` は使わず `async UniTaskVoid` にする
