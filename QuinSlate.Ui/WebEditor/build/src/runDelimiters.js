// What a "*" or "~" run is allowed to do (Docs/Specs/23-MARKDOWN-FORMATTING.md § What is emphasis).
//
// Its own module for the reason codeSpans.js is: both inlineScan.js and links.js have to agree on
// these rules and neither may depend on the other — inlineScan.js already imports findLinks, so the
// dependency between those two runs one way only. inlineScan.js builds emphasis spans out of these
// predicates; links.js needs just one question answered, whether a run is still open at some point
// in the line, to decide whether a trailing delimiter closes it or belongs to the URL.

export const EMPHASIS_CHAR = "*";
export const STRIKE_CHAR = "~";

// Letters, digits and "_": what a delimiter may not touch on its outside. Anything else — a space,
// a bracket, a quote, the start of the content — is a boundary.
const WORD_CHAR = /[\p{L}\p{N}_]/u;
const WHITESPACE = /\s/;

export function runLengthAt(text, start, char) {
  let end = start;
  while (end < text.length && text[end] === char) {
    end++;
  }
  return end - start;
}

export function isBoundary(char) {
  return char == null || !WORD_CHAR.test(char);
}

export function charAt(text, index, from) {
  return index < from || index >= text.length ? null : text[index];
}

// A run opens a span when nothing word-like sits in front of it and its first content character is
// not whitespace. The outside rule is what keeps "2*3*4" arithmetic rather than emphasis — the
// inline calculator (Docs/Specs/11-INLINE-CALC.md) has to see that line intact.
export function canOpen(text, runFrom, runTo, from) {
  const inner = charAt(text, runTo, from);
  return isBoundary(charAt(text, runFrom - 1, from)) && inner != null && !WHITESPACE.test(inner);
}

export function canClose(text, runFrom, runTo, from) {
  const inner = charAt(text, runFrom - 1, from);
  return inner != null && !WHITESPACE.test(inner) && isBoundary(charAt(text, runTo, from));
}

// Tracks, per delimiter character, whether a run is still open at a given point in one line.
//
// `isOpenBefore` must be called with non-decreasing limits — findLinks walks a line left to right —
// so the scan resumes where it left off instead of restarting. Restarting would make a line of many
// URLs quadratic, which is the cost inlineScan.js's own closer search goes out of its way to avoid.
//
// Depth rather than "does this character appear earlier": a closed run is not an open one.
// "fix *urgent* — see https://host/bucket/*" must keep its wildcard, and "2 * 3 … /*" must too,
// because a lone "*" between spaces can neither open nor close.
export function openRunScanner(text) {
  const scans = new Map();

  return {
    isOpenBefore(char, limit) {
      let scan = scans.get(char);
      if (scan == null) {
        scan = { pos: 0, depth: 0 };
        scans.set(char, scan);
      }
      while (scan.pos < limit) {
        if (text[scan.pos] !== char) {
          scan.pos++;
          continue;
        }
        const runTo = scan.pos + runLengthAt(text, scan.pos, char);
        if (scan.depth > 0 && canClose(text, scan.pos, runTo, 0)) {
          scan.depth--;
        } else if (canOpen(text, scan.pos, runTo, 0)) {
          scan.depth++;
        }
        scan.pos = runTo;
      }
      return scan.depth > 0;
    },
  };
}
