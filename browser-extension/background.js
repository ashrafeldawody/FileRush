const api = typeof browser !== "undefined" ? browser : chrome;
const HOST = "com.filerush.host";
const DEFAULTS = {
  enabled: true,
  catchLinks: true,
  catchAll: false,
  showPanel: true,
  minMediaSize: 1048576,
  extensions: "3GP 7Z AAC ACE APK ARJ ASF AVI BIN BZ2 CAB CHM CSV DEB DJVU DMG DOC DOCX EPUB EXE FLAC FLV GZ ISO JAR LZH M4A M4V MKV MOV MP3 MP4 MPEG MPG MSI MSU OGG PDF PPT PPTX RAR RPM SEA SIT SITX TAR TGZ TIF TIFF TXT WAV WEBM WMA WMV XLS XLSX XZ Z ZIP ZST",
  exceptions: ""
};
const BINARY_MIMES = new Set([
  "application/zip", "application/x-zip-compressed", "application/x-rar-compressed", "application/vnd.rar",
  "application/x-7z-compressed", "application/x-msdownload", "application/x-msi", "application/octet-stream",
  "application/x-iso9660-image", "application/gzip", "application/x-tar", "application/x-bzip2", "application/pdf",
  "application/vnd.android.package-archive", "application/x-debian-package", "application/java-archive"
]);
const MEDIA_EXTENSIONS = /\.(mp4|webm|mkv|m4a|m4v|mp3|flv|mov|avi|wmv|ogg|ogv|opus|aac|wav|3gp|ts)(\?|$)/i;
const STREAM_SEGMENTS = /\.(m3u8|mpd|ts|m4s|key|vtt)(\?|$)/i;

let settings = { ...DEFAULTS };
let skipUntil = 0;
const mediaByTab = new Map();

async function loadSettings() {
  try {
    const stored = await api.storage.sync.get(DEFAULTS);
    settings = { ...DEFAULTS, ...stored };
  } catch (e) {
    settings = { ...DEFAULTS };
  }
}
loadSettings();
api.storage.onChanged.addListener((changes, area) => {
  if (area === "sync") loadSettings();
});

function extensionSet() {
  return new Set(settings.extensions.split(/[\s,;]+/).filter(Boolean).map(e => e.replace(/^\./, "").toUpperCase()));
}

function extensionOfName(name) {
  const dot = name.lastIndexOf(".");
  return dot >= 0 && dot < name.length - 1 ? name.substring(dot + 1).toUpperCase() : "";
}

function extensionOfUrl(url) {
  try {
    const path = new URL(url).pathname;
    return extensionOfName(path.substring(path.lastIndexOf("/") + 1));
  } catch (e) {
    return "";
  }
}

function basename(path) {
  if (!path) return "";
  const normalized = path.replace(/\\/g, "/");
  return normalized.substring(normalized.lastIndexOf("/") + 1);
}

function isException(url) {
  const patterns = settings.exceptions.split(/[\s,;]+/).filter(Boolean);
  if (!patterns.length) return false;
  let host = "";
  try {
    host = new URL(url).hostname.toLowerCase();
  } catch (e) {
    return false;
  }
  const lower = url.toLowerCase();
  return patterns.some(raw => {
    const p = raw.toLowerCase().replace(/^\*\./, "");
    return host === p || host.endsWith("." + p) || lower.startsWith(p);
  });
}

function shouldCatch(url, fileName, mime) {
  if (!settings.enabled || Date.now() < skipUntil) return false;
  if (!/^(https?|ftp):/i.test(url) || isException(url)) return false;
  if (settings.catchAll) return true;
  const set = extensionSet();
  const fromName = fileName ? extensionOfName(fileName) : "";
  if (fromName && set.has(fromName)) return true;
  const fromUrl = extensionOfUrl(url);
  if (fromUrl && set.has(fromUrl)) return true;
  if (mime) {
    const m = mime.toLowerCase().split(";")[0].trim();
    if (m.startsWith("video/") || m.startsWith("audio/") || BINARY_MIMES.has(m)) return true;
  }
  return false;
}

async function sendToApp(message) {
  try {
    const response = await api.runtime.sendNativeMessage(HOST, message);
    return response || { ok: true };
  } catch (e) {
    return { ok: false, error: (e && e.message) || String(e) };
  }
}

async function cookieHeader(url) {
  try {
    const cookies = await api.cookies.getAll({ url });
    return cookies.map(c => `${c.name}=${c.value}`).join("; ");
  } catch (e) {
    return "";
  }
}

function notify(title, message) {
  try {
    api.notifications.create({ type: "basic", iconUrl: api.runtime.getURL("icons/icon128.png"), title, message });
  } catch (e) {
  }
}

async function downloadWithApp(url, extra = {}) {
  const message = {
    type: "download",
    url,
    referrer: extra.referrer || "",
    cookie: await cookieHeader(url),
    userAgent: navigator.userAgent,
    fileName: extra.fileName || "",
    mime: extra.mime || "",
    size: typeof extra.size === "number" ? extra.size : -1
  };
  const response = await sendToApp(message);
  if (!response.ok) {
    notify("File Rush is not reachable", response.error || "Start File Rush and register browser integration in its Options.");
  }
  return response;
}

async function sendBatch(urls, referrer) {
  const message = {
    type: "batch",
    urls,
    referrer: referrer || "",
    cookie: await cookieHeader(referrer || urls[0]),
    userAgent: navigator.userAgent
  };
  const response = await sendToApp(message);
  if (!response.ok) {
    notify("File Rush is not reachable", response.error || "Start File Rush and register browser integration in its Options.");
  }
  return response;
}

api.downloads.onCreated.addListener(async item => {
  if (item.byExtensionId) return;
  if (item.state && item.state !== "in_progress") return;
  const url = item.finalUrl || item.url;
  const fileName = basename(item.filename);
  if (!shouldCatch(url, fileName, item.mime)) return;
  try {
    await api.downloads.cancel(item.id);
  } catch (e) {
  }
  try {
    await api.downloads.erase({ id: item.id });
  } catch (e) {
  }
  const response = await downloadWithApp(url, { referrer: item.referrer, fileName, mime: item.mime, size: item.fileSize });
  if (!response.ok) {
    skipUntil = Date.now() + 5000;
    try {
      await api.downloads.download({ url });
    } catch (e) {
    }
  }
});

async function createMenus() {
  try {
    await api.contextMenus.removeAll();
  } catch (e) {
  }
  api.contextMenus.create({ id: "link", title: "Download with File Rush", contexts: ["link"] });
  api.contextMenus.create({ id: "media", title: "Download with File Rush", contexts: ["image", "video", "audio"] });
  api.contextMenus.create({ id: "page-links", title: "Download all links with File Rush", contexts: ["page"] });
  api.contextMenus.create({ id: "selection-links", title: "Download selected links with File Rush", contexts: ["selection"] });
  api.contextMenus.create({ id: "page-media", title: "Download page media with File Rush", contexts: ["page"] });
}
api.runtime.onInstalled.addListener(createMenus);
api.runtime.onStartup.addListener(createMenus);

function collectLinksInPage(selectionOnly) {
  const seen = new Set();
  const urls = [];
  const add = href => {
    if (!href || !/^(https?|ftp):/i.test(href)) return;
    const clean = href.split("#")[0];
    if (seen.has(clean)) return;
    seen.add(clean);
    urls.push(clean);
  };
  if (selectionOnly) {
    const selection = window.getSelection();
    if (selection && selection.rangeCount) {
      for (let i = 0; i < selection.rangeCount; i++) {
        const fragment = selection.getRangeAt(i).cloneContents();
        fragment.querySelectorAll("a[href]").forEach(a => add(a.href));
      }
      const anchor = selection.anchorNode && selection.anchorNode.parentElement ? selection.anchorNode.parentElement.closest("a[href]") : null;
      if (anchor) add(anchor.href);
    }
    return urls;
  }
  document.querySelectorAll("a[href]").forEach(a => add(a.href));
  return urls;
}

function collectMediaInPage() {
  const seen = new Set();
  const urls = [];
  const add = src => {
    if (!src || !/^(https?):/i.test(src) || src.startsWith("blob:")) return;
    if (seen.has(src)) return;
    seen.add(src);
    urls.push(src);
  };
  document.querySelectorAll("video, audio, source, img").forEach(el => {
    add(el.currentSrc || el.src);
  });
  return urls;
}

async function collect(tabId, func, args) {
  try {
    const results = await api.scripting.executeScript({ target: { tabId }, func, args });
    return (results && results[0] && results[0].result) || [];
  } catch (e) {
    return [];
  }
}

api.contextMenus.onClicked.addListener(async (info, tab) => {
  const referrer = info.pageUrl || (tab && tab.url) || "";
  if (info.menuItemId === "link" && info.linkUrl) {
    await downloadWithApp(info.linkUrl, { referrer });
    return;
  }
  if (info.menuItemId === "media" && info.srcUrl) {
    await downloadWithApp(info.srcUrl, { referrer });
    return;
  }
  if (!tab || tab.id === undefined) return;
  let urls = [];
  if (info.menuItemId === "page-links") urls = await collect(tab.id, collectLinksInPage, [false]);
  else if (info.menuItemId === "selection-links") urls = await collect(tab.id, collectLinksInPage, [true]);
  else if (info.menuItemId === "page-media") urls = await collect(tab.id, collectMediaInPage, []);
  if (urls.length) await sendBatch(urls, referrer);
  else notify("File Rush", "No links were found.");
});

function mediaList(tabId) {
  const map = mediaByTab.get(tabId);
  return map ? Array.from(map.values()) : [];
}

async function updateBadge(tabId) {
  const count = mediaList(tabId).length;
  try {
    await api.action.setBadgeText({ tabId, text: count ? String(count) : "" });
    await api.action.setBadgeBackgroundColor({ tabId, color: "#2E9E3F" });
  } catch (e) {
  }
}

function pushMedia(tabId) {
  api.tabs.sendMessage(tabId, { type: "media", items: mediaList(tabId) }).catch(() => {});
}

function addMedia(tabId, item) {
  let map = mediaByTab.get(tabId);
  if (!map) {
    map = new Map();
    mediaByTab.set(tabId, map);
  }
  const key = item.url.split("#")[0];
  const existing = map.get(key);
  if (existing) {
    if (item.size > existing.size) existing.size = item.size;
    return;
  }
  if (map.size >= 50) return;
  map.set(key, { ...item, url: key });
  updateBadge(tabId);
  pushMedia(tabId);
}

api.webRequest.onHeadersReceived.addListener(details => {
  if (details.tabId < 0 || details.statusCode >= 300) return;
  const headers = details.responseHeaders || [];
  const get = name => {
    const header = headers.find(h => h.name.toLowerCase() === name);
    return header && header.value ? header.value : "";
  };
  const type = get("content-type").toLowerCase().split(";")[0].trim();
  const url = details.url;
  if (STREAM_SEGMENTS.test(url)) return;
  const looksLikeMedia = type.startsWith("video/") || type.startsWith("audio/")
    || (type === "application/octet-stream" && MEDIA_EXTENSIONS.test(url))
    || (details.type === "media" && !type.startsWith("text/") && !type.startsWith("image/"));
  if (!looksLikeMedia) return;
  let size = -1;
  const range = /\/(\d+)\s*$/.exec(get("content-range"));
  if (range) size = Number(range[1]);
  else if (get("content-length")) size = Number(get("content-length"));
  if (size >= 0 && size < settings.minMediaSize) return;
  addMedia(details.tabId, {
    url,
    type,
    size,
    referrer: details.initiator || details.documentUrl || "",
    time: Date.now()
  });
}, { urls: ["<all_urls>"], types: ["media", "xmlhttprequest", "other", "object", "main_frame", "sub_frame"] }, ["responseHeaders"]);

api.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (changeInfo.status === "loading" && changeInfo.url) {
    mediaByTab.delete(tabId);
    updateBadge(tabId);
  }
});
api.tabs.onRemoved.addListener(tabId => mediaByTab.delete(tabId));

api.runtime.onMessage.addListener((message, sender, sendResponse) => {
  (async () => {
    const tabId = (sender.tab && sender.tab.id !== undefined) ? sender.tab.id : message.tabId;
    switch (message && message.type) {
      case "getSettings":
        sendResponse(settings);
        return;
      case "setSettings":
        await api.storage.sync.set(message.settings || {});
        await loadSettings();
        sendResponse(settings);
        return;
      case "skipNext":
        skipUntil = Date.now() + 3000;
        sendResponse({ ok: true });
        return;
      case "downloadLink":
      case "download":
        sendResponse(await downloadWithApp(message.url, { referrer: message.referrer, fileName: message.fileName }));
        return;
      case "getMedia":
        sendResponse(mediaList(tabId));
        return;
      case "batchLinks":
        {
          const urls = await collect(tabId, collectLinksInPage, [false]);
          sendResponse(urls.length ? await sendBatch(urls, message.referrer) : { ok: false, error: "No links were found on this page." });
        }
        return;
      case "ping":
        sendResponse(await sendToApp({ type: "ping" }));
        return;
      default:
        sendResponse({ ok: false, error: "Unknown message" });
    }
  })();
  return true;
});
