// Heading ids and same-document links.
//
// Headings get GitHub-style ids (markdown-it-anchor with the slugify below) so
// "[see](#some-heading)" works the way it does on GitHub.
//
// Clicking a "#fragment" link cannot be left to the browser here: the loader
// page has <base href="http://local.example/..."> while the document itself
// was loaded with NavigateToString (about:blank), so the browser treats the
// fragment as a navigation to another URL, which the host then cancels. The
// click handler below scrolls to the target instead. This also makes footnote
// references and their back-links work.

export function slugify(text: string): string {
  return text
    .trim()
    .toLowerCase()
    // keep letters, numbers, spaces, hyphens and underscores (any script)
    .replace(/[^\p{L}\p{N}\p{M}\s_-]/gu, "")
    .replace(/\s+/g, "-");
}

let installed = false;

export function InitAnchorLinks() {
  if (installed) {
    return;
  }
  installed = true;
  document.addEventListener("click", (e) => {
    if (e.defaultPrevented || e.button !== 0) {
      return;
    }
    const a = (e.target as Element | null)?.closest?.("a[href]") as HTMLAnchorElement | null;
    if (!a) {
      return;
    }
    const href = a.getAttribute("href") ?? "";
    if (!href.startsWith("#")) {
      return;
    }
    e.preventDefault();
    if (href === "#") {
      return;
    }
    let id = href.substring(1);
    try {
      id = decodeURIComponent(id);
    }
    catch (err) { }
    const target = document.getElementById(id)
      ?? document.querySelector(`[name="${CSS.escape(id)}"]`);
    if (target) {
      const y = target.getBoundingClientRect().top + window.pageYOffset - 8;
      window.scrollTo({ top: Math.max(0, y), behavior: "smooth" });
    }
  });
}
