using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using AnotherMarkdown.Entities;
using AnotherMarkdown.Forms;
using AnotherMarkdown.Properties;
using DiffPlex;
using Kbg.NppPluginNET.PluginInfrastructure;
using PanelCommon;
using TheArtOfDev.HtmlRenderer.WinForms.Utilities;
using Webview2Viewer;

namespace AnotherMarkdown
{
  public class MarkdownPanelController : IDisposable
  {
    private MarkdownPreviewForm PreviewForm
    {
      get {
        if (_previewForm == null) {
          lock (_lock) {
            if (_previewForm == null) {
              try {
                _previewForm = MarkdownPreviewForm.Create(_settings);
                _previewForm.OnEvent.DocumentChanged += (_, e) => DocumentChanged(e);
                _previewForm.OnEvent.FirstLineChanged += (_, e) => FirstLineChanged(e);
                _previewForm.OnEvent.PasteImage += (_, e) => PasteImage(e);
                _previewForm.OnEvent.Navigate += (_, e) => OpenFile(e);
                _previewForm.OnEvent.RenderCompleted += (_, e) => RenderCompleted(e);
                _previewForm.DockClosed += (_, e) => PanelClosedByUser();
                _previewForm.SaveAsHtmlAction = () => SaveAsHtml(lightTheme: false);
                _previewForm.SaveAsHtmlLightAction = () => SaveAsHtml(lightTheme: true);
                _previewForm.CopyHtmlAction = CopyHtmlToClipboard;
                _previewForm.ExportPdfAction = ExportToPdf;
              }
              catch (Exception ex) {
                Console.WriteLine(ex.ToString());
              }
            }
          }
        }
        return _previewForm;
      }
    }
    private bool SyncViewEnabled => (_settings.SyncViewWithCaretPosition || _settings.SyncViewWithFirstVisibleLine);

    public MarkdownPanelController()
    {
      AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
      scintillaGatewayFactory = PluginBase.GetGatewayFactory();
      _nppGateway = new NotepadPPGateway();
      SetIniFilePath();
      _settings = LoadSettingsFromIni();
    }

    private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
    {
      var di = new DirectoryInfo(Path.Combine(PluginUtils.GetPluginDirectory(), "lib"));

      var modulename = args.Name.Split(',')[0];

      var module = di.GetFiles().FirstOrDefault(i => i.Name == modulename + ".dll");
      if (module != null) {
        return Assembly.LoadFrom(module.FullName);
      }
      return null;
    }

    private Settings LoadSettingsFromIni()
    {
      Settings settings = new Settings();
      settings.SyncViewWithCaretPosition = (Win32.GetPrivateProfileInt("Options", "SyncViewWithCaretPosition", 0, _iniFilePath) != 0);
      settings.SyncViewWithFirstVisibleLine = (Win32.GetPrivateProfileInt("Options", "SyncWithFirstVisibleLine", 0, _iniFilePath) != 0);

      settings.EnabledMarkdownPlugins = Win32.ReadIniValue("Options", "EnabledMarkdownPlugins", _iniFilePath, "tasks-list;attrs;qrcode;pano360;frontmatter")
        .Split(';')
        .Select(li => li.Trim())
        .ToArray();

      settings.PreProcessorCommandFilename = Win32.ReadIniValue("Options", "PreProcessorExe", _iniFilePath, "");
      settings.PreProcessorArguments = Win32.ReadIniValue("Options", "PreProcessorArguments", _iniFilePath, "");
      settings.PostProcessorCommandFilename = Win32.ReadIniValue("Options", "PostProcessorExe", _iniFilePath, "");
      settings.PostProcessorArguments = Win32.ReadIniValue("Options", "PostProcessorArguments", _iniFilePath, "");
      settings.AssetsPath = Win32.ReadIniValue("Options", "AssetsPath", _iniFilePath, "");
      settings.CssFileName = Win32.ReadIniValue("Options", "CssFileName", _iniFilePath, "style.css");
      settings.CssDarkModeFileName = Win32.ReadIniValue("Options", "CssDarkModeFileName", _iniFilePath, "style-dark.css");
      settings.ZoomLevel = Win32.GetPrivateProfileInt("Options", "ZoomLevel", 130, _iniFilePath);
      settings.ShowToolbar = PluginUtils.ReadIniBool("Options", "ShowToolbar", _iniFilePath);
      settings.ShowStatusbar = PluginUtils.ReadIniBool("Options", "ShowStatusbar", _iniFilePath);
      settings.HtmlFileName = Win32.ReadIniValue("Options", "HtmlFileName", _iniFilePath, "");
      settings.ShowOutline = PluginUtils.ReadIniBool("Options", "ShowOutline", _iniFilePath);
      settings.EnableThreeStateToggle = PluginUtils.ReadIniBool("Options", "EnableThreeStateToggle", _iniFilePath);
      settings.SupportedFileExt = Win32.ReadIniValue("Options", "SupportedFileExt", _iniFilePath, Settings.DEFAULT_SUPPORTED_FILE_EXT);
      settings.AllowAllExtensions = PluginUtils.ReadIniBool("Options", "AllowAllExtensions", _iniFilePath);
      settings.SupportFilesWithNoExt = PluginUtils.ReadIniBool("Options", "SupportFilesWithNoExt", _iniFilePath);
      settings.AutoShowPanel = PluginUtils.ReadIniBool("Options", "AutoShowPanel", _iniFilePath);
      settings.IsDarkModeEnabled = IsDarkModeEnabled();
      return settings;
    }

    public void OnNotification(ScNotification notification)
    {
      try {
        NotificationHandler(notification);
      }
      catch (Exception) { }
    }

    private void NotificationHandler(ScNotification notification)
    {
      if (_disposedValue) {
        return;
      }

      switch (notification.Header.Code) {
        case (uint) SciMsg.SCN_UPDATEUI: {
          if (_isPanelVisible && (_settings.SyncViewWithCaretPosition || _settings.SyncViewWithFirstVisibleLine)) {
            lock (_syncViewLock) {
              _syncViewPending = true;
              if (_syncViewTask == null || (_syncViewTask.IsCompleted || _syncViewTask.IsFaulted)) {
                _syncViewTask = SyncViewTask();
              }
            }
          }
          break;
        }
        case (uint) NppMsg.NPPN_BUFFERACTIVATED: {
          if (_settings.AutoShowPanel) {
            AutoShowOrHidePanel(_nppGateway.GetCurrentFilePath());
          }
          if (_skipSyncEventsDue < DateTime.UtcNow) {
            RenderMarkdown(force: true);
          }
          break;
        }
        // Save As / rename can change the extension and therefore whether the
        // file is previewed at all.
        case (uint) NppMsg.NPPN_FILESAVED:
        case (uint) NppMsg.NPPN_FILERENAMED: {
          if (_settings.AutoShowPanel) {
            AutoShowOrHidePanel(_nppGateway.GetCurrentFilePath());
          }
          if (_isPanelVisible) {
            RenderMarkdown(force: true);
          }
          break;
        }
        case (uint) (NppMsg.NPPN_FIRST + 27): {
          _settings.IsDarkModeEnabled = IsDarkModeEnabled();
          if (_isPanelVisible) {
            PreviewForm.UpdateSettings(_settings);
            RenderMarkdown(force: true);
          }
          break;
        }
        case (uint) SciMsg.SCN_MODIFIED: {
          if (_skipSyncEventsDue < DateTime.UtcNow) {
            RenderMarkdown();
          }
          break;
        }
      }
    }

    private async Task SyncViewTask()
    {
      while(_isPanelVisible) {
        await Task.Delay(50);
        if (_disposedValue || !_isPanelVisible) {
          return;
        }
        if ((_settings.SyncViewWithFirstVisibleLine || _settings.SyncViewWithCaretPosition) == false) {
          return;
        }

        lock (_syncViewLock) {
          if (!_syncViewPending) {
            return;
          }
          _syncViewPending = false;
        }

        var currentFile = _nppGateway.GetCurrentFilePath();
        if (currentFile != _currentFile) {
          _lastScrollToLine = -1;
        }

        int nLine = -1;
        var scintillaGateway = scintillaGatewayFactory();

        if (_settings.SyncViewWithFirstVisibleLine) {
          nLine = scintillaGateway.GetFirstVisibleLine();
          nLine = scintillaGateway.DocLineFromVisible(nLine);
        }
        else if (_settings.SyncViewWithCaretPosition) {
          nLine = scintillaGateway.GetCurrentLineNumber();
        }

        if (nLine == -1 || nLine == _lastScrollToLine) {
          return;
        }

        _lastScrollToLine = nLine;
        if (_skipSyncEventsDue < DateTime.UtcNow && _isPanelVisible) {
          await PreviewForm.ScrollToElementWithLineNo(nLine);
        }
      }
    }

    public void InitCommandMenu()
    {
      PluginBase.SetCommand(0, "Toggle &Markdown Panel", TogglePanelVisible);
      PluginBase.SetCommand(1, "---", null);
      PluginBase.SetCommand(2, "Synchronize with &caret position", SyncViewWithCaretClicked, _settings.SyncViewWithCaretPosition);
      PluginBase.SetCommand(3, "Synchronize with &first visible line in editor", SyncViewWithFirstVisibleLineClicked, _settings.SyncViewWithFirstVisibleLine);
      PluginBase.SetCommand(4, "Show &outline", ShowOutlineClicked, _settings.ShowOutline);
      PluginBase.SetCommand(5, "---", null);
      PluginBase.SetCommand(6, "Save as &HTML...", () => SaveAsHtml(lightTheme: false));
      PluginBase.SetCommand(7, "Save as HTML (&light theme)...", () => SaveAsHtml(lightTheme: true));
      PluginBase.SetCommand(8, "&Copy HTML to clipboard", CopyHtmlToClipboard);
      PluginBase.SetCommand(9, "Export to &PDF...", ExportToPdf);
      PluginBase.SetCommand(10, "---", null);
      PluginBase.SetCommand(11, "&Settings", EditSettings);
      PluginBase.SetCommand(12, "&Help", ShowHelp);
      PluginBase.SetCommand(13, "&About", ShowAboutDialog);
      _myDlgId = 0;
    }

    private void EditSettings()
    {
      var settingsForm = new SettingsForm(_settings);
      if (settingsForm.ShowDialog() == DialogResult.OK) {
        _settings.AssetsPath = settingsForm.AssetsPath;
        _settings.CssFileName = settingsForm.CssFileName;
        _settings.CssDarkModeFileName = settingsForm.CssDarkModeFileName;
        _settings.ZoomLevel = settingsForm.ZoomLevel;
        _settings.ShowToolbar = settingsForm.ShowToolbar;
        _settings.ShowStatusbar = settingsForm.ShowStatusbar;
        _settings.HtmlFileName = settingsForm.HtmlFileName;
        SetShowOutline(settingsForm.ShowOutline);
        _settings.EnableThreeStateToggle = settingsForm.EnableThreeStateToggle;
        _settings.SupportedFileExt = settingsForm.SupportedFileExt;
        _settings.AllowAllExtensions = settingsForm.AllowAllExtensions;
        _settings.SupportFilesWithNoExt = settingsForm.SupportFilesWithNoExt;
        _settings.AutoShowPanel = settingsForm.AutoShowPanel;
        _settings.EnabledMarkdownPlugins = settingsForm.AllowedMarkdownPlugins;

        _settings.IsDarkModeEnabled = IsDarkModeEnabled();
        SaveSettings();
        //Update Preview
        if (_isPanelVisible) {
          PreviewForm.UpdateSettings(_settings);
          RenderMarkdown(force: true);
        }
      }
    }

    #region Export

    private bool EnsurePreviewForExport()
    {
      if (!_isPanelVisible) {
        MessageBox.Show("Open the Markdown preview panel first.", Main.PluginTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
      }
      return true;
    }

    private string SuggestedExportName(string extension)
    {
      var current = _nppGateway.GetCurrentFilePath();
      var name = Path.GetFileNameWithoutExtension(current);
      return (string.IsNullOrEmpty(name) ? "markdown" : name) + extension;
    }

    private string SuggestedExportDirectory()
    {
      var current = _nppGateway.GetCurrentFilePath();
      var dir = Path.GetDirectoryName(current);
      return (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) ? dir : "";
    }

    private async void SaveAsHtml(bool lightTheme)
    {
      if (!EnsurePreviewForExport()) {
        return;
      }
      string fileName;
      using (var dialog = new SaveFileDialog()) {
        dialog.Title = lightTheme ? "Save as HTML (light theme)" : "Save as HTML";
        dialog.Filter = "HTML files (*.html)|*.html|All files (*.*)|*.*";
        dialog.DefaultExt = "html";
        dialog.AddExtension = true;
        dialog.RestoreDirectory = true;
        dialog.InitialDirectory = SuggestedExportDirectory();
        dialog.FileName = SuggestedExportName(".html");
        if (dialog.ShowDialog() != DialogResult.OK) {
          return;
        }
        fileName = dialog.FileName;
      }
      try {
        var html = await PreviewForm.ExportHtmlAsync(lightTheme);
        if (html == null) {
          MessageBox.Show("Nothing to export: the preview is empty or the current document is not a Markdown file.", Main.PluginTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
          return;
        }
        File.WriteAllText(fileName, html, new UTF8Encoding(false));
      }
      catch (Exception ex) {
        MessageBox.Show("Could not save the HTML file:\n" + ex.Message, Main.PluginTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }

    private async void CopyHtmlToClipboard()
    {
      if (!EnsurePreviewForExport()) {
        return;
      }
      try {
        var html = await PreviewForm.ExportHtmlAsync(lightTheme: true);
        if (html == null) {
          return;
        }
        // CF_HTML for applications that paste formatted text (Word, Outlook, ...),
        // plus the raw HTML as plain text for editors.
        ClipboardHelper.CopyToClipboard(html, html);
      }
      catch (Exception ex) {
        MessageBox.Show("Could not copy to the clipboard:\n" + ex.Message, Main.PluginTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }

    private async void ExportToPdf()
    {
      if (!EnsurePreviewForExport()) {
        return;
      }
      string fileName;
      using (var dialog = new SaveFileDialog()) {
        dialog.Title = "Export to PDF";
        dialog.Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*";
        dialog.DefaultExt = "pdf";
        dialog.AddExtension = true;
        dialog.RestoreDirectory = true;
        dialog.InitialDirectory = SuggestedExportDirectory();
        dialog.FileName = SuggestedExportName(".pdf");
        if (dialog.ShowDialog() != DialogResult.OK) {
          return;
        }
        fileName = dialog.FileName;
      }
      try {
        if (!await PreviewForm.ExportPdfAsync(fileName)) {
          MessageBox.Show("The PDF could not be written.", Main.PluginTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
      }
      catch (Exception ex) {
        MessageBox.Show("Could not export the PDF:\n" + ex.Message, Main.PluginTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }

    // Automatic HTML output: after every render, write the preview to the configured file.
    private async void RenderCompleted(RenderCompletedEvent args)
    {
      var target = _settings.HtmlFileName;
      if (string.IsNullOrWhiteSpace(target) || _disposedValue) {
        return;
      }
      try {
        var html = await PreviewForm.ExportHtmlAsync(lightTheme: false);
        if (html != null) {
          File.WriteAllText(target, html, new UTF8Encoding(false));
        }
      }
      catch (Exception) {
        // A bad path or a locked file must never disturb the preview itself.
      }
    }

    #endregion

    private void OpenFile(NavigateToEvent args)
    {
      if (!File.Exists(args.Filename)) {
        return;
      }
      Win32.SendMessage(PluginBase.nppData._nppHandle, (uint) NppMsg.NPPM_DOOPEN, 0, args.Filename);
    }

    private void PasteImage(PasteImageEvent args)
    {
      var path = _nppGateway.GetCurrentFilePath();
      var rootDir = Path.GetFullPath(Path.GetDirectoryName(path));
      var targetDir = Path.GetFullPath(Path.Combine(rootDir, Path.GetDirectoryName(args.Filename) ?? ""));

      // The target folder comes from the page; never write outside the document's folder.
      var rootPrefix = rootDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
      if (targetDir != rootDir.TrimEnd(Path.DirectorySeparatorChar)
          && !targetDir.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) {
        return;
      }

      var extension = Path.GetExtension(args.Filename).ToLower();
      if (extension.Length < 2 || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
        return;
      }
      extension = extension.Substring(1);

      if (!Directory.Exists(targetDir)) {
        Directory.CreateDirectory(targetDir);
      }

      var sameFiles = Directory.GetFiles(targetDir, $"*.{extension}", SearchOption.TopDirectoryOnly);
      string filename = null;

      if (sameFiles.Length != 0) {
        using (var md5 = MD5.Create()) {
          var hash2 = string.Join("", md5.ComputeHash(args.Content).Select(li => $"{li}:X2"));
          foreach (var file in sameFiles) {
            var hash1 = string.Join("", md5.ComputeHash(File.ReadAllBytes(file)).Select(li => $"{li}:X2"));
            if (hash1 == hash2) {
              filename = file;
              break;
            }
          }
        }
      }

      if (filename == null) {
        var index = 10;
        while (true) {
          var fname = $"{index:D3}";
          if (Directory.GetFiles(targetDir, $"{fname}.*", SearchOption.TopDirectoryOnly).Length == 0) {
            break;
          }
          index += 5;
        }

        filename = (targetDir + $"/{index:D3}.{extension}").Replace("\\", "/");
        File.WriteAllBytes(filename, args.Content);
      }

      Uri rootUri = new Uri(rootDir + Path.DirectorySeparatorChar);
      Uri fileUri = new Uri(filename);
      var relativePath = rootUri.MakeRelativeUri(fileUri).ToString();

      var scintillaGateway = scintillaGatewayFactory();
      var pos = scintillaGateway.GetCurrentPos();
      scintillaGateway.InsertText(pos, $"![](./{relativePath})\r\n");
    }

    private void FirstLineChanged(FirstLineChangedEvent args)
    {
      var scintillaGateway = scintillaGatewayFactory();
      var visibleLine = scintillaGateway.GetFirstVisibleLine();
      var docLine = scintillaGateway.DocLineFromVisible(visibleLine);
      var newVisibleLine = scintillaGateway.VisibleFromDocLine(args.Line);

      if (visibleLine != newVisibleLine) {
        scintillaGateway.LineScroll(0, newVisibleLine - visibleLine);
        _skipSyncEventsDue = DateTime.UtcNow.AddSeconds(1);
      }
    }

    private void DocumentChanged(DocumentChangedEvent args)
    {
      var scintillaGateway = scintillaGatewayFactory();

      var currentTextLength = scintillaGateway.GetLength();
      var currentText = scintillaGateway.GetText(currentTextLength + 1);
      var newText = args.Content;
      if (newText == currentText) {
        return;
      }
      var len1 = currentText.Length;
      var len2 = newText.Length;

      var differ = new Differ();
      var diff = differ.CreateCharacterDiffs(currentText, newText, false);
      _skipSyncEventsDue = DateTime.UtcNow.AddSeconds(600);
      scintillaGateway.BeginUndoAction();
      try {
        foreach (var block in diff.DiffBlocks.Reverse()) {
          int deleteStartA = block.DeleteStartA;
          for (var i = 0; i < block.DeleteStartA; i++) {
            var ch = currentText[i];
            if (char.IsHighSurrogate(ch)) {
              deleteStartA += 2;
              i++;
            }
            else if (ch >= 0x800) {
              deleteStartA += 2;
            }
            else if (ch >= 0x80) {
              deleteStartA += 1;
            }
          }

          if (block.DeleteCountA > 0) {
            scintillaGateway.DeleteRange(deleteStartA, block.DeleteCountA);
          }
          if (block.InsertCountB > 0) {
            var insertText = newText.Substring(block.InsertStartB, block.InsertCountB).Replace("\n", "\r\n");
            scintillaGateway.InsertText(deleteStartA, insertText);
          }
        }
      }
      finally {
        scintillaGateway.EndUndoAction();
        _skipSyncEventsDue = DateTime.UtcNow.AddSeconds(0.5);
      }
    }

    private void ShowHelp()
    {
      var currentPluginPath = PluginUtils.GetPluginDirectory();
      var helpFile = Path.Combine(currentPluginPath, "README.md");
      Win32.SendMessage(PluginBase.nppData._nppHandle, (uint) NppMsg.NPPM_DOOPEN, 0, helpFile);
      if (!_isPanelVisible) {
        TogglePanelVisible();
      }
      RenderMarkdown(force: true);
    }

    private void SetIniFilePath()
    {
      StringBuilder sbIniFilePath = new StringBuilder(Win32.MAX_PATH);
      Win32.SendMessage(PluginBase.nppData._nppHandle, (uint) NppMsg.NPPM_GETPLUGINSCONFIGDIR, Win32.MAX_PATH, sbIniFilePath);
      _iniFilePath = sbIniFilePath.ToString();
      if (!Directory.Exists(_iniFilePath)) {
        Directory.CreateDirectory(_iniFilePath);
      }

      _iniFilePath = Path.Combine(_iniFilePath, Main.ModuleName + ".ini");
    }

    private void SyncViewWithCaretClicked()
    {
      var wasSyncView = SyncViewEnabled;
      SetSyncViewWithCaretPosition(!_settings.SyncViewWithCaretPosition);
      if (SyncViewEnabled != wasSyncView) {
        RenderMarkdown(force: true);
      }
    }

    private void SyncViewWithFirstVisibleLineClicked()
    {
      var wasSyncView = SyncViewEnabled;
      SetSyncViewWithFirstVisibleLine(!_settings.SyncViewWithFirstVisibleLine);
      if (SyncViewEnabled != wasSyncView) {
        RenderMarkdown(force: true);
      }
    }

    private void ShowOutlineClicked()
    {
      SetShowOutline(!_settings.ShowOutline);
      SaveSettings();
      if (_isPanelVisible) {
        RenderMarkdown(force: true);
      }
    }

    private void SetShowOutline(bool enabled)
    {
      _settings.ShowOutline = enabled;
      Win32.CheckMenuItem(Win32.GetMenu(PluginBase.nppData._nppHandle), PluginBase._funcItems.Items[4]._cmdID, Win32.MF_BYCOMMAND
        | (enabled ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
    }

    private void SetSyncViewWithCaretPosition(bool enabled)
    {
      if (_settings.SyncViewWithCaretPosition == enabled) {
        return;
      }
      _settings.SyncViewWithCaretPosition = enabled;
      if (enabled) {
        SetSyncViewWithFirstVisibleLine(false);
      }

      Win32.CheckMenuItem(Win32.GetMenu(PluginBase.nppData._nppHandle), PluginBase._funcItems.Items[2]._cmdID, Win32.MF_BYCOMMAND
        | (enabled ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
    }

    private void SetSyncViewWithFirstVisibleLine(bool enabled)
    {
      if (_settings.SyncViewWithFirstVisibleLine == enabled) {
        return;
      }
      _settings.SyncViewWithFirstVisibleLine = enabled;
      if (enabled) {
        SetSyncViewWithCaretPosition(false);
      }
      Win32.CheckMenuItem(Win32.GetMenu(PluginBase.nppData._nppHandle), PluginBase._funcItems.Items[3]._cmdID, Win32.MF_BYCOMMAND
        | (enabled ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
    }

    public void SetToolBarIcon()
    {
      toolbarIcons tbIconsOld = new toolbarIcons();
      tbIconsOld.hToolbarBmp = Resources.markdown_16x16_solid.GetHbitmap();
      tbIconsOld.hToolbarIcon = Resources.markdown_16x16_solid_dark.GetHicon();
      IntPtr pTbIcons = Marshal.AllocHGlobal(Marshal.SizeOf(tbIconsOld));
      Marshal.StructureToPtr(tbIconsOld, pTbIcons, false);
      Win32.SendMessage(PluginBase.nppData._nppHandle, (uint) NppMsg.NPPM_ADDTOOLBARICON, PluginBase._funcItems.Items[_myDlgId]._cmdID, pTbIcons);
      Marshal.FreeHGlobal(pTbIcons);
    }

    public void PluginCleanUp()
    {
      SaveSettings();
    }

    private void SaveSettings()
    {
      Win32.WritePrivateProfileString("Options", "SyncViewWithCaretPosition", _settings.SyncViewWithCaretPosition ? "1" : "0", _iniFilePath);
      Win32.WritePrivateProfileString("Options", "SyncWithFirstVisibleLine", _settings.SyncViewWithFirstVisibleLine ? "1" : "0", _iniFilePath);
      Win32.WritePrivateProfileString("Options", "EnabledMarkdownPlugins", string.Join(";", _settings.EnabledMarkdownPlugins), _iniFilePath);

      Win32.WriteIniValue("Options", "AssetsPath", _settings.AssetsPath, _iniFilePath);
      Win32.WriteIniValue("Options", "CssFileName", _settings.CssFileName, _iniFilePath);
      Win32.WriteIniValue("Options", "CssDarkModeFileName", _settings.CssDarkModeFileName, _iniFilePath);
      Win32.WriteIniValue("Options", "ZoomLevel", _settings.ZoomLevel.ToString(), _iniFilePath);
      Win32.WriteIniValue("Options", "ShowToolbar", _settings.ShowToolbar.ToString(), _iniFilePath);
      Win32.WriteIniValue("Options", "ShowStatusbar", _settings.ShowStatusbar.ToString(), _iniFilePath);
      Win32.WriteIniValue("Options", "HtmlFileName", _settings.HtmlFileName ?? "", _iniFilePath);
      Win32.WriteIniValue("Options", "ShowOutline", _settings.ShowOutline.ToString(), _iniFilePath);
      Win32.WriteIniValue("Options", "EnableThreeStateToggle", _settings.EnableThreeStateToggle.ToString(), _iniFilePath);
      Win32.WriteIniValue("Options", "SupportedFileExt", _settings.SupportedFileExt ?? "", _iniFilePath);
      Win32.WriteIniValue("Options", "AllowAllExtensions", _settings.AllowAllExtensions.ToString(), _iniFilePath);
      Win32.WriteIniValue("Options", "SupportFilesWithNoExt", _settings.SupportFilesWithNoExt.ToString(), _iniFilePath);
      Win32.WriteIniValue("Options", "AutoShowPanel", _settings.AutoShowPanel.ToString(), _iniFilePath);
    }

    private void ShowAboutDialog()
    {
      var aboutDialog = new AboutForm();
      aboutDialog.ShowDialog();
    }

    private bool IsSupportedFile(string path)
    {
      if (string.IsNullOrEmpty(path)) {
        return false;
      }
      var extension = Path.GetExtension(path);
      if (string.IsNullOrEmpty(extension)) {
        return _settings.SupportFilesWithNoExt;
      }
      if (_settings.AllowAllExtensions) {
        return true;
      }
      extension = extension.Substring(1);
      return (_settings.SupportedFileExt ?? "")
        .Split(',')
        .Select(li => li.Trim().TrimStart('.'))
        .Any(li => li.Length != 0 && li.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    private Webview2WebbrowserControl.DocumentKind ClassifyDocument(string path)
    {
      if (path != null && path.EndsWith(".pano360.json", StringComparison.OrdinalIgnoreCase)) {
        return Webview2WebbrowserControl.DocumentKind.Auto;
      }
      return IsSupportedFile(path)
        ? Webview2WebbrowserControl.DocumentKind.Markdown
        : Webview2WebbrowserControl.DocumentKind.Unsupported;
    }

    // "Automatically show panel": open for supported files, close for others.
    private void AutoShowOrHidePanel(string path)
    {
      var supported = IsSupportedFile(path) || (path != null && path.EndsWith(".pano360.json", StringComparison.OrdinalIgnoreCase));
      if (supported && _panelState == PanelState.Hidden) {
        ShowPanel();
        _panelState = PanelState.Docked;
        PreviewForm.UpdateSettings(_settings);
      }
      else if (!supported && _panelState != PanelState.Hidden) {
        if (_panelState == PanelState.FullWidth) {
          RestoreFromFullWidth();
        }
        HidePanel();
        _panelState = PanelState.Hidden;
      }
    }

    private enum PanelState { Hidden, Docked, FullWidth }

    // "Toggle Markdown Panel": hidden <-> docked, or with the three-state option
    // hidden -> docked -> full width -> hidden. Full width keeps the panel docked
    // and moves the docking splitter so the editor pane collapses.
    private void TogglePanelVisible()
    {
      switch (_panelState) {
        case PanelState.Hidden:
          ShowPanel();
          _panelState = PanelState.Docked;
          break;
        case PanelState.Docked:
          if (_settings.EnableThreeStateToggle && GoFullWidth()) {
            _panelState = PanelState.FullWidth;
          }
          else {
            HidePanel();
            _panelState = PanelState.Hidden;
          }
          break;
        case PanelState.FullWidth:
          RestoreFromFullWidth();
          HidePanel();
          _panelState = PanelState.Hidden;
          break;
      }

      if (_isPanelVisible) {
        PreviewForm.UpdateSettings(_settings);
        RenderMarkdown(force: true);
      }
    }

    private void ShowPanel()
    {
      if (!_ptrNppTbData.HasValue) {
        var tbData = new NppTbData();
        tbData.hClient = PreviewForm.Handle;
        tbData.pszName = Main.PluginTitle;
        tbData.dlgID = _myDlgId;
        tbData.uMask = NppTbMsg.DWS_DF_CONT_RIGHT | NppTbMsg.DWS_ICONTAB | NppTbMsg.DWS_ICONBAR;
        tbData.hIconTab = (uint) ConvertBitmapToIcon(Resources.markdown_16x16_solid_bmp).Handle;
        tbData.pszModuleName = $"{Main.ModuleName}.dll";

        _ptrNppTbData = Marshal.AllocHGlobal(Marshal.SizeOf(tbData));
        Marshal.StructureToPtr(tbData, _ptrNppTbData.Value, false);

        Win32.SendMessage(PluginBase.nppData._nppHandle, (uint) NppMsg.NPPM_DMMREGASDCKDLG, 0, _ptrNppTbData.Value);
      }
      Win32.SendMessage(PluginBase.nppData._nppHandle, (uint) NppMsg.NPPM_DMMSHOW, 0, PreviewForm.Handle);
      _isPanelVisible = true;
    }

    private void HidePanel()
    {
      if (_ptrNppTbData.HasValue) {
        Win32.SendMessage(PluginBase.nppData._nppHandle, (uint) NppMsg.NPPM_DMMHIDE, 0, PreviewForm.Handle);
      }
      _isPanelVisible = false;
    }

    // The user closed the panel with its own close button (DMN_CLOSE).
    private void PanelClosedByUser()
    {
      if (_panelState == PanelState.FullWidth) {
        RestoreFromFullWidth();
      }
      _panelState = PanelState.Hidden;
      _isPanelVisible = false;
    }

    private bool GoFullWidth()
    {
      var nppHandle = PluginBase.nppData._nppHandle;
      var containerHandle = Win32.GetParent(PreviewForm.Handle);
      var dockMgrHandle = Win32.FindWindowEx(nppHandle, IntPtr.Zero, Win32.DOCKING_MANAGER_CLASS, null);
      _fullWidthSplitterHandle = FindSplitterLeftOf(nppHandle, containerHandle);
      if (dockMgrHandle == IntPtr.Zero || _fullWidthSplitterHandle == IntPtr.Zero) {
        // Floating, or docked somewhere without a vertical splitter: nothing to do.
        _fullWidthSplitterHandle = IntPtr.Zero;
        return false;
      }

      Win32.GetClientRect(nppHandle, out RECT nppClient);
      Win32.GetWindowRect(containerHandle, out RECT containerRect);
      var currentWidth = containerRect.Right - containerRect.Left;
      _savedWidthRatio = (double) currentWidth / nppClient.Right;
      var offset = nppClient.Right - currentWidth - 4;
      Win32.SendMessage(dockMgrHandle, (uint) DockMgrMsg.DMM_MOVE_SPLITTER, offset, _fullWidthSplitterHandle);
      return true;
    }

    private void RestoreFromFullWidth()
    {
      var nppHandle = PluginBase.nppData._nppHandle;
      var dockMgrHandle = Win32.FindWindowEx(nppHandle, IntPtr.Zero, Win32.DOCKING_MANAGER_CLASS, null);
      if (dockMgrHandle != IntPtr.Zero && _fullWidthSplitterHandle != IntPtr.Zero && _savedWidthRatio > 0) {
        Win32.GetClientRect(nppHandle, out RECT nppClient);
        var containerHandle = Win32.GetParent(PreviewForm.Handle);
        Win32.GetWindowRect(containerHandle, out RECT containerRect);
        var currentWidth = containerRect.Right - containerRect.Left;
        var targetWidth = (int) (nppClient.Right * _savedWidthRatio);
        Win32.SendMessage(dockMgrHandle, (uint) DockMgrMsg.DMM_MOVE_SPLITTER, targetWidth - currentWidth, _fullWidthSplitterHandle);
      }
      _savedWidthRatio = 0;
      _fullWidthSplitterHandle = IntPtr.Zero;
    }

    // The docking splitter whose right edge touches the left edge of our container.
    private static IntPtr FindSplitterLeftOf(IntPtr nppHandle, IntPtr containerHandle)
    {
      Win32.GetWindowRect(containerHandle, out RECT containerRect);
      var hwnd = IntPtr.Zero;
      while ((hwnd = Win32.FindWindowEx(nppHandle, hwnd, Win32.VERT_SPLITTER_CLASS, null)) != IntPtr.Zero) {
        Win32.GetWindowRect(hwnd, out RECT splitterRect);
        if (splitterRect.Right >= containerRect.Left - 10 && splitterRect.Right <= containerRect.Left + 5) {
          return hwnd;
        }
      }
      return IntPtr.Zero;
    }

    private Icon ConvertBitmapToIcon(Bitmap bitmapImage)
    {
      if (_icon != null) {
        return _icon;
      }

      _iconBmp = new Bitmap(16, 16);
      using (Graphics g = Graphics.FromImage(_iconBmp)) {
        ColorMap[] colorMap = new ColorMap[1];
        colorMap[0] = new ColorMap();
        colorMap[0].OldColor = Color.Fuchsia;
        colorMap[0].NewColor = Color.FromKnownColor(KnownColor.ButtonFace);
        ImageAttributes attr = new ImageAttributes();
        attr.SetRemapTable(colorMap);
        g.DrawImage(bitmapImage, new Rectangle(0, 0, 16, 16), 0, 0, 16, 16, GraphicsUnit.Pixel, attr);
        _icon = Icon.FromHandle(_iconBmp.GetHicon());
      }
      return _icon;
    }

    public void RenderMarkdown(bool force = false)
    {
      lock (_renderMarkdownLock) {
        if (force) {
          _renderMarkdownAt = DateTime.UtcNow;
        }
        else {
          if (_renderMarkdownAt == DateTime.MinValue) {
            _renderMarkdownAt = DateTime.UtcNow.Add(InputUpdateThreshold);
          }
        }
        if (_renderMarkdownTask == null || (_renderMarkdownTask.IsCompleted || _renderMarkdownTask.IsFaulted)) {
          _renderMarkdownTask = RenderMarkdownTask();
        }
      }
    }

    private async Task RenderMarkdownTask()
    {
      try {
        while (!_disposedValue && _isPanelVisible) {
          await Task.Delay(20);
          if (_disposedValue) {
            break;
          }
          if (_renderMarkdownAt > DateTime.UtcNow) {
            continue;
          }
          _renderMarkdownAt = DateTime.MinValue;

          var scintillaGateway = scintillaGatewayFactory();
          var currentText = scintillaGateway.GetText(scintillaGateway.GetLength() + 1);

          var currentFile = _nppGateway.GetCurrentFilePath();
          _currentFile = currentFile;

          await PreviewForm.RenderMarkdown(currentText, currentFile, ClassifyDocument(currentFile), _settings.SupportedFileExt);
        }
      }
      catch (Exception err) {
        _renderMarkdownAt = DateTime.MinValue;
      }
    }

    private bool IsDarkModeEnabled()
    {
      // NPPM_ISDARKMODEENABLED (NPPMSG + 107)
      IntPtr ret = Win32.SendMessage(PluginBase.nppData._nppHandle, (uint) (Constants.NPPMSG + 107), UNUSED, UNUSED);
      return ret.ToInt32() == 1;
    }

    protected virtual void Dispose(bool disposing)
    {
      if (!disposing) {
        return;
      }
      if (_disposedValue) {
        return;
      }

      _disposedValue = true;

      _icon?.Dispose();
      _iconBmp?.Dispose();
      _icon = null;
      _iconBmp = null;

      if (_ptrNppTbData.HasValue) {
        Marshal.DestroyStructure(_ptrNppTbData.Value, typeof(NppTbData));
        Marshal.FreeHGlobal(_ptrNppTbData.Value);
        _ptrNppTbData = null;
      }
      _previewForm?.Dispose();
      _previewForm = null;
    }

    public void Dispose()
    {
      // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
      Dispose(disposing: true);
      GC.SuppressFinalize(this);
    }

    private const int UNUSED = 0;

    private bool _isPanelVisible;
    private PanelState _panelState = PanelState.Hidden;
    private IntPtr _fullWidthSplitterHandle = IntPtr.Zero;
    private double _savedWidthRatio;
    private MarkdownPreviewForm _previewForm;
    private object _lock = new object();
    private int _myDlgId = -1;
    private readonly Func<IScintillaGateway> scintillaGatewayFactory;
    private readonly INotepadPPGateway _nppGateway;
    private string _iniFilePath;
    private int _lastCaretPosition;
    private int _lastScrollToLine;
    private Settings _settings;
    private IntPtr? _ptrNppTbData;
    private Icon _icon;
    private Bitmap _iconBmp;
    private bool _disposedValue;

    private DateTime _skipSyncEventsDue = DateTime.MinValue;
    private string _currentFile;
    private object _syncViewLock = new object();
    private bool _syncViewPending = false;
    private Task _syncViewTask;

    private DateTime _renderMarkdownAt = DateTime.MinValue;
    private object _renderMarkdownLock = new object();
    private Task _renderMarkdownTask;

    private static readonly TimeSpan InputUpdateThreshold = TimeSpan.FromMilliseconds(200);
  }
}
