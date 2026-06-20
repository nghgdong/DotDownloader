// DotDownloader — MV3 service worker.
// LƯU Ý: service worker MV3 không giữ state lâu → cấu hình bền hóa qua chrome.storage.local.
// State runtime (danh sách video theo tab) chỉ giữ trong RAM, đủ dùng khi đang duyệt.

const PORTS = Array.from({ length: 10 }, (_, i) => 51820 + i); // 51820..51829
const PING_TTL_MS = 3000;
const PING_TIMEOUT_MS = 1000;

const DEFAULTS = {
  enabled: true,
  token: "dev-token", // dev: khớp DM_DEV_TOKEN=dev-token; pairing thật ở Phase 8
  port: 51820,
  captureExtensions: [
    "zip", "rar", "7z", "gz", "tar", "iso", "exe", "msi", "dmg", "apk", "deb",
    "pdf", "epub", "doc", "docx", "xls", "xlsx", "ppt", "pptx",
    "mp3", "flac", "wav", "m4a",
    "mp4", "mkv", "avi", "mov", "webm", "bin"
  ]
};

// ---------------- Config ----------------

async function getConfig() {
  return await chrome.storage.local.get(DEFAULTS);
}

async function setConfig(patch) {
  await chrome.storage.local.set(patch);
}

// ---------------- App connection (ping) ----------------

let pingCache = { at: 0, result: null };

function orderedPorts(preferred) {
  const rest = PORTS.filter((p) => p !== preferred);
  return [preferred, ...rest];
}

async function probeOne(port, token) {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), PING_TIMEOUT_MS);
  try {
    const res = await fetch(`http://127.0.0.1:${port}/api/ping`, {
      headers: { "X-DM-Token": token },
      signal: ctrl.signal
    });
    if (!res.ok) throw new Error(`status ${res.status}`);
    const data = await res.json();
    return { ok: true, port, version: data.version, activeDownloads: data.activeDownloads };
  } finally {
    clearTimeout(timer);
  }
}

async function pingApp(force = false) {
  const now = Date.now();
  if (!force && pingCache.result && now - pingCache.at < PING_TTL_MS) {
    return pingCache.result;
  }
  const cfg = await getConfig();
  let result;
  try {
    // Thử port đã nhớ trước, các port còn lại song song; lấy cái sống đầu tiên.
    result = await Promise.any(orderedPorts(cfg.port).map((p) => probeOne(p, cfg.token)));
  } catch {
    result = { ok: false };
  }
  pingCache = { at: now, result };
  if (result.ok && result.port !== cfg.port) {
    await setConfig({ port: result.port });
  }
  return result;
}

async function postDownload(payload) {
  const status = await pingApp();
  if (!status.ok) return { ok: false, error: "App chưa chạy" };
  const cfg = await getConfig();
  try {
    const res = await fetch(`http://127.0.0.1:${status.port}/api/download`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-DM-Token": cfg.token },
      body: JSON.stringify(payload)
    });
    if (!res.ok) return { ok: false, error: `HTTP ${res.status}` };
    const data = await res.json();
    return { ok: true, taskId: data.taskId };
  } catch (e) {
    return { ok: false, error: String(e) };
  }
}

// ---------------- Helpers ----------------

async function getCookieHeader(url) {
  try {
    const cookies = await chrome.cookies.getAll({ url });
    return cookies.map((c) => `${c.name}=${c.value}`).join("; ");
  } catch {
    return "";
  }
}

function extOf(name) {
  const m = /\.([a-z0-9]+)$/i.exec(name || "");
  return m ? m[1].toLowerCase() : "";
}

function baseName(pathOrName) {
  const base = (pathOrName || "").split(/[\\/]/).pop();
  return base || "";
}

/// MIME media → có nên bắt không (video/audio). Endpoint .php trả video/* vẫn bắt được.
function isMediaMime(mime) {
  const m = (mime || "").toLowerCase();
  return m.startsWith("video/") || m.startsWith("audio/");
}

const MEDIA_EXTS = [
  "mp4", "mkv", "webm", "mov", "m4v", "flv", "avi", "3gp", "ts", "m3u8", "mpd",
  "mp3", "m4a", "aac", "flac", "wav", "ogg"
];

/// MIME → đuôi file hợp lý (để sửa tên khi URL không có đuôi media, vd .php).
function mimeExtension(mime) {
  const m = (mime || "").toLowerCase().split(";")[0].trim();
  const map = {
    "video/mp4": "mp4", "video/webm": "webm", "video/x-matroska": "mkv",
    "video/quicktime": "mov", "video/x-flv": "flv", "video/3gpp": "3gp",
    "video/mpeg": "mpg", "video/mp2t": "ts",
    "audio/mpeg": "mp3", "audio/mp4": "m4a", "audio/aac": "aac",
    "audio/ogg": "ogg", "audio/wav": "wav", "audio/x-wav": "wav", "audio/flac": "flac"
  };
  return map[m] || null;
}

/// Tìm đuôi media trong URL: ưu tiên path (vd /x.mp4), sau đó query (vd ?file=...mp4&token=...).
function mediaExtFromUrl(url) {
  try {
    const u = new URL(url);
    let e = extOf(decodeURIComponent(baseName(u.pathname)));
    if (MEDIA_EXTS.includes(e)) return e;
    for (const v of u.searchParams.values()) {
      let dec;
      try { dec = decodeURIComponent(v); } catch { dec = v; }
      const token = baseName(dec.split(/[?#]/)[0]);
      e = extOf(token);
      if (MEDIA_EXTS.includes(e)) return e;
    }
  } catch { /* ignore */ }
  return null;
}

/// Đuôi media mong muốn: ưu tiên MIME, rồi đến gợi ý từ URL (path/query).
function wantedMediaExt(item) {
  return mimeExtension(item.mime) || mediaExtFromUrl(item.finalUrl || item.url || "");
}

// ---------------- Bắt download file thường ----------------

function shouldCapture(item, exts) {
  const url = item.finalUrl || item.url || "";
  if (!/^https?:/i.test(url)) return false;
  let ext = extOf(item.filename);
  if (!ext) {
    try { ext = extOf(new URL(url).pathname); } catch { /* ignore */ }
  }
  if (exts.includes(ext)) return true;
  // Đuôi path không khớp (vd .php, không đuôi) nhưng MIME media HOẶC URL có đuôi media trong query → vẫn bắt.
  return isMediaMime(item.mime) || mediaExtFromUrl(url) !== null;
}

function fileNameOf(item) {
  let name = baseName(item.filename);
  if (!name) {
    try { name = decodeURIComponent(baseName(new URL(item.finalUrl || item.url).pathname)); }
    catch { name = ""; }
  }

  // Đuôi hiện tại không phải media (vd .php) → đổi theo MIME hoặc gợi ý từ URL.
  const wantExt = wantedMediaExt(item);
  if (wantExt && extOf(name) !== wantExt) {
    const dot = name.lastIndexOf(".");
    let stem = dot > 0 ? name.slice(0, dot) : name;
    if (!stem) stem = "video";
    name = `${stem}.${wantExt}`;
  }

  return name || "download";
}

async function handleDownload(item) {
  const cfg = await getConfig();
  if (!cfg.enabled) return false;
  if (!shouldCapture(item, cfg.captureExtensions)) return false;

  const status = await pingApp();
  if (!status.ok) return false; // app chết → để browser tự tải (graceful fallback)

  const url = item.finalUrl || item.url;
  const payload = {
    url,
    fileName: fileNameOf(item),
    referrer: item.referrer || "",
    cookies: await getCookieHeader(url),
    type: "file"
  };
  const r = await postDownload(payload);
  if (!r.ok) return false; // không đẩy được → để browser tải

  try { await chrome.downloads.cancel(item.id); } catch { /* ignore */ }
  try { await chrome.downloads.erase({ id: item.id }); } catch { /* ignore */ }
  return true;
}

chrome.downloads.onDeterminingFilename.addListener((item, suggest) => {
  handleDownload(item)
    .then((handled) => { if (!handled) suggest(); })
    .catch(() => suggest());
  return true; // xử lý bất đồng bộ
});

// ---------------- Phát hiện video (webRequest) ----------------

// tabId -> Map(url -> {url, type, headers, contentType, title})
const tabVideos = new Map();
// requestId -> {Referer, Origin, Cookie, User-Agent}  (gom ở onBeforeSendHeaders)
const pendingHeaders = new Map();

function headerValue(headers, name) {
  const lc = name.toLowerCase();
  for (const h of headers || []) {
    if (h.name.toLowerCase() === lc) return h.value || "";
  }
  return "";
}

function detectType(url, contentType) {
  const u = (url.split("?")[0] || "").toLowerCase();
  const ct = (contentType || "").toLowerCase();
  if (u.endsWith(".m3u8") || ct.includes("mpegurl")) return "HLS";
  if (u.endsWith(".mpd") || ct.includes("dash+xml")) return "DASH";
  if (/\.(mp4|webm|mkv|m4v|mov)$/.test(u)) return "MP4";
  // Chỉ nhận video/* dạng file hoàn chỉnh; bỏ qua segment .ts (mp2t) để khỏi spam.
  if (/^video\/(mp4|webm|x-matroska|quicktime)/.test(ct) && !/\.ts(\?|$)/.test(url)) return "MP4";
  return null;
}

function isMediaCandidate(url) {
  const u = (url.split("?")[0] || "").toLowerCase();
  return /\.(m3u8|mpd|mp4|webm|mkv|m4v|mov)$/.test(u);
}

async function updateBadge(tabId) {
  const map = tabVideos.get(tabId);
  const count = map ? map.size : 0;
  try {
    await chrome.action.setBadgeText({ tabId, text: count > 0 ? String(count) : "" });
    await chrome.action.setBadgeBackgroundColor({ tabId, color: "#2d7ff9" });
  } catch { /* tab có thể đã đóng */ }
}

function addVideo(tabId, entry) {
  if (tabId < 0) return;
  let map = tabVideos.get(tabId);
  if (!map) {
    map = new Map();
    tabVideos.set(tabId, map);
  }
  if (map.has(entry.url)) return;
  map.set(entry.url, entry);
  updateBadge(tabId);
}

chrome.webRequest.onBeforeSendHeaders.addListener(
  (details) => {
    if (!isMediaCandidate(details.url)) return;
    const h = {};
    for (const x of details.requestHeaders || []) {
      const n = x.name.toLowerCase();
      if (n === "referer") h.Referer = x.value;
      else if (n === "origin") h.Origin = x.value;
      else if (n === "cookie") h.Cookie = x.value;
      else if (n === "user-agent") h["User-Agent"] = x.value;
    }
    pendingHeaders.set(details.requestId, h);
  },
  { urls: ["<all_urls>"] },
  ["requestHeaders", "extraHeaders"]
);

chrome.webRequest.onResponseStarted.addListener(
  (details) => {
    const contentType = headerValue(details.responseHeaders, "content-type");
    const type = detectType(details.url, contentType);
    if (!type) {
      pendingHeaders.delete(details.requestId);
      return;
    }
    const headers = pendingHeaders.get(details.requestId) || {};
    pendingHeaders.delete(details.requestId);

    addVideo(details.tabId, {
      url: details.url,
      type,
      headers,
      contentType,
      title: baseName(details.url.split("?")[0]) || details.url
    });
  },
  { urls: ["<all_urls>"] },
  ["responseHeaders", "extraHeaders"]
);

// Dọn rác headers cho request không phải media / kết thúc.
function dropPending(details) { pendingHeaders.delete(details.requestId); }
chrome.webRequest.onCompleted.addListener(dropPending, { urls: ["<all_urls>"] });
chrome.webRequest.onErrorOccurred.addListener(dropPending, { urls: ["<all_urls>"] });

// Dọn danh sách video khi tab đổi URL hoặc đóng.
chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (changeInfo.url) {
    tabVideos.delete(tabId);
    updateBadge(tabId);
  }
});
chrome.tabs.onRemoved.addListener((tabId) => {
  tabVideos.delete(tabId);
});

function listVideos(tabId) {
  const map = tabVideos.get(tabId);
  return map ? Array.from(map.values()) : [];
}

// ---------------- Context menu ----------------

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({
    id: "dm-download",
    title: "Download with DotDownloader",
    contexts: ["link", "video", "audio"]
  });
});

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId !== "dm-download") return;
  const url = info.linkUrl || info.srcUrl;
  if (!url) return;
  const payload = {
    url,
    referrer: info.pageUrl || (tab && tab.url) || "",
    cookies: await getCookieHeader(url),
    type: "file"
  };
  await postDownload(payload);
});

// ---------------- Message API cho popup ----------------

chrome.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
  (async () => {
    switch (msg && msg.type) {
      case "getStatus":
        sendResponse(await pingApp(true));
        break;
      case "getVideos":
        sendResponse({ videos: listVideos(msg.tabId) });
        break;
      case "download":
        sendResponse(await postDownload(msg.payload));
        break;
      default:
        sendResponse({ ok: false, error: "unknown message" });
    }
  })();
  return true; // trả lời bất đồng bộ
});
