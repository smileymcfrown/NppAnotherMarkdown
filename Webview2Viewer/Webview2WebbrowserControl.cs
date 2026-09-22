using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PanelCommon;
using Webview2Viewer.Web;
using WebView2 = Microsoft.Web.WebView2.WinForms.WebView2;

namespace Webview2Viewer
{
  public class Webview2WebbrowserControl : IDisposable
  {
    public Action<string> StatusTextChangedAction { get; set; }
    public Action RenderingDoneAction { get; set; }

    public Webview2WebbrowserControl()
    {
      _webView = null;
    }

    public void Dispose()
    {
      _webView?.Dispose();
      _webView = null;
    }

    public void Initialize(ISettings settings, IEventDispatcher eventDispatcher)
    {
      lock (_webViewInitLock) {
        if (_webView == null) {
          _settings = settings;
          _on = eventDispatcher;
          _webView = InitializeWebViewAsync();
        }
      }
    }

    private async Task<WebView2> InitializeWebViewAsync()
    {
      var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CONFIG_FOLDER_NAME, "webview2");

      var webView = new WebView2();
      var opt = new CoreWebView2EnvironmentOptions();
      var webEnvironment = await CoreWebView2Environment.CreateAsync(null, cacheDir, opt);
      await webView.EnsureCoreWebView2Async(webEnvironment);

      webView.AccessibleName = "webView";
      webView.Name = "webView";
      webView.Source = new Uri("about:blank", UriKind.Absolute);
      webView.Location = new Point(1, 27);
      webView.Size = new Size(800, 424);
      webView.Dock = DockStyle.Fill;
      webView.TabIndex = 0;
      webView.NavigationStarting += OnWebBrowser_NavigationStarting;
      webView.CoreWebView2.NewWindowRequested += OnWebBrowser_NewWindowRequested;
      webView.ZoomFactor = ConvertToZoomFactor(_settings.ZoomLevel);
      webView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

      var fs = new LocalFileService(webEnvironment, "local.example", _on, _sessionToken);
      AddWebService(webView, fs);

      var api = new ApiService(webEnvironment, "api.example", _on, _sessionToken);
      api.OutlineCollapsedChanged = (collapsed) => _outlineCollapsed = collapsed;
      AddWebService(webView, api);
      return webView;
    }

    private void AddWebService(WebView2 webView, IWebService webService)
    {
      webView.CoreWebView2.AddWebResourceRequestedFilter($"http://{webService.Hostname}/*", CoreWebView2WebResourceContext.All);
      _webServices.Add(webService);
    }

    private void CoreWebView2_WebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
      var uri = new Uri(e.Request.Uri);
      foreach (var webservice in _webServices) {
        if (webservice.Hostname == uri.DnsSafeHost) {
          if (webservice.HandleRequest(e)) {
            return;
          }
        }
      }
    }

    public void AddToHost(Control host)
    {
      ExecuteWebviewAction((webView) => {
        host.Controls.Add(webView);
      });
    }

    public async Task ScrollToElementWithLineNo(int lineNo)
    {
      if (lineNo <= 0) {
        lineNo = 0;
      }
      await ExecuteWebviewActionAsync((webView) => webView.ExecuteScriptAsync($"window.scrollToLine({lineNo})"));
    }

    public async Task SetContentAsync(string content, string documentPath)
    {
      await _webView;
      var fs = _webServices.OfType<LocalFileService>().First();

      var assetsPath = (!string.IsNullOrEmpty(_settings.AssetsPath) && Directory.Exists(_settings.AssetsPath))
        ? _settings.AssetsPath
        : _settings.DefaultAssetPath;

      var cssFile = _settings.IsDarkModeEnabled ? _settings.CssDarkModeFileName : _settings.CssFileName;
      if (!File.Exists(cssFile)) {
        cssFile = _settings.IsDarkModeEnabled ? _settings.DefaultDarkModeCssFile : _settings.DefaultCssFile;
      }
      var lineMark = (_settings.SyncViewWithFirstVisibleLine || _settings.SyncViewWithCaretPosition);
      var reload = (_documentPath != documentPath);
      reload = reload || (_assetPath != assetsPath);
      reload = reload || (_cssFile != cssFile);
      reload = reload || (_lineMark != lineMark);
      reload = reload || (_trackFirstLine != _settings.SyncViewWithFirstVisibleLine);
      reload = reload || (_enabledMarkdownPlugins != string.Join(",", _settings.EnabledMarkdownPlugins));
      reload = reload || (_showOutline != _settings.ShowOutline);
      reload = reload || (_darkMode != _settings.IsDarkModeEnabled);

      if (_assetPath != assetsPath) {
        await ExecuteWebviewActionAsync((webView) => {
          webView.CoreWebView2.SetVirtualHostNameToFolderMapping("assets.example", assetsPath, CoreWebView2HostResourceAccessKind.Allow);
        });
        _assetPath = assetsPath;
      }

      var baseDir = Path.GetDirectoryName(documentPath);
      var replaceFileMapping = "file://" + baseDir;
      content = content.Replace(replaceFileMapping, $"http://{fs.Hostname}");
      fs.SetContent(documentPath, content);

      if (!reload) {
        await ExecuteWebviewActionAsync((webView) => webView.ExecuteScriptAsync("window.contentChanged();"));
        return;
      }

      if (!string.IsNullOrEmpty(_documentPath) && _documentPath != documentPath) {
        await ExecuteWebviewActionAsync(async (webView) => {
          var value = await webView.ExecuteScriptAsync("window.pageYOffset");
          _preservePosition[_documentPath] = (int) double.Parse(value, CultureInfo.InvariantCulture);
        });
      }

      _documentPath = documentPath;
      _cssFile = cssFile;
      _lineMark = lineMark;
      _trackFirstLine = _settings.SyncViewWithFirstVisibleLine;
      _enabledMarkdownPlugins = string.Join(",", _settings.EnabledMarkdownPlugins);
      _showOutline = _settings.ShowOutline;
      _darkMode = _settings.IsDarkModeEnabled;

      var loader = File.ReadAllText(assetsPath + "/loader.html");
      cssFile = cssFile.Replace("\\", "/");
      assetsPath = assetsPath.Replace("\\", "/");

      if (cssFile.StartsWith(assetsPath + "/")) {
        cssFile = cssFile.Substring((assetsPath).Length + 1);
        cssFile = "http://assets.example/" + HttpUtility2.UrlPathEncode(cssFile);
      }
      else {
        cssFile = $"http://{fs.Hostname}/" + HttpUtility2.PathToUri(cssFile);
      }

      loader = loader.Replace("__BASE_URL__", HttpUtility2.PathToUri(baseDir));
      var options = new JObject {
        ["document"] = "http://local.example" + fs.DocumentUri,
        // Handed only to our own loader page; the view plugins send it back as the
        // X-AnotherMarkdown-Token header on every request that changes state.
        ["token"] = _sessionToken
      };

      if (documentPath.EndsWith(".md")) {
        options["css"] = cssFile;
        options["lineMark"] = (_settings.SyncViewWithFirstVisibleLine || _settings.SyncViewWithCaretPosition);
        options["trackFirstLine"] = _settings.SyncViewWithFirstVisibleLine;
        if (_preservePosition.TryGetValue(_documentPath, out var pageYOffset)) {
          options["pageYOffset"] = pageYOffset;
        }
        options["md.extensions"] = JToken.FromObject(_settings.EnabledMarkdownPlugins);
        options["darkMode"] = _settings.IsDarkModeEnabled;
        options["outline"] = _settings.ShowOutline;
        options["outlineCollapsed"] = _outlineCollapsed;
      }

      loader = loader.Replace("__OPTIONS__", JsonConvert.SerializeObject(options));

      await ExecuteWebviewActionAsync((webView) => webView.NavigateToString(loader));
      await SetZoomLevel(_settings.ZoomLevel);
    }

    /// <summary>
    /// Standalone HTML of the current preview: the rendered body with every
    /// stylesheet inlined and resource URLs rewritten to file://, so the file can be
    /// opened from anywhere. Returns null when nothing is rendered yet or the current
    /// view plugin (e.g. the pano360 editor) has nothing to export.
    /// </summary>
    /// <param name="lightTheme">Use the light stylesheet even while dark mode is active.</param>
    public async Task<string> ExportHtmlAsync(bool lightTheme)
    {
      const string script = "(function(){try{return window.exportDocument?JSON.stringify(window.exportDocument()):null;}catch(e){return null;}})()";
      var result = await ExecuteWebviewFuncAsync((webView) => webView.ExecuteScriptAsync(script));
      if (string.IsNullOrEmpty(result) || result == "null") {
        return null;
      }
      var snapshot = JObject.Parse(JsonConvert.DeserializeObject<string>(result));

      var title = snapshot["title"]?.ToString() ?? "";
      var body = MapVirtualHostsToFileUris(snapshot["body"]?.ToString() ?? "");

      var lightCss = File.Exists(_settings.CssFileName) ? _settings.CssFileName : _settings.DefaultCssFile;
      var head = new StringBuilder();
      foreach (var style in snapshot["styles"] ?? new JArray()) {
        var text = style["text"]?.ToString();
        if (text != null) {
          head.Append("<style>\n").Append(text).Append("\n</style>\n");
          continue;
        }
        var href = style["href"]?.ToString();
        if (string.IsNullOrEmpty(href)) {
          continue;
        }
        var cssPath = VirtualHostUriToPath(href);
        if (cssPath == null) {
          head.Append("<link rel=\"stylesheet\" href=\"").Append(WebUtility.HtmlEncode(href)).Append("\">\n");
          continue;
        }
        if (lightTheme && _cssFile != null && PathEquals(cssPath, _cssFile)) {
          cssPath = lightCss;
        }
        if (!File.Exists(cssPath)) {
          continue;
        }
        var css = InlineCssUrls(File.ReadAllText(cssPath), Path.GetDirectoryName(cssPath));
        head.Append("<style>\n").Append(css).Append("\n</style>\n");
      }

      return "<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n"
        + "<meta name=\"generator\" content=\"AnotherMarkdown for Notepad++\">\n"
        + "<title>" + WebUtility.HtmlEncode(title) + "</title>\n"
        + head
        + "</head>\n<body>\n<div id=\"content\">\n"
        + body
        + "\n</div>\n</body>\n</html>\n";
    }

    public async Task<bool> ExportPdfAsync(string filePath)
    {
      return await ExecuteWebviewFuncAsync((webView) => webView.CoreWebView2.PrintToPdfAsync(filePath, null));
    }

    // http://local.example/diskD/docs/x.png -> file:///D:/docs/x.png
    // http://assets.example/markdown/x.css  -> file:///<assets>/markdown/x.css
    private string MapVirtualHostsToFileUris(string html)
    {
      return Regex.Replace(html, @"http://(local|assets)\.example/[^""'\s<>)]*", m => {
        var path = VirtualHostUriToPath(m.Value);
        if (path == null) {
          return m.Value;
        }
        try {
          // AbsolutePath (used for the disk path) drops the fragment; keep "#section".
          var fragment = Uri.TryCreate(m.Value, UriKind.Absolute, out var original) ? original.Fragment : "";
          return new Uri(path).AbsoluteUri + fragment;
        }
        catch (Exception) {
          return m.Value;
        }
      });
    }

    private string VirtualHostUriToPath(string uriText)
    {
      if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri)) {
        return null;
      }
      var assetsPath = _assetPath ?? _settings.DefaultAssetPath;
      switch (uri.DnsSafeHost) {
        case "local.example":
          return HttpUtility2.UriToPath(uri.AbsolutePath).Replace('/', '\\');
        case "assets.example":
          return Path.Combine(assetsPath, HttpUtility.UrlDecode(uri.AbsolutePath).TrimStart('/')).Replace('/', '\\');
        default:
          return null;
      }
    }

    // Fonts and images referenced relatively from a stylesheet (JetBrains Mono,
    // KaTeX fonts, ...) would break once the CSS is inlined into a file elsewhere.
    private static string InlineCssUrls(string css, string cssDirectory)
    {
      return Regex.Replace(css, @"url\(\s*(['""]?)(?!data:|https?:|file:|//)([^'"")]+)\1\s*\)", m => {
        var reference = m.Groups[2].Value.Trim();
        var suffixAt = reference.IndexOfAny(new[] { '?', '#' });
        var suffix = suffixAt >= 0 ? reference.Substring(suffixAt) : "";
        var relative = suffixAt >= 0 ? reference.Substring(0, suffixAt) : reference;
        try {
          var absolute = Path.GetFullPath(Path.Combine(cssDirectory, HttpUtility.UrlDecode(relative)));
          return "url(\"" + new Uri(absolute).AbsoluteUri + suffix + "\")";
        }
        catch (Exception) {
          return m.Value;
        }
      });
    }

    private static bool PathEquals(string a, string b)
    {
      try {
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
      }
      catch (Exception) {
        return false;
      }
    }

    public async Task SetZoomLevel(int zoomLevel)
    {
      double zoomFactor = ConvertToZoomFactor(zoomLevel);
      await ExecuteWebviewActionAsync((webView) => {
        if (webView.ZoomFactor != zoomFactor) {
          webView.ZoomFactor = zoomFactor;
        }
      });
    }

    private double ConvertToZoomFactor(int zoomLevel) => Convert.ToDouble(zoomLevel) / 100;

    void OnWebBrowser_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
    {
      if (e.Uri.ToString().StartsWith("about:blank")) {
        e.Cancel = true;
      }
      else if (!e.Uri.ToString().StartsWith("data:")) {
        e.Cancel = true;
        HandleExternalNavigation(e.Uri);
      }
    }

    // Links that open a new window (target="_blank", window.open) must not spawn a
    // popup WebView2 window: it would be a full browser outside our NavigationStarting
    // filter. Route them through the same allowlist as ordinary navigation.
    private void OnWebBrowser_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
      e.Handled = true;
      HandleExternalNavigation(e.Uri);
    }

    private void HandleExternalNavigation(string uriText)
    {
      if (!Uri.TryCreate(uriText, UriKind.Absolute, out var navUri)) {
        return;
      }

      if (navUri.DnsSafeHost == "local.example") {
        if (_on.Navigate != null && navUri.AbsolutePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) {
          var path = HttpUtility2.UriToPath(navUri.AbsolutePath);
          if (File.Exists(path)) {
            _on.Navigate(this, new NavigateToEvent { Filename = path });
          }
        }
        return;
      }

      // Only web/mail schemes are handed to the shell. Anything else (file:, UNC,
      // custom protocol handlers, ...) would let a crafted document launch programs
      // via ShellExecute; those links are simply ignored.
      if (!IsShellSafeScheme(navUri)) {
        return;
      }
      try {
        Process.Start(new ProcessStartInfo(navUri.AbsoluteUri) { UseShellExecute = true });
      }
      catch (Exception) { }
    }

    private static bool IsShellSafeScheme(Uri uri)
    {
      switch (uri.Scheme.ToLowerInvariant()) {
        case "http":
        case "https":
        case "mailto":
          return true;
        default:
          return false;
      }
    }

    private void ExecuteWebviewAction(Func<WebView2, Task> action)
    {
      var asyncTask = new Task(async () => {
        await ExecuteWebviewActionAsync(action);
      });

      asyncTask.Start(TaskScheduler.FromCurrentSynchronizationContext());
      asyncTask.Wait();
    }

    private void ExecuteWebviewAction(Action<WebView2> action)
    {
      var asyncTask = new Task(async () => {
        await ExecuteWebviewActionAsync(action);
      });

      asyncTask.Start(TaskScheduler.FromCurrentSynchronizationContext());
      asyncTask.Wait();
    }

    private async Task ExecuteWebviewActionAsync(Action<WebView2> action)
    {
      try {
        if (_webView != null) {
          var webView = await _webView;
          webView.Invoke(new Action(() => action(webView)));
        }
      }
      catch (Exception) { }
    }

    private async Task<T> ExecuteWebviewFuncAsync<T>(Func<WebView2, Task<T>> func)
    {
      try {
        if (_webView != null) {
          var webView = await _webView;
          var tcs = new TaskCompletionSource<T>();
          webView.BeginInvoke(new Action(async () => {
            try {
              tcs.SetResult(await func(webView));
            }
            catch (Exception ex) {
              tcs.SetException(ex);
            }
          }));
          return await tcs.Task;
        }
      }
      catch (Exception) { }
      return default(T);
    }

    private async Task ExecuteWebviewActionAsync(Func<WebView2, Task> action)
    {
      try {
        if (_webView != null) {
          var webView = await _webView;
          var tcs = new TaskCompletionSource<bool>();
          var asyncResult = webView.BeginInvoke(new Action(() => {
            try {
              var task = action(webView);
              task.ContinueWith(t => {
                tcs.SetResult(true);
              });
            }
            catch (Exception ex) {
              tcs.SetException(ex);
            }
          }));
          await tcs.Task;
          webView.EndInvoke(asyncResult);
        }
      }
      catch (Exception) { }
    }

    const string CONFIG_FOLDER_NAME = "AnotherMarkdown";

    private Task<WebView2> _webView;
    private object _webViewInitLock = new object();
    // Random per-instance secret shared with the page (see SetContentAsync) and
    // checked by the web services before any write.
    private readonly string _sessionToken = WebSession.NewToken();
    private string _cssFile;
    private string _assetPath;
    private ISettings _settings;

    private string _documentPath;
    private bool _lineMark;
    private bool _trackFirstLine;
    private string _enabledMarkdownPlugins;
    private bool _showOutline;
    private bool _darkMode;
    // Burger-button state of the outline, reported by the page and handed back on reload.
    private bool _outlineCollapsed;

    private Dictionary<string, double> _preservePosition = new Dictionary<string, double>();
    private List<IWebService> _webServices = new List<IWebService>();
    private IEventDispatcher _on;
  }
}
