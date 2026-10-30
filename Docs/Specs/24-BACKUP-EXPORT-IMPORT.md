# Backup export and import

> _Last updated: 2026-10-30_

The slates are plain files in `%AppData%\QuinSlate\`, which makes them easy to
read but awkward to carry around: five `.txt` files and an `images\` folder have
to be copied together or the pictures in a slate break. This feature packages
everything into a single `.quinslate` file and adds two buttons to the About
card — **Export backup…** and **Import backup…** — so the archive can be dropped
in OneDrive, a USB stick, or any other machine and restored in one step.

`settings.json` stays out of the backup on purpose. It holds window geometry,
theme, pin state, tab names, and recent emoji — machine-local preferences rather
than content. A restore replaces slates and images only; the receiving app keeps
its own settings.

## Archive format

A `.quinslate` file is an ordinary ZIP:

```
manifest.json
buffers/buffer-1.txt
buffers/buffer-2.txt
buffers/buffer-3.txt
buffers/buffer-4.txt
buffers/buffer-5.txt
images/<name>.<ext>
```

`manifest.json` is the version marker and the authoritative image list:

```json
{
  "format": "quinslate-backup",
  "version": 1,
  "createdUtc": "2026-10-30T12:34:56.7890000+00:00",
  "buffers": 5,
  "images": ["20261030-142233-ab12.png"]
}
```

Buffer entries are written as UTF-8 with a BOM, the same encoding the app uses
for the live files. A slate that has never been written still gets an entry, so
every archive carries all five and an import can always replace all five. Images
are copied byte-for-byte; their names already are the app's generated
`yyyyMMdd-HHmmss-xxxx.<ext>` form.

## Export

The picker suggests `QuinSlate-<timestamp>.quinslate` in Documents. Before the
archive is written, the editor is asked to flush pending keystrokes through the
normal content-sync path, and the buffer texts are read from `BufferService`.
Images are enumerated from the `images\` folder, filtered to the formats the
editor accepts (PNG, JPEG, GIF, WebP, BMP), and sorted by name so the manifest is
stable.

The archive is built at `<destination>.tmp` and renamed over the destination
only after the ZIP is closed, so a failed or interrupted export never leaves a
half-written `.quinslate` where a previous good one used to be.

## Import

Import is destructive, so it validates first and changes nothing until the whole
archive has passed:

1. Open the ZIP and reject duplicate entry names.
2. Read and validate the manifest: format marker, version, buffer count, and an
   image list of strings.
3. Validate every image name: no path separators, no invalid filename
   characters, a supported extension, and no case-insensitive duplicates. Every
   listed image must have a matching entry.
4. Require the entry set to be exactly the manifest, five buffers, and the listed
   images — no extras, no omissions.
5. Cap the archive at 1 GB of uncompressed entries and each buffer at the
   character limit already enforced by the editor. Buffers are decoded as strict
   UTF-8 (an optional BOM is skipped); invalid bytes fail the import.
6. Extract images to a staging folder under `backup-staging\<guid>\`, enforcing
   the 40 MB per-image limit while copying, and check each file's magic bytes
   against its extension.

Before the commit starts, the editor is flushed and any debounced buffer write
is forced to disk, so a late autosave cannot land on top of the restored files.

Only then does the import touch live data. It first writes a safety backup of
the current state to `backups\pre-import-<timestamp>.quinslate`; if that fails,
the import is cancelled. The three newest safety backups are kept and older ones
are pruned. Images move from staging into `images\`, buffers are written through
`AtomicFileWriter`, and images no longer referenced by the manifest are deleted.

After a successful import the panel reloads the buffers from disk and reloads
the WebView page. The reload re-initialises all five editor states from the new
text, and content-sync messages arriving from the outgoing page are suppressed
until the new page reports ready. Image URLs carry a per-page-load cache token,
so a restored image that replaced an older file with the same name is fetched
fresh instead of coming out of the WebView2 cache.

Cancelling the picker or the confirmation dialog changes nothing. Errors are
reported in a dialog with a single message; the technical detail goes to the log,
not the UI.

## UI

The data card in About gains a second button row under Open folder / Copy path.
Both buttons disable while an operation runs so a second picker cannot open on
top of the first. The card grows from 440 to 490 DIP to fit the row.

The pickers are `Microsoft.Windows.Storage.Pickers.FileSavePicker` and
`FileOpenPicker`, constructed with the About window's `WindowId` so they are
owned by the dialog rather than the main window. Dialogs use the About view's
`XamlRoot`.

## Structure

| File | Role |
|---|---|
| `Models/BackupResult.cs` | Success flag, buffer/image counts, user-facing error |
| `Models/BackupContents.cs` | Validated buffer texts and image names read from an archive |
| `Services/BackupFormat.cs` | Constants, entry names, filename and extension rules |
| `Services/BackupArchiveWriter.cs` | Writes manifest, buffers, and images into the ZIP |
| `Services/BackupArchiveReader.cs` | Validates an archive and stages its images |
| `Services/BackupService.cs` | Export/import orchestration, safety backups, pruning, commit |
| `Components/BackupController.cs` | Pickers, confirmation, result dialogs, busy state |

The archive logic is split into reader, writer, and format helpers to keep each
class under the project's 500-line ceiling. No third-party package is involved;
`System.IO.Compression` and `System.Text.Json` are in the framework.

`ImageService.MaximumBytes` and `ImageService.DetectExtension` are `internal` so
the reader can apply the same limits and signature checks as the paste path.
`BufferService.BufferFileNameFormat` is `internal` for the same reason.

## Testing

`QuinSlate.Tests/Services/BackupServiceTests.cs` covers a full export/import
round trip, stale-image removal, JPEG acceptance, rejection of a non-ZIP file, a
missing buffer, an unexpected entry, a path-traversal image name, a file whose
signature contradicts its extension, invalid UTF-8 in a buffer, and retention of
only the three newest safety backups. The flush acknowledgement is covered by
`build/test/hostMessages.test.mjs`.

## Non-goals

- No merge or per-slate import. Import replaces all five slates and the image
  folder, which is what makes the result predictable.
- No scheduled or automatic cloud upload. The file is meant to be handed to
  whatever sync tool the user already has.
- No backup of `settings.json`, logs, or the WebView2 profile.
- No password protection or encryption. A `.quinslate` is a plain ZIP; users who
  need encryption should put it in an encrypted container.
