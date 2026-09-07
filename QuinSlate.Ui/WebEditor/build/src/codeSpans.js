// Backtick code spans (Docs/Specs/23-MARKDOWN-FORMATTING.md). Its own module because two callers
// need it and neither may depend on the other: inlineScan.js claims code before emphasis, and
// links.js drops URLs that fall inside a code span.
//
// Line-local, like every construct in that spec: a run never pairs across a line break. Fenced
// code blocks are the one markdown construct whose state runs down the document, and excluding
// them is what keeps this — and the whole feature — a per-line scan.

const BACKTICK = "`";

function runLengthAt(text, start) {
  let end = start;
  while (end < text.length && text[end] === BACKTICK) {
    end++;
  }
  return end - start;
}

// Every code span in one line's text at or after `from`, as offsets into that text. A span opens on
// a backtick run and closes on the next run of exactly the same length; a run that never finds its
// pair is ordinary text.
export function findCodeSpans(text, from = 0) {
  const spans = [];
  let pos = from;
  while (pos < text.length) {
    if (text[pos] !== BACKTICK) {
      pos++;
      continue;
    }
    const openLength = runLengthAt(text, pos);
    const contentFrom = pos + openLength;
    let closeFrom = -1;
    let search = contentFrom;
    while (search < text.length) {
      if (text[search] !== BACKTICK) {
        search++;
        continue;
      }
      const closeLength = runLengthAt(text, search);
      if (closeLength === openLength) {
        closeFrom = search;
        break;
      }
      search += closeLength;
    }
    if (closeFrom < 0) {
      pos = contentFrom;
      continue;
    }
    spans.push({ from: pos, to: closeFrom + openLength, contentFrom, contentTo: closeFrom });
    pos = closeFrom + openLength;
  }
  return spans;
}

// Whether [from, to) touches any of `spans`. Used to keep a URL inside a code span from becoming a
// link; overlap rather than containment, so a span that only clips the URL still suppresses it.
export function overlapsAny(spans, from, to) {
  for (const span of spans) {
    if (from < span.to && to > span.from) {
      return true;
    }
  }
  return false;
}
