import MarkdownIt from "markdown-it";
import StateCore from "markdown-it/lib/rules_core/state_core.mjs"
import { Token } from "markdown-it/index.js";

import { Hashmap } from "../Lib/Common/Hashmap";

export default function markdownItLineMark(md: MarkdownIt, options: any) {
  // Last in the core chain: the anchors are inserted as extra block-level
  // tokens, and plugins that rely on the standard token layout (markdown-it-
  // anchor and toc read tokens[i + 1] after heading_open, the task-list plugin
  // checks tokens[i - 2]) must have run before that.
  md.core.ruler.push('linemark', function (state) {
    const ruler = new LineMarkRuler(state);
    ruler.Render();
  });
}

class LineMarkRuler {
  constructor(state: StateCore) {
    this.state = state;
  }

  public Render() {
    const { state } = this;

    this.line = ""
    this.nline = -1;
    this.lines = this.state.src.split('\n');
    this.moveToNextLine();

    state.tokens = this.handleTokens(state.tokens);
  }

  private moveToNextLine() {
    const { nline, lines } = this;

    for (let n = nline + 1; n < lines.length; n++) {
      const line = lines[n].trim();
      if (/^[=\*\-\|]+$/.test(line)) {
        continue;
      }
      if (line.length !== 0) {
        this.nline = n;
        this.line = line;
        return;
      }
    }

    this.nline = -1;
    this.line = '';
  }

  // Walks a token list once and returns a new list with the line anchors
  // inserted in front of the tokens they belong to. Building the result in one
  // pass matters: inserting into the array per anchor copied the whole array
  // every time, which was quadratic - ~23 s on a 2.3 MB document.
  private handleTokens(tokens: Token[]): Token[] {
    const result: Token[] = [];
    for (const token of tokens) {
      this.handleToken(token, result);
    }
    return result;
  }

  // Appends the line anchor (when one belongs here) and then the token itself.
  private handleToken(token: Token, result: Token[]) {
    if (token.children) {
      token.children = this.handleTokens(token.children);
    }

    if (token.type === 'text' && token.content && this.nline !== -1) {
      let a = token.content.trim();
      if (a.length === 0) {
        result.push(token);
        return;
      }

      let b = this.line;
      let match = (a == b) ? true : false;
      if (!match) {
        match = b.startsWith(a);
      }
      if (!match) {
        let len = Math.min(a.length, b.length);
        a = (a.length > len) ? a.slice(0, len) : a;
        b = (b.length > len) ? b.slice(0, len) : b;
        match = (a == b) ? true : false;
      }
      if (!match) {
        a = token.content.trim();
        b = this.line;
        b = b.replace(/^(\*+|\=+|#+|\-+|\|\s)/, '').trim();

        const len = Math.min(a.length, b.length);
        a = (a.length > len) ? a.slice(0, len) : a;
        b = (b.length > len) ? b.slice(0, len) : b;
        match = (a == b) ? true : false;
      }

      if (match) {
        this.insertLineMarker(result, this.nline);
        this.moveToNextLine();
      }
      result.push(token);
      return;
    }
    if (token.nesting === 1 || token.nesting === -1) {
      result.push(token);
      return;
    }
    if (token.map) {
      const nline = token.map[0];
      this.insertLineMarker(result, nline);
      if (this.nline < nline) {
        this.nline = nline;
        this.moveToNextLine();
      }
    }
    result.push(token);
  }

  private insertLineMarker(result: Token[], nline: number) {
    if (this.mark[`L${nline}`] === true) {
      return;
    }

    this.mark[`L${nline}`] = true;
    const anchor = new this.state.Token('html_inline', '', 0);
    anchor.content = `<span id='LINE${nline}' class="linemark"></span>`;
    result.push(anchor);
  }

  private mark: Hashmap<boolean> = {};
  private nline = -1;
  private line: string = "";
  private lines: string[] = []
  private state: StateCore
}
