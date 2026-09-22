import { notifyWebEvent } from "../Client/Webevent";

// Document outline: a fixed sidebar listing the headings of the rendered
// document, a burger button to collapse it, and highlighting of the heading
// that is currently at the top of the viewport. Modeled on the outline of
// mohzy83/NppMarkdownPanel.
//
// The collapsed state is reported to the host ("outlineCollapsed" web event),
// which hands it back in the page options, so it survives the full page reload
// that happens when switching documents. localStorage is not usable here: the
// loader page is loaded with NavigateToString and has an opaque origin.

const SIDEBAR_ID = "outline-sidebar";
const TOGGLE_ID = "outline-toggle";
const HEADINGS = "h1, h2, h3, h4, h5, h6";

let enabled = false;
let collapsed = false;
let container: HTMLElement | null = null;
let scrollHandlerInstalled = false;

export function InitOutline(show: boolean, startCollapsed: boolean, content: HTMLElement) {
  container = content;
  if (!show) {
    if (enabled) {
      removeOutline();
    }
    enabled = false;
    return;
  }
  enabled = true;
  lastSignature = "";
  collapsed = startCollapsed;
  ensureElements();
  buildOutline();
  applyCollapsed();
}

// Called after every render. Rebuilding the list means a layout pass over every
// heading, so it only happens when the headings actually changed.
let lastSignature = "";

export function RefreshOutline() {
  if (!enabled) {
    return;
  }
  const signature = JSON.stringify(headings().map(h => h.tagName + ":" + (h.textContent ?? "")));
  if (signature === lastSignature) {
    return;
  }
  lastSignature = signature;
  buildOutline();
  updateActiveItem();
}

function ensureElements() {
  if (document.getElementById(SIDEBAR_ID)) {
    return;
  }
  const sidebar = document.createElement("nav");
  sidebar.id = SIDEBAR_ID;
  sidebar.className = "outline-sidebar";
  sidebar.innerHTML = '<div class="outline-header">Outline</div><div class="outline-content"></div>';

  const toggle = document.createElement("button");
  toggle.id = TOGGLE_ID;
  toggle.className = "outline-toggle";
  toggle.type = "button";
  toggle.title = "Toggle outline";
  toggle.innerHTML = "&#9776;";
  toggle.addEventListener("click", () => {
    collapsed = !collapsed;
    applyCollapsed();
    notifyWebEvent("outlineCollapsed", { collapsed }).catch(() => { });
  });

  document.body.insertBefore(sidebar, document.body.firstChild);
  document.body.appendChild(toggle);

  if (!scrollHandlerInstalled) {
    scrollHandlerInstalled = true;
    document.addEventListener("scroll", scheduleUpdateActiveItem, { passive: true });
  }
}

function removeOutline() {
  document.getElementById(SIDEBAR_ID)?.remove();
  document.getElementById(TOGGLE_ID)?.remove();
  document.documentElement.classList.remove("outline-open", "outline-enabled");
}

function applyCollapsed() {
  const sidebar = document.getElementById(SIDEBAR_ID);
  const toggle = document.getElementById(TOGGLE_ID);
  if (!sidebar || !toggle) {
    return;
  }
  const hasHeadings = sidebar.querySelector(".outline-item") !== null;
  sidebar.classList.toggle("collapsed", collapsed || !hasHeadings);
  toggle.classList.toggle("collapsed", collapsed);
  toggle.style.display = hasHeadings ? "" : "none";
  document.documentElement.classList.add("outline-enabled");
  document.documentElement.classList.toggle("outline-open", hasHeadings && !collapsed);
}

function headings(): HTMLHeadingElement[] {
  return container ? Array.from(container.querySelectorAll<HTMLHeadingElement>(HEADINGS)) : [];
}

function buildOutline() {
  const sidebar = document.getElementById(SIDEBAR_ID);
  if (!sidebar) {
    return;
  }
  const list = sidebar.querySelector(".outline-content") as HTMLElement;
  list.innerHTML = "";

  const hs = headings();
  let min = 7;
  for (const h of hs) {
    min = Math.min(min, parseInt(h.tagName.substring(1)));
  }
  hs.forEach((h, index) => {
    const level = parseInt(h.tagName.substring(1)) - min + 1;
    const item = document.createElement("a");
    item.className = `outline-item outline-l${level}`;
    item.dataset.index = String(index);
    item.textContent = h.textContent ?? "";
    item.title = item.textContent;
    item.href = "#";
    item.addEventListener("click", (e) => {
      e.preventDefault();
      const target = headings()[index];
      if (target) {
        const y = target.getBoundingClientRect().top + window.pageYOffset - 8;
        // Plain scrollTo (not SyncView's scrollToY) so the scroll handler still
        // fires and the editor follows when two-way sync is on.
        window.scrollTo({ top: Math.max(0, y), behavior: "smooth" });
      }
    });
    list.appendChild(item);
  });
  applyCollapsed();
}

// One layout pass per frame at most: a large document has many headings.
let updateScheduled = false;
function scheduleUpdateActiveItem() {
  if (updateScheduled) {
    return;
  }
  updateScheduled = true;
  requestAnimationFrame(() => {
    updateScheduled = false;
    updateActiveItem();
  });
}

function updateActiveItem() {
  const sidebar = document.getElementById(SIDEBAR_ID);
  if (!enabled || !sidebar) {
    return;
  }
  const hs = headings();
  const items = sidebar.querySelectorAll<HTMLElement>(".outline-item");
  if (hs.length === 0 || items.length !== hs.length) {
    return;
  }
  const threshold = window.pageYOffset + Math.min(140, window.innerHeight / 3);
  let active = -1;
  for (let i = 0; i < hs.length; i++) {
    if (hs[i].getBoundingClientRect().top + window.pageYOffset <= threshold) {
      active = i;
    }
  }
  if (active === -1 && hs.length > 0) {
    active = 0;
  }
  items.forEach((item, i) => item.classList.toggle("active", i === active));
  if (active >= 0) {
    const item = items[active];
    const box = sidebar.getBoundingClientRect();
    const r = item.getBoundingClientRect();
    if (r.top < box.top || r.bottom > box.bottom) {
      item.scrollIntoView({ block: "nearest" });
    }
  }
}
