using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui.Services;

internal sealed class BackupArchiveWriter
{
    private static readonly UTF8Encoding Utf8WithBom = new UTF8Encoding(true);

    private readonly string imagesDirectory;

    public BackupArchiveWriter(string imagesDirectory)
    {
        if (imagesDirectory == null)
        {
            throw new ArgumentNullException(nameof(imagesDirectory));
        }

        this.imagesDirectory = imagesDirectory;
    }

    public async Task WriteAsync(string path, IReadOnlyList<string> bufferContents, IReadOnlyList<string> imageNames)
    {
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BackupFormat.CopyBufferSize, useAsync: true))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
        {
            for (int index = Buffer.MinIndex; index <= Buffer.MaxIndex; index++)
            {
                ZipArchiveEntry entry = archive.CreateEntry(BackupFormat.GetBufferEntryName(index), CompressionLevel.Optimal);
                await using (Stream entryStream = entry.Open())
                await using (var writer = new StreamWriter(entryStream, Utf8WithBom))
                {
                    await writer.WriteAsync(bufferContents[index - Buffer.MinIndex] ?? string.Empty).ConfigureAwait(false);
                }
            }

            foreach (string imageName in imageNames)
            {
                string sourcePath = Path.Combine(imagesDirectory, imageName);
                ZipArchiveEntry entry = archive.CreateEntry(BackupFormat.ImagesEntryPrefix + imageName, CompressionLevel.Optimal);
                await using (Stream entryStream = entry.Open())
                await using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BackupFormat.CopyBufferSize, useAsync: true))
                {
                    await sourceStream.CopyToAsync(entryStream, BackupFormat.CopyBufferSize).ConfigureAwait(false);
                }
            }

            ZipArchiveEntry manifestEntry = archive.CreateEntry(BackupFormat.ManifestEntryName, CompressionLevel.Optimal);
            await using (Stream manifestStream = manifestEntry.Open())
            {
                using var writer = new Utf8JsonWriter(manifestStream, new JsonWriterOptions { Indented = true });
                writer.WriteStartObject();
                writer.WriteString("format", BackupFormat.ManifestFormat);
                writer.WriteNumber("version", BackupFormat.ManifestVersion);
                writer.WriteString("createdUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                writer.WriteNumber("buffers", BackupFormat.BufferCount);
                writer.WriteStartArray("images");
                foreach (string imageName in imageNames)
                {
                    writer.WriteStringValue(imageName);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.Flush();
            }
        }
    }
}
