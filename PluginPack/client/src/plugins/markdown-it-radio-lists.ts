import MarkdownIt from "markdown-it/lib/index.mjs";
import StateCore from "markdown-it/lib/rules_core/state_core.mjs";
import Token from "markdown-it/lib/token.mjs";

import { MarkdownRenderContext } from "../Misc/MarkdownRenderContext";
import { authHeaders } from "../Client/Session";

// Radio lists, the counterpart of task lists (same syntax as NppMarkdownPanel):
//
//   - ( ) option A
//   - (x) option B
//
// Every list item that starts with "( )" or "(x)" gets a radio button. All
// radios of one list (the items of the same <ul>/<ol>) form a group: picking one
// clears the others, and the Markdown source is updated through the same
// PUT-the-document path the task-list checkboxes use.

const pattern = /^\((x|\s|)\)/i;

export default function markdownItRadioList(md: MarkdownIt) {
  md.core.ruler.after('inline', 'radio-lists', function (state: StateCore) {
    const tokens = state.tokens;
    for (let i = 2; i < tokens.length; i++) {
      if (isRadioItem(tokens, i)) {
        makeRadio(tokens[i], state);
        attrSet(tokens[i - 2], 'class', 'task-list-item radio-list-item enabled');
        const list = parentToken(tokens, i - 2);
        if (list >= 0) {
          attrSet(tokens[list], 'class', 'contains-radio-list');
        }
      }
    }

    MarkdownRenderContext.postRender.push(async () => {
      const inputs = Array
        .from(document.getElementsByClassName('radio-list-item-radio'))
        .filter(li => li.localName === "input")
        .map(li => li as HTMLInputElement);
      for (const input of inputs) {
        // Browsers only group radios by name; each list gets its own group name
        // so a click cannot uncheck a radio in another list.
        const list = input.closest('ul, ol');
        if (list) {
          // Named after the first radio's source line: unique per list, and the
          // same on every re-render of the same document.
          input.name = 'radio-list-' + (list.querySelector<HTMLInputElement>('input.radio-list-item-radio')?.dataset.line ?? '0');
        }
        input.onclick = (e) => onRadioClicked(e);
      }
    });
  });
}

function makeRadio(token: Token, state: StateCore) {
  const match = token.content.match(pattern);
  if (!match || !token.map || !token.children) {
    return;
  }
  const map: [number, number] = token.map;
  const symbol = match[1].toLowerCase();

  const radio = new state.Token('html_inline', '', 0);
  radio.content = `<input class="radio-list-item-radio" type="radio" data-line="${map[0]}" data-symbol="${symbol}"`;
  if (symbol === "x") {
    radio.content += ' checked';
  }
  radio.content += ">";
  radio.markup = match[0];
  radio.map = map;
  delete (token as any).map;

  const textToken = token.children.find(li => li.type === 'text');
  if (textToken) {
    textToken.content = textToken.content.slice(radio.markup.length);
  }
  token.content = '';
  token.children.unshift(radio);
}

async function onRadioClicked(e: Event) {
  const target = e.target as HTMLInputElement | null;
  if (!target || !target.dataset.line) {
    return;
  }
  const context = MarkdownRenderContext;
  const lines = context.source.split("\n");
  const list = target.closest('ul, ol');
  const group = list
    ? Array.from(list.querySelectorAll<HTMLInputElement>('input.radio-list-item-radio')).filter(li => li.closest('ul, ol') === list)
    : [target];

  // Clicking the selected option clears it (like NppMarkdownPanel), so a group
  // may end up with nothing selected.
  const wasChecked = target.dataset.symbol === "x";
  const select = !wasChecked;
  target.checked = select;

  let changed = false;
  for (const radio of group) {
    const symbol = (radio === target && select) ? "x" : " ";
    if (setSymbol(lines, radio, symbol)) {
      changed = true;
    }
  }
  if (!changed) {
    return;
  }

  context.source = lines.join("\n");
  await fetch(context.sourceUrl, {
    method: "PUT",
    headers: authHeaders({
      "Content-Type": "text/text"
    }),
    body: context.source
  });
}

// Rewrites "(<old>)" on the radio's source line to "(<symbol>)"; returns whether the line changed.
function setSymbol(lines: string[], radio: HTMLInputElement, symbol: string): boolean {
  const nline = Number.parseInt(radio.dataset.line ?? "");
  const current = radio.dataset.symbol ?? "";
  if (Number.isNaN(nline) || nline >= lines.length || current === symbol) {
    return false;
  }
  const line = lines[nline];
  const marker = new RegExp("\\(" + (current === "x" ? "[xX]" : (current === "" ? "" : " ")) + "\\)");
  const match = marker.exec(line);
  if (!match) {
    return false;
  }
  lines[nline] = line.substring(0, match.index) + "(" + symbol + ")" + line.substring(match.index + match[0].length);
  radio.dataset.symbol = symbol;
  radio.checked = (symbol === "x");
  return true;
}

function attrSet(token: Token, name: string, value: string) {
  const index = token.attrIndex(name);
  if (index < 0 || !token.attrs) {
    token.attrPush([name, value]);
  } else {
    token.attrs[index] = [name, value];
  }
}

function parentToken(tokens: Token[], index: number) {
  const targetLevel = tokens[index].level - 1;
  for (let i = index - 1; i >= 0; i--) {
    if (tokens[i].level === targetLevel) {
      return i;
    }
  }
  return -1;
}

function isRadioItem(tokens: Token[], index: number) {
  return tokens[index].type === 'inline' &&
    tokens[index - 1].type === 'paragraph_open' &&
    tokens[index - 2].type === 'list_item_open' &&
    pattern.test(tokens[index].content);
}
