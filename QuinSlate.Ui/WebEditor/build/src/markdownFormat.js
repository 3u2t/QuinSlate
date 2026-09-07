// Markdown formatting decorations (Docs/Specs/23-MARKDOWN-FORMATTING.md): headings, bold, italic,
// strikethrough, inline code, and horizontal rules. Rendering only — this module registers no
// keymap and writes nothing to the document, so the buffer .txt is byte-for-byte what it would be
// without the feature and undo has nothing extra to unwind.
//
// Markers are hidden by a replace decoration and revealed on the line the selection is on. That
// reveal is also what keeps the caret out of every hidden range — arriving on a line shows its
// markers — so none of the atomic-range machinery tasks.js and lists.js need applies here.

import { Decoration, ViewPlugin, WidgetType } from "@codemirror/view";
import { parseItem } from "./listItems.js";
import { parseBlock, BLOCK_KIND_RULE } from "./blockFormat.js";
import {
  scanInline,
  SPAN_KIND_CODE,
  SPAN_KIND_BOLD,
  SPAN_KIND_ITALIC,
  SPAN_KIND_BOLD_ITALIC,
  SPAN_KIND_STRIKE,
} from "./inlineScan.js";

const MARKER_CLASS = "cm-md-marker";
// Set on every replace decoration the reveal toggles. verticalMotion.js counts these when it maps
// a caret position back out of the hidden text, and must not count the list and task widgets:
// those are replaced whether or not the line is revealed, so their width is already in the
// position CM6 hands back.
const HIDDEN_MARKER_SPEC = { mdMarker: true };
const HEADING_CLASS_PREFIX = "cm-md-h";
const RULE_CLASS = "cm-md-rule";

const CLASS_BY_SPAN_KIND = {
  [SPAN_KIND_CODE]: "cm-md-code",
  [SPAN_KIND_BOLD]: "cm-md-bold",
  [SPAN_KIND_ITALIC]: "cm-md-italic",
  [SPAN_KIND_BOLD_ITALIC]: "cm-md-bold cm-md-italic",
  [SPAN_KIND_STRIKE]: "cm-md-strike",
};

class RuleWidget extends WidgetType {
  eq() {
    return true;
  }

  // A drawn line rather than a repeated glyph, the way .cm-list-bullet draws its dot.
  toDOM() {
    const rule = document.createElement("span");
    rule.className = RULE_CLASS;
    rule.setAttribute("aria-hidden", "true");
    return rule;
  }

  ignoreEvent() {
    return false;
  }
}

function addMarker(marks, from, to, revealed) {
  if (to <= from) {
    return;
  }
  marks.push(
    revealed
      ? Decoration.mark({ class: MARKER_CLASS }).range(from, to)
      : Decoration.replace(HIDDEN_MARKER_SPEC).range(from, to)
  );
}

function addInlineSpans(marks, line, contentStart, revealed) {
  for (const span of scanInline(line.text, contentStart)) {
    addMarker(marks, line.from + span.from, line.from + span.contentFrom, revealed);
    if (span.contentTo > span.contentFrom) {
      marks.push(
        Decoration.mark({ class: CLASS_BY_SPAN_KIND[span.kind] }).range(
          line.from + span.contentFrom,
          line.from + span.contentTo
        )
      );
    }
    addMarker(marks, line.from + span.contentTo, line.from + span.to, revealed);
  }
}

// A revealed rule shows its dimmed dashes and nothing else: the line is all marker, so there is no
// content left to keep formatted while it is edited.
function addRule(marks, line, revealed) {
  if (revealed) {
    addMarker(marks, line.from, line.to, true);
    return;
  }
  marks.push(
    Decoration.replace({ widget: new RuleWidget(), mdMarker: true }).range(line.from, line.to)
  );
}

function addLine(marks, line, revealed) {
  const item = parseItem(line.text);
  if (item != null) {
    addInlineSpans(marks, line, item.prefixLength, revealed);
    return;
  }

  const block = parseBlock(line.text);
  if (block == null) {
    addInlineSpans(marks, line, 0, revealed);
    return;
  }
  if (block.kind === BLOCK_KIND_RULE) {
    addRule(marks, line, revealed);
    return;
  }

  // An empty heading keeps its marker on screen however the selection sits. Hiding it would leave
  // a blank line that is still heading-tall and gives no sign anything is there — a task or bullet
  // in the same state still shows its widget, and a heading has none.
  const hasText = line.text.slice(block.markerLength).trim().length > 0;
  marks.push(Decoration.line({ class: HEADING_CLASS_PREFIX + block.level }).range(line.from));
  addMarker(marks, line.from, line.from + block.markerLength, revealed || !hasText);
  addInlineSpans(marks, line, block.markerLength, revealed);
}

// True for the replace decorations addMarker and addRule produce, and only those.
export function isHiddenMarker(decoration) {
  return decoration.spec.mdMarker === true;
}

// Exported for the tests: this is the plugin's whole body, and it needs nothing from the DOM —
// only `state` and `visibleRanges` off the view.
export function buildDecorations(view) {
  const marks = [];
  const state = view.state;
  // Whole-line granularity, and the selection's whole span: character proximity would flicker
  // markers in and out as the caret moves along a line. allowMultipleSelections is off, so the
  // main range is the only one.
  const selection = state.selection.main;
  const firstRevealed = state.doc.lineAt(selection.from).number;
  const lastRevealed = state.doc.lineAt(selection.to).number;

  for (const range of view.visibleRanges) {
    let pos = range.from;
    while (pos <= range.to) {
      const line = state.doc.lineAt(pos);
      addLine(marks, line, line.number >= firstRevealed && line.number <= lastRevealed);
      pos = line.to + 1;
    }
  }
  return Decoration.set(marks, true);
}

export const markdownPlugin = ViewPlugin.fromClass(
  class {
    constructor(view) {
      this.decorations = buildDecorations(view);
    }

    // selectionSet as well as docChanged: the marker reveal follows the selection's line, and it
    // does so whether or not the editor has focus, so an unfocused editor renders identically.
    update(update) {
      if (update.docChanged || update.viewportChanged || update.selectionSet) {
        this.decorations = buildDecorations(update.view);
      }
    }
  },
  {
    decorations: (value) => value.decorations,
  }
);
