# 作業の入口
- このリポジトリは Taurus 2026 秋ゲームジャムの Unity プロジェクト（Unity 6000.3 / URP）。期間が短いので、完成と動作を優先し、設計は必要なだけにとどめる。
- 現在のユーザー指示を優先する。既存の未コミット変更は保持し、今回の対象パスだけを stage する。
- まず対象・完了条件・必要な検証を決める。ファイル名探索は対象フォルダ内の `rg --files -g`、本文探索は `rg -n` で絞る。

# コーディング規則
- C# の変更・レビュー時は [設計方針](Assets/Scripts/DesignPhilosophy.md) と [コード規約](Assets/Scripts/CodeGuidelines.md) を読む。
- 外部アセット・NuGet パッケージは `Assets/AssetStoreTools`（サブモジュール）に置く。ここを変えたらサブモジュール側でコミット・プッシュしてから、本体の参照を更新する。

# 作業の許可範囲
- コードの実装・修正は、依頼者が明示的に頼んだときに行う。レビュー・調査だけの依頼ではコードを変更しない。
- 依頼で指定された出力先・Git 操作・Unity 操作・外部送信の制限は、それぞれ独立した条件として守る。作業を委任するときも、そのまま渡す。

# Unity の操作
- Unity の操作は Unity CLI（`unity`）で行う。起動は `unity open` → `unity status --until-ready`、コンパイル確認は `unity recompile`（対象は `--project-path`）。
- recompile・テスト・Play の前に、未保存の Scene・Prefab がないか、Play 中でないかを確かめる。あればユーザーに保存・破棄・停止を確認する。自分が始めた Play は止めてよい。

# ブランチ・PR
- main を直接編集・コミットしない。作業は `feature/<作業名>` ブランチで行い、main へ PR を出す。エージェントの作業ブランチは `feature/<作業名>/agent` にする。
- PR は `.github/PULL_REQUEST_TEMPLATE.md` に従い、確認済み・未確認を正確に書く。マージ可否は現在のユーザー指示に従う。
