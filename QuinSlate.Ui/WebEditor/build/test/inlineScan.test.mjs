// inlineScan.js: which runs of a line's content are emphasis, strikethrough, or code, and — just
// as importantly — which are not. The narrowings from Docs/Specs/23-MARKDOWN-FORMATTING.md
// § What is emphasis carry most of the weight here: a scratchpad is full of text that a full
// CommonMark scanner would emphasise by accident.

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  scanInline,
  SPAN_KIND_CODE,
  SPAN_KIND_BOLD,
  SPAN_KIND_ITALIC,
  SPAN_KIND_BOLD_ITALIC,
  SPAN_KIND_STRIKE,
} from "../src/inlineScan.js";

const TICK = String.fromCharCode(96);

// Every span as "kind:content", in document order.
function spans(text, from = 0) {
  return scanInline(text, from).map(
    (span) => span.kind + ":" + text.slice(span.contentFrom, span.contentTo)
  );
}

describe("emphasis", () => {
  it("finds bold, italic, and both", () => {
    assert.deepEqual(spans("*a*"), [SPAN_KIND_ITALIC + ":a"]);
    assert.deepEqual(spans("**a**"), [SPAN_KIND_BOLD + ":a"]);
    assert.deepEqual(spans("***a***"), [SPAN_KIND_BOLD_ITALIC + ":a"]);
  });

  it("finds strikethrough", () => {
    assert.deepEqual(spans("~~gone~~"), [SPAN_KIND_STRIKE + ":gone"]);
  });

  it("reports the full extent including the delimiters", () => {
    const found = scanInline("see **bold** here");
    assert.equal(found.length, 1);
    assert.equal(found[0].from, 4);
    assert.equal(found[0].to, 12);
    assert.equal(found[0].contentFrom, 6);
    assert.equal(found[0].contentTo, 10);
  });

  it("finds several spans on one line", () => {
    assert.deepEqual(spans("**a** and *b*"), [SPAN_KIND_BOLD + ":a", SPAN_KIND_ITALIC + ":b"]);
  });

  it("opens after punctuation and closes before it", () => {
    assert.deepEqual(spans("(*aside*)"), [SPAN_KIND_ITALIC + ":aside"]);
    assert.deepEqual(spans("*done*, next"), [SPAN_KIND_ITALIC + ":done"]);
  });
});

describe("what is not emphasis", () => {
  it("leaves arithmetic alone — the inline calculator has to see it intact", () => {
    assert.deepEqual(spans("2*3*4"), []);
    assert.deepEqual(spans("2*3*4 ="), []);
  });

  it("never treats an underscore as a delimiter", () => {
    assert.deepEqual(spans("snake_case_name"), []);
    assert.deepEqual(spans("__init__"), []);
    assert.deepEqual(spans("_italic_"), []);
  });

  it("refuses a delimiter touching whitespace on the inside", () => {
    assert.deepEqual(spans("* not italic *"), []);
    assert.deepEqual(spans("a ** b ** c"), []);
  });

  it("refuses a delimiter touching a word character on the outside", () => {
    assert.deepEqual(spans("a*b*c"), []);
  });

  it("leaves an unclosed or empty run as text", () => {
    assert.deepEqual(spans("**bold"), []);
    assert.deepEqual(spans("**"), []);
    assert.deepEqual(spans("****"), []);
    assert.deepEqual(spans("**a*"), []);
  });

  it("does not nest — a bold run containing an italic run renders the outer run only", () => {
    assert.deepEqual(spans("**bold with *inner* italic**"), [
      SPAN_KIND_BOLD + ":bold with *inner* italic",
    ]);
  });
});

describe("precedence", () => {
  it("claims code before emphasis, so a code span's asterisks stay literal", () => {
    const text = TICK + "**literal**" + TICK;
    assert.deepEqual(spans(text), [SPAN_KIND_CODE + ":**literal**"]);
  });

  it("claims a URL, so its characters never act as delimiters", () => {
    assert.deepEqual(spans("https://example.com/*x*/y"), []);
  });

  it("still emphasises around a URL", () => {
    assert.deepEqual(spans("**see https://example.com**"), [
      SPAN_KIND_BOLD + ":see https://example.com",
    ]);
  });

  it("emits no span for a URL — links.js owns those", () => {
    assert.deepEqual(spans("https://example.com"), []);
  });

  it("keeps a URL inside a code span as code", () => {
    const text = TICK + "https://example.com" + TICK;
    assert.deepEqual(spans(text), [SPAN_KIND_CODE + ":https://example.com"]);
  });

  // links.js drops a link that touches a code span, so this line is not a link at all. Its
  // characters still have to stay literal: the URL is claimed anyway, or its own asterisks would
  // become delimiters and italicise the middle of it.
  it("claims a URL that a code span disqualified, so it cannot emphasise itself", () => {
    const text = "https://ex.com/*a*/" + TICK + "b" + TICK;
    assert.deepEqual(spans(text), [SPAN_KIND_CODE + ":b"]);
  });
});

describe("content offset", () => {
  it("scans a task's content, not its marker", () => {
    assert.deepEqual(spans("- [ ] **do it**", 6), [SPAN_KIND_BOLD + ":do it"]);
  });

  it("scans a heading's text", () => {
    assert.deepEqual(spans("# A *big* title", 2), [SPAN_KIND_ITALIC + ":big"]);
  });

  it("treats the content start as a boundary, so a span may open on it", () => {
    assert.deepEqual(spans("- *first*", 2), [SPAN_KIND_ITALIC + ":first"]);
  });

  it("ignores everything before the content start", () => {
    assert.deepEqual(spans("**skipped** kept", 12), []);
  });
});
