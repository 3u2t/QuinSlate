// codeSpans.js: backtick pairing within one line. Shared by inlineScan.js (code is claimed before
// emphasis) and links.js (a URL inside a code span is not a link), so it is tested on its own.

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { findCodeSpans, overlapsAny } from "../src/codeSpans.js";

const TICK = String.fromCharCode(96);

// The content of every code span found in `text`, in order.
function contents(text) {
  return findCodeSpans(text).map((span) => text.slice(span.contentFrom, span.contentTo));
}

describe("code span detection", () => {
  it("finds a single-backtick span", () => {
    assert.deepEqual(contents("run " + TICK + "npm test" + TICK + " first"), ["npm test"]);
  });

  it("reports the full extent including the backticks", () => {
    const spans = findCodeSpans("a " + TICK + "x" + TICK);
    assert.equal(spans.length, 1);
    assert.equal(spans[0].from, 2);
    assert.equal(spans[0].to, 5);
  });

  it("finds several spans on one line", () => {
    const tick = TICK;
    assert.deepEqual(contents(tick + "a" + tick + " and " + tick + "b" + tick), ["a", "b"]);
  });

  it("closes only on a run of equal length", () => {
    const double = TICK + TICK;
    assert.deepEqual(contents(double + "a " + TICK + " b" + double), ["a " + TICK + " b"]);
  });

  it("leaves an unclosed run as text", () => {
    assert.deepEqual(contents("a " + TICK + "b"), []);
    assert.deepEqual(contents(TICK), []);
    assert.deepEqual(contents(TICK + TICK), []);
  });

  it("starts scanning at the given content offset", () => {
    const text = "- [ ] " + TICK + "x" + TICK;
    assert.deepEqual(findCodeSpans(text, 6).map((span) => span.from), [6]);
  });
});

describe("overlap test", () => {
  it("reports a range inside a span", () => {
    const spans = findCodeSpans(TICK + "abc" + TICK);
    assert.equal(overlapsAny(spans, 1, 4), true);
  });

  it("reports a range that only clips a span", () => {
    const spans = findCodeSpans(TICK + "abc" + TICK + "def");
    assert.equal(overlapsAny(spans, 3, 8), true);
  });

  it("reports nothing for a range clear of every span", () => {
    const spans = findCodeSpans(TICK + "abc" + TICK + " def");
    assert.equal(overlapsAny(spans, 6, 9), false);
    assert.equal(overlapsAny([], 0, 5), false);
  });
});
