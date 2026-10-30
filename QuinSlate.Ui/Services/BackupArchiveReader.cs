using QuinSlate.Ui.Constants;
using QuinSlate.Ui.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui.Services;

internal sealed class BackupArchiveReader
{
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public async Task<BackupContents> ReadAsync(string sourcePath, string stagingDirectory)
    {
        using ZipArchive archive = ZipFile.OpenRead(sourcePath);

        if (archive.Entries.Count > BackupFormat.BufferCount + BackupFormat.MaximumImageCount + 1)
        {
            throw new InvalidDataException("Too many archive entries.");
        }

        Dictionary<string, ZipArchiveEntry> entries = IndexEntries(archive);
        List<string> manifestImages = ReadManifest(entries);
        List<string> imageNames = ValidateImageNames(manifestImages, entries);
        ValidateEntrySet(entries, imageNames);

        if (GetTotalEntryBytes(entries, imageNames) > BackupFormat.MaximumArchiveBytes)
        {
            throw new InvalidDataException("Archive too large.");
        }

        IReadOnlyList<string> bufferTexts = ReadBuffers(entries);

        Directory.CreateDirectory(stagingDirectory);
        long totalImageBytes = 0;
        foreach (string imageName in imageNames)
        {
            ZipArchiveEntry entry = entries[BackupFormat.ImagesEntryPrefix + imageName];
            if (entry.Length > ImageService.MaximumBytes)
            {
                throw new InvalidDataException("Image too large.");
            }

            string stagedPath = Path.Combine(stagingDirectory, imageName);
            totalImageBytes += await ExtractEntryAsync(entry, stagedPath, ImageService.MaximumBytes).ConfigureAwait(false);
            if (totalImageBytes > BackupFormat.MaximumArchiveBytes)
            {
                throw new InvalidDataException("Archive too large.");
            }

            string detectedExtension = await DetectImageExtensionAsync(stagedPath).ConfigureAwait(false);
            if (detectedExtension == null || BackupFormat.ExtensionMatches(imageName, detectedExtension) == false)
            {
                throw new InvalidDataException("Unsupported image.");
            }
        }

        return new BackupContents(bufferTexts, imageNames);
    }

    private static Dictionary<string, ZipArchiveEntry> IndexEntries(ZipArchive archive)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entries.ContainsKey(entry.FullName))
            {
                throw new InvalidDataException("Duplicate archive entry.");
            }

            entries.Add(entry.FullName, entry);
        }

        return entries;
    }

    private static List<string> ReadManifest(Dictionary<string, ZipArchiveEntry> entries)
    {
        if (entries.TryGetValue(BackupFormat.ManifestEntryName, out ZipArchiveEntry entry) == false)
        {
            throw new InvalidDataException("Missing manifest.");
        }

        if (entry.Length <= 0 || entry.Length > BackupFormat.MaximumManifestBytes)
        {
            throw new InvalidDataException("Invalid manifest size.");
        }

        using var memory = new MemoryStream();
        using (Stream stream = entry.Open())
        {
            var buffer = new byte[BackupFormat.CopyBufferSize];
            while (true)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                if (memory.Length + read > BackupFormat.MaximumManifestBytes)
                {
                    throw new InvalidDataException("Invalid manifest size.");
                }

                memory.Write(buffer, 0, read);
            }
        }

        if (memory.Length == 0)
        {
            throw new InvalidDataException("Invalid manifest size.");
        }

        memory.Position = 0;
        using JsonDocument document = JsonDocument.Parse(memory);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Invalid manifest.");
        }

        if (ReadString(root, "format") != BackupFormat.ManifestFormat
            || ReadInt(root, "version") != BackupFormat.ManifestVersion)
        {
            throw new InvalidDataException("Unsupported backup format.");
        }

        if (ReadInt(root, "buffers") != BackupFormat.BufferCount)
        {
            throw new InvalidDataException("Wrong buffer count.");
        }

        if (root.TryGetProperty("images", out JsonElement images) == false || images.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Missing image list.");
        }

        var names = new List<string>();
        foreach (JsonElement image in images.EnumerateArray())
        {
            if (image.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Invalid image name.");
            }

            names.Add(image.GetString());
        }

        return names;
    }

    private static List<string> ValidateImageNames(IReadOnlyList<string> manifestImages, Dictionary<string, ZipArchiveEntry> entries)
    {
        if (manifestImages.Count > BackupFormat.MaximumImageCount)
        {
            throw new InvalidDataException("Too many images.");
        }

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in manifestImages)
        {
            if (BackupFormat.IsSupportedImageName(name) == false || seen.Add(name) == false)
            {
                throw new InvalidDataException("Invalid image name.");
            }

            if (entries.ContainsKey(BackupFormat.ImagesEntryPrefix + name) == false)
            {
                throw new InvalidDataException("Missing image.");
            }

            names.Add(name);
        }

        return names;
    }

    private static void ValidateEntrySet(Dictionary<string, ZipArchiveEntry> entries, IReadOnlyList<string> imageNames)
    {
        if (entries.Count != BackupFormat.BufferCount + imageNames.Count + 1)
        {
            throw new InvalidDataException("Unexpected archive entry.");
        }

        for (int index = Buffer.MinIndex; index <= Buffer.MaxIndex; index++)
        {
            if (entries.ContainsKey(BackupFormat.GetBufferEntryName(index)) == false)
            {
                throw new InvalidDataException("Missing buffer.");
            }
        }

        foreach (string imageName in imageNames)
        {
            if (entries.ContainsKey(BackupFormat.ImagesEntryPrefix + imageName) == false)
            {
                throw new InvalidDataException("Missing image.");
            }
        }
    }

    private static long GetTotalEntryBytes(Dictionary<string, ZipArchiveEntry> entries, IReadOnlyList<string> imageNames)
    {
        long total = entries[BackupFormat.ManifestEntryName].Length;
        for (int index = Buffer.MinIndex; index <= Buffer.MaxIndex; index++)
        {
            total += entries[BackupFormat.GetBufferEntryName(index)].Length;
        }

        foreach (string imageName in imageNames)
        {
            total += entries[BackupFormat.ImagesEntryPrefix + imageName].Length;
        }

        return total;
    }

    private static IReadOnlyList<string> ReadBuffers(Dictionary<string, ZipArchiveEntry> entries)
    {
        var texts = new List<string>(BackupFormat.BufferCount);
        for (int index = Buffer.MinIndex; index <= Buffer.MaxIndex; index++)
        {
            ZipArchiveEntry entry = entries[BackupFormat.GetBufferEntryName(index)];
            if (entry.Length > BackupFormat.MaximumBufferBytes)
            {
                throw new InvalidDataException("Buffer too large.");
            }

            var data = new byte[(int)entry.Length];
            using (Stream stream = entry.Open())
            {
                int offset = 0;
                while (offset < data.Length)
                {
                    int read = stream.Read(data, offset, data.Length - offset);
                    if (read == 0)
                    {
                        throw new InvalidDataException("Truncated buffer.");
                    }

                    offset += read;
                }
            }

            int start = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
            string text = StrictUtf8.GetString(data, start, data.Length - start);
            if (text.Length > AppConstants.MaxBufferLength)
            {
                throw new InvalidDataException("Buffer too long.");
            }

            texts.Add(text);
        }

        return texts;
    }

    private static async Task<long> ExtractEntryAsync(ZipArchiveEntry entry, string path, long maximumBytes)
    {
        await using Stream source = entry.Open();
        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BackupFormat.CopyBufferSize, useAsync: true);
        var buffer = new byte[BackupFormat.CopyBufferSize];
        long written = 0;

        while (true)
        {
            int read = await source.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            written += read;
            if (written > maximumBytes)
            {
                throw new InvalidDataException("Entry too large.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
        }

        return written;
    }

    private static async Task<string> DetectImageExtensionAsync(string path)
    {
        var header = new byte[BackupFormat.ImageHeaderBytes];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BackupFormat.ImageHeaderBytes, useAsync: true);
        int read = await stream.ReadAsync(header).ConfigureAwait(false);
        if (read <= 0)
        {
            return null;
        }

        byte[] data = read == header.Length ? header : header[..read];
        return ImageService.DetectExtension(data);
    }

    private static string ReadString(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        throw new InvalidDataException("Missing manifest value.");
    }

    private static int ReadInt(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int result))
        {
            return result;
        }

        throw new InvalidDataException("Missing manifest value.");
    }
}
