// runDelimiters.js: the shared "*"/"~" rules. canOpen/canClose are exercised through
// inlineScan.test.mjs, which is what builds spans out of them; what only this module answers is
// whether a run is still open at a point in the line, which links.js uses to decide whether a
// trailing delimiter closes a run or belongs to the URL.

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { openRunScanner } from "../src/runDelimiters.js";

function openAt(text, char, limit) {
  return openRunScanner(text).isOpenBefore(char, limit);
}

describe("open run before a position", () => {
  it("is false with nothing before the position", () => {
    assert.equal(openAt("https://host/x/*", "*", 0), false);
  });

  it("is true while a run is open", () => {
    assert.equal(openAt("*open and more", "*", 6), true);
  });

  it("is false once the run has closed again", () => {
    assert.equal(openAt("fix *urgent* and more", "*", 13), false);
  });

  it("is false for an asterisk that can neither open nor close", () => {
    assert.equal(openAt("2 * 3 and more", "*", 10), false);
  });

  it("counts a two-character run the same as a one-character run", () => {
    assert.equal(openAt("**bold and more", "*", 7), true);
  });

  it("tracks each delimiter character separately", () => {
    const runs = openRunScanner("~~struck~~ and *open and more");
    assert.equal(runs.isOpenBefore("~", 20), false);
    assert.equal(runs.isOpenBefore("*", 20), true);
  });

  it("resumes rather than restarts across increasing limits", () => {
    const runs = openRunScanner("*a* *b and more");
    assert.equal(runs.isOpenBefore("*", 2), true);
    assert.equal(runs.isOpenBefore("*", 4), false);
    assert.equal(runs.isOpenBefore("*", 7), true);
  });
});
