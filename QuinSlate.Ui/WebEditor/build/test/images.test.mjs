import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { findImages, buildImageDecorations } from "../src/images.js";
import { makeView, makeLineSelectionView } from "./editorHarness.mjs";

function wholeDoc(inner) {
  return {
    get state() {
      return inner.state;
    },
    get visibleRanges() {
      return [{ from: 0, to: inner.state.doc.length }];
    },
  };
}

function decorations(text) {
  const view = wholeDoc(makeView(text));
  const out = [];
  const cursor = buildImageDecorations(view).iter();
  while (cursor.value != null) {
    if (cursor.value.spec.widget != null) {
      out.push("image " + cursor.value.spec.widget.src + " " + JSON.stringify(cursor.value.spec.widget.alt));
    }
    cursor.next();
  }
  return out;
}

describe("findImages", () => {
  it("finds an image with empty alt text", () => {
    const text = "![](images/a.png)";
    assert.deepEqual(findImages(text), [{ from: 0, to: text.length, alt: "", src: "images/a.png" }]);
  });

  it("keeps alt text and a jpeg source", () => {
    const text = "![cat](images/cat.jpg)";
    assert.deepEqual(findImages(text), [{ from: 0, to: text.length, alt: "cat", src: "images/cat.jpg" }]);
  });

  it("finds images in the middle of a line", () => {
    const text = "before ![](images/a.png) after";
    const image = findImages(text)[0];
    assert.equal(image.from, 7);
    assert.equal(image.to, 24);
    assert.equal(image.src, "images/a.png");
  });

  it("ignores images inside code spans", () => {
    assert.deepEqual(findImages("`![](images/a.png)`"), []);
  });

  it("ignores external image sources", () => {
    assert.deepEqual(findImages("![](https://example.com/a.png)"), []);
  });

  it("ignores an image without a closing parenthesis", () => {
    assert.deepEqual(findImages("![](images/a.png"), []);
  });
});

describe("image decorations", () => {
  it("replaces the markdown with an image on a line the selection is not on", () => {
    assert.deepEqual(decorations("x\n![](images/a.png)"), ['image images/a.png ""']);
  });

  it("leaves the markdown visible on the caret's line", () => {
    assert.deepEqual(decorations("x\n|![](images/a.png)"), []);
  });

  it("reveals the selected line and keeps the other line rendered", () => {
    const view = wholeDoc(makeLineSelectionView("![](images/a.png)\n![](images/b.png)", 2, 2));
    const out = [];
    const cursor = buildImageDecorations(view).iter();
    while (cursor.value != null) {
      out.push(cursor.value.spec.widget.src);
      cursor.next();
    }
    assert.deepEqual(out, ["images/a.png"]);
  });

  it("does not decorate an image inside a code span", () => {
    assert.deepEqual(decorations("x\n`![](images/a.png)`"), []);
  });
});
