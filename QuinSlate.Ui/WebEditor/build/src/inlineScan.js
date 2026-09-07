// Inline markdown spans in one line's content (Docs/Specs/23-MARKDOWN-FORMATTING.md § Inline
// constructs): code, bold, italic, bold italic, strikethrough. Pure — it takes a string and
// returns offsets into it; the decorations are markdownFormat.js's job.
//
// Precedence is left to right in three passes, each skipping what the earlier ones claimed: code
// first (so a code span's asterisks and URLs stay literal), then links (so a URL's own characters
// can never act as delimiters), then emphasis.

import { findCodeSpans } from "./codeSpans.js";
import { findLinks } from "./links.js";
import {
  EMPHASIS_CHAR,
  STRIKE_CHAR,
  canClose,
  canOpen,
  runLengthAt,
} from "./runDelimiters.js";

export const SPAN_KIND_CODE = "code";
export const SPAN_KIND_BOLD = "bold";
export const SPAN_KIND_ITALIC = "italic";
export const SPAN_KIND_BOLD_ITALIC = "boldItalic";
export const SPAN_KIND_STRIKE = "strike";

const STRIKE_RUN_LENGTH = 2;

const EMPHASIS_KIND_BY_RUN = {
  1: SPAN_KIND_ITALIC,
  2: SPAN_KIND_BOLD,
  3: SPAN_KIND_BOLD_ITALIC,
};

function kindFor(char, runLength) {
  if (char === STRIKE_CHAR) {
    return runLength === STRIKE_RUN_LENGTH ? SPAN_KIND_STRIKE : null;
  }
  const kind = EMPHASIS_KIND_BY_RUN[runLength];
  return kind == null ? null : kind;
}

// The start of the run that closes an opener of `runLength`, or -1. Only an equal-length run
// closes, so "**a*" stays text; a run inside an already-claimed range (a code span, a URL) is not a
// delimiter at all.
//
// `exhausted` holds the openers already proved unclosable, and is what keeps a line linear. Every
// opener that finds no closer scans to the end of the line, so a line of unclosable runs — a list
// of shell globs, "**a " repeated — costs one full scan each: 235 ms for 38 KB, 3.5 s for 62 KB,
// paid on every keystroke *and* every caret move, because markdownFormat.js rebuilds on
// selectionSet too. A failure generalises to every later opener of the same character and length:
// no closer in [searchFrom, end) means none in any subrange of it, and claiming only ever removes
// candidates, so a later search cannot find one this search missed.
function findCloser(text, searchFrom, char, runLength, claimed, from, exhausted) {
  const key = char + runLength;
  if (exhausted.has(key)) {
    return -1;
  }
  let pos = searchFrom;
  while (pos < text.length) {
    if (claimed[pos] || text[pos] !== char) {
      pos++;
      continue;
    }
    const closeLength = runLengthAt(text, pos, char);
    if (closeLength === runLength && pos > searchFrom && canClose(text, pos, pos + closeLength, from)) {
      return pos;
    }
    pos += closeLength;
  }
  exhausted.add(key);
  return -1;
}

function claim(claimed, from, to) {
  for (let i = from; i < to; i++) {
    claimed[i] = 1;
  }
}

// Every inline span in `text` at or after `from` — the offset where the line's content starts, past
// a list item's atomic prefix or a heading's marker. `from`/`to` bound the whole span, markers
// included; `contentFrom`/`contentTo` bound the part that carries the styling.
export function scanInline(text, from = 0) {
  const claimed = new Uint8Array(text.length);
  const spans = [];
  const exhausted = new Set();

  for (const span of findCodeSpans(text, from)) {
    spans.push({
      kind: SPAN_KIND_CODE,
      from: span.from,
      to: span.to,
      contentFrom: span.contentFrom,
      contentTo: span.contentTo,
    });
    claim(claimed, span.from, span.to);
  }

  // Links are claimed but never emitted: links.js owns their decoration and their click handling.
  // Claiming them here is only so an emphasis delimiter cannot come from inside a URL.
  //
  // Claimed even where a code span already took part of the run. links.js drops such a link
  // (a URL touching a code span is not a link), and leaving the rest of the URL unclaimed here
  // would hand its own characters to the emphasis pass — "https://ex.com/*a*/`b`" would italicise
  // the middle of a URL that is not even a link. Claiming is idempotent, so the code span keeps
  // its span and the leftovers stay literal.
  for (const link of findLinks(text)) {
    if (link.from >= from) {
      claim(claimed, link.from, link.to);
    }
  }

  let pos = from;
  while (pos < text.length) {
    const char = text[pos];
    if (claimed[pos] || (char !== EMPHASIS_CHAR && char !== STRIKE_CHAR)) {
      pos++;
      continue;
    }
    const runLength = runLengthAt(text, pos, char);
    const kind = kindFor(char, runLength);
    const contentFrom = pos + runLength;
    if (kind == null || !canOpen(text, pos, contentFrom, from)) {
      pos = contentFrom;
      continue;
    }
    const contentTo = findCloser(text, contentFrom, char, runLength, claimed, from, exhausted);
    if (contentTo < 0) {
      pos = contentFrom;
      continue;
    }
    const to = contentTo + runLength;
    spans.push({ kind, from: pos, to, contentFrom, contentTo });
    // The whole span is claimed, so nothing nests inside it: a bold run containing an italic run
    // renders the outer run only (spec § Non-goals).
    claim(claimed, pos, to);
    pos = to;
  }

  spans.sort((first, second) => first.from - second.from);
  return spans;
}
