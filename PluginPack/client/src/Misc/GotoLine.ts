import { notifyWebEvent } from "../Client/Webevent";

// Double-click in the preview moves the editor caret to the corresponding
// source line: the line of the nearest preceding linemark anchor (the
// <span class="linemark" id="LINE<n>"> elements markdown-it-linemark inserts).

let installed = false;

export function InitGotoLine(container: HTMLElement) {
  if (installed) {
    return;
  }
  installed = true;
  container.addEventListener("dblclick", (e) => {
    const target = e.target as Element | null;
    if (!target || target.closest("a, input, button, .am-findbar, .panorama")) {
      return;
    }
    const line = lineAt(container, target);
    if (line !== null) {
      notifyWebEvent("gotoLine", { line }).catch(() => { });
    }
  });
}

function lineAt(container: HTMLElement, target: Element): number | null {
  const anchors = container.querySelectorAll<HTMLElement>("span.linemark");
  if (anchors.length === 0) {
    return null;
  }
  // Binary search for the last anchor that is not after the clicked element:
  // either it precedes the element, or it sits inside it (a block's own anchor
  // is its first child).
  let lo = 0;
  let hi = anchors.length - 1;
  let found = -1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    const position = anchors[mid].compareDocumentPosition(target);
    const notAfter = (position & (Node.DOCUMENT_POSITION_FOLLOWING | Node.DOCUMENT_POSITION_CONTAINS)) !== 0
      || anchors[mid] === target;
    if (notAfter) {
      found = mid;
      lo = mid + 1;
    }
    else {
      hi = mid - 1;
    }
  }
  if (found < 0) {
    return null;
  }
  // If the element contains anchors (a list, a table, ...), use its first one.
  while (found > 0 && target.contains(anchors[found - 1])) {
    found--;
  }
  const line = Number.parseInt(anchors[found].id.replace(/^LINE/, ""));
  return Number.isNaN(line) ? null : line;
}
