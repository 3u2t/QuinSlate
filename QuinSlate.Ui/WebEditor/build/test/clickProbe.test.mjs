// clickProbe.js: the two log lines the caret/click offset probe emits, and the caret rect read that
// feeds the first of them. Listener registration and the geometry read need a laid-out view, so
// what is asserted here is the arithmetic and the formatting — the part that has to be right for a
// captured log to mean anything.

import { describe, it } from "node:test";
import assert from "node:assert/strict";

import { formatProbeEntry, formatGeometryEntry, caretTop } from "../src/clickProbe.js";

function viewWithCaretAt(top) {
  return {
    dom: {
      querySelector(selector) {
        if (selector !== ".cm-cursor-primary" || top == null) {
          return null;
        }
        return { getBoundingClientRect: () => ({ top }) };
      },
    },
  };
}

describe("caretTop", () => {
  it("reads the drawn primary cursor's top", () => {
    assert.equal(caretTop(viewWithCaretAt(48)), 48);
  });

  it("returns null when no cursor is drawn", () => {
    assert.equal(caretTop(viewWithCaretAt(null)), null);
  });
});

describe("formatProbeEntry", () => {
  // The editor's own metric: 15px at line-height 1.6 is a 24px line box.
  it("reports a caret on the clicked line as a delta above -(line height)", () => {
    assert.equal(
      formatProbeEntry(1, 40, 50, 48),
      "Click probe (page) #1: x=40 y=50 caretTopDelta=-2 px.");
  });

  it("reports a caret one line up as a delta past -(line height)", () => {
    assert.equal(
      formatProbeEntry(7, 40, 50, 24),
      "Click probe (page) #7: x=40 y=50 caretTopDelta=-26 px.");
  });

  it("rounds each reported number, taking the delta before rounding", () => {
    assert.equal(
      formatProbeEntry(2, 40.4, 50.6, 48.2),
      "Click probe (page) #2: x=40 y=51 caretTopDelta=-2 px.");
  });

  it("says n/a when no caret was drawn", () => {
    assert.equal(
      formatProbeEntry(3, 40, 50, null),
      "Click probe (page) #3: x=40 y=50 caretTopDelta=n/a px.");
  });

  // The index is what pairs a page line with its host partner, and what keeps pageLog.js's
  // per-distinct-message flood guard from silencing repeated presses on the same spot.
  it("makes repeated presses on one spot distinct messages", () => {
    assert.notEqual(formatProbeEntry(1, 40, 50, 48), formatProbeEntry(2, 40, 50, 48));
  });

  it("carries no document text", () => {
    assert.match(formatProbeEntry(1, 40, 50, 48), /^[\w\s().=:,;#\-/]+$/);
  });
});

describe("formatGeometryEntry", () => {
  it("reports the scale, viewport, line height and content top", () => {
    assert.equal(
      formatGeometryEntry(1.25, 380.4, 520.7, 24, 0),
      "Editor geometry (page): dpr=1.25 viewport=380x521 css px lineHeight=24.0 px contentTop=0.0 px.");
  });

  it("keeps a fractional line height visible", () => {
    assert.equal(
      formatGeometryEntry(1, 400, 300, 22.35, -12.5),
      "Editor geometry (page): dpr=1 viewport=400x300 css px lineHeight=22.4 px contentTop=-12.5 px.");
  });
});
