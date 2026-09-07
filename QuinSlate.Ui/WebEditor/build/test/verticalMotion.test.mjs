// verticalMotion.js: the mapping that takes a caret position CM6 measured against a line's hidden
// text and turns it back into the position it should have once that line is revealed.
//
// The measuring itself is DOM geometry and stays untested here; the correction is not, and it is
// the whole of the fix. buildDecorations gives the same DecorationSet the plugin renders, so these
// run against exactly what the mapping sees at runtime.

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { buildDecorations } from "../src/markdownFormat.js";
import { hiddenMarkerLengthBefore } from "../src/verticalMotion.js";
import { makeView } from "./editorHarness.mjs";

// The caret sits on line 1 throughout, so line 2 is always the hidden one under test.
function hiddenOnSecondLine(text, columnInLine) {
  const inner = makeView("|x\n" + text);
  const view = {
    get state() {
      return inner.state;
    },
    get visibleRanges() {
      return [{ from: 0, to: inner.state.doc.length }];
    },
  };
  const line = view.state.doc.line(2);
  return hiddenMarkerLengthBefore(buildDecorations(view), line.from, line.from + columnInLine);
}

describe("hidden marker length before a position", () => {
  it("counts nothing before the first marker", () => {
    assert.equal(hiddenOnSecondLine("**bold** and more", 0), 0);
  });

  it("counts an opening marker the position has passed", () => {
    assert.equal(hiddenOnSecondLine("**bold** and more", 4), 2);
  });

  it("counts both markers once the position is past the run", () => {
    assert.equal(hiddenOnSecondLine("**bold** and more", 8), 4);
  });

  // The reported bug: CM6 hands back column 8 because that is where the goal x lands in "bold and
  // more", and 8 - 4 is the column the caret visually had.
  it("maps the measured position back to the column the caret keeps", () => {
    assert.equal(8 - hiddenOnSecondLine("**bold** and more", 8), 4);
  });

  it("stops counting at the position, not at the end of the marker it is inside", () => {
    assert.equal(hiddenOnSecondLine("**bold** and more", 1), 1);
  });

  it("counts a heading's hashes", () => {
    assert.equal(hiddenOnSecondLine("## Title", 5), 3);
  });

  it("counts a rule, which hides the whole line", () => {
    assert.equal(hiddenOnSecondLine("---", 3), 3);
  });

  it("counts markers inside a list item but not the item's own prefix", () => {
    assert.equal(hiddenOnSecondLine("- **bold** tail", 6), 2);
  });

  it("counts nothing on a line with no markers", () => {
    assert.equal(hiddenOnSecondLine("plain text here", 9), 0);
  });

  it("counts nothing on the revealed line the caret is on", () => {
    const inner = makeView("**bo|ld** and more\nx");
    const view = {
      get state() {
        return inner.state;
      },
      get visibleRanges() {
        return [{ from: 0, to: inner.state.doc.length }];
      },
    };
    const line = view.state.doc.line(1);
    assert.equal(hiddenMarkerLengthBefore(buildDecorations(view), line.from, line.from + 8), 0);
  });
});
