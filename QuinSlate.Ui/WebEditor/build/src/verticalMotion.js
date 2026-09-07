// Vertical caret motion across hidden markdown markers (Docs/Specs/23-MARKDOWN-FORMATTING.md).
//
// The marker reveal follows the selection's line, so the line the caret is leaving is the only one
// showing its markers and the line it is about to land on is still hiding them. CM6 measures that
// destination as it stands — it takes the position sitting at the goal x in the shortened text —
// and arriving there reveals the markers and slides the rest of the line right. A caret at
// "**bo|ld**" that goes up and comes straight back lands at "**bold**|", four characters adrift,
// and it drifts again on the next round trip.
//
// The caret only ever sits on a revealed line, so the column worth keeping is the one it has in
// the revealed text: mapping the position back through the markers the destination is still
// hiding produces exactly that.
//
// The correction is injected into CM6's own commands rather than replacing them. moveVertically is
// the single point every line and page command routes through, so a view whose moveVertically is
// wrapped fixes all eight at once and keeps the behaviour around them — collapsing a non-empty
// selection to its edge, the line-boundary fallback at the first and last line, the undirectional
// normalisation Shift motion does first, page scrolling — as CM6 defines it rather than as a fork
// of it here.

import { EditorSelection } from "@codemirror/state";
import { keymap } from "@codemirror/view";
import {
  cursorLineDown,
  cursorLineUp,
  cursorPageDown,
  cursorPageUp,
  selectLineDown,
  selectLineUp,
  selectPageDown,
  selectPageUp,
} from "@codemirror/commands";
import { markdownPlugin, isHiddenMarker } from "./markdownFormat.js";

// coordsAtPos is sub-pixel and the goal x is carried across lines, so a caret aimed at the row's
// last character can measure a hair past it. Only a clearly larger x counts as past the end.
const PAST_END_SLACK_PX = 1;

// How many characters the decorations hide between `from` and `pos`. Exported for the tests: this
// is the whole of the mapping, and it needs a DecorationSet rather than a laid-out view.
export function hiddenMarkerLengthBefore(decorations, from, pos) {
  let hidden = 0;
  decorations.between(from, pos, (rangeFrom, rangeTo, value) => {
    if (isHiddenMarker(value)) {
      hidden += Math.min(rangeTo, pos) - Math.max(rangeFrom, from);
    }
  });
  return hidden;
}

// The end of the *visual row* the position is on, which on a wrapped line is not the end of the
// logical line. Measuring the logical end instead reads the last row — often much shorter — and a
// goal x aimed into a long first row then looks past the end, skipping the correction on exactly
// the wrapped lines the spec promises it works on.
function rowEndCoords(view, range) {
  const rowEnd = view.moveToLineBoundary(EditorSelection.cursor(range.head, range.assoc), true);
  return view.coordsAtPos(rowEnd.head, -1);
}

function correct(view, moved) {
  const plugin = view.plugin(markdownPlugin);
  if (plugin == null || moved.goalColumn == null) {
    return moved;
  }

  // Past the end of the rendered row there is no column to keep, and CM6's own clamp to the row
  // end is what landing on a shorter line should do, so it stands.
  const end = rowEndCoords(view, moved);
  const contentLeft = view.contentDOM.getBoundingClientRect().left;
  if (end != null && moved.goalColumn > end.left - contentLeft + PAST_END_SLACK_PX) {
    return moved;
  }

  const line = view.state.doc.lineAt(moved.head);
  const head = moved.head - hiddenMarkerLengthBefore(plugin.decorations, line.from, moved.head);
  if (head === moved.head) {
    return moved;
  }
  return EditorSelection.cursor(head, moved.assoc, undefined, moved.goalColumn);
}

// A stand-in for the view that answers moveVertically with the corrected position and is otherwise
// the view itself. Every other member is read straight off it, and its methods stay bound to it, so
// the commands mutate and dispatch against the real view rather than this wrapper.
function correcting(view) {
  return new Proxy(view, {
    get(target, property) {
      if (property === "moveVertically") {
        return (range, forward, distance) =>
          correct(target, target.moveVertically(range, forward, distance));
      }
      const value = Reflect.get(target, property, target);
      return typeof value === "function" ? value.bind(target) : value;
    },
  });
}

function corrected(command) {
  return (view) => command(correcting(view));
}

// Registered before standardKeymap, which binds these same eight to CM6's uncorrected motion.
export const verticalMotionKeymap = keymap.of([
  { key: "ArrowUp", run: corrected(cursorLineUp), shift: corrected(selectLineUp) },
  { key: "ArrowDown", run: corrected(cursorLineDown), shift: corrected(selectLineDown) },
  { key: "PageUp", run: corrected(cursorPageUp), shift: corrected(selectPageUp) },
  { key: "PageDown", run: corrected(cursorPageDown), shift: corrected(selectPageDown) },
]);
