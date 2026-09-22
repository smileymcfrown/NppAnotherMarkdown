import DOMPurify from 'dompurify';

// One shared instance: the config below is applied on every call, so hooks and
// settings never leak between renders.
const purifier = DOMPurify();

// What the plugin pack legitimately emits and the defaults would strip:
//  - KaTeX wraps its MathML in <semantics>/<annotation encoding="application/x-tex">.
//  - Links may carry target="_blank"; the host routes new-window requests through
//    the same scheme allowlist as normal navigation, so allowing it is safe.
const ADD_TAGS = ['semantics', 'annotation'];
const ADD_ATTR = ['target'];

// Never wanted in a preview, even though DOMPurify's defaults tolerate some of them:
//  - <form> could be pointed at the api.example/local.example endpoints,
//  - <meta> could inject http-equiv refresh / CSP tricks,
//  - <base> would redirect every relative URL on the page.
const FORBID_TAGS = ['form', 'meta', 'base', 'link'];

// DOMPurify's SANITIZE_DOM drops every id that happens to be a property name of
// `document` ("links", "images", "title", "body", ...), which would break the
// GitHub-style heading anchors for headings with those names. Document-level
// DOM clobbering only works through the `name` attribute of img/embed/form/
// iframe/object (the last four are removed anyway), so strip exactly that.
purifier.addHook('uponSanitizeAttribute', (node, data) => {
  if (data.attrName === 'name' && ['IMG', 'EMBED', 'FORM', 'IFRAME', 'OBJECT'].includes(node.nodeName)) {
    data.keepAttr = false;
  }
});

export function sanitizeMarkdownHtml(html: string): DocumentFragment {
  return purifier.sanitize(html, {
    USE_PROFILES: { html: true, svg: true, svgFilters: true, mathMl: true },
    ADD_TAGS,
    ADD_ATTR,
    FORBID_TAGS,
    SANITIZE_DOM: false,
    // Keep the text of removed elements (e.g. a stripped <form> still shows its
    // fields' labels) - but never the content of script-like elements, which
    // DOMPurify drops entirely regardless of this flag.
    KEEP_CONTENT: true,
    // Parse everything as body content so a fragment that starts with <style> or a
    // comment is not shunted into <head> and lost.
    FORCE_BODY: true,
    // Hand back live nodes instead of a string: saves a second HTML parse on large
    // documents and avoids any mutation-XSS window between serialization and insert.
    RETURN_DOM_FRAGMENT: true
  });
}
