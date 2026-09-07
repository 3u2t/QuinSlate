# Markdown formatting

> _Last updated: 2026-09-04_

The fourth formatting feature of the CodeMirror editor, alongside
[checkable tasks](19-CHECKABLE-TASKS.md), [lists](20-LISTS.md), and
[links](21-LINKS.md): **headings, bold, italic, strikethrough, inline code, and
horizontal rules** render as formatting while their markdown markers stay in the
buffer text. No toolbar, no formatting shortcuts, no UI chrome.

Like links and unlike tasks and lists, this feature **writes nothing to the
document and registers no keymap**. It is entirely a rendering pass: markers are
hidden by replace decorations and the text between them is marked. The buffer
`.txt` is byte-for-byte what it would be without the feature, copy and paste
carry the markers, and undo has nothing extra to unwind.

Everything lives page-side in the web editor bundle — three pure scanners
(`build/src/blockFormat.js`, `build/src/codeSpans.js`, `build/src/inlineScan.js`),
one view plugin (`build/src/markdownFormat.js`), and `editor.css`. No host or
bridge changes.
The choice not to add a markdown parser is
[ADR 0005](../Decisions/05-MARKDOWN-IN-HOUSE-SCANNER.md).

## Text representation

- Markers are ordinary characters in the document. They are what lands in the
  buffer `.txt`, what copy/paste produces, and what a reload recognises. There
  is no formatting state outside the document text.
- Recognition applies anywhere a marker appears — typed, loaded from disk,
  pasted, or restored by undo — and re-runs from the line's text on every
  change.
- Every construct below is **line-local**: nothing spans a line break. This is
  why fenced code blocks are excluded (see Non-goals) — they are the one
  construct in this family whose state runs down the document, and without them
  the existing per-line `visibleRanges` scan carries the whole feature.
- Markers count toward the character cap
  ([18-CHARACTER-LIMIT-NOTICE.md](18-CHARACTER-LIMIT-NOTICE.md)) like any other
  text.

## Block constructs

A line has at most one block kind. `blockFormat.js` answers which, and it is
consulted **after** `listItems.js`: a task, bullet, or numbered line is an item,
and a heading marker inside an item's content is just text.

| Kind | Marker | Rendered |
|---|---|---|
| Heading | `# ` to `###### ` — one to six `#`, then one space, at column 0 | Larger, bold text; the hashes and their space are hidden |
| Horizontal rule | A line whose entire content is three or more `-` (trailing spaces allowed) | A drawn 1px rule across the content width; the dashes are hidden |

- A heading marker must be at **column 0** — no leading spaces. Indentation
  belongs to list nesting ([19 § Nesting](19-CHECKABLE-TASKS.md#nesting)), and
  an indented hash in a scratchpad is far more often an outline marker or a
  comment than a heading.
- The space after the hashes is required, so `#1`, `#tag`, and `#include` are
  text. This is the same discipline [21-LINKS.md](21-LINKS.md) applies in
  requiring an explicit URL scheme: in a notes app a false positive costs more
  than a construct the user has to write out fully.
- **`---` only for the rule** — no `***` or `___` aliases, matching
  [20-LISTS.md](20-LISTS.md)'s "no `* ` / `+ ` bullet aliases". Dropping `***`
  also removes its collision with bold-italic outright.
- A rule line does not collide with a bullet: a bullet marker is `- ` (dash then
  space), which `---` is not.

## Inline constructs

`inlineScan.js` scans one line's **content** — the text after any list item's
atomic prefix, or after a heading's marker — and returns the spans it claims.

| Kind | Marker | Rendered |
|---|---|---|
| Inline code | backtick-wrapped text | Tinted background behind the text |
| Bold | `**text**` | `font-weight: 700` |
| Italic | `*text*` | `font-style: italic` |
| Bold italic | `***text***` | Both |
| Strikethrough | `~~text~~` | `line-through` |

Headings carry inline spans in their text; so does a task's or list item's
content. Formatting inside formatting is not supported beyond the `***`
combination (see Non-goals).

### What is emphasis

Three rules keep emphasis out of text that is not emphasis. All three are
deliberate narrowings of CommonMark.

1. **`*` is the only emphasis delimiter — never `_`.** A scratchpad is full of
   `snake_case`, `__init__`, and `file_name_v2`, and every one of them would
   emphasise. This is the same judgement [21-LINKS.md](21-LINKS.md) made against
   bare-domain link detection, for the same reason.
2. **Delimiters may not touch whitespace on the inside.** `* not italic *` is
   text; `*italic*` is not. An unclosed or empty run (`**`, `****`) is text.
3. **Delimiters may not touch a word character on the outside.** An opening
   delimiter must sit at the start of the content or after whitespace or
   punctuation; a closing delimiter must be followed by end-of-content,
   whitespace, or punctuation.

Rule 3 is what protects the inline calculator
([11-INLINE-CALC.md](11-INLINE-CALC.md)). `2*3*4 =` is arithmetic a user really
types, and under CommonMark's intraword rules for `*` it would render with an
italic `3`. Here the first `*` follows the word character `2`, so it never
opens, and calc sees the line it expects. `(*aside*)` still works, because `(`
is punctuation.

Strikethrough follows rules 2 and 3 with `~~`.

### Precedence

Within a line's content, spans are claimed left to right in this order, and each
pass skips characters already claimed:

1. **Inline code**, matched between equal-length backtick runs. Code wins over
   everything: a code span containing `**` shows the asterisks, and a URL inside
   a code span is not a link.
2. **Links** ([21-LINKS.md](21-LINKS.md)), so a URL's own characters can never
   act as emphasis delimiters.
3. **Strikethrough and emphasis**, outermost run first (`***`, then `**`, then
   `*`).

The inline-calc result highlight
([12-CALC-RESULT-ANIMATION.md](12-CALC-RESULT-ANIMATION.md)) is a transient mark
over a range and layers on top of all of this; it is not part of the ordering.

`links.js` is the one existing module this feature changes, in two places. It
filters its matches against the claimed code spans, and its trailing-punctuation
trim now also drops `*` and `~` — a URL match runs to the next whitespace, so
without that trim `**see https://example.com**` swallowed its own closing
delimiters into the href and the bold run never closed. Only a trailing
delimiter is dropped; an asterisk inside a path survives, exactly as the
sentence punctuation rule already worked. See
[21-LINKS.md § Where a link ends](21-LINKS.md#where-a-link-ends).

## Marker visibility

**Markers are hidden, and revealed on the line the selection is on.**

| State | `**Ship Friday**` renders as |
|---|---|
| Selection elsewhere | **Ship Friday** |
| Selection anywhere on this line | `**Ship Friday**` — asterisks dimmed, text still bold |

- The unit is the **whole line**, not proximity to the caret. Character-level
  proximity flickers markers in and out as the user arrows along a line;
  line-level reveal changes at most once per vertical move.
- A selection spanning several lines reveals **every line it touches**.
- A revealed **rule** shows its dimmed dashes and nothing else. The line is all
  marker, so unlike a bold run there is no content left to keep formatted while
  it is being edited.
- An **empty heading** (`## ` with no text yet) keeps its marker on screen
  wherever the selection is. Hiding it would leave a line that is blank but
  still heading-tall, with nothing to say anything is there; a task or bullet in
  the same state still shows its widget, and a heading has none.
- Reveal follows the selection whether or not the editor has focus, so an
  unfocused editor and a focused one render identically. On load the selection
  sits at position 0, so the first line shows its markers.
- Dimming the revealed markers rather than showing them at full strength keeps
  the line reading as formatted while it is being edited.

This rule is what makes the feature safe to build without atomic ranges. **The
caret is never inside a hidden range**, because arriving on a line reveals it —
so there is no hidden prefix to step over, no Backspace that must delete a
marker whole, and none of the caret-mapping machinery that tasks and lists need
for their atomic prefixes. The one visible consequence is that a formatted line
shifts horizontally as the selection enters and leaves it. That is accepted: the
shift happens on the line the user is already looking at, and it is the same
trade Obsidian and Bear make.

### Vertical motion across that shift

Arrow Up/Down must land on the column the caret visually had. CM6 measures the
destination line **before** the move, while it is still hiding its markers, and
takes the position sitting at the goal x in that shortened text; arriving then
reveals the markers and slides the rest of the line right. Uncorrected, a caret
at `**bo|ld**` that goes up and comes straight back lands at `**bold**|` — four
characters adrift, and adrift again on the next round trip.

`verticalMotion.js` maps the position back through the markers the destination
is still hiding. The caret only ever sits on a revealed line, so the column worth
keeping is the one it has in the revealed text, and that mapping produces exactly
it. Three rules complete it:

- Only the markers **this** feature hides are counted. List and task prefixes
  are replaced whether or not the line is revealed, so their width is already in
  the position CM6 hands back; `markdownFormat.js` tags its own replace
  decorations and `isHiddenMarker` is the test.
- When the goal x is **past the end of the rendered visual row** there is no
  column to keep, and CM6's own clamp to the row end is what landing on a
  shorter line should do, so it stands unchanged. The *visual row*, not the
  logical line: with line wrapping on, the end of a wrapped line is its last
  row, which is usually much shorter than the row the caret is landing on, and
  measuring that instead skips the correction on exactly the wrapped lines it is
  needed for.
- The correction is **injected into CM6's own commands**, not substituted for
  them. `moveVertically` is the one point all eight line and page commands route
  through, so `verticalMotion.js` binds Arrow Up/Down and Page Up/Down ahead of
  `standardKeymap` and hands them a view whose `moveVertically` is wrapped.
  Everything around it — collapsing a non-empty selection to its edge, the
  line-boundary fallback at the first and last line, the undirectional
  normalisation Shift motion does first, page scrolling — stays CM6's, rather
  than a fork of it that drifts.

The goal x rides along on the selection in every case, so a column held through
several presses stays held — including through a heading, a rule and a wrapped
line, none of which the correction treats specially.

## Rendering

Metrics are set against the editor's 15px / 1.6 Cascadia Code grid
(`editorTheme.js`).

| Class | Style |
|---|---|
| `.cm-md-h1` … `.cm-md-h6` | 22px / 19px / 17px / 15px / 15px / 15px, all `font-weight: 700`, `line-height: 1.3` |
| `.cm-md-bold` | `font-weight: 700` |
| `.cm-md-italic` | `font-style: italic` |
| `.cm-md-strike` | `text-decoration: line-through` |
| `.cm-md-code` | Background from `--text` at low alpha, `border-radius: 2px` |
| `.cm-md-rule` | Drawn 1px line, `--text` at 55%, full content width, zero-height box |
| `.cm-md-marker` | `opacity: 0.45`, contextual alternates off — the revealed markers |

- **Headings flatten past H3.** H4–H6 are recognised (so a pasted document shows
  no raw `####`) but render at body size with weight only. A panel this size has
  no room for six legible tiers, and inventing them would produce sizes that
  read as noise rather than hierarchy.
- Headings override the theme's `line-height: 1.6`. At 22px the inherited ratio
  opens a gap wide enough to read as a paragraph break.
- **Inline code gets background only — no horizontal padding.** Padding on an
  inline mark shifts every glyph after it off the monospace grid, and CM6
  measures selection rectangles from computed line padding (see the `.cm-line`
  comment in `editorTheme.js`), so it would skew selection bands too. The editor
  is already monospace, so the tint carries the whole signal a font change would
  otherwise provide.
- Italic depends on the font stack having an italic face. Cascadia Code ships
  one on Windows 11; where it is missing, Chromium synthesises an oblique. Both
  are acceptable and neither needs detecting.
- Strikethrough is `line-through`, which is **not** how a completed task is
  drawn: `.cm-task-done` dims with `opacity` by deliberate choice
  ([19-CHECKABLE-TASKS.md](19-CHECKABLE-TASKS.md)). A struck run inside a done
  task shows both, and that is correct — they mean different things.
- **Markers switch contextual alternates off** (`font-feature-settings: "calt" 0,
  "liga" 0`). Cascadia Code ligates `(*` and `*)`, and Chromium shapes ligatures
  across inline element boundaries, so a revealed `(*` was shaped as one glyph pair
  spanning the plain `(` and the marker span — the asterisk painted into the wrong
  run, and hiding the marker again left the glyph behind, overlapping the first
  content character until the buffer was reloaded. Breaking the shaping run at the
  boundary that moves is the whole fix; ligatures elsewhere in the buffer are
  untouched.
- The rule is a drawn element rather than a repeated glyph, matching
  `.cm-list-bullet`'s drawn dot. Its box is **zero height** — the border is the
  box — and lifted off the baseline with `vertical-align`. An empty inline-block
  takes its baseline from its bottom margin edge, so any height or top margin on
  it is claimed entirely above the baseline and grows the line box; a `---` line
  then stands taller than its neighbours, the inflation `.cm-list-number`
  documents. A 1px box sits inside the line's ascent and costs nothing.
- All colours derive from `--text`, so light and dark are covered by the
  existing theme message with no new host-side variable.

## Structure and testing

Pure functions carry the recognition, in the shape `build/test/` already tests
(`node:test`, no framework, `editorHarness.mjs` where a view is needed):

- `blockFormat.js` — `parseBlock(text)` returns the heading level and marker
  length, the rule, or null.
- `codeSpans.js` — `findCodeSpans(text, from)` and `overlapsAny(spans, from, to)`.
  Its own module because both `inlineScan.js` and `links.js` need it and neither
  may depend on the other.
- `runDelimiters.js` — what a `*` or `~` run may do: `canOpen`/`canClose` and the
  character tests under them, plus `openRunScanner(text)`, whose `isOpenBefore`
  says whether a run is still open at a point in the line. Its own module for the
  same reason `codeSpans.js` is. `inlineScan.js` builds spans from the predicates;
  `links.js` asks only `isOpenBefore`, to decide whether a trailing delimiter
  closes a run or belongs to the URL ([21-LINKS.md](21-LINKS.md)).
- `inlineScan.js` — `scanInline(text, from)` returns the claimed spans as
  `{ kind, from, to, contentFrom, contentTo }`, with the precedence above
  applied. `from`/`to` bound the whole span, markers included; the inner pair
  bounds the part that carries the styling, so the two marker ranges follow
  without a separate list. An opener that finds no closer is remembered for the
  rest of the scan: without that, a line of unclosable runs — shell globs, `**`
  repeated — costs a full scan per opener, which measured 235 ms at 38 KB and
  3.5 s at 62 KB, paid on every keystroke *and* every caret move.
- `markdownFormat.js` — the `ViewPlugin` that walks `visibleRanges`, calls all
  three, and builds the decorations, plus the selection-line reveal.
  `buildDecorations` is exported for the tests: it reads only `state` and
  `visibleRanges`, so `editorHarness.mjs` plus one range covering the document
  drives it exactly as the plugin does, and the decorations are read back off
  the returned set.
- `verticalMotion.js` — `hiddenMarkerLengthBefore(decorations, from, pos)` counts
  the characters the decorations hide before a position. It is the whole of the
  vertical-motion correction and takes a `DecorationSet` rather than a view, so
  `buildDecorations` feeds it the same set the plugin renders. The measuring
  around it is DOM geometry and is verified in the app, not here.

Cases the tests must cover, at minimum: a hash without a following space; a
seven-hash run; an indented hash; `---` versus `- ` versus `--`; `*` adjacent to
whitespace on either side; `2*3*4`; `snake_case_name`; `**` inside a code span;
a URL inside a code span; a URL a code span disqualified, whose own characters
must still not emphasise; an unclosed `**`; `***both***`; and inline spans
inside a heading and inside a task's content. For the plugin: the reveal window
for a caret and for a multi-line selection, the heading line decoration, the
empty-heading marker, the rule's revealed and hidden forms, and that a hash in
a list item's content is not a heading. For the vertical-motion mapping: a
position before, inside and past a marker; a heading's hashes; a rule; markers
inside a list item but not the item's own prefix; and zero on a revealed line.
For the run scanner: open, closed again, and an asterisk that can neither open
nor close; the two delimiter characters tracked apart; and that repeated calls
resume rather than restart.

## Non-goals (for now)

- **No fenced code blocks** and no indented code blocks. They are the only
  construct here whose state spans lines, and excluding them is what keeps every
  scanner line-local — see
  [ADR 0005](../Decisions/05-MARKDOWN-IN-HOUSE-SCANNER.md). Revisit that ADR
  before adding them.
- No `_` or `__` emphasis, and no intraword emphasis (see What is emphasis).
- No tables, blockquotes, images, footnotes, reference links, `[text](url)` link
  syntax, HTML blocks, setext headings, angle-bracket autolinks, or backslash
  escapes.
- No formatting keyboard shortcuts (Ctrl+B / Ctrl+I) and no toolbar. Those write
  to the document, which this feature otherwise never does; adding them is a
  separate change with its own selection and undo behaviour.
- No nested emphasis beyond `***` — a bold run containing an italic run renders
  the outer run only.
- No folding, outline view, or heading-based navigation.
- No export or rendered-preview mode. The buffer is a `.txt` file and stays one.
