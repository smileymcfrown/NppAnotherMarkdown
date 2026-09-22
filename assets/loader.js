window.createView = async function (args) {
  let options = {
    document: "",
    css: "",
    lineMark: false
  }
  options = { ...options, ...args };

  const plugins = [
    // [ matches document URL, script, stylesheet, view name the host may request ]
    [/\.(md)$/i, 'http://assets.example/markdown/markdown.min.js', options.css, 'markdown'],
    [/\.pano360\.(json)$/i, 'http://assets.example/pano360/editor.js', null, 'pano360']
  ]

  // The host decides which files count as Markdown (supported extension list,
  // "allow all extensions", files without extension); options.view carries the
  // result. "auto" falls back to matching the file name, as before.
  const view = options.view || 'auto';
  if (view === 'unsupported') {
    if (options.css) {
      const link = document.createElement("link");
      link.href = options.css;
      link.rel = 'stylesheet';
      document.head.appendChild(link);
    }
    const content = document.getElementById("content");
    const name = decodeURIComponent(options.document.replace(/^.*\//, ''));
    content.innerHTML = '<h3>The current file <u></u> has no supported Markdown file extension.</h3><div></div>';
    content.querySelector('u').textContent = name;
    content.querySelector('div').textContent = 'Supported file extensions: ' + (options.supportedExtensions || '');
    return {
      scrollToLine: () => { },
      documentChanged: () => { },
      dispose: () => { }
    }
  }

  for (let plugin of plugins) {
    const selected = (view === 'auto') ? options.document.match(plugin[0]) : (view === plugin[3]);
    if (selected) {
      if (plugin[2]) {
        const link = document.createElement("link");
        link.href = plugin[2];
        link.rel = 'stylesheet';
        document.head.appendChild(link);
      }
      const script = document.createElement("script");
      script.src = plugin[1];
      script.defer = true;
      const scriptLoad = new Promise((resolve) => {
        script.onload = () => resolve();
        document.head.appendChild(script);
      });
      await scriptLoad;

      const viewPlugin = window.viewPlugin;

      const documentChanged = async (modified = true) => {
        const args = options = {
          ...options,
          modified
        };
        await viewPlugin.setDocument(document.getElementById("content"), args);
      }

      await documentChanged(false);
      return {
        scrollToLine: viewPlugin.scrollToLine,
        exportDocument: viewPlugin.exportDocument,
        showFind: viewPlugin.showFind,
        documentChanged,
        dispose: () => { }
      }
    }
  }
  console.log(`unsupported file extension: ${options.document}`);
  return {
    scrollToLine: () => { },
    documentChanged: () => { },
    dispose: () => { }
  }
}
