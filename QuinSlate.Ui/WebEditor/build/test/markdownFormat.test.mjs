// markdownFormat.js: the decorations the scanners' offsets turn into. The scanners have their own
// tests; what is only testable here is the wiring — which line kind is dispatched where, and the
// selection-line reveal that decides whether a marker is hidden or dimmed.
//
// buildDecorations needs `state` and `visibleRanges` and nothing else, so the harness view plus a
// single range covering the document drives it exactly as the plugin does.

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { buildDecorations } from "../src/markdownFormat.js";
import { makeView, makeLineSelectionView } from "./editorHarness.mjs";

const TICK = String.fromCharCode(96);

function wholeDoc(inner) {
  return {
    get state() {
      return inner.state;
    },
    get visibleRanges() {
      return [{ from: 0, to: inner.state.doc.length }];
    },
  };
}

// Every decoration in document order as "<what> <text it covers>". A zero-length range is a line
// decoration; a range with no class is a hidden marker, or the rule when it carries the widget.
function decorations(text) {
  const view = wholeDoc(makeView(text));
  const doc = view.state.doc;
  const out = [];
  const cursor = buildDecorations(view).iter();
  while (cursor.value != null) {
    const { from, to, value } = cursor;
    if (from === to) {
      out.push("line " + value.spec.class);
    } else if (value.spec.class != null) {
      out.push(value.spec.class + " " + JSON.stringify(doc.sliceString(from, to)));
    } else if (value.spec.widget != null) {
      out.push("rule " + JSON.stringify(doc.sliceString(from, to)));
    } else {
      out.push("hidden " + JSON.stringify(doc.sliceString(from, to)));
    }
    cursor.next();
  }
  return out;
}

function decorationsWithLineSelection(text, fromLine, toLine) {
  const view = wholeDoc(makeLineSelectionView(text, fromLine, toLine));
  const out = [];
  const cursor = buildDecorations(view).iter();
  while (cursor.value != null) {
    if (cursor.from !== cursor.to && cursor.value.spec.class == null) {
      out.push("hidden");
    } else if (cursor.value.spec.class === "cm-md-marker") {
      out.push("marker");
    }
    cursor.next();
  }
  return out;
}

describe("marker visibility", () => {
  it("hides the markers on a line the selection is not on", () => {
    assert.deepEqual(decorations("a\n*x* b"), ['hidden "*"', 'cm-md-italic "x"', 'hidden "*"']);
  });

  it("dims them instead on the line the caret is on", () => {
    assert.deepEqual(decorations("|*x* b"), [
      'cm-md-marker "*"',
      'cm-md-italic "x"',
      'cm-md-marker "*"',
    ]);
  });

  it("reveals every line a multi-line selection touches, and no others", () => {
    // Three italic lines; the selection covers the first two.
    assert.deepEqual(decorationsWithLineSelection("*a*\n*b*\n*c*", 1, 2), [
      "marker",
      "marker",
      "marker",
      "marker",
      "hidden",
      "hidden",
    ]);
  });
});

describe("headings", () => {
  it("styles the line and hides the hashes", () => {
    assert.deepEqual(decorations("x\n## Title"), ["line cm-md-h2", 'hidden "## "']);
  });

  it("scans the text past the marker", () => {
    assert.deepEqual(decorations("x\n# A *big* title"), [
      "line cm-md-h1",
      'hidden "# "',
      'hidden "*"',
      'cm-md-italic "big"',
      'hidden "*"',
    ]);
  });

  it("keeps an empty heading's marker on screen so the line is not blank", () => {
    assert.deepEqual(decorations("x\n### "), ["line cm-md-h3", 'cm-md-marker "### "']);
  });

  it("treats a whitespace-only heading as empty too", () => {
    assert.deepEqual(decorations("x\n#   "), ["line cm-md-h1", 'cm-md-marker "# "']);
  });
});

describe("horizontal rule", () => {
  it("replaces the whole line with the drawn widget", () => {
    assert.deepEqual(decorations("x\n---"), ['rule "---"']);
  });

  it("shows the dashes instead on the line the caret is on", () => {
    assert.deepEqual(decorations("|---"), ['cm-md-marker "---"']);
  });
});

describe("line dispatch", () => {
  it("leaves a list item's own prefix alone and scans only its content", () => {
    assert.deepEqual(decorations("x\n- [ ] **do it**"), ['hidden "**"', 'cm-md-bold "do it"', 'hidden "**"']);
  });

  it("does not read a hash inside an item's content as a heading", () => {
    assert.deepEqual(decorations("x\n- # not a heading"), []);
  });

  it("carries every inline kind on a plain line", () => {
    const text = "x\n**b** *i* ***bi*** ~~s~~ " + TICK + "c" + TICK;
    assert.deepEqual(decorations(text).filter((entry) => !entry.startsWith("hidden")), [
      'cm-md-bold "b"',
      'cm-md-italic "i"',
      'cm-md-bold cm-md-italic "bi"',
      'cm-md-strike "s"',
      'cm-md-code "c"',
    ]);
  });
});
