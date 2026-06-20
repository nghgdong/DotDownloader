// Popup: bật/tắt extension, trạng thái kết nối app, danh sách video trên tab + nút Tải.

const $ = (id) => document.getElementById(id);

function send(msg) {
  return new Promise((resolve) => chrome.runtime.sendMessage(msg, resolve));
}

function toast(text, ok = true) {
  const el = $("toast");
  el.textContent = text;
  el.style.color = ok ? "#2bb673" : "#d34";
  setTimeout(() => { el.textContent = ""; }, 2500);
}

async function currentTabId() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  return tab ? tab.id : -1;
}

// --- Toggle bật/tắt (bền hóa storage) ---
async function initToggle() {
  const { enabled } = await chrome.storage.local.get({ enabled: true });
  $("toggleEnabled").checked = enabled;
  $("toggleEnabled").addEventListener("change", async (e) => {
    await chrome.storage.local.set({ enabled: e.target.checked });
    toast(e.target.checked ? "Đã bật bắt link" : "Đã tắt bắt link");
  });
}

// --- Token pairing (copy từ app → dán vào đây) ---
async function initToken() {
  const { token } = await chrome.storage.local.get({ token: "dev-token" });
  $("tokenInput").value = token;
  $("tokenSave").addEventListener("click", async () => {
    const value = $("tokenInput").value.trim();
    if (!value) return;
    await chrome.storage.local.set({ token: value });
    toast("Đã lưu token");
    await refreshStatus(); // ping lại với token mới
  });
}

// --- Trạng thái kết nối app ---
async function refreshStatus() {
  const status = await send({ type: "getStatus" });
  const dot = $("statusDot");
  if (status && status.ok) {
    dot.className = "dot on";
    $("statusText").textContent = `Đã kết nối app (port ${status.port}, v${status.version})`;
  } else {
    dot.className = "dot off";
    $("statusText").textContent = "Chưa kết nối — hãy mở app DotDownloader";
  }
}

// --- Danh sách video ---
function videoRow(v) {
  const row = document.createElement("div");
  row.className = "video";

  const meta = document.createElement("div");
  meta.className = "meta";
  const name = document.createElement("div");
  name.className = "name";
  name.textContent = v.title || v.url;
  name.title = v.url;
  const badge = document.createElement("span");
  badge.className = "badge";
  badge.textContent = v.type;
  meta.appendChild(badge);
  meta.appendChild(name);

  const btn = document.createElement("button");
  btn.className = "dl";
  btn.textContent = "Tải";
  btn.addEventListener("click", async () => {
    btn.disabled = true;
    const r = await send({
      type: "download",
      payload: { url: v.url, type: v.type, headers: v.headers || {}, referrer: (v.headers && v.headers.Referer) || "" }
    });
    if (r && r.ok) {
      toast("Đã gửi sang app ✓");
      btn.textContent = "Đã gửi";
    } else {
      toast(`Lỗi: ${(r && r.error) || "không gửi được"}`, false);
      btn.disabled = false;
    }
  });

  row.appendChild(meta);
  row.appendChild(btn);
  return row;
}

async function refreshVideos() {
  const tabId = await currentTabId();
  const { videos } = await send({ type: "getVideos", tabId });
  const list = $("videoList");
  list.innerHTML = "";
  if (!videos || videos.length === 0) {
    list.innerHTML = '<div class="empty">Chưa phát hiện video.</div>';
    return;
  }
  for (const v of videos) {
    list.appendChild(videoRow(v));
  }
}

document.addEventListener("DOMContentLoaded", async () => {
  await initToggle();
  await initToken();
  await refreshStatus();
  await refreshVideos();
});
