#!/usr/bin/env node
/**
 * Discord Bot ↔ Antigravity 受付ハーネス（Node.js 22+ 組み込み API のみ・外部依存ゼロ）
 *
 * 【仕組み】
 * 1. Discord Gateway v10 に Bot としてログインし、メッセージ（MESSAGE_CREATE）を受信する。
 * 2. 受付条件（@Bot メンション / Bot への返信 / DM、および許可チャンネル・許可ユーザー）を満たしたら、
 *    - 返信元メッセージ（message_reference）
 *    - チャンネル／スレッド情報と直近の会話履歴（デフォルト 15 件）
 *    - スレッド開始メッセージ（スレッド内で履歴外になった場合）
 *    を Discord REST API で取得し、文脈付きプロンプトとして Antigravity ConversationService へ投入する。
 * 3. GetConversation で処理完了（RUN_STATE_IDLE）を待機し、
 *    transcript.jsonl から最終応答を抽出して Discord へ返信する（2000文字超は自動分割）。
 * 4. Antigravity が利用制限（レート制限・クォータ超過）に掛かった場合、または未設定の場合は、
 *    Claude Code CLI（`claude -p`）へ同じ依頼を引き継いで処理する（Antigravity 優先・Claude 予備）。
 *    利用制限を検知したら DISCORD_AGY_COOLDOWN_MS の間は Antigravity を飛ばして Claude を直接使う。
 *
 * 【起動方法】
 *   node Tools/DiscordHarness/bot.mjs
 *   ※ .env に DISCORD_BOT_TOKEN=... を設定しておくこと。
 */

import { spawn } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const REPO_ROOT = path.resolve(__dirname, "..", "..");

// ---------- .env ローダー ----------
function loadEnvFile(filePath) {
  if (!fs.existsSync(filePath)) return;
  const lines = fs.readFileSync(filePath, "utf8").split(/\r?\n/);
  for (const rawLine of lines) {
    const line = rawLine.trim();
    if (!line || line.startsWith("#")) continue;
    const eqIndex = line.indexOf("=");
    if (eqIndex <= 0) continue;
    const key = line.slice(0, eqIndex).trim();
    let value = line.slice(eqIndex + 1).trim();
    if ((value.startsWith('"') && value.endsWith('"')) || (value.startsWith("'") && value.endsWith("'"))) {
      value = value.slice(1, -1);
    }
    if (!process.env[key]) {
      process.env[key] = value;
    }
  }
}

loadEnvFile(path.join(REPO_ROOT, ".env"));
loadEnvFile(path.join(__dirname, ".env"));

// ---------- 設定値 ----------
const DISCORD_TOKEN = (process.env.DISCORD_BOT_TOKEN || "").trim();
const ALLOWED_CHANNELS = parseCsvSet(process.env.DISCORD_ALLOWED_CHANNEL_IDS);
const ALLOWED_USERS = parseCsvSet(process.env.DISCORD_ALLOWED_USER_IDS);
const REQUIRE_MENTION = (process.env.DISCORD_REQUIRE_MENTION ?? "true").toLowerCase() !== "false";
const REPLY_TO_BOT_TRIGGERS = (process.env.DISCORD_REPLY_TO_BOT_TRIGGERS ?? "true").toLowerCase() !== "false";
const HISTORY_LIMIT = Math.max(0, Math.min(50, Number(process.env.DISCORD_HISTORY_LIMIT || 15)));
const MAX_HISTORY_CHARS = Math.max(1000, Number(process.env.DISCORD_MAX_HISTORY_CHARS || 8000));
const AGY_MODEL = (process.env.DISCORD_AGY_MODEL || "flash").trim();
// 大きな実装＋Unity確認＋PR作成まで完了できるようデフォルト 30 分にする
const REPLY_TIMEOUT_MS = Number(process.env.DISCORD_REPLY_TIMEOUT_MS || 1_800_000);
// RUN_STATE_IDLE を連続何回確認したら完了とみなすか（一時的なIDLE揺らぎでの誤終了防止）
const IDLE_CONFIRM_POLLS = 2;

const AGY_LS_ADDRESS = (process.env.ANTIGRAVITY_LS_ADDRESS || "").trim();
const AGY_CSRF_TOKEN = (process.env.ANTIGRAVITY_CSRF_TOKEN || "").trim();
const AGY_PROJECT_ID = (process.env.ANTIGRAVITY_PROJECT_ID || "").trim();
const AGY_BRAIN_DIR = path.join(os.homedir(), ".gemini", "antigravity", "brain");
// 利用制限を検知した後、Antigravity を飛ばして Claude を直接使う期間（デフォルト 30 分）
const AGY_COOLDOWN_MS = Number(process.env.DISCORD_AGY_COOLDOWN_MS || 1_800_000);
// Antigravity の残り利用枠（5時間枠・週枠のうち少ない方）がこの割合以下なら、最初から Claude を使う（デフォルト 10%）
const AGY_MIN_REMAINING = Number(process.env.DISCORD_AGY_MIN_REMAINING ?? 0.1);
// Antigravity の利用制限とみなすエラー文言
const AGY_RATE_LIMIT_PATTERN =
  /RESOURCE_EXHAUSTED|rate[\s_-]?limit|quota|too many requests|usage limit|HTTP 429|利用制限|上限に達/i;

// ---------- Claude Code（予備エージェント） ----------
const CLAUDE_ENABLED = (process.env.DISCORD_CLAUDE_ENABLED ?? "true").toLowerCase() !== "false";
const CLAUDE_BIN = (process.env.DISCORD_CLAUDE_BIN || (process.platform === "win32" ? "claude.cmd" : "claude")).trim();
const CLAUDE_MODEL = (process.env.DISCORD_CLAUDE_MODEL || "").trim();
// Discord 経由の依頼なので全許可（bypassPermissions）にはせず、編集の自動承認＋許可ツールの明示に留める
const CLAUDE_PERMISSION_MODE = (process.env.DISCORD_CLAUDE_PERMISSION_MODE || "acceptEdits").trim();
const CLAUDE_ALLOWED_TOOLS = (
  process.env.DISCORD_CLAUDE_ALLOWED_TOOLS ||
  "Read,Edit,Write,Glob,Grep,Bash(git:*),Bash(gh:*),Bash(unity:*),Bash(node:*),Bash(rg:*)"
)
  .split(",")
  .map((s) => s.trim())
  .filter(Boolean);

const DISCORD_API_BASE = "https://discord.com/api/v10";
// GUILDS(1<<0) | GUILD_MESSAGES(1<<9) | DIRECT_MESSAGES(1<<12) | MESSAGE_CONTENT(1<<15)
const DISCORD_INTENTS = (1 << 0) | (1 << 9) | (1 << 12) | (1 << 15);
const MAX_DISCORD_MESSAGE_LEN = 1900;
const THREAD_CHANNEL_TYPES = new Set([10, 11, 12]); // ANNOUNCEMENT_THREAD, PUBLIC_THREAD, PRIVATE_THREAD

function parseCsvSet(raw) {
  if (!raw || !raw.trim()) return null;
  const items = raw
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean);
  return items.length > 0 ? new Set(items) : null;
}

if (!DISCORD_TOKEN) {
  console.error("[DiscordHarness] 環境変数 DISCORD_BOT_TOKEN が未設定です。.env に設定してください。");
  process.exit(1);
}

// ---------- 状態管理 ----------
let botUserId = null;
let botTag = "unknown";
let ws = null;
let heartbeatTimer = null;
let lastSeq = null;
let reconnectDelayMs = 2000;
let activeJobs = 0;
// Antigravity の利用制限を検知した時刻（0 なら未検知）
let agyRateLimitedAt = 0;
const processedMessageIds = new Set();

/** Antigravity の利用制限を表すエラー。これを受けたら Claude へ引き継ぐ。 */
class AgentRateLimitError extends Error {
  constructor(message, conversationId = null) {
    super(message);
    this.name = "AgentRateLimitError";
    this.conversationId = conversationId;
  }
}

// ---------- Discord REST API ヘルパー ----------
async function discordFetch(endpoint, options = {}) {
  const res = await fetch(`${DISCORD_API_BASE}${endpoint}`, {
    ...options,
    headers: {
      Authorization: `Bot ${DISCORD_TOKEN}`,
      "Content-Type": "application/json",
      ...(options.headers || {}),
    },
  });

  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new Error(`Discord API ${res.status} ${endpoint}: ${body.slice(0, 300)}`);
  }

  if (res.status === 204) return null;
  return res.json();
}

async function sendDiscordMessage(channelId, content, replyToMessageId = null) {
  const chunks = splitMessage(content, MAX_DISCORD_MESSAGE_LEN);
  let first = true;
  for (const chunk of chunks) {
    const payload = { content: chunk };
    if (first && replyToMessageId) {
      payload.message_reference = { message_id: replyToMessageId };
      payload.allowed_mentions = { replied_user: false };
    }
    first = false;

    // 一時的なネットワーク断やレート制限に対して最大3回リトライする
    let lastErr = null;
    for (let attempt = 1; attempt <= 3; attempt++) {
      try {
        await discordFetch(`/channels/${channelId}/messages`, {
          method: "POST",
          body: JSON.stringify(payload),
        });
        lastErr = null;
        break;
      } catch (err) {
        lastErr = err;
        console.warn(`[DiscordHarness] メッセージ送信リトライ (${attempt}/3):`, err.message);
        await sleep(1500 * attempt);
      }
    }
    if (lastErr) throw lastErr;
  }
}

async function triggerTyping(channelId) {
  try {
    await discordFetch(`/channels/${channelId}/typing`, { method: "POST" });
  } catch {
    // 入力中表示の失敗は無視して処理継続
  }
}

function splitMessage(text, maxLen) {
  const normalized = (text || "").trim() || "（応答が空でした）";
  if (normalized.length <= maxLen) return [normalized];

  const chunks = [];
  let rest = normalized;
  while (rest.length > maxLen) {
    let cut = rest.lastIndexOf("\n", maxLen);
    if (cut < maxLen * 0.5) cut = maxLen;
    chunks.push(rest.slice(0, cut));
    rest = rest.slice(cut).trimStart();
  }
  if (rest.length > 0) chunks.push(rest);
  return chunks;
}

// ---------- 会話履歴・返信元コンテキスト取得 ----------
function formatAuthor(author) {
  if (!author) return "unknown";
  const name = author.global_name || author.username || "unknown";
  return author.id === botUserId ? `${name} (Bot/あなた)` : name;
}

function formatMessageLine(m) {
  const author = formatAuthor(m.author);
  const time = m.timestamp ? m.timestamp.slice(11, 19) : "";
  const content = (m.content || "").trim();
  const attachments = Array.isArray(m.attachments) && m.attachments.length > 0
    ? ` [添付: ${m.attachments.map((a) => `${a.filename} (${a.url})`).join(", ")}]`
    : "";
  const embeds = Array.isArray(m.embeds) && m.embeds.length > 0
    ? ` [Embed: ${m.embeds.map((e) => e.title || e.description || "").filter(Boolean).join(" / ").slice(0, 200)}]`
    : "";
  const replyNote = m.message_reference?.message_id ? ` (返信先ID:${m.message_reference.message_id})` : "";
  const body = content || (attachments || embeds ? "（テキストなし）" : "（空）");
  return `[${time}] ${author}${replyNote}: ${body}${attachments}${embeds}`;
}

/**
 * 返信元メッセージを取得する（ペイロードに含まれていればそれを使い、無ければ REST API で取得）。
 */
async function fetchReferencedMessage(msg) {
  if (msg.referenced_message) {
    return msg.referenced_message;
  }

  const refMsgId = msg.message_reference?.message_id;
  if (!refMsgId) return null;

  const refChannelId = msg.message_reference?.channel_id || msg.channel_id;
  try {
    return await discordFetch(`/channels/${refChannelId}/messages/${refMsgId}`);
  } catch (err) {
    console.warn(`[DiscordHarness] 返信元メッセージ取得失敗 (${refMsgId}):`, err.message);
    return null;
  }
}

/**
 * チャンネル／スレッドのメタ情報・スレッド開始メッセージ・直近履歴を取得して整形する。
 */
async function buildConversationContext(msg, referencedMessage) {
  const sections = [];

  // 1. チャンネル／スレッド情報
  let channelInfo = null;
  try {
    channelInfo = await discordFetch(`/channels/${msg.channel_id}`);
  } catch (err) {
    console.warn("[DiscordHarness] チャンネル情報取得失敗:", err.message);
  }

  const isThread = channelInfo && THREAD_CHANNEL_TYPES.has(channelInfo.type);
  const channelLabel = channelInfo?.name
    ? isThread
      ? `スレッド「${channelInfo.name}」`
      : `#${channelInfo.name}`
    : `Channel:${msg.channel_id}`;

  // 2. 直近メッセージ履歴（現在メッセージより前の最大 HISTORY_LIMIT 件）
  let historyMessages = [];
  if (HISTORY_LIMIT > 0) {
    try {
      const fetched = await discordFetch(
        `/channels/${msg.channel_id}/messages?before=${msg.id}&limit=${HISTORY_LIMIT}`
      );
      if (Array.isArray(fetched)) {
        // Discord API は新しい順で返すため、古い順（時系列）に反転
        historyMessages = fetched.reverse();
      }
    } catch (err) {
      console.warn("[DiscordHarness] 履歴取得失敗（Read Message History 権限を確認）:", err.message);
    }
  }

  // 3. スレッドの場合、親チャンネルにあるスレッド開始メッセージ（id === threadId）も補完
  if (isThread && channelInfo.parent_id) {
    const alreadyInHistory = historyMessages.some((m) => m.id === msg.channel_id);
    if (!alreadyInHistory) {
      try {
        const starter = await discordFetch(`/channels/${channelInfo.parent_id}/messages/${msg.channel_id}`);
        if (starter) {
          sections.push(`[スレッド開始メッセージ]\n${formatMessageLine(starter)}`);
        }
      } catch {
        // フォーラムスレッドや権限不足時はスキップ
      }
    }
  }

  // 4. 直近履歴の整形（文字数上限を超える場合は新しい側を優先して残す）
  if (historyMessages.length > 0) {
    const lines = historyMessages.map(formatMessageLine);
    let joined = lines.join("\n");
    if (joined.length > MAX_HISTORY_CHARS) {
      joined = "…（古い履歴を省略）\n" + joined.slice(-MAX_HISTORY_CHARS);
    }
    sections.push(`[直近の会話履歴（${channelLabel} / ${historyMessages.length}件・古い順）]\n${joined}`);
  }

  // 5. 返信元メッセージがある場合は明確に強調表示
  if (referencedMessage) {
    sections.push(`[★ユーザーが「返信」で指定した対象メッセージ]\n${formatMessageLine(referencedMessage)}`);
  }

  // 6. 今回のメッセージ自身の添付ファイルがあれば付記
  if (Array.isArray(msg.attachments) && msg.attachments.length > 0) {
    const attLines = msg.attachments.map((a) => `- ${a.filename}: ${a.url}`).join("\n");
    sections.push(`[今回のメッセージの添付ファイル]\n${attLines}`);
  }

  return {
    channelLabel,
    contextText: sections.join("\n\n"),
    historyCount: historyMessages.length,
    hasReference: Boolean(referencedMessage),
  };
}

// ---------- エージェント共通 ----------
/**
 * Antigravity・Claude 共通の依頼プロンプトを組み立てる。
 * handoffNote があれば、前のエージェントから引き継いだ旨を先頭に足す。
 */
function buildAgentPrompt(promptText, meta, handoffNote = null) {
  const promptParts = [
    `[Discord からの依頼 / 送信者: ${meta.authorName} / 場所: ${meta.channelLabel}]`,
    "以下のルールを必ず守って依頼に対応してください：",
    "1. 会話履歴・返信対象メッセージを踏まえて対応する。",
    "2. コード・シーン・Prefab・設定などのファイル変更を伴うタスクを完了した場合は、必ず「対象ファイルのみをステージ → 作業ブランチ（feature/<作業名>/agent）へコミット＆プッシュ → main へ PR 作成（.github/PULL_REQUEST_TEMPLATE.md 準拠）」まで完了させてから最終回答する。",
    "3. 最終回答は Discord へそのまま返信されるため、実施内容・確認結果・作成した PR の URL を簡潔な日本語で必ず記載して終える（ツール実行だけで無言終了しないこと）。",
  ];

  if (handoffNote) {
    promptParts.push("", `[引き継ぎ] ${handoffNote}`);
  }

  if (meta.contextText) {
    promptParts.push("", meta.contextText);
  }

  promptParts.push("", "[今回のユーザー依頼]", promptText);
  return promptParts.join("\n");
}

function isAgyCoolingDown() {
  return agyRateLimitedAt > 0 && Date.now() - agyRateLimitedAt < AGY_COOLDOWN_MS;
}

/**
 * 会話状態のうちエラー系フィールドだけを見て、利用制限の文言があれば返す。
 * summary などの本文は依頼内容に「quota」等が含まれうるため対象にしない。
 */
function findRateLimitInState(state, depth = 0) {
  if (!state || typeof state !== "object" || depth > 4) return null;
  for (const [key, value] of Object.entries(state)) {
    if (/summary|title|prompt|content/i.test(key)) continue;
    if (typeof value === "string") {
      if (/error|status|reason|message|code/i.test(key) && AGY_RATE_LIMIT_PATTERN.test(value)) {
        return `${key}=${value.slice(0, 200)}`;
      }
    } else if (value && typeof value === "object") {
      const found = findRateLimitInState(value, depth + 1);
      if (found) return found;
    }
  }
  return null;
}

/**
 * Antigravity の残り利用枠を取得する。
 * AGY_MODEL が属するモデルグループ（Gemini か、Claude/GPT か）の各枠のうち、残りが最も少ないものを返す。
 * 取得できなければ null（その場合は残量判定をせず Antigravity を試す）。
 */
async function fetchAgyQuota() {
  try {
    const res = await fetch(
      `http://${AGY_LS_ADDRESS}/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary`,
      {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Connect-Protocol-Version": "1",
          "x-antigravity-csrf-token": AGY_CSRF_TOKEN,
        },
        body: "{}",
        signal: AbortSignal.timeout(5000),
      }
    );
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const groups = (await res.json())?.response?.groups || [];
    const groupPattern = /claude|gpt/i.test(AGY_MODEL) ? /claude|gpt/i : /gemini/i;
    const group = groups.find((g) => groupPattern.test(g.displayName || "")) || groups[0];

    let lowest = null;
    for (const b of group?.buckets || []) {
      const fraction = Number(b.remainingFraction);
      if (!Number.isFinite(fraction)) continue;
      if (!lowest || fraction < lowest.fraction) {
        lowest = { fraction, label: b.displayName || b.bucketId, resetTime: b.resetTime || null };
      }
    }
    return lowest;
  } catch (err) {
    console.warn("[DiscordHarness] Antigravity 残量取得失敗（残量判定なしで続行）:", err.message);
    return null;
  }
}

function formatResetTime(iso) {
  if (!iso) return "";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  return d.toLocaleString("ja-JP", { timeZone: "Asia/Tokyo", month: "numeric", day: "numeric", hour: "2-digit", minute: "2-digit" });
}

/**
 * 最初にどちらのエージェントを使うかを決める。
 * Antigravity が未設定・利用制限中・残り利用枠が AGY_MIN_REMAINING 以下なら Claude。
 */
async function chooseFirstAgent() {
  if (!isAntigravityAvailable()) {
    return { agent: "Claude", reason: "Antigravity が未設定のため" };
  }
  if (isAgyCoolingDown()) {
    const restMin = Math.ceil((AGY_COOLDOWN_MS - (Date.now() - agyRateLimitedAt)) / 60_000);
    return { agent: "Claude", reason: `Antigravity が利用制限中のため（あと約 ${restMin} 分は Claude を使用）` };
  }
  const quota = await fetchAgyQuota();
  if (quota && quota.fraction <= AGY_MIN_REMAINING) {
    const pct = (quota.fraction * 100).toFixed(1);
    const reset = formatResetTime(quota.resetTime);
    return {
      agent: "Claude",
      reason: `Antigravity の残り利用枠が少ないため（${quota.label}: ${pct}%${reset ? `、${reset} に回復` : ""}）`,
      quota,
    };
  }
  return { agent: "Antigravity", reason: null, quota };
}

/**
 * Antigravity を先に使い、利用制限・残量不足（または未設定）なら Claude へ引き継ぐ。
 * plan は chooseFirstAgent() の結果（省略時はここで決める）。
 * onSwitch は Antigravity を試した後に Claude へ切り替える直前に呼ばれる（Discord への途中経過通知用）。
 */
async function runAgentWithFallback(promptText, meta, onSwitch, plan = null) {
  plan ||= await chooseFirstAgent();
  let handoffNote = null;
  let switchReason = plan.reason;

  if (plan.agent === "Antigravity") {
    try {
      const result = await runAntigravityPrompt(promptText, meta);
      agyRateLimitedAt = 0;
      return { ...result, agent: "Antigravity" };
    } catch (err) {
      if (!(err instanceof AgentRateLimitError)) throw err;
      agyRateLimitedAt = Date.now();
      console.warn(`[DiscordHarness] ${err.message} → Claude へ切り替えます`);
      switchReason = "Antigravity が利用制限に掛かったため";
      if (err.conversationId) {
        handoffNote =
          `この依頼は Antigravity（会話ID: ${err.conversationId}）が途中まで進めた後、利用制限で止まったものです。` +
          "まず git status・git branch・git log で途中の変更や作業ブランチが残っていないか確認し、残っていれば続きから仕上げてください。";
      }
    }
  }

  if (!CLAUDE_ENABLED) {
    throw new Error(`${switchReason}処理できません（Claude 予備は DISCORD_CLAUDE_ENABLED=false で無効）。`);
  }

  // 最初から Claude で受け付けた場合は受付メッセージで理由を伝え済みなので、切り替え通知は出さない
  if (onSwitch && plan.agent === "Antigravity") await onSwitch(switchReason);
  const result = await runClaudePrompt(promptText, meta, handoffNote);
  return { ...result, agent: "Claude" };
}

// ---------- Claude Code CLI 連携 ----------
function quoteForCmd(arg) {
  // shell 経由（Windows の .cmd 起動）時のみ使う。空白・括弧・* を含む引数を二重引用符で囲む
  return /[\s()*&|<>^"]/.test(arg) ? `"${arg.replace(/"/g, '""')}"` : arg;
}

/**
 * Claude Code を非対話モード（claude -p）でリポジトリ直下から起動し、最終応答テキストを返す。
 * プロンプトは標準入力で渡す（コマンドライン長・エスケープの問題を避けるため）。
 */
function runClaudePrompt(promptText, meta, handoffNote) {
  const args = ["-p", "--output-format", "json", "--permission-mode", CLAUDE_PERMISSION_MODE];
  if (CLAUDE_ALLOWED_TOOLS.length > 0) args.push("--allowedTools", ...CLAUDE_ALLOWED_TOOLS);
  if (CLAUDE_MODEL) args.push("--model", CLAUDE_MODEL);

  const useShell = process.platform === "win32" && /\.(cmd|bat)$/i.test(CLAUDE_BIN);
  const child = spawn(useShell ? quoteForCmd(CLAUDE_BIN) : CLAUDE_BIN, useShell ? args.map(quoteForCmd) : args, {
    cwd: REPO_ROOT,
    shell: useShell,
    windowsHide: true,
    stdio: ["pipe", "pipe", "pipe"],
  });

  console.log(`[DiscordHarness] Claude 起動 (pid=${child.pid}, 履歴=${meta.historyCount}件, 引き継ぎ=${Boolean(handoffNote)})`);

  return new Promise((resolve, reject) => {
    let stdout = "";
    let stderr = "";
    let timedOut = false;

    const timer = setTimeout(() => {
      timedOut = true;
      killProcessTree(child);
    }, REPLY_TIMEOUT_MS);

    child.stdout.on("data", (d) => (stdout += d));
    child.stderr.on("data", (d) => (stderr += d));
    child.on("error", (err) => {
      clearTimeout(timer);
      reject(new Error(`Claude を起動できません（${CLAUDE_BIN}）: ${err.message}`));
    });
    child.on("close", (code) => {
      clearTimeout(timer);
      if (timedOut) {
        resolve({ reply: "Claude の処理がタイムアウトしました（作業ブランチに途中の変更が残っている可能性があります）。" });
        return;
      }

      const parsed = parseClaudeJson(stdout);
      if (parsed) {
        console.log(`[DiscordHarness] Claude 完了 (code=${code}, session=${parsed.session_id || "?"}, error=${Boolean(parsed.is_error)})`);
        const text = typeof parsed.result === "string" ? parsed.result.trim() : "";
        if (parsed.is_error) {
          // 未ログイン等では subtype が "success" のまま is_error=true になるため、subtype は success 以外のときだけ添える
          const kind = parsed.subtype && parsed.subtype !== "success" ? `（${parsed.subtype}）` : "";
          resolve({ reply: `Claude の処理でエラーになりました${kind}:\n${text.slice(0, 1500)}` });
        } else {
          resolve({ reply: text || "Claude の処理が完了しました（応答本文なし）。" });
        }
        return;
      }

      reject(new Error(`Claude が異常終了しました (code=${code}): ${(stderr || stdout).trim().slice(0, 800)}`));
    });

    // Claude が入力を読み切る前に終了した場合の EPIPE で Bot ごと落ちないようにする
    child.stdin.on("error", (err) => console.warn("[DiscordHarness] Claude 標準入力エラー:", err.message));
    child.stdin.end(buildAgentPrompt(promptText, meta, handoffNote));
  });
}

/** --output-format json の出力（最後の JSON オブジェクト）を取り出す。 */
function parseClaudeJson(stdout) {
  const text = (stdout || "").trim();
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch {
    const start = text.lastIndexOf("\n{");
    if (start >= 0) {
      try {
        return JSON.parse(text.slice(start + 1));
      } catch {
        // 解析不能
      }
    }
    return null;
  }
}

function killProcessTree(child) {
  if (!child.pid) return;
  if (process.platform === "win32") {
    // shell 経由で起動すると子プロセスが cmd.exe の下にぶら下がるため、ツリーごと止める
    spawn("taskkill", ["/pid", String(child.pid), "/T", "/F"], { windowsHide: true });
  } else {
    child.kill("SIGTERM");
  }
}

// ---------- Antigravity ConversationService 連携 ----------
function isAntigravityAvailable() {
  return Boolean(AGY_LS_ADDRESS && AGY_CSRF_TOKEN);
}

async function callConversationService(method, bodyObj) {
  const url = `http://${AGY_LS_ADDRESS}/product.v1.ConversationService/${method}`;
  const res = await fetch(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Connect-Protocol-Version": "1",
      "x-antigravity-csrf-token": AGY_CSRF_TOKEN,
    },
    body: JSON.stringify(bodyObj),
  });

  if (!res.ok) {
    const errText = await res.text().catch(() => "");
    throw new Error(`ConversationService/${method} HTTP ${res.status}: ${errText.slice(0, 300)}`);
  }

  return res.json();
}

/**
 * Antigravity に新規会話を立てて文脈付きプロンプトを送り、完了まで待ってから応答テキストを返す。
 */
async function runAntigravityPrompt(promptText, meta) {
  if (!isAntigravityAvailable()) {
    throw new Error(
      "ANTIGRAVITY_LS_ADDRESS / ANTIGRAVITY_CSRF_TOKEN が未設定のため Antigravity に接続できません。" +
        "Antigravity エージェント経由で起動してください。"
    );
  }

  const createBody = {
    title: `Discord (${meta.channelLabel}): ${promptText.slice(0, 36)}`,
    model: AGY_MODEL,
    tags: ["discord"],
    prompt: [{ text: buildAgentPrompt(promptText, meta) }],
  };

  if (AGY_PROJECT_ID) {
    createBody.projectId = AGY_PROJECT_ID;
  }

  let created;
  try {
    created = await callConversationService("CreateConversation", createBody);
  } catch (err) {
    if (AGY_RATE_LIMIT_PATTERN.test(String(err?.message || err))) {
      throw new AgentRateLimitError(`Antigravity 利用制限: ${err.message}`);
    }
    throw err;
  }
  const conversationId = created?.conversation?.id;
  if (!conversationId) {
    throw new Error("CreateConversation のレスポンスに conversation.id がありません。");
  }

  console.log(
    `[DiscordHarness] Antigravity 会話開始: ${conversationId} (履歴=${meta.historyCount}件, 返信元=${meta.hasReference})`
  );

  // 完了（RUN_STATE_IDLE が連続安定＋応答ログ確認）までポーリング
  const deadline = Date.now() + REPLY_TIMEOUT_MS;
  let lastState = null;
  let hasObservedRunning = false;
  let consecutiveIdleCount = 0;

  await sleep(2500);

  while (Date.now() < deadline) {
    let info;
    try {
      info = await callConversationService("GetConversation", { conversationId });
    } catch (err) {
      console.warn(`[DiscordHarness] GetConversation 一時エラー (${conversationId}):`, err.message);
      await sleep(3000);
      continue;
    }

    const conv = info?.conversation;
    lastState = conv?.state;
    const runState = lastState?.runState;

    // 実行途中で利用制限に掛かった場合は、会話状態のエラー情報に現れる
    const stateError = findRateLimitInState(lastState);
    if (stateError) {
      throw new AgentRateLimitError(`Antigravity 利用制限: ${stateError}`, conversationId);
    }

    if (runState === "RUN_STATE_RUNNING") {
      hasObservedRunning = true;
      consecutiveIdleCount = 0;
    } else if (runState === "RUN_STATE_IDLE") {
      const reply = extractLastAssistantReply(conversationId);
      // 起動直後にまだ RUNNING に入る前の空 IDLE を誤検知しないよう、
      // 「一度 RUNNING を見た」または「すでに最終応答が書き出されている」場合のみカウントする
      if (hasObservedRunning || reply) {
        consecutiveIdleCount++;
      }

      if (consecutiveIdleCount >= IDLE_CONFIRM_POLLS) {
        // transcript.jsonl の最終フラッシュを少し待ってから再取得
        await sleep(800);
        const finalReply = extractLastAssistantReply(conversationId) || reply;
        // 利用制限で打ち切られると、短いエラー文だけが最終応答になる
        if (finalReply && finalReply.length < 400 && AGY_RATE_LIMIT_PATTERN.test(finalReply)) {
          throw new AgentRateLimitError(`Antigravity 利用制限: ${finalReply}`, conversationId);
        }
        console.log(
          `[DiscordHarness] Antigravity 会話完了: ${conversationId} (replyLen=${finalReply ? finalReply.length : 0})`
        );
        if (finalReply) {
          return { conversationId, reply: finalReply };
        }
        const summary = lastState?.summary || "処理が完了しました。";
        return {
          conversationId,
          reply: `${summary}\n（会話ID: \`${conversationId}\`）`,
        };
      }
    }

    await sleep(3000);
  }

  console.warn(`[DiscordHarness] タイムアウト: ${conversationId}`);
  const partialReply = extractLastAssistantReply(conversationId);
  return {
    conversationId,
    reply: partialReply
      ? `処理がタイムアウトしました（直近の進捗）:\n${partialReply.slice(0, 1500)}\n（会話ID: \`${conversationId}\`）`
      : `タイムアウトしました（現在もバックグラウンド実行中の可能性があります / 会話ID: \`${conversationId}\`）。`,
  };
}

/**
 * Antigravity の transcript.jsonl から最後のアシスタント応答（PLANNER_RESPONSE）を抽出する。
 * ツール出力（type === "GENERIC"）や思考のみ（content なし）のステップは除外する。
 */
function extractLastAssistantReply(conversationId) {
  const candidates = [
    path.join(AGY_BRAIN_DIR, conversationId, ".system_generated", "logs", "transcript_full.jsonl"),
    path.join(AGY_BRAIN_DIR, conversationId, ".system_generated", "logs", "transcript.jsonl"),
  ];

  for (const filePath of candidates) {
    if (!fs.existsSync(filePath)) continue;
    try {
      const lines = fs.readFileSync(filePath, "utf8").split(/\r?\n/).filter(Boolean);
      for (let i = lines.length - 1; i >= 0; i--) {
        let entry;
        try {
          entry = JSON.parse(lines[i]);
        } catch {
          continue;
        }
        const text = findAssistantTextInEntry(entry);
        if (text && text.trim()) {
          return text.trim();
        }
      }
    } catch {
      // 読み込み失敗時は次の候補へ
    }
  }

  return null;
}

function findAssistantTextInEntry(entry) {
  if (!entry || typeof entry !== "object") return null;

  const type = (entry.type || "").toString().toUpperCase();
  const source = (entry.source || "").toString().toUpperCase();

  // ツール実行結果（GENERIC）やユーザー入力・システム通知は除外する
  if (type === "GENERIC" || type === "USER_INPUT" || type === "SYSTEM_MESSAGE") {
    return null;
  }

  // Antigravity transcript の正式なアシスタント応答形式：source=MODEL, type=PLANNER_RESPONSE
  if (type === "PLANNER_RESPONSE" && source === "MODEL") {
    if (typeof entry.content === "string" && entry.content.trim()) {
      return entry.content;
    }
    return null;
  }

  // 将来フォーマット変更時のフォールバック
  const role = (entry.role || entry.author || "").toString().toLowerCase();
  if (role === "assistant" || role === "model") {
    if (typeof entry.content === "string" && entry.content.trim()) return entry.content;
    if (typeof entry.text === "string" && entry.text.trim()) return entry.text;
  }

  return null;
}

// ---------- メッセージ処理 ----------
async function handleMessageCreate(msg) {
  if (!msg || !msg.id || !msg.channel_id) return;
  if (msg.author?.bot) return;
  if (processedMessageIds.has(msg.id)) return;
  processedMessageIds.add(msg.id);
  if (processedMessageIds.size > 500) {
    const oldest = processedMessageIds.values().next().value;
    processedMessageIds.delete(oldest);
  }

  const isDm = !msg.guild_id;
  if (!isDm && ALLOWED_CHANNELS && !ALLOWED_CHANNELS.has(msg.channel_id)) {
    return;
  }

  if (ALLOWED_USERS && !ALLOWED_USERS.has(msg.author?.id)) {
    return;
  }

  // 返信元メッセージがあれば先に取得し、Bot への返信かどうかも判定する
  const referencedMessage = await fetchReferencedMessage(msg);
  const isReplyToBot = Boolean(botUserId && referencedMessage?.author?.id === botUserId);

  const rawContent = (msg.content || "").trim();
  const isMentioned =
    botUserId &&
    (rawContent.includes(`<@${botUserId}>`) ||
      rawContent.includes(`<@!${botUserId}>`) ||
      (Array.isArray(msg.mentions) && msg.mentions.some((m) => m.id === botUserId)));

  if (!isDm && REQUIRE_MENTION && !isMentioned && !(REPLY_TO_BOT_TRIGGERS && isReplyToBot)) {
    return;
  }

  // メンション部分を除去して本文を取り出す
  let prompt = rawContent;
  if (botUserId) {
    prompt = prompt.replace(new RegExp(`<@!?${botUserId}>`, "g"), "").trim();
  }

  if (!prompt) {
    await sendDiscordMessage(msg.channel_id, "ご用件をメンションの後ろに書いてください（例: `@Bot 現在の進捗を教えて`）。", msg.id);
    return;
  }

  // 疎通・状態確認用の軽量コマンド
  if (prompt === "!ping") {
    await sendDiscordMessage(msg.channel_id, "pong! ハーネス稼働中です（Antigravity 優先・Claude 予備／履歴・返信コンテキスト対応済み）。", msg.id);
    return;
  }

  if (prompt === "!status") {
    const quota = isAntigravityAvailable() ? await fetchAgyQuota() : null;
    const quotaText = quota
      ? `${(quota.fraction * 100).toFixed(1)}%（${quota.label}${quota.resetTime ? ` / ${formatResetTime(quota.resetTime)} に回復` : ""}、${(AGY_MIN_REMAINING * 100).toFixed(0)}% 以下で Claude へ迂回）`
      : "取得できず";
    const statusText = [
      `**Discord Harness Status**`,
      `- Bot: \`${botTag}\``,
      `- Antigravity API: ${isAntigravityAvailable() ? (isAgyCoolingDown() ? "利用制限中（Claude へ迂回）" : "接続可能") : "未設定"}`,
      `- Antigravity 残り利用枠: ${quotaText}`,
      `- Claude 予備: ${CLAUDE_ENABLED ? `有効（\`${CLAUDE_BIN}\` / ${CLAUDE_PERMISSION_MODE}${CLAUDE_MODEL ? ` / ${CLAUDE_MODEL}` : ""}）` : "無効"}`,
      `- 実行中タスク数: ${activeJobs}`,
      `- Antigravity モデル: \`${AGY_MODEL}\``,
      `- 履歴取得件数: 直近 ${HISTORY_LIMIT} 件（スレッド開始・返信元メッセージ対応）`,
    ].join("\n");
    await sendDiscordMessage(msg.channel_id, statusText, msg.id);
    return;
  }

  // エージェントへジョブ投入（Antigravity 優先、利用制限時は Claude）
  activeJobs++;
  const authorName = msg.author?.global_name || msg.author?.username || "unknown";
  console.log(`[DiscordHarness] 受信 (${authorName}): ${prompt.slice(0, 80)}`);

  const typingTimer = setInterval(() => triggerTyping(msg.channel_id), 8000);
  try {
    await triggerTyping(msg.channel_id);
    const plan = await chooseFirstAgent();
    const acceptText =
      plan.agent === "Claude" && plan.reason
        ? `受け付けました。${plan.reason}、Claude で処理しています…`
        : `受け付けました。会話履歴を確認して ${plan.agent} で処理しています…`;
    await sendDiscordMessage(msg.channel_id, acceptText, msg.id);

    const ctx = await buildConversationContext(msg, referencedMessage);
    const { reply, agent } = await runAgentWithFallback(
      prompt,
      {
        authorName,
        channelId: msg.channel_id,
        channelLabel: ctx.channelLabel,
        contextText: ctx.contextText,
        historyCount: ctx.historyCount,
        hasReference: ctx.hasReference,
      },
      async (reason) => {
        await sendDiscordMessage(msg.channel_id, `${reason}、Claude に切り替えて続けます…`, msg.id).catch(() => {});
      },
      plan
    );

    await sendDiscordMessage(msg.channel_id, agent === "Claude" ? `${reply}\n\n（処理: Claude）` : reply, msg.id);
  } catch (err) {
    console.error("[DiscordHarness] 処理エラー:", err);
    await sendDiscordMessage(
      msg.channel_id,
      `処理中にエラーが発生しました:\n\`\`\`\n${String(err?.message || err).slice(0, 1200)}\n\`\`\``,
      msg.id
    ).catch(() => {});
  } finally {
    clearInterval(typingTimer);
    activeJobs--;
  }
}

// ---------- Discord Gateway WebSocket 接続 ----------
async function connectGateway() {
  stopHeartbeat();

  let gatewayUrl = "wss://gateway.discord.gg/?v=10&encoding=json";
  try {
    const info = await discordFetch("/gateway/bot");
    if (info?.url) {
      gatewayUrl = `${info.url.replace(/\/$/, "")}/?v=10&encoding=json`;
    }
  } catch (err) {
    console.error("[DiscordHarness] /gateway/bot 取得失敗:", err.message);
    scheduleReconnect();
    return;
  }

  console.log("[DiscordHarness] Discord Gateway へ接続中...");
  ws = new WebSocket(gatewayUrl);

  ws.addEventListener("open", () => {
    console.log("[DiscordHarness] WebSocket 接続確立");
    reconnectDelayMs = 2000;
  });

  ws.addEventListener("message", (event) => {
    let payload;
    try {
      payload = JSON.parse(event.data);
    } catch {
      return;
    }

    const { op, t, s, d } = payload;
    if (s !== null && s !== undefined) {
      lastSeq = s;
    }

    switch (op) {
      case 10: // HELLO
        startHeartbeat(d.heartbeat_interval);
        sendIdentify();
        break;
      case 1: // Heartbeat request
        sendHeartbeat();
        break;
      case 7: // RECONNECT
        console.log("[DiscordHarness] サーバーから再接続要求を受信");
        ws.close(4000, "Reconnect requested");
        break;
      case 9: // INVALID SESSION
        console.warn("[DiscordHarness] セッション無効。再接続します");
        ws.close(4000, "Invalid session");
        break;
      case 0: // DISPATCH
        if (t === "READY") {
          botUserId = d.user?.id || null;
          botTag = d.user ? `${d.user.username}#${d.user.discriminator}` : "unknown";
          console.log(`[DiscordHarness] ログイン完了: ${botTag} (ID: ${botUserId})`);
          console.log(
            `[DiscordHarness] 設定: requireMention=${REQUIRE_MENTION}, replyToBot=${REPLY_TO_BOT_TRIGGERS}, historyLimit=${HISTORY_LIMIT}, antigravity=${isAntigravityAvailable()}, model=${AGY_MODEL}`
          );
        } else if (t === "MESSAGE_CREATE") {
          handleMessageCreate(d).catch((err) => console.error("[DiscordHarness] handleMessageCreate error:", err));
        }
        break;
    }
  });

  ws.addEventListener("close", (event) => {
    console.warn(`[DiscordHarness] 切断されました (code=${event.code}, reason=${event.reason || "none"})`);
    stopHeartbeat();
    if (event.code === 4004) {
      console.error("[DiscordHarness] Bot トークンが無効です。DISCORD_BOT_TOKEN を確認してください。");
      process.exit(1);
    }
    if (event.code === 4014) {
      console.error(
        "[DiscordHarness] MESSAGE CONTENT INTENT が無効です。Discord Developer Portal > Bot > Privileged Gateway Intents で 'Message Content Intent' を ON にしてください。"
      );
      process.exit(1);
    }
    scheduleReconnect();
  });

  ws.addEventListener("error", (err) => {
    console.error("[DiscordHarness] WebSocket エラー:", err?.message || err);
  });
}

function sendIdentify() {
  if (!ws || ws.readyState !== WebSocket.OPEN) return;
  ws.send(
    JSON.stringify({
      op: 2,
      d: {
        token: DISCORD_TOKEN,
        intents: DISCORD_INTENTS,
        properties: {
          os: process.platform,
          browser: "antigravity-discord-harness",
          device: "antigravity-discord-harness",
        },
      },
    })
  );
}

function startHeartbeat(intervalMs) {
  stopHeartbeat();
  heartbeatTimer = setInterval(sendHeartbeat, intervalMs);
}

function stopHeartbeat() {
  if (heartbeatTimer) {
    clearInterval(heartbeatTimer);
    heartbeatTimer = null;
  }
}

function sendHeartbeat() {
  if (!ws || ws.readyState !== WebSocket.OPEN) return;
  ws.send(JSON.stringify({ op: 1, d: lastSeq }));
}

function scheduleReconnect() {
  const delay = reconnectDelayMs;
  reconnectDelayMs = Math.min(reconnectDelayMs * 2, 30_000);
  console.log(`[DiscordHarness] ${delay / 1000}秒後に再接続します...`);
  setTimeout(connectGateway, delay);
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

process.on("SIGINT", () => {
  stopHeartbeat();
  if (ws) ws.close(1000, "Shutdown");
  process.exit(0);
});

connectGateway();
