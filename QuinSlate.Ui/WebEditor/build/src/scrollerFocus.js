// Clicking the editor's scrollbar must not blank the caret. Chromium focuses a scrollable element
// when its scrollbar is pressed, and CM6's scroller is that element: DOM focus moves off the
// contenteditable, the view drops .cm-focused, and the caret disappears — even though the WebView2
// still holds the window's focus and the user only meant to scroll. Nothing is dispatched to the
// page for a native scrollbar press (no mousedown to preventDefault), so the move is undone after
// the fact: focus lands on the scroller, we hand it straight back to the content. The selection is
// untouched, so the caret returns exactly where it was.

import { ViewPlugin } from "@codemirror/view";

class ScrollerFocusReturn {
  constructor(view) {
    this.view = view;
    this.onFocusIn = (event) => {
      if (event.target === this.view.scrollDOM) {
        // preventScroll inside CM6's own focus(), so returning focus never yanks the view back to
        // the caret and undoes the scroll the click just performed.
        this.view.focus();
      }
    };
    this.view.scrollDOM.addEventListener("focusin", this.onFocusIn);
  }

  destroy() {
    this.view.scrollDOM.removeEventListener("focusin", this.onFocusIn);
  }
}

export const scrollerFocusPlugin = ViewPlugin.fromClass(ScrollerFocusReturn);
