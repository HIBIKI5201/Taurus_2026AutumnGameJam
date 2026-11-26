# Discord Bot ↔ Antigravity 受付ハーネス

Discord Bot としてログインし、受信したメッセージを **返信元メッセージ・スレッド開始メッセージ・直近の会話履歴** と合わせてローカルの Antigravity（`ConversationService`）へ投入し、結果を Discord に返信する軽量ハーネスです。
Node.js 22+ の組み込み `WebSocket` / `fetch` のみで動作するため、`npm install` は不要です。

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
```

## 3. 使い方
- `@Bot !ping` : 疎通確認
- `@Bot !status` : Bot・Antigravity 接続状態と履歴設定・実行中タスク数の確認
- `@Bot <依頼内容>` : 直近履歴を含めて Antigravity で処理し結果を返信
- 特定メッセージへ「返信」＋ `@Bot これについて教えて` : 返信先メッセージの内容を明示的に参照して回答
- Bot の返信へそのまま「返信」: メンション無しでも会話の続きとして応答
