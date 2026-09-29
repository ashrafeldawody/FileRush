# File Rush Integration Module

Browser extension that connects Chrome, Edge, Brave, Vivaldi, Chromium, Opera and Firefox to File Rush, the way the IDM integration module does.

What it does:

* Takes over downloads the browser starts (file types are configurable, or catch everything) and hands them to File Rush with the page's cookies, referrer and user agent.
* Catches clicks on links to monitored file types. Hold **Alt** while clicking to let the browser handle a link.
* Sniffs video and audio streams on the current tab, shows a count on the toolbar icon and a floating "Download video" panel on the page.
* Context menu: download a link, image, video or audio, all links on the page, links in the selection, or the page's media as a batch.
* Popup: connection status, quick enable/disable, detected media, "Download all links".

## Install (Chrome, Edge, Brave, Vivaldi, Chromium, Opera)

1. Start File Rush once. It registers the native messaging host for all supported browsers automatically (Options, General shows the state and has "Register browser integration").
2. Open the browser's extensions page (`chrome://extensions`, `edge://extensions`, `brave://extensions`, ...), turn on **Developer mode**, press **Load unpacked** and select this `browser-extension` folder.
3. The extension has a fixed ID (`gpmhgpppbgfloobhjejdhaoajaielbpd`), so the host manifest written by File Rush already allows it.

## Install (Firefox)

1. Replace `manifest.json` with `manifest.firefox.json` (copy the folder first if you also use a Chromium browser).
2. Open `about:debugging#/runtime/this-firefox`, press **Load Temporary Add-on** and select `manifest.json`. Temporary add-ons are removed when Firefox exits; Firefox Developer Edition or Nightly with `xpinstall.signatures.required=false` can install the folder permanently as a zip.
3. Grant the extension access to all sites in `about:addons` (Firefox treats host permissions as optional in Manifest V3).

## Native host

The host is File Rush itself: the browser starts `FileRush.exe` with the extension origin as an argument, the app reads the native messaging stream and forwards messages to the running instance through a named pipe (starting it minimized if needed). Manifests are written to `%LocalAppData%\FileRush\NativeHost` and registered under `HKCU\Software\<browser>\NativeMessagingHosts\com.filerush.host`.
