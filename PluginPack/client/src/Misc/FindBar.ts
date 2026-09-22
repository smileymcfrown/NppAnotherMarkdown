// In-page find (Ctrl+F). WebView2 ships no find UI, so the preview has its own:
// a small bar at the top right, all matches highlighted with the CSS Custom
// Highlight API (no DOM changes, so it stays fast on very large documents),
// Enter / Shift+Enter or F3 / Shift+F3 to move between matches, Esc to close.
// Highlights are recomputed after every render while the bar is open.

const BAR_ID = "am-findbar";
const HIGHLIGHT_ALL = "am-find";
const HIGHLIGHT_CURRENT = "am-find-current";
const MAX_MATCHES = 5000;

let container: HTMLElement | null = null;
let matches: Range[] = [];
let current = -1;
let lastQuery = "";

const highlightsSupported = () =>
  typeof (window as any).Highlight === "function" && (CSS as any).highlights !== undefined;

export function InitFindBar(content: HTMLElement) {
  container = content;
  if (document.getElementById(BAR_ID)) {
    return;
  }
  const bar = document.createElement("div");
  bar.id = BAR_ID;
  bar.className = "am-findbar";
  bar.hidden = true;
  bar.innerHTML =
    '<input type="text" class="am-find-input" placeholder="Find" spellcheck="false">' +
    '<span class="am-find-count"></span>' +
    '<button type="button" class="am-find-prev" title="Previous (Shift+Enter)">&#9650;</button>' +
    '<button type="button" class="am-find-next" title="Next (Enter)">&#9660;</button>' +
    '<button type="button" class="am-find-close" title="Close (Esc)">&#10005;</button>';
  document.body.appendChild(bar);

  const input = bar.querySelector<HTMLInputElement>(".am-find-input")!;
  input.addEventListener("input", () => search(input.value, true));
  input.addEventListener("keydown", (e) => {
    if (e.key === "Enter") {
      e.preventDefault();
      step(e.shiftKey ? -1 : 1);
    }
    else if (e.key === "Escape") {
      e.preventDefault();
      hide();
    }
  });
  bar.querySelector(".am-find-prev")!.addEventListener("click", () => step(-1));
  bar.querySelector(".am-find-next")!.addEventListener("click", () => step(1));
  bar.querySelector(".am-find-close")!.addEventListener("click", hide);

  document.addEventListener("keydown", (e) => {
    if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === "f") {
      e.preventDefault();
      show();
    }
    else if (e.key === "F3") {
      e.preventDefault();
      if (isOpen()) {
        step(e.shiftKey ? -1 : 1);
      }
      else {
        show();
      }
    }
    else if (e.key === "Escape" && isOpen()) {
      hide();
    }
  });
}

// Re-run the current search after the document was re-rendered.
export function RefreshFind() {
  if (isOpen() && lastQuery) {
    search(lastQuery, false);
  }
}

export function ShowFind() {
  show();
}

function bar() { return document.getElementById(BAR_ID) as HTMLDivElement | null; }
function isOpen() { const b = bar(); return b !== null && !b.hidden; }

function show() {
  const b = bar();
  if (!b) {
    return;
  }
  b.hidden = false;
  const input = b.querySelector<HTMLInputElement>(".am-find-input")!;
  // Prefill with the current text selection, like browsers do.
  const selection = window.getSelection()?.toString().trim();
  if (selection && selection.length < 200 && !selection.includes("\n")) {
    input.value = selection;
  }
  input.focus();
  input.select();
  search(input.value, true);
}

function hide() {
  const b = bar();
  if (b) {
    b.hidden = true;
  }
  clearHighlights();
  matches = [];
  current = -1;
  (document.activeElement as HTMLElement | null)?.blur?.();
}

function search(query: string, resetPosition: boolean) {
  lastQuery = query;
  clearHighlights();
  matches = query ? findRanges(query) : [];
  if (resetPosition || current < 0 || current >= matches.length) {
    current = matches.length ? nearestMatchToViewport() : -1;
  }
  applyHighlights();
  if (current >= 0) {
    scrollToMatch(matches[current]);
  }
  updateCount();
}

function step(direction: number) {
  if (matches.length === 0) {
    const input = bar()?.querySelector<HTMLInputElement>(".am-find-input");
    if (input && input.value !== lastQuery) {
      search(input.value, true);
    }
    return;
  }
  current = (current + direction + matches.length) % matches.length;
  applyHighlights();
  scrollToMatch(matches[current]);
  updateCount();
}

function updateCount() {
  const label = bar()?.querySelector<HTMLElement>(".am-find-count");
  if (!label) {
    return;
  }
  if (!lastQuery) {
    label.textContent = "";
  }
  else if (matches.length === 0) {
    label.textContent = "0/0";
  }
  else {
    label.textContent = `${current + 1}/${matches.length}${matches.length >= MAX_MATCHES ? "+" : ""}`;
  }
  bar()?.classList.toggle("no-match", !!lastQuery && matches.length === 0);
}

// Case-insensitive search over the text of the rendered document. Text nodes are
// concatenated once; each hit is mapped back to a DOM Range.
function findRanges(query: string): Range[] {
  if (!container) {
    return [];
  }
  const needle = query.toLowerCase();
  const nodes: Text[] = [];
  const starts: number[] = [];
  let text = "";
  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT, {
    acceptNode: (n) => {
      const parent = n.parentElement;
      if (!parent || parent.closest("script, style, noscript, .am-findbar")) {
        return NodeFilter.FILTER_REJECT;
      }
      return NodeFilter.FILTER_ACCEPT;
    }
  });
  let n: Node | null;
  while ((n = walker.nextNode())) {
    nodes.push(n as Text);
    starts.push(text.length);
    text += (n as Text).data;
  }
  const haystack = text.toLowerCase();

  const result: Range[] = [];
  let from = 0;
  let nodeIndex = 0;
  while (result.length < MAX_MATCHES) {
    const at = haystack.indexOf(needle, from);
    if (at < 0) {
      break;
    }
    const end = at + needle.length;
    while (nodeIndex + 1 < nodes.length && starts[nodeIndex + 1] <= at) {
      nodeIndex++;
    }
    let endIndex = nodeIndex;
    while (endIndex + 1 < nodes.length && starts[endIndex + 1] < end) {
      endIndex++;
    }
    const range = document.createRange();
    range.setStart(nodes[nodeIndex], at - starts[nodeIndex]);
    range.setEnd(nodes[endIndex], end - starts[endIndex]);
    result.push(range);
    from = end;
  }
  return result;
}

function nearestMatchToViewport(): number {
  const top = window.pageYOffset;
  for (let i = 0; i < matches.length; i++) {
    if (matches[i].getBoundingClientRect().top + window.pageYOffset >= top) {
      return i;
    }
  }
  return 0;
}

function applyHighlights() {
  if (!highlightsSupported()) {
    // Fallback: select the current match so it is at least visible.
    const selection = window.getSelection();
    selection?.removeAllRanges();
    if (current >= 0) {
      selection?.addRange(matches[current]);
    }
    return;
  }
  const registry = (CSS as any).highlights as Map<string, unknown>;
  const HighlightCtor = (window as any).Highlight;
  registry.set(HIGHLIGHT_ALL, new HighlightCtor(...matches));
  registry.set(HIGHLIGHT_CURRENT, new HighlightCtor(...(current >= 0 ? [matches[current]] : [])));
}

function clearHighlights() {
  if (highlightsSupported()) {
    const registry = (CSS as any).highlights as Map<string, unknown>;
    registry.delete(HIGHLIGHT_ALL);
    registry.delete(HIGHLIGHT_CURRENT);
  }
  else {
    window.getSelection()?.removeAllRanges();
  }
}

function scrollToMatch(range: Range) {
  const rect = range.getBoundingClientRect();
  if (rect.top < 60 || rect.bottom > window.innerHeight - 20) {
    window.scrollTo({ top: rect.top + window.pageYOffset - window.innerHeight / 3, behavior: "auto" });
  }
}
