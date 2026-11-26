# Discord Bot ↔ Antigravity / Claude 受付ハーネス

Discord Bot としてログインし、受信したメッセージを **返信元メッセージ・スレッド開始メッセージ・直近の会話履歴** と合わせてローカルの Antigravity（`ConversationService`）へ投入し、結果を Discord に返信する軽量ハーネスです。
Node.js 22+ の組み込み `WebSocket` / `fetch` のみで動作するため、`npm install` は不要です。

## Antigravity 優先・Claude 予備
依頼はまず Antigravity が処理します。次の場合は Claude Code（`claude -p`）が同じ依頼を引き継ぎます。

- Antigravity の残り利用枠が 10% 以下のとき（`DISCORD_AGY_MIN_REMAINING` で変更可）。
  - 依頼を受けるたびに、Antigravity の `RetrieveUserQuotaSummary` で 5 時間枠と週枠の残りを取得し、少ない方で判定します。
  - 見るのは `DISCORD_AGY_MODEL` が属するモデルグループの枠です（既定の flash / pro なら Gemini）。
  - 残量を取得できなかったときは判定を飛ばし、Antigravity を試します。

- Antigravity が利用制限に掛かったとき。`RESOURCE_EXHAUSTED`・`rate limit`・`quota`・HTTP 429 などのエラーで判定します。
  - 会話を作る段階で弾かれた場合と、実行途中で止まった場合の両方が対象です。
  - 途中で止まった場合、Claude には Antigravity の会話ID を渡し、残っている変更や作業ブランチを確認してから続きを仕上げるよう指示します。
- `ANTIGRAVITY_LS_ADDRESS` / `ANTIGRAVITY_CSRF_TOKEN` が未設定のとき。

利用制限を一度検知すると、`DISCORD_AGY_COOLDOWN_MS`（デフォルト 30 分）の間は Antigravity を飛ばし、最初から Claude で処理します。
切り替えたときは Discord に「Claude に切り替えて続けます…」と通知し、最終回答の末尾に「（処理: Claude）」を付けます。

Claude はリポジトリ直下で起動します。Claude Code CLI がインストール済みでログインしていることが前提です。
Discord 経由の依頼なので全許可（`bypassPermissions`）にはしていません。編集の自動承認（`acceptEdits`）と、許可ツールの明示（`DISCORD_CLAUDE_ALLOWED_TOOLS`）に留めています。
許可リストにないコマンドは実行されません。Claude に必要な操作が増えたら、この許可リストに足してください。

## 依頼は 1 件ずつ順番に処理する
同じマシン・同じリポジトリを共用しているので、2 件を同時に進めるとブランチ移動がぶつかります。そのためハーネスは依頼を 1 件ずつ処理します。

- 処理中に届いた依頼には「前の作業が終わるまで待機します（先に N 件あります）」と返し、届いた順に実行します。
- 次の依頼は、前の依頼が終わって `main` に戻してから始めます。
- `!ping` と `!status` は待たずにすぐ答えます。`!status` には実行中と待機中の件数が出ます。
- Antigravity には会話を止める API がなく、タイムアウトしても裏で作業が続きます。そのため、タイムアウトしたときは実行が止まるまで次の依頼を始めません。待つのは最長 `DISCORD_AGY_DRAIN_MS`（既定 30 分）です。

## 作業後は main に戻る
依頼の処理が終わるたびに、ハーネスがリポジトリを `main` に戻します（`git switch main` → `git pull --ff-only origin main`）。成功・失敗・エラーのどれで終わっても同じです。

- 未コミットの変更は、git の通常の動きどおり `main` へ持ち越します。変更を捨てたり、強制的に切り替えたりはしません。
  - 変更が衝突して切り替えられないときは、Discord に警告を出します。マージやリベースの途中のときも同じです。
- エージェントには「作業ブランチの変更は終える前にコミット＆プッシュする」よう指示しています。
- 無効にするなら `DISCORD_RETURN_TO_MAIN=false`、戻す先を変えるなら `DISCORD_MAIN_BRANCH` を設定します。

## 1. Discord Developer Portal 側の事前設定
1. Bot を作成したアプリの **Bot** タブを開く。
2. **Privileged Gateway Intents** の **`MESSAGE CONTENT INTENT`** を **ON** にして保存する。
3. **OAuth2 > URL Generator** で `bot` スコープを選び、以下の権限でサーバーへ招待する：
   - `View Channels`（チャンネルを表示）
   - `Send Messages`（メッセージを送る）
   - `Send Messages in Threads`（Threads でメッセージを送る）
   - `Read Message History`（メッセージ履歴を読む — **履歴・返信元取得に必須**）

## 2. `.env` の設定
リポジトリ直下の `.env`（Git管理外）に以下を追記します：

```env
DISCORD_BOT_TOKEN=ここにBotのトークンを貼る

# 以下は任意設定
# 反応を許可するチャンネルID（カンマ区切り。未指定なら全チャンネル）
# DISCORD_ALLOWED_CHANNEL_IDS=123456789012345678

# 反応を許可するユーザーID（カンマ区切り。セキュリティのため設定推奨）
# DISCORD_ALLOWED_USER_IDS=987654321098765432

# true: @Bot メンション / Bot への返信 / DM のみ反応（デフォルト）
# DISCORD_REQUIRE_MENTION=true
# DISCORD_REPLY_TO_BOT_TRIGGERS=true

# 会話履歴の取得件数（デフォルト 15、最大 50）
# DISCORD_HISTORY_LIMIT=15

# Antigravity で使うモデル（flash / pro / flash_lite）
# DISCORD_AGY_MODEL=flash

# Antigravity の残り利用枠がこの割合以下なら最初から Claude を使う（0〜1、デフォルト 0.1 = 10%）
# DISCORD_AGY_MIN_REMAINING=0.1

# 利用制限を検知した後、Antigravity を飛ばして Claude を使う期間（ミリ秒、デフォルト 30 分）
# DISCORD_AGY_COOLDOWN_MS=1800000

# 作業後に戻すブランチ（無効にするなら DISCORD_RETURN_TO_MAIN=false）
# DISCORD_RETURN_TO_MAIN=true
# DISCORD_MAIN_BRANCH=main

# Claude 予備の設定
# DISCORD_CLAUDE_ENABLED=true
# DISCORD_CLAUDE_BIN=claude.cmd            # Windows の既定。PATH 外なら絶対パスを指定
# DISCORD_CLAUDE_MODEL=                    # 空なら Claude Code の既定モデル
# DISCORD_CLAUDE_PERMISSION_MODE=acceptEdits
# DISCORD_CLAUDE_ALLOWED_TOOLS=Read,Edit,Write,Glob,Grep,Bash(git:*),Bash(gh:*),Bash(unity:*),Bash(node:*),Bash(rg:*)
```

## 3. 使い方
- `@Bot !ping` : 疎通確認
- `@Bot !status` : Bot・Antigravity 接続状態（利用制限中か）・残り利用枠・Claude 予備の設定・履歴設定・実行中タスク数の確認
- `@Bot <依頼内容>` : 直近履歴を含めて Antigravity で処理し結果を返信（利用制限時は Claude が代わりに処理）
- 特定メッセージへ「返信」＋ `@Bot これについて教えて` : 返信先メッセージの内容を明示的に参照して回答
- Bot の返信へそのまま「返信」: メンション無しでも会話の続きとして応答
