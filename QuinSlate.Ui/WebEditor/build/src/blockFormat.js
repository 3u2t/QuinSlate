// Block-level markdown kinds (Docs/Specs/23-MARKDOWN-FORMATTING.md § Block constructs): a heading
// or a horizontal rule. A line has at most one, and this module is only consulted once
// listItems.js has declined the line — a task, bullet, or numbered line is an item, and a "#" in
// an item's content is text.

export const BLOCK_KIND_HEADING = "heading";
export const BLOCK_KIND_RULE = "rule";

const MAX_HEADING_LEVEL = 6;

// At column 0 and with the space required: indentation belongs to list nesting, and "#1", "#tag"
// and "#include" are what a scratchpad is actually full of. Built from MAX_HEADING_LEVEL so the cap
// lives in one place — a seventh hash is not a heading.
const HEADING_PATTERN = new RegExp("^(#{1," + MAX_HEADING_LEVEL + "}) ");

// "---" only, no "***" / "___" aliases — the same call 20-LISTS.md made against "* " / "+ "
// bullets, and it keeps "***" free for bold italic. Three or more dashes, nothing else on the line.
const RULE_PATTERN = /^-{3,}[ \t]*$/;

// The block descriptor for a line's text, or null when the line carries no block marker.
// `markerLength` is the hidden prefix for a heading and the whole line for a rule; `level` is only
// meaningful for a heading.
export function parseBlock(text) {
  if (RULE_PATTERN.test(text)) {
    return { kind: BLOCK_KIND_RULE, level: 0, markerLength: text.length };
  }
  const match = HEADING_PATTERN.exec(text);
  if (match == null) {
    return null;
  }
  return { kind: BLOCK_KIND_HEADING, level: match[1].length, markerLength: match[0].length };
}
