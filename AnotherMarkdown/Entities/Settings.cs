using System.IO;
using System.Reflection;

namespace AnotherMarkdown.Entities
{
  public class Settings
  {
    public bool SyncViewWithCaretPosition { get; set; } = false;
    public bool SyncViewWithFirstVisibleLine { get; set; } = false;
    public string AssetsPath { get; set; }
    public string CssFileName { get; set; }
    public string CssDarkModeFileName { get; set; }
    public int ZoomLevel { get; set; }
    public bool IsDarkModeEnabled { get; set; }
    public bool ShowToolbar { get; set; }
    public bool ShowStatusbar { get; set; }
    // When set, the rendered HTML is written here after every preview update.
    public string HtmlFileName { get; set; }
    public bool ShowOutline { get; set; }
    // Toggle command cycles hidden -> docked -> full width (source pane pushed away) -> hidden.
    public bool EnableThreeStateToggle { get; set; }
    // Comma separated list of extensions (without dot) that are previewed as Markdown.
    public string SupportedFileExt { get; set; } = DEFAULT_SUPPORTED_FILE_EXT;
    public bool AllowAllExtensions { get; set; }
    public bool SupportFilesWithNoExt { get; set; }
    // Open the panel for supported files, close it for others, when switching tabs.
    public bool AutoShowPanel { get; set; }

    public const string DEFAULT_SUPPORTED_FILE_EXT = "md,mkd,mdwn,mdown,mdtxt,markdown,txt";
    public string[] EnabledMarkdownPlugins { get; set; }
    public string PreProcessorCommandFilename { get; set; }
    public string PreProcessorArguments { get; set; }

    public string DefaultAssetPath
    {
      get { return _defaultAssetPath; }
    }

    public string DefaultCssFile
    {
      get { return _defaultAssetPath + "/markdown/markdown.css"; }
    }

    public string DefaultDarkModeCssFile
    {
      get { return _defaultAssetPath + "/markdown/markdown-dark.css"; }
    }

    private string _defaultAssetPath => (Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/assets").Replace("\\", "/");
  }
}
