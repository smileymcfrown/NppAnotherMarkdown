export interface IDocumentOptions {
  document: string,
  modified: boolean,
  lineMark: boolean,
  trackFirstLine: boolean,
  pageYOffset: number|null,
  token: string,
  darkMode: boolean,
  outline: boolean,
  outlineCollapsed: boolean,
  "md.extensions": string[]
}

export interface IViewPlugin {
  setDocument(container: HTMLElement, args: Partial<IDocumentOptions>): void;
  scrollToLine(nline: number): void;
  // Optional: snapshot of the rendered document for Save-as-HTML / clipboard.
  exportDocument?(): unknown;
  dispose(): void;
}