const api = typeof browser !== "undefined" ? browser : chrome;

async function load() {
  const settings = await api.runtime.sendMessage({ type: "getSettings" });
  document.getElementById("enabled").checked = !!settings.enabled;
  document.getElementById("catchLinks").checked = !!settings.catchLinks;
  document.getElementById("catchAll").checked = !!settings.catchAll;
  document.getElementById("showPanel").checked = !!settings.showPanel;
  document.getElementById("minMediaSize").value = Math.round((settings.minMediaSize || 0) / 1024);
  document.getElementById("extensions").value = settings.extensions || "";
  document.getElementById("exceptions").value = (settings.exceptions || "").split(/[\s,;]+/).filter(Boolean).join("\n");
}

async function save() {
  const settings = {
    enabled: document.getElementById("enabled").checked,
    catchLinks: document.getElementById("catchLinks").checked,
    catchAll: document.getElementById("catchAll").checked,
    showPanel: document.getElementById("showPanel").checked,
    minMediaSize: Math.max(0, Number(document.getElementById("minMediaSize").value) || 0) * 1024,
    extensions: document.getElementById("extensions").value.trim(),
    exceptions: document.getElementById("exceptions").value.split(/\r?\n/).map(s => s.trim()).filter(Boolean).join(" ")
  };
  await api.runtime.sendMessage({ type: "setSettings", settings });
  const saved = document.getElementById("saved");
  saved.textContent = "Saved";
  setTimeout(() => saved.textContent = "", 2000);
}

document.getElementById("save").addEventListener("click", save);
load();
