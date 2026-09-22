// "Copy" button on code blocks. The text goes to the host through
// chrome.webview.postMessage (the page has an opaque origin, so the async
// Clipboard API is not available to it) and the host puts it on the clipboard.

const WRAPPER_CLASS = "am-code";
const BUTTON_CLASS = "am-copy-code";

export function InitCodeCopy(container: HTMLElement) {
  for (const pre of Array.from(container.querySelectorAll<HTMLPreElement>("pre"))) {
    if (pre.classList.contains("mermaid") || pre.closest("." + WRAPPER_CLASS)) {
      continue;
    }
    const wrapper = document.createElement("div");
    wrapper.className = WRAPPER_CLASS;
    pre.parentNode?.insertBefore(wrapper, pre);
    wrapper.appendChild(pre);

    const button = document.createElement("button");
    button.type = "button";
    button.className = BUTTON_CLASS;
    button.textContent = "Copy";
    button.title = "Copy code to clipboard";
    button.addEventListener("click", () => {
      const webview = (window as any).chrome?.webview;
      if (webview && typeof webview.postMessage === "function") {
        webview.postMessage({ event: "copyText", text: pre.innerText.replace(/\n$/, "") });
      }
      button.textContent = "Copied";
      button.classList.add("done");
      setTimeout(() => {
        button.textContent = "Copy";
        button.classList.remove("done");
      }, 1500);
    });
    wrapper.appendChild(button);
  }
}
