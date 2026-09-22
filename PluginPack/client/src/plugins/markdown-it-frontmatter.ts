import MarkdownIt from 'markdown-it'

// YAML front matter (a "---" block at the very top of the file, as used by Jekyll,
// Hugo, Obsidian, ...) is shown as a yaml code block instead of being rendered
// as a horizontal rule followed by garbled paragraphs.
//
// This runs on the source text before any block parsing and replaces the two
// delimiter lines with a fence, so the line count is unchanged and the
// linemark/scroll-sync line numbers stay correct.
const FRONT_MATTER = /^(?:﻿)?---[ \t]*\r?\n([\s\S]*?\r?\n)?(---|\.\.\.)[ \t]*(\r?\n|$)/;

export default function markdownItFrontMatter(md: MarkdownIt) {
  md.core.ruler.before('normalize', 'frontmatter', function (state) {
    const match = FRONT_MATTER.exec(state.src);
    if (!match) {
      return;
    }
    const yaml = match[1] ?? '';
    state.src = '```yaml\n' + yaml + '```' + match[3] + state.src.substring(match[0].length);
  });
}
