# コード規約

SymphonyKillChord のコード規約（CG-xxx）から、ゲームジャムでも守るものを抜き出した。番号は元の規約と同じにしている。

## 設定

- CG-001 cs, md, txt のファイルは UTF-8 を使用する
- CG-002 AI もコードの実装・修正と PR の作成を行う。AI の変更を取り込む PR のレビューは人が行う。実装・修正は依頼者が明示的に頼んだときだけ行う（[AGENTS.md](../../AGENTS.md#作業の許可範囲)）

## 順序

CG-010 クラス・構造体のメンバーは次の順に並べる。

1. コンストラクタ
2. イベント
3. パブリックプロパティ
4. パブリック定数
5. パブリックメソッド
6. パブリックな入れ子の型（enum / class / struct）
7. 定数
8. インスペクター表示フィールド（`[SerializeField]`）
9. プライベートフィールド
10. Unity ライフサイクルメソッド（Awake, Start, Update など）
11. イベントハンドラメソッド（Handle〇〇）
12. プロテクト・仮想メソッド
13. プライベートメソッド
14. プライベートな入れ子の型
15. デバッグ機能

## サマリー・コメント

- CG-020 全てのメソッドにはサマリーを付ける
- CG-021 パブリックなプロパティとイベントにはサマリーを付ける
- CG-022 プロパティ・イベント・フィールドの一行のサマリーは一行で書く
```csharp
/// <summary> 移動速度。 </summary>
```
- CG-023 クラス・メソッド、または二行以上のサマリーは改行してインデントを上げる
```csharp
/// <summary>
///     プレイヤーを移動させる。
/// </summary>
```
- CG-024 コメントは基本的に日本語を使用する
- CG-025 日本語の最後は「。」で終える
- CG-027 `[SerializeField]` のフィールドには日本語の `Tooltip` を付ける
```csharp
[SerializeField, Tooltip("移動速度")]
private float _moveSpeed;
```
- CG-028 クラスにもサマリーを付ける

## 命名規則

- CG-030 フィールド変数はキャメルケースでプレフィックスに `_`
- CG-031 クラス・プロパティ・メソッドはパスカルケース
- CG-032 引数はキャメルケース
- CG-033 定数（const / static readonly）はアッパースネークケース
- CG-034 イベントのプレフィックスは `On`
- CG-035 イベントハンドラのメソッド名は `Handle〇〇`
- CG-036 インターフェースは `I` で始める
- CG-037 メソッドは動詞で始める
- CG-038 bool 型のプロパティは `Is` または `Has` で始める
- CG-039 uxml, uss の命名はロウワーチェーンケース
- CG-040 名前空間はフォルダ構成を反映する（[設計方針](DesignPhilosophy.md#フォルダと名前空間)）

## その他

- CG-050 `using` はファイルの先頭にまとめる
- CG-051 フィールドを公開するときはプロパティを使う。変更は `public set` ではなく意図が伝わるメソッドを介する
- CG-052 アクセス修飾子は必ず明示する
- CG-053 マジックナンバーは定数にする
- CG-054 一行のブロックでも波カッコを使う
- CG-055 1 ファイルに公開型は 1 つ。ファイル名を型名と一致させる
- CG-058 静的シングルトン（`static Instance`）を作らない。ServiceLocator へ登録するか、必要な箇所へ渡す
- CG-060 1 ファイル 400 行、1 メソッド 60 行を超えたら分割を検討する
- CG-062 値の変化の通知は C# の event より R3 の ReactiveProperty を優先する。トゥイーンは LitMotion を使う

## Unity API

- CG-070 データ用の ScriptableObject は、外部からは読み取り専用にし、`[SerializeField]` でだけ変更できるようにする
- CG-071 `FindObjectsByType` などでシーンを走査して依存を探さない。参照は `[SerializeField]` か Composition から渡す
- CG-072 `Camera.main` を直接参照しない。カメラは持ち主から受け取る

## ログ出力

- CG-080 `Debug.LogError` / `Debug.LogWarning` は `[{nameof(ClassName)}] メッセージ` の形式で書き、MonoBehaviour では第 2 引数に `this` を渡す
```csharp
Debug.LogError($"[{nameof(GameComposition)}] プレイヤーが設定されていません。", this);
```

## 非同期

- `async void` は Unity のイベント関数やイベントハンドラなど、戻り値を `void` にしなければならない場合だけ使う。その場合は本体全体を `try`/`catch` で囲み、`OperationCanceledException` 以外は `Debug.LogException` で出す。それ以外は `UniTask` / `Awaitable` を返す
