// Link preview for the host's status bar: while the mouse is over a link the
// target is sent to the host, which shows it in the preview window's status
// bar (Settings: "Show Statusbar in Preview Window").
//
// Sent through chrome.webview.postMessage rather than the api.example host:
// it is a fire-and-forget UI hint, and postMessage is only reachable from the
// page's own scripts.

let installed = false;
let lastText = "";
let defaultText = "";
let hovering = false;

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

// Text shown while no link is hovered (word count etc.).
export function SetStatusInfo(text: string) {
  defaultText = text;
  if (!hovering) {
    postStatus(defaultText);
  }
}

// Words / characters / estimated reading time of the rendered document.
export function UpdateDocumentStats(container: HTMLElement) {
  // Text nodes joined with spaces (innerText would force layout on the whole,
  // possibly huge, document); UI added by the preview itself is skipped.
  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT, {
    acceptNode: (n) => n.parentElement?.closest("button, script, style") ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT
  });
  const parts: string[] = [];
  let n: Node | null;
  while ((n = walker.nextNode())) {
    parts.push((n as Text).data);
  }
  const text = parts.join(" ");
  const words = text.split(/\s+/).filter(w => w.length !== 0).length;
  const chars = text.replace(/\s/g, "").length;
  const minutes = Math.max(1, Math.round(words / 200));
  SetStatusInfo(`${words.toLocaleString()} words, ${chars.toLocaleString()} characters, ~${minutes} min read`);
}

export function InitStatusBar() {
  if (installed) {
    return;
  }
  installed = true;
  document.addEventListener("mouseover", (e) => {
    const a = (e.target as Element | null)?.closest?.("a[href]") as HTMLAnchorElement | null;
    hovering = a !== null;
    postStatus(a ? linkTarget(a) : defaultText);
  }, { passive: true });
  document.addEventListener("mouseleave", () => {
    hovering = false;
    postStatus(defaultText);
  }, { passive: true });
}
