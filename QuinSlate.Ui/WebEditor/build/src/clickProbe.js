// Click-coordinate probe for the "caret lands one line above the click" report
// (Docs/Investigations/07-EDITOR-CLICK-CARET-LINE-OFFSET.md). Content-free: coordinates, sizes and
// pixel deltas only, never document text.
//
// Two questions, one probe. The page logs the clientY it was handed for a press and where the
// caret was actually drawn a frame later; Components/EditorPointerProbe.cs logs the same press in
// element-relative DIPs on the host side. The pair separates the two candidate faults:
//
//   host y != page clientY  -> the WebView2 input path is offset from its rendering, which nothing
//                              page-side can see (everything the page is handed is already shifted).
//   host y == page clientY,
//   caretTopDelta past
//   -(line height)          -> the press was mapped correctly and the editor still put the caret on
//                              the wrong line.
//
// The caret is measured from the drawn `.cm-cursor-primary` rect rather than re-derived with
// posAtCoords, for two reasons. It is what the user is actually reporting — where the caret ended
// up, not where the coordinate maths says it should be. And posAtCoords forces a synchronous
// re-measure, which would hand CM6 a fresh measurement microseconds before it resolves the click:
// a probe that repairs the stale measurement it is hunting for.

import { logInformation } from "./pageLog.js";

// Presses logged per burst. The counter resets on every resize, because a resize is exactly when
// the leading hypothesis says the offset appears — a hard per-session cap would be spent on the
// first forty ordinary clicks and log nothing for the maximize that triggered the bug.
const MaxProbeEntriesPerBurst = 40;

// A resize drag fires continuously; the geometry line is only worth one entry per settled size.
const GeometrySettleMs = 400;

const PrimaryButton = 0;
const CursorSelector = ".cm-cursor-primary";

// Monotonic across the page load and printed in every entry: it pairs host and page lines
// unambiguously, and — because pageLog.js's flood guard counts occurrences of each *distinct*
// message — it also keeps clicking the same spot forty times from silencing the page side after
// ten identical entries.
let pressIndex = 0;
let entriesInBurst = 0;
let geometryTimer = null;

function round(value) {
  return Math.round(value);
}

// Where the caret is drawn, or null when no cursor is on screen (no focus, or a range selection).
export function caretTop(view) {
  const cursor = view.dom.querySelector(CursorSelector);
  return cursor == null ? null : cursor.getBoundingClientRect().top;
}

// One press. `top` is the drawn caret's top, or null. The delta is `top - y`, so a caret landing on
// the line that was clicked reads between -(line height) and 0.
export function formatProbeEntry(index, x, y, top) {
  const delta = top == null ? "n/a" : String(round(top - y));
  return "Click probe (page) #" + index + ": x=" + round(x) + " y=" + round(y) +
    " caretTopDelta=" + delta + " px.";
}

// devicePixelRatio is the one that answers WebView2Feedback#3839 (an extra, uncontrollable zoom
// factor on a small share of machines): anything other than the host's rasterization scale means
// the page is rendered at a scale the host does not know about, and every coordinate crossing the
// boundary is wrong by that ratio.
export function formatGeometryEntry(devicePixelRatio, width, height, lineHeight, contentTop) {
  return "Editor geometry (page): dpr=" + devicePixelRatio +
    " viewport=" + round(width) + "x" + round(height) + " css px" +
    " lineHeight=" + lineHeight.toFixed(1) + " px" +
    " contentTop=" + contentTop.toFixed(1) + " px.";
}

// Logged once the host has sized the WebView2 and CM6 has measured itself, and again after every
// settled resize — the event the investigation needs to correlate the offset's onset with.
export function logEditorGeometry(view) {
  if (view == null) {
    return;
  }
  logInformation(formatGeometryEntry(
    window.devicePixelRatio,
    window.innerWidth,
    window.innerHeight,
    view.defaultLineHeight,
    view.contentDOM.getBoundingClientRect().top));
}

// On scrollDOM, not contentDOM: the reserved scrollbar gutter and the strip past the end of a line
// are inside the scroller but outside the content, and the host probe sees presses there. Only a
// press on the native scrollbar itself reaches neither (Chromium dispatches nothing to the page for
// it — see scrollerFocus.js), which is why the entries carry an index rather than relying on
// adjacency to pair up.
//
// Bubble phase, so CM6 has already applied the selection; the caret rect is then read one frame
// later, once the cursor layer's own measure cycle has drawn it.
export function registerClickProbe(view) {
  view.scrollDOM.addEventListener("mousedown", (event) => {
    if (event.button !== PrimaryButton || entriesInBurst >= MaxProbeEntriesPerBurst) {
      return;
    }
    entriesInBurst++;
    pressIndex++;
    const index = pressIndex;
    const x = event.clientX;
    const y = event.clientY;
    requestAnimationFrame(() => logInformation(formatProbeEntry(index, x, y, caretTop(view))));
  });

  window.addEventListener("resize", () => {
    entriesInBurst = 0;
    if (geometryTimer != null) {
      clearTimeout(geometryTimer);
    }
    geometryTimer = setTimeout(() => logEditorGeometry(view), GeometrySettleMs);
  });
}
