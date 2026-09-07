// blockFormat.js: which lines are headings and which are horizontal rules. Both markers stay in
// the document, so every assertion here is about recognition alone — what gets hidden and styled,
// never what is written.

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { parseBlock, BLOCK_KIND_HEADING, BLOCK_KIND_RULE } from "../src/blockFormat.js";

describe("heading recognition", () => {
  it("recognises all six levels", () => {
    for (let level = 1; level <= 6; level++) {
      const block = parseBlock("#".repeat(level) + " Title");
      assert.equal(block.kind, BLOCK_KIND_HEADING);
      assert.equal(block.level, level);
    }
  });

  it("reports the marker length, hashes and the space", () => {
    assert.equal(parseBlock("### Title").markerLength, 4);
  });

  it("requires the space after the hashes", () => {
    assert.equal(parseBlock("#1"), null);
    assert.equal(parseBlock("#tag"), null);
    assert.equal(parseBlock("#include <stdio.h>"), null);
  });

  it("requires column 0 — an indented hash belongs to list nesting", () => {
    assert.equal(parseBlock("  # Title"), null);
    assert.equal(parseBlock(" # Title"), null);
  });

  it("stops at six hashes", () => {
    assert.equal(parseBlock("####### Title"), null);
  });

  it("accepts a heading with no text yet", () => {
    const block = parseBlock("# ");
    assert.equal(block.kind, BLOCK_KIND_HEADING);
    assert.equal(block.markerLength, 2);
  });
});

describe("horizontal rule recognition", () => {
  it("recognises three or more dashes", () => {
    assert.equal(parseBlock("---").kind, BLOCK_KIND_RULE);
    assert.equal(parseBlock("--------").kind, BLOCK_KIND_RULE);
  });

  it("takes the whole line as the marker", () => {
    assert.equal(parseBlock("---").markerLength, 3);
    assert.equal(parseBlock("---  ").markerLength, 5);
  });

  it("allows trailing whitespace only", () => {
    assert.equal(parseBlock("---\t").kind, BLOCK_KIND_RULE);
    assert.equal(parseBlock("--- x"), null);
  });

  it("needs three dashes", () => {
    assert.equal(parseBlock("--"), null);
    assert.equal(parseBlock("-"), null);
  });

  it("does not claim a bullet — a bullet marker is a dash and a space", () => {
    assert.equal(parseBlock("- item"), null);
  });

  it("has no asterisk or underscore alias", () => {
    assert.equal(parseBlock("***"), null);
    assert.equal(parseBlock("___"), null);
  });
});

describe("plain lines", () => {
  it("returns null for text, blank lines, and list items", () => {
    assert.equal(parseBlock(""), null);
    assert.equal(parseBlock("just text"), null);
    assert.equal(parseBlock("1. numbered"), null);
    assert.equal(parseBlock("- [ ] task"), null);
  });
});
