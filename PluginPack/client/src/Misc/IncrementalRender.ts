import MarkdownIt from 'markdown-it'
import { Token } from 'markdown-it/index.js'
import { sanitizeMarkdownHtml } from './Sanitize'

// Incremental rendering.
//
// markdown-it re-parses the whole source on every keystroke (there is no
// incremental parser), but parsing is the cheap part. Sanitizing the resulting
// HTML and rebuilding the DOM is what hurts on a large document, and both are
// avoidable: the token stream is split into top-level blocks, each block is
// rendered to its own HTML string, and only the blocks that actually changed
// are sanitized and patched into the DOM.
//
// Keeping the untouched nodes alive also means the scroll position, rendered
// Mermaid diagrams, loaded images and the browser's own selection survive an
// edit elsewhere in the document.

interface RenderedBlock {
  html: string;
  // html with the line-anchor numbers masked out - this is what blocks are
  // compared by. Pressing Enter shifts the line number of every block below the
  // caret, and comparing raw html would mark all of them as changed.
  key: string;
  // The line numbers this block's anchors carry, in document order.
  ids: number[];
  // How many top-level DOM nodes this block contributes, so a block index can
  // be translated into a position among the container's children.
  nodes: number;
}

export interface RenderResult {
  blocksTotal: number;
  blocksChanged: number;
  nodesReplaced: number;
  fullRender: boolean;
}

interface Segment {
  oldStart: number;
  oldEnd: number;
  newStart: number;
  newEnd: number;
}

// Absolute source line numbers in the rendered HTML: the line-mark anchors and
// the data-line of task/radio inputs. Both shift when a line is inserted.
const LINE_NUMBER = /id='LINE(\d+)'|data-line="(\d+)"/g;
const LINE_NUMBER_SELECTOR = "span.linemark,[data-line]";
const EMPTY_IDS: number[] = [];

let previous: RenderedBlock[] = [];
let previousContainer: HTMLElement | null = null;
// Cleared when the block -> node mapping cannot be trusted, which forces full
// renders for the rest of this document's lifetime.
let mappingExact = false;

export function ResetIncrementalRender() {
  previous = [];
  previousContainer = null;
  mappingExact = false;
}

export function RenderIncremental(md: MarkdownIt, source: string, container: HTMLElement): RenderResult {
  const env = {};
  const tokens = md.parse(source, env);
  const groups = splitTopLevelBlocks(tokens);
  const options = (md as any).options;
  const html = groups.map(group => md.renderer.render(group, options, env));

  if (previousContainer !== container || previous.length === 0 || !mappingExact) {
    return fullRender(html, container, groups.length);
  }

  const fresh = html.map(blockHtml => describe(blockHtml, 0));

  // Unchanged prefix and suffix; everything between them is replaced. Ordinary
  // editing changes one region, so the middle is one or two blocks.
  let start = 0;
  const maxStart = Math.min(previous.length, fresh.length);
  while (start < maxStart && previous[start].key === fresh[start].key) {
    start++;
  }
  let endOld = previous.length;
  let endNew = fresh.length;
  while (endOld > start && endNew > start && previous[endOld - 1].key === fresh[endNew - 1].key) {
    endOld--;
    endNew--;
  }

  if (start === endOld && start === endNew) {
    // Nothing structural changed, but the line numbers may have shifted.
    const shifted = syncLineAnchors(previous, fresh, container, []);
    if (shifted < 0) {
      return fullRender(html, container, groups.length);
    }
    previous = adopt(previous, fresh, []);
    return { blocksTotal: groups.length, blocksChanged: 0, nodesReplaced: shifted, fullRender: false };
  }

  // Cutting text here and pasting it there changes two distant regions, and the
  // span between them would look changed to plain prefix/suffix trimming. Split
  // that span at blocks that occur exactly once on both sides, so each part can
  // be trimmed on its own and only the real edits are patched.
  const segments = alignSegments(previous, fresh, start, endOld, endNew);

  const existing = Array.from(container.childNodes);
  const blocksBefore = previous;
  const rendered: Segment[] = [];
  let blocksChanged = 0;
  let nodesReplaced = 0;

  for (const segment of segments) {
    const fragment = document.createDocumentFragment();
    // Sanitizing block by block costs ~0.25 ms per call - far too slow for a
    // whole document, irrelevant for the few blocks that actually changed, and
    // it yields an exact node count per block.
    for (let i = segment.newStart; i < segment.newEnd; i++) {
      const blockFragment = sanitizeMarkdownHtml(html[i]);
      fresh[i].nodes = blockFragment.childNodes.length;
      fragment.appendChild(blockFragment);
    }

    const firstNode = nodeIndexOfBlock(blocksBefore, segment.oldStart);
    const lastNode = nodeIndexOfBlock(blocksBefore, segment.oldEnd);
    const anchor = existing[lastNode] ?? null;
    const insertedCount = fragment.childNodes.length;

    for (let i = firstNode; i < lastNode; i++) {
      container.removeChild(existing[i]);
    }
    if (insertedCount !== 0) {
      container.insertBefore(fragment, anchor);
    }

    rendered.push(segment);
    blocksChanged += segment.newEnd - segment.newStart;
    nodesReplaced += insertedCount + (lastNode - firstNode);
  }

  // Blocks that were kept keep their node counts; their anchors may need new
  // line numbers.
  const shifted = syncLineAnchors(blocksBefore, fresh, container, rendered);
  previous = adopt(blocksBefore, fresh, rendered);
  if (shifted < 0) {
    return fullRender(html, container, groups.length);
  }

  return { blocksTotal: groups.length, blocksChanged, nodesReplaced, fullRender: false };
}

function fullRender(html: string[], container: HTMLElement, blocksTotal: number): RenderResult {
  const fragment = sanitizeMarkdownHtml(html.join(""));
  const nodeCount = fragment.childNodes.length;
  container.replaceChildren(fragment);

  // Node counts come from parsing each block on its own (much cheaper than
  // sanitizing it). That matches the sanitized DOM unless the sanitizer dropped
  // a top-level node - a raw <script>/<iframe>/<form> in the document. When the
  // totals disagree the mapping would be off by a node, so incremental patching
  // is disabled for this document rather than risking a wrong patch.
  const template = document.createElement("template");
  const blocks: RenderedBlock[] = [];
  let counted = 0;
  for (const blockHtml of html) {
    template.innerHTML = blockHtml;
    const count = template.content.childNodes.length;
    blocks.push(describe(blockHtml, count));
    counted += count;
  }

  previous = blocks;
  previousContainer = container;
  mappingExact = (counted === nodeCount);

  return { blocksTotal, blocksChanged: blocksTotal, nodesReplaced: nodeCount, fullRender: true };
}

function describe(html: string, nodes: number): RenderedBlock {
  let ids: number[] | null = null;
  const key = html.replace(LINE_NUMBER, (match, anchor, data) => {
    (ids ??= []).push(anchor !== undefined ? +anchor : +data);
    return anchor !== undefined ? "id='LINE'" : 'data-line=""';
  });
  return { html, key, ids: ids ?? EMPTY_IDS, nodes };
}

// Walks the kept blocks in parallel on both sides. `rendered` lists the
// segments that were re-rendered and therefore need no adoption.
function walkKept(before: RenderedBlock[], fresh: RenderedBlock[], rendered: Segment[],
                  visit: (freshIndex: number, oldIndex: number) => boolean | void) {
  let oldIndex = 0;
  let segment = 0;
  for (let i = 0; i < fresh.length; i++) {
    while (segment < rendered.length && i >= rendered[segment].newEnd) {
      oldIndex = rendered[segment].oldEnd;
      segment++;
    }
    if (segment < rendered.length && i >= rendered[segment].newStart) {
      continue;
    }
    if (visit(i, oldIndex++) === false) {
      return;
    }
  }
}

// Carries the node counts of kept blocks over to the freshly described ones.
function adopt(before: RenderedBlock[], fresh: RenderedBlock[], rendered: Segment[]): RenderedBlock[] {
  walkKept(before, fresh, rendered, (freshIndex, oldIndex) => {
    const old = before[oldIndex];
    if (old) {
      fresh[freshIndex].nodes = old.nodes;
    }
  });
  return fresh;
}

// Writes the new line numbers onto the anchors of blocks that were not
// re-rendered. Returns the number of anchors updated, or -1 when the DOM no
// longer lines up with the block list (the caller then does a full render).
function syncLineAnchors(before: RenderedBlock[], fresh: RenderedBlock[], container: HTMLElement, rendered: Segment[]): number {
  let shifted = false;
  walkKept(before, fresh, rendered, (freshIndex, oldIndex) => {
    const old = before[oldIndex];
    if (old && !sameIds(old.ids, fresh[freshIndex].ids)) {
      shifted = true;
      return false;
    }
  });
  if (!shifted) {
    return 0;
  }

  const expected: number[] = [];
  for (const block of fresh) {
    for (const id of block.ids) {
      expected.push(id);
    }
  }
  // Document order matches the order the numbers were collected in, because no
  // element carries both an anchor id and a data-line attribute.
  const carriers = container.querySelectorAll<HTMLElement>(LINE_NUMBER_SELECTOR);
  if (carriers.length !== expected.length) {
    return -1;
  }
  let updated = 0;
  for (let i = 0; i < expected.length; i++) {
    const element = carriers[i];
    const value = expected[i];
    if (element.classList.contains("linemark")) {
      const id = "LINE" + value;
      if (element.id !== id) {
        element.id = id;
        updated++;
      }
    }
    else if (element.dataset.line !== String(value)) {
      element.dataset.line = String(value);
      updated++;
    }
  }
  return updated;
}

function sameIds(a: number[], b: number[]): boolean {
  if (a === b) {
    return true;
  }
  if (a.length !== b.length) {
    return false;
  }
  for (let i = 0; i < a.length; i++) {
    if (a[i] !== b[i]) {
      return false;
    }
  }
  return true;
}

// Splits the changed span into the smallest possible replacement segments.
// Blocks whose content occurs exactly once in both the old and the new span are
// reliable alignment points (the idea behind patience diff); between two
// consecutive points the spans are trimmed again from both ends.
function alignSegments(before: RenderedBlock[], fresh: RenderedBlock[], start: number, endOld: number, endNew: number): Segment[] {
  const whole: Segment = { oldStart: start, oldEnd: endOld, newStart: start, newEnd: endNew };
  // Not worth the bookkeeping for a small span - and that is the usual case.
  if (Math.min(endOld - start, endNew - start) < 32) {
    return [whole];
  }

  const oldIndex = uniqueIndex(before, start, endOld);
  const newIndex = uniqueIndex(fresh, start, endNew);

  const anchors: { oldAt: number, newAt: number }[] = [];
  let lastOld = start - 1;
  for (const [key, newAt] of newIndex) {
    const oldAt = oldIndex.get(key);
    // newIndex iterates in ascending order, so keeping the old side increasing
    // as well is enough to keep the segments disjoint and ordered.
    if (oldAt !== undefined && oldAt > lastOld) {
      anchors.push({ oldAt, newAt });
      lastOld = oldAt;
    }
  }
  if (anchors.length === 0) {
    return [whole];
  }

  const segments: Segment[] = [];
  let oldCursor = start;
  let newCursor = start;
  for (const anchor of anchors.concat([{ oldAt: endOld, newAt: endNew }])) {
    const segment = trim(before, fresh, oldCursor, anchor.oldAt, newCursor, anchor.newAt);
    if (segment) {
      segments.push(segment);
    }
    oldCursor = anchor.oldAt + 1;
    newCursor = anchor.newAt + 1;
  }
  return segments;
}

// Map of block key -> index, for blocks that occur exactly once in [from, to).
function uniqueIndex(blocks: RenderedBlock[], from: number, to: number): Map<string, number> {
  const seen = new Map<string, number>();
  for (let i = from; i < to; i++) {
    const key = blocks[i].key;
    seen.set(key, seen.has(key) ? -1 : i);
  }
  for (const [key, index] of seen) {
    if (index < 0) {
      seen.delete(key);
    }
  }
  return seen;
}

// Trims equal blocks from both ends of a span; returns null when nothing differs.
function trim(before: RenderedBlock[], fresh: RenderedBlock[], oldStart: number, oldEnd: number, newStart: number, newEnd: number): Segment | null {
  while (oldStart < oldEnd && newStart < newEnd && before[oldStart].key === fresh[newStart].key) {
    oldStart++;
    newStart++;
  }
  while (oldEnd > oldStart && newEnd > newStart && before[oldEnd - 1].key === fresh[newEnd - 1].key) {
    oldEnd--;
    newEnd--;
  }
  return (oldStart === oldEnd && newStart === newEnd) ? null : { oldStart, oldEnd, newStart, newEnd };
}

// Splits the token stream into top-level blocks: runs of tokens that start and
// end at nesting depth 0. Leading line-mark anchors (html_inline tokens inserted
// by the linemark plugin) stay with the block that follows them, so an anchor
// and its block always move together.
function splitTopLevelBlocks(tokens: Token[]): Token[][] {
  const groups: Token[][] = [];
  let current: Token[] = [];
  let depth = 0;
  let onlyAnchors = true;

  for (const token of tokens) {
    current.push(token);
    if (token.nesting === 1) {
      depth++;
      onlyAnchors = false;
    }
    else if (token.nesting === -1) {
      depth--;
    }
    else if (!isLineAnchor(token)) {
      onlyAnchors = false;
    }
    if (depth === 0 && !onlyAnchors) {
      groups.push(current);
      current = [];
      onlyAnchors = true;
    }
  }
  if (current.length !== 0) {
    groups.push(current);
  }
  return groups;
}

function isLineAnchor(token: Token): boolean {
  return token.type === 'html_inline' && token.content.indexOf('class="linemark"') >= 0;
}

function nodeIndexOfBlock(blocks: RenderedBlock[], blockIndex: number): number {
  let index = 0;
  const limit = Math.min(blockIndex, blocks.length);
  for (let i = 0; i < limit; i++) {
    index += blocks[i].nodes;
  }
  return index;
}
