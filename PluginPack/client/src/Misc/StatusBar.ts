// Link preview for the host's status bar: while the mouse is over a link the
// target is sent to the host, which shows it in the preview window's status
// bar (Settings: "Show Statusbar in Preview Window").
//
// Sent through chrome.webview.postMessage rather than the api.example host:
// it is a fire-and-forget UI hint, and postMessage is only reachable from the
// page's own scripts.

let installed = false;
let lastText = "";

function postStatus(text: string) {
  if (text === lastText) {
    return;
  }
  lastText = text;
  const webview = (window as any).chrome?.webview;
  if (webview && typeof webview.postMessage === "function") {
    webview.postMessage({ event: "statusText", text });
  }
}

function linkTarget(a: HTMLAnchorElement): string {
  const raw = a.getAttribute("href") ?? "";
  if (raw.startsWith("#")) {
    return raw;
  }
  // Relative links resolve against the local.example base; show them as paths.
  return a.href.replace(/^http:\/\/local\.example\/disk(\w)\//i, "$1:/");
}

export function InitStatusBar() {
  if (installed) {
    return;
  }
  installed = true;
  document.addEventListener("mouseover", (e) => {
    const a = (e.target as Element | null)?.closest?.("a[href]") as HTMLAnchorElement | null;
    postStatus(a ? linkTarget(a) : "");
  }, { passive: true });
  document.addEventListener("mouseleave", () => postStatus(""), { passive: true });
}
