import { Decoration, ViewPlugin, WidgetType } from "@codemirror/view";
import { postToHost } from "./hostBridge.js";
import { getView } from "./editorContext.js";
import { findCodeSpans, overlapsAny } from "./codeSpans.js";
import { logWarning, describeError } from "./pageLog.js";

const IMAGE_PATTERN = /!\[([^\]]*)\]\((images\/[^)\s]+)\)/g;
const IMAGE_SOURCE_PREFIX = "images/";
const IMAGE_BASE_URL = "https://quinslate.images/";
const IMAGE_CLASS = "cm-image";
const IMAGE_CONTENT_CLASS = "cm-image-content";
const IMAGE_BROKEN_CLASS = "cm-image-broken";
const SAVE_IMAGE_MESSAGE = "saveImage";
const REQUEST_ID_PREFIX = "image-";
const MAX_CHUNK_SIZE = 0x8000;

let nextRequestId = 0;
const pendingImages = new Map();

export function findImages(text) {
  const images = [];
  const codeSpans = findCodeSpans(text);
  IMAGE_PATTERN.lastIndex = 0;
  let match = IMAGE_PATTERN.exec(text);
  while (match != null) {
    const from = match.index;
    const to = from + match[0].length;
    if (!overlapsAny(codeSpans, from, to)) {
      images.push({ from, to, alt: match[1], src: match[2] });
    }
    match = IMAGE_PATTERN.exec(text);
  }
  return images;
}

function imageUrl(src) {
  const relative = src.slice(IMAGE_SOURCE_PREFIX.length);
  return IMAGE_BASE_URL + relative.split("/").map(encodeURIComponent).join("/");
}

class ImageWidget extends WidgetType {
  constructor(src, alt) {
    super();
    this.src = src;
    this.alt = alt;
  }

  eq(other) {
    return other.src === this.src && other.alt === this.alt;
  }

  toDOM() {
    const wrapper = document.createElement("span");
    wrapper.className = IMAGE_CLASS;
    wrapper.setAttribute("data-alt", this.alt || this.src.slice(IMAGE_SOURCE_PREFIX.length));
    wrapper.title = this.src.slice(IMAGE_SOURCE_PREFIX.length);

    const image = document.createElement("img");
    image.className = IMAGE_CONTENT_CLASS;
    image.src = imageUrl(this.src);
    image.alt = this.alt;
    image.draggable = false;
    image.addEventListener("error", () => {
      wrapper.classList.add(IMAGE_BROKEN_CLASS);
      image.remove();
    });

    wrapper.appendChild(image);
    return wrapper;
  }

  ignoreEvent() {
    return false;
  }
}

export function buildImageDecorations(view) {
  const marks = [];
  const state = view.state;
  const selection = state.selection.main;
  const firstRevealed = state.doc.lineAt(selection.from).number;
  const lastRevealed = state.doc.lineAt(selection.to).number;

  for (const range of view.visibleRanges) {
    let pos = range.from;
    while (pos <= range.to) {
      const line = state.doc.lineAt(pos);
      if (line.number < firstRevealed || line.number > lastRevealed) {
        for (const image of findImages(line.text)) {
          marks.push(
            Decoration.replace({
              widget: new ImageWidget(image.src, image.alt),
            }).range(line.from + image.from, line.from + image.to)
          );
        }
      }
      pos = line.to + 1;
    }
  }

  return Decoration.set(marks, true);
}

function imageFilesFrom(dataTransfer) {
  const files = [];
  if (dataTransfer == null || dataTransfer.files == null) {
    return files;
  }

  for (let i = 0; i < dataTransfer.files.length; i++) {
    const file = dataTransfer.files[i];
    if (file != null && file.type.indexOf("image/") === 0) {
      files.push(file);
    }
  }

  if (files.length > 0 || dataTransfer.items == null) {
    return files;
  }

  for (let i = 0; i < dataTransfer.items.length; i++) {
    const item = dataTransfer.items[i];
    if (item.kind === "file" && item.type.indexOf("image/") === 0) {
      const file = item.getAsFile();
      if (file != null) {
        files.push(file);
      }
    }
  }

  return files;
}

function toBase64(buffer) {
  const bytes = new Uint8Array(buffer);
  let binary = "";
  for (let offset = 0; offset < bytes.length; offset += MAX_CHUNK_SIZE) {
    binary += String.fromCharCode.apply(null, bytes.subarray(offset, offset + MAX_CHUNK_SIZE));
  }
  return btoa(binary);
}

async function saveImage(file, userEvent) {
  let base64;
  try {
    base64 = toBase64(await file.arrayBuffer());
  } catch (error) {
    logWarning("Reading an image from the clipboard or a drop failed: " + describeError(error));
    return;
  }

  const requestId = REQUEST_ID_PREFIX + (++nextRequestId);
  pendingImages.set(requestId, userEvent);
  postToHost(SAVE_IMAGE_MESSAGE, { requestId, data: base64 });
}

function saveImages(files, userEvent) {
  for (const file of files) {
    saveImage(file, userEvent);
  }
}

function handlePaste(event) {
  if (event.clipboardData == null) {
    return false;
  }

  const files = imageFilesFrom(event.clipboardData);
  if (files.length === 0) {
    return false;
  }

  event.preventDefault();
  saveImages(files, "input.paste");
  return true;
}

function handleDrop(event, view) {
  const files = imageFilesFrom(event.dataTransfer);
  if (files.length === 0) {
    return false;
  }

  event.preventDefault();
  const position = view.posAtCoords({ x: event.clientX, y: event.clientY });
  if (position != null) {
    view.dispatch({ selection: { anchor: position } });
  }
  saveImages(files, "input.drop");
  return true;
}

export function applySavedImage(message) {
  const userEvent = pendingImages.get(message.requestId);
  if (userEvent == null) {
    return;
  }

  pendingImages.delete(message.requestId);

  if (message.ok !== true || typeof message.src !== "string" || message.src.length === 0) {
    logWarning("The host could not store an image from the editor.");
    return;
  }

  const view = getView();
  if (view == null) {
    return;
  }

  view.focus();
  view.dispatch(view.state.replaceSelection("![](" + message.src + ")"), { userEvent });
}

export const imagePlugin = ViewPlugin.fromClass(
  class {
    constructor(view) {
      this.decorations = buildImageDecorations(view);
    }

    update(update) {
      if (update.docChanged || update.viewportChanged || update.selectionSet) {
        this.decorations = buildImageDecorations(update.view);
      }
    }
  },
  {
    decorations: (value) => value.decorations,
    eventHandlers: {
      paste: handlePaste,
      drop: handleDrop,
    },
  }
);
