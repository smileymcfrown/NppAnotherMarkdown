// Snapshot of the rendered preview for "Save as HTML" / "Copy to clipboard" on
// the C# side. The host turns this into a standalone document: it inlines the
// stylesheets (mapping the assets.example / local.example hosts back to disk
// paths) and rewrites resource URLs to file:// so the export works from anywhere.

export interface IExportStyle {
  href?: string;   // external stylesheet (link[rel=stylesheet])
  text?: string;   // inline <style> from the head or from the document itself
}

export interface IExportedDocument {
  title: string;
  body: string;
  styles: IExportStyle[];
}

const RESOLVABLE = ['src', 'href', 'poster', 'data'];

export function exportDocument(container: HTMLElement): IExportedDocument {
  const clone = container.cloneNode(true) as HTMLElement;

  // Relative URLs resolve against <base href="http://local.example/..."> in the
  // live page; freeze them to absolute so the exported file does not depend on
  // where it is saved. Same-document anchors stay as they are.
  for (const el of Array.from(clone.querySelectorAll('*'))) {
    for (const attr of RESOLVABLE) {
      const value = el.getAttribute(attr);
      if (value === null || value === '' || value.startsWith('#') || value.startsWith('data:')) {
        continue;
      }
      try {
        el.setAttribute(attr, new URL(value, document.baseURI).href);
      }
      catch (err) { }
    }
    if (el.tagName === 'IMG' && el.hasAttribute('srcset')) {
      el.removeAttribute('srcset');
    }
  }

  // Scroll-sync anchors (markdown-it-linemark) are noise outside the preview.
  for (const el of Array.from(clone.querySelectorAll('span.linemark, button.am-copy-code'))) {
    el.remove();
  }

  const styles: IExportStyle[] = [];
  for (const node of Array.from(document.head.querySelectorAll('link[rel="stylesheet"], style'))) {
    if (node.tagName === 'LINK') {
      styles.push({ href: (node as HTMLLinkElement).href });
    }
    else if (node.textContent) {
      styles.push({ text: node.textContent });
    }
  }

  return {
    title: document.title,
    body: clone.innerHTML,
    styles
  };
}
