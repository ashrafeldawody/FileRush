const api = typeof browser !== "undefined" ? browser : chrome;

function send(message) {
  return api.runtime.sendMessage(message);
}

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
    return decodeURIComponent(path.substring(path.lastIndexOf("/") + 1)) || url;
  } catch (e) {
    return url;
  }
}

async function activeTab() {
  const tabs = await api.tabs.query({ active: true, currentWindow: true });
  return tabs && tabs[0];
}

function setStatus(ok, text) {
  const dot = document.getElementById("dot");
  dot.className = "dot " + (ok ? "ok" : "bad");
  document.getElementById("statusText").textContent = text;
}

async function init() {
  const settings = await send({ type: "getSettings" });
  const enabled = document.getElementById("enabled");
  enabled.checked = !!settings.enabled;
  enabled.addEventListener("change", () => send({ type: "setSettings", settings: { enabled: enabled.checked } }));

  const tab = await activeTab();
  document.getElementById("allLinks").addEventListener("click", async () => {
    if (!tab) return;
    const response = await send({ type: "batchLinks", tabId: tab.id, referrer: tab.url });
    if (!response.ok) alert(response.error || "Could not send the links to File Rush.");
    else window.close();
  });
  document.getElementById("options").addEventListener("click", () => api.runtime.openOptionsPage());

  const container = document.getElementById("media");
  const items = tab ? await send({ type: "getMedia", tabId: tab.id }) : [];
  if (Array.isArray(items) && items.length) {
    container.innerHTML = "";
    for (const item of items) {
      const row = document.createElement("div");
      row.className = "item";
      const name = document.createElement("span");
      name.className = "n";
      name.textContent = nameOf(item.url);
      name.title = item.url;
      const meta = document.createElement("span");
      meta.className = "m";
      meta.textContent = [item.type, formatSize(item.size)].filter(Boolean).join(" · ");
      const button = document.createElement("button");
      button.className = "primary";
      button.textContent = "Download";
      button.addEventListener("click", async () => {
        const response = await send({ type: "download", url: item.url, referrer: tab ? tab.url : "" });
        if (!response.ok) alert(response.error || "Could not send the file to File Rush.");
        else window.close();
      });
      row.appendChild(name);
      row.appendChild(meta);
      row.appendChild(button);
      container.appendChild(row);
    }
  }

  const ping = await send({ type: "ping" });
  if (ping && ping.ok) setStatus(true, ping.running === false ? "File Rush will start on demand" : "Connected to File Rush");
  else setStatus(false, "File Rush host not found");
}

init().catch(e => setStatus(false, e && e.message ? e.message : "Error"));
