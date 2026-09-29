(() => {
  if (window.top !== window) return;
  const api = typeof browser !== "undefined" ? browser : chrome;
  let settings = null;
  let extensionSet = null;
  let items = [];
  let host = null;
  let dismissed = false;
  let expanded = false;

  function applySettings(next) {
    settings = next || null;
    extensionSet = settings
      ? new Set(String(settings.extensions || "").split(/[\s,;]+/).filter(Boolean).map(e => e.replace(/^\./, "").toUpperCase()))
      : null;
    render();
  }

  function refreshSettings() {
    try {
      api.runtime.sendMessage({ type: "getSettings" }).then(applySettings).catch(() => {});
    } catch (e) {
    }
  }
  refreshSettings();
  try {
    api.storage.onChanged.addListener(() => refreshSettings());
  } catch (e) {
  }

  function extensionOf(url) {
    try {
      const path = new URL(url).pathname;
      const name = path.substring(path.lastIndexOf("/") + 1);
      const dot = name.lastIndexOf(".");
      return dot >= 0 && dot < name.length - 1 ? name.substring(dot + 1).toUpperCase() : "";
    } catch (e) {
      return "";
    }
  }

  document.addEventListener("click", event => {
    const target = event.target;
    const anchor = target && target.closest ? target.closest("a[href]") : null;
    if (!anchor) return;
    if (event.altKey) {
      try {
        api.runtime.sendMessage({ type: "skipNext" }).catch(() => {});
      } catch (e) {
      }
      return;
    }
    if (!settings || !settings.enabled || !settings.catchLinks) return;
    if (event.button !== 0 || event.ctrlKey || event.shiftKey || event.metaKey) return;
    const href = anchor.href;
    if (!/^(https?|ftp):/i.test(href)) return;
    if (anchor.hasAttribute("download") && !extensionSet.has(extensionOf(href))) return;
    const ext = extensionOf(href);
    if (!ext || !extensionSet.has(ext)) return;
    event.preventDefault();
    event.stopPropagation();
    try {
      api.runtime.sendMessage({ type: "downloadLink", url: href, referrer: location.href }).catch(() => {});
    } catch (e) {
    }
  }, true);

  function formatSize(size) {
    if (size < 0) return "";
    const units = ["B", "KB", "MB", "GB"];
    let value = size;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
      value /= 1024;
      unit++;
    }
    return value.toFixed(unit === 0 ? 0 : 1) + " " + units[unit];
  }

  function nameOf(url) {
    try {
      const path = new URL(url).pathname;
      const name = decodeURIComponent(path.substring(path.lastIndexOf("/") + 1));
      return name || url;
    } catch (e) {
      return url;
    }
  }

  function download(url) {
    try {
      api.runtime.sendMessage({ type: "download", url, referrer: location.href }).catch(() => {});
    } catch (e) {
    }
  }

  function render() {
    const visible = settings && settings.showPanel && !dismissed && items.length > 0;
    if (!visible) {
      if (host) {
        host.remove();
        host = null;
      }
      return;
    }
    if (!host) {
      host = document.createElement("div");
      host.id = "filerush-panel-host";
      host.style.all = "initial";
      host.style.position = "fixed";
      host.style.top = "12px";
      host.style.right = "12px";
      host.style.zIndex = "2147483647";
      (document.body || document.documentElement).appendChild(host);
      host.attachShadow({ mode: "open" });
    }
    const root = host.shadowRoot;
    root.innerHTML = "";
    const style = document.createElement("style");
    style.textContent = `
      .panel { font: 13px/1.4 "Segoe UI", Arial, sans-serif; color: #111; background: #fff; border: 1px solid #b9c3d0; border-radius: 6px; box-shadow: 0 4px 16px rgba(0,0,0,.25); min-width: 220px; max-width: 420px; }
      .header { display: flex; align-items: center; gap: 8px; padding: 6px 8px; background: linear-gradient(#fdfdfd, #e9edf3); border-bottom: 1px solid #d4dbe4; border-radius: 6px 6px 0 0; }
      .logo { width: 18px; height: 18px; border-radius: 50%; background: #2e9e3f; display: inline-flex; align-items: center; justify-content: center; color: #fff; font-weight: bold; font-size: 12px; }
      .title { flex: 1; font-weight: 600; cursor: pointer; }
      .close { cursor: pointer; padding: 0 4px; color: #555; }
      .list { max-height: 260px; overflow: auto; }
      .item { display: flex; align-items: center; gap: 8px; padding: 6px 8px; border-bottom: 1px solid #eef1f5; }
      .item:last-child { border-bottom: none; }
      .name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 260px; }
      .meta { color: #6b7280; font-size: 11px; }
      button { font: inherit; background: #2e9e3f; color: #fff; border: none; border-radius: 4px; padding: 4px 10px; cursor: pointer; }
      button:hover { background: #26833a; }
    `;
    root.appendChild(style);
    const panel = document.createElement("div");
    panel.className = "panel";
    const header = document.createElement("div");
    header.className = "header";
    const logo = document.createElement("span");
    logo.className = "logo";
    logo.textContent = "↓";
    const title = document.createElement("span");
    title.className = "title";
    title.textContent = items.length === 1 ? "Download this video" : `Download video (${items.length})`;
    title.addEventListener("click", () => {
      if (items.length === 1) download(items[0].url);
      else {
        expanded = !expanded;
        render();
      }
    });
    const close = document.createElement("span");
    close.className = "close";
    close.textContent = "×";
    close.title = "Hide";
    close.addEventListener("click", () => {
      dismissed = true;
      render();
    });
    header.appendChild(logo);
    header.appendChild(title);
    header.appendChild(close);
    panel.appendChild(header);
    if (expanded || items.length === 1) {
      const list = document.createElement("div");
      list.className = "list";
      for (const item of items) {
        const row = document.createElement("div");
        row.className = "item";
        const name = document.createElement("span");
        name.className = "name";
        name.textContent = nameOf(item.url);
        name.title = item.url;
        const meta = document.createElement("span");
        meta.className = "meta";
        meta.textContent = [item.type, formatSize(item.size)].filter(Boolean).join(" · ");
        const button = document.createElement("button");
        button.textContent = "Download";
        button.addEventListener("click", () => download(item.url));
        row.appendChild(name);
        row.appendChild(meta);
        row.appendChild(button);
        list.appendChild(row);
      }
      panel.appendChild(list);
    }
    root.appendChild(panel);
  }

  try {
    api.runtime.onMessage.addListener(message => {
      if (message && message.type === "media") {
        items = Array.isArray(message.items) ? message.items : [];
        render();
      }
    });
    api.runtime.sendMessage({ type: "getMedia" }).then(list => {
      items = Array.isArray(list) ? list : [];
      render();
    }).catch(() => {});
  } catch (e) {
  }
})();
