# ADR 0005: Markdown formatting scanned in-house, no markdown parser dependency

> _Last updated: 2026-09-04_

## Status
Accepted — 2026-09-04

## Context
The editor already renders three markdown-shaped constructs — checkable tasks
([19](../Specs/19-CHECKABLE-TASKS.md)), bullet and numbered lists
([20](../Specs/20-LISTS.md)), and links ([21](../Specs/21-LINKS.md)) — all
recognised by line-local regular expressions in
`QuinSlate.Ui/WebEditor/build/src/`. None of them is syntax highlighting: the
marker text stays in the buffer `.txt` and is replaced on screen by a widget or
a mark. That is the live-preview model, and it is the half of markdown support
no library provides.

[23-MARKDOWN-FORMATTING.md](../Specs/23-MARKDOWN-FORMATTING.md) adds headings,
bold, italic, strikethrough, inline code, and horizontal rules on the same
model. The question was whether recognising them justifies a markdown parser.

## Alternatives considered

**`@codemirror/lang-markdown`.** The obvious candidate: same authors as the CM6
already vendored under ADR 0004, same pinned-and-committed build, a real
incremental GFM parser, and a syntax tree the decorations could read. Two costs.
It statically imports `@codemirror/lang-html` for embedded HTML blocks, which
drags in `lang-javascript`, `lang-css`, and `@codemirror/autocomplete` — a large
addition to a 281 KB bundle for a feature explicitly out of scope. And a tree
becomes a **second authority on line identity** next to `listItems.js`
`parseItem()`, which owns item kind, depth, and the atomic-prefix contract that
`indent.js`, `tasks.js`, and `lists.js` all depend on. Reconciling two models
would be permanent overhead in every future change to any of them.

**`@lezer/markdown` directly**, wrapped in a `Language` by hand. Avoids the
HTML/JS/CSS chain and most of the bundle cost, but keeps the two-authorities
problem, and moves the module tests off pure functions: `build/test/` is
`node:test` over `findLinks(text)`-shaped helpers with `editorHarness.mjs` as a
headless `EditorView` stand-in, whereas tree assertions need a real
`EditorState` carrying a language extension.

**A WYSIWYG markdown editor** — ProseMirror + `prosemirror-markdown`, Milkdown,
TipTap, Toast UI. Not a plugin for what exists; a replacement for it. Adopting
one discards CM6 and everything built on it — `charLimit`, `inlineCalc`,
`calcHighlight`, `contentSync`, `startupReveal`'s caret hold, and the whole
`EditorHost` bridge contract — a second full editor migration within a year, to
obtain a parser. Rejected without qualification.

## Decision
Recognise the new constructs with in-house line-local scanners, adding no npm
package. `blockFormat.js` answers "what block kind is this line", `inlineScan.js`
answers "which spans does this line's content carry", and both are pure
functions over a string, tested the way `links.js` and `listItems.js` already
are.

Three things make this cheap where it would normally be reckless:

- **The scope is a subset, and its hard parts were cut.** Fenced code blocks are
  the only construct in the shortlist whose state spans lines; excluding them
  (spec § Non-goals) keeps every scanner line-local, so the existing
  `visibleRanges` scanning model carries over unchanged and no whole-document
  pass is needed at the 1,000,000-character cap.
- **Full CommonMark is not promised.** Emphasis is CommonMark's hardest
  algorithm only because of the delimiter-run flanking rules around pathological
  nesting. The spec commits to a documented subset with the failures listed as
  non-goals, exactly as [21-LINKS.md](../Specs/21-LINKS.md) did when it declined
  bare-domain detection.
- **`_` is not an emphasis delimiter** (spec § What is emphasis). Dropping it
  removes the false-positive class a scratchpad actually suffers from —
  `snake_case`, `__init__`, `file_name_v2` — and halves the delimiter table.

The four-line rule in `CLAUDE.md` — "no third-party NuGet packages unless
absolutely necessary", which `WebEditor/CLAUDE.md` extends to npm — resolves the
remaining doubt. At this scope a parser is not necessary.

## Consequences
- Zero bundle growth; `editor.bundle.js` keeps its three CM6 dependencies.
- Editing behaviour that would come free from `markdownKeymap` is not taken (it
  duplicates `listKeymap`'s Enter handling and would fight it). This costs
  nothing, because the feature is pure rendering: it registers no keymap and
  writes nothing to the document.
- The constructs the scanners refuse are refused **loudly, in the spec's
  non-goals**, not silently. If tables, fenced code, or reference links are
  wanted later, this ADR is the one to revisit — a parser's value rises sharply
  the moment any construct spans lines.
- `links.js` gains one dependency on `inlineScan.js` so a URL inside a code span
  stays literal. It is the only existing module this feature modifies.
