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
 *
 * 【起動方法】
 *   node Tools/DiscordHarness/bot.mjs
 *   ※ .env に DISCORD_BOT_TOKEN=... を設定しておくこと。
 */

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
const REPLY_TIMEOUT_MS = Number(process.env.DISCORD_REPLY_TIMEOUT_MS || 600_000);

const AGY_LS_ADDRESS = (process.env.ANTIGRAVITY_LS_ADDRESS || "").trim();
const AGY_CSRF_TOKEN = (process.env.ANTIGRAVITY_CSRF_TOKEN || "").trim();
const AGY_PROJECT_ID = (process.env.ANTIGRAVITY_PROJECT_ID || "").trim();
const AGY_BRAIN_DIR = path.join(os.homedir(), ".gemini", "antigravity", "brain");

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
const processedMessageIds = new Set();

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
    await discordFetch(`/channels/${channelId}/messages`, {
      method: "POST",
      body: JSON.stringify(payload),
    });
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

  const promptParts = [
    `[Discord からの依頼 / 送信者: ${meta.authorName} / 場所: ${meta.channelLabel}]`,
    "以下の会話履歴・返信対象メッセージを踏まえて依頼に対応し、最後に Discord へそのまま返せる簡潔な日本語で回答してください。",
  ];

  if (meta.contextText) {
    promptParts.push("", meta.contextText);
  }

  promptParts.push("", "[今回のユーザー依頼]", promptText);

  const createBody = {
    title: `Discord (${meta.channelLabel}): ${promptText.slice(0, 36)}`,
    model: AGY_MODEL,
    tags: ["discord"],
    prompt: [{ text: promptParts.join("\n") }],
  };

  if (AGY_PROJECT_ID) {
    createBody.projectId = AGY_PROJECT_ID;
  }

  const created = await callConversationService("CreateConversation", createBody);
  const conversationId = created?.conversation?.id;
  if (!conversationId) {
    throw new Error("CreateConversation のレスポンスに conversation.id がありません。");
  }

  console.log(
    `[DiscordHarness] Antigravity 会話開始: ${conversationId} (履歴=${meta.historyCount}件, 返信元=${meta.hasReference})`
  );

  // 完了（RUN_STATE_IDLE）になるまでポーリング
  const deadline = Date.now() + REPLY_TIMEOUT_MS;
  let lastState = null;

  await sleep(1500);

  while (Date.now() < deadline) {
    const info = await callConversationService("GetConversation", { conversationId });
    const conv = info?.conversation;
    lastState = conv?.state;
    const runState = lastState?.runState;

    if (runState === "RUN_STATE_IDLE") {
      const reply = extractLastAssistantReply(conversationId);
      if (reply) {
        return { conversationId, reply };
      }
      const summary = lastState?.summary || "処理が完了しました。";
      return {
        conversationId,
        reply: `${summary}\n（会話ID: \`${conversationId}\`）`,
      };
    }

    await sleep(2500);
  }

  return {
    conversationId,
    reply: `タイムアウトしました（現在もバックグラウンド実行中の可能性があります / 会話ID: \`${conversationId}\`）。`,
  };
}

/**
 * Antigravity の transcript.jsonl から最後のアシスタント応答テキストを抽出する。
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

  const role = (entry.role || entry.author || entry.sender || "").toString().toLowerCase();
  const type = (entry.type || "").toString().toLowerCase();
  const isAssistant =
    role === "assistant" ||
    role === "model" ||
    type.includes("assistant") ||
    type.includes("response") ||
    type.includes("model");

  if (!isAssistant) return null;

  if (typeof entry.text === "string" && entry.text.trim()) return entry.text;
  if (typeof entry.content === "string" && entry.content.trim()) return entry.content;
  if (typeof entry.message === "string" && entry.message.trim()) return entry.message;

  if (Array.isArray(entry.content)) {
    const joined = entry.content
      .map((part) => (typeof part === "string" ? part : part?.text || ""))
      .filter(Boolean)
      .join("\n");
    if (joined.trim()) return joined;
  }

  if (Array.isArray(entry.parts)) {
    const joined = entry.parts
      .map((part) => (typeof part === "string" ? part : part?.text || ""))
      .filter(Boolean)
      .join("\n");
    if (joined.trim()) return joined;
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
    await sendDiscordMessage(msg.channel_id, "pong! Antigravity ハーネス稼働中です（履歴・返信コンテキスト対応済み）。", msg.id);
    return;
  }

  if (prompt === "!status") {
    const statusText = [
      `**Discord Harness Status**`,
      `- Bot: \`${botTag}\``,
      `- Antigravity API: ${isAntigravityAvailable() ? "接続可能" : "未設定"}`,
      `- 実行中タスク数: ${activeJobs}`,
      `- モデル: \`${AGY_MODEL}\``,
      `- 履歴取得件数: 直近 ${HISTORY_LIMIT} 件（スレッド開始・返信元メッセージ対応）`,
    ].join("\n");
    await sendDiscordMessage(msg.channel_id, statusText, msg.id);
    return;
  }

  // Antigravity へジョブ投入
  activeJobs++;
  const authorName = msg.author?.global_name || msg.author?.username || "unknown";
  console.log(`[DiscordHarness] 受信 (${authorName}): ${prompt.slice(0, 80)}`);

  const typingTimer = setInterval(() => triggerTyping(msg.channel_id), 8000);
  try {
    await triggerTyping(msg.channel_id);
    await sendDiscordMessage(msg.channel_id, "受け付けました。会話履歴を確認して Antigravity で処理しています…", msg.id);

    const ctx = await buildConversationContext(msg, referencedMessage);
    const { reply } = await runAntigravityPrompt(prompt, {
      authorName,
      channelId: msg.channel_id,
      channelLabel: ctx.channelLabel,
      contextText: ctx.contextText,
      historyCount: ctx.historyCount,
      hasReference: ctx.hasReference,
    });

    await sendDiscordMessage(msg.channel_id, reply, msg.id);
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
