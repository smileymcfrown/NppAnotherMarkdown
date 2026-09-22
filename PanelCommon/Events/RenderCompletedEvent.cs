namespace PanelCommon
{
  // Raised by the preview page once markdown-it output (and post-render work such
  // as mermaid) is in the DOM, i.e. when the rendered HTML can be exported.
  public struct RenderCompletedEvent
  {
    public string DocumentUri;
  }
}
