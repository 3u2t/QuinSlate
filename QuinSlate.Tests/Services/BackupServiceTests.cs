using QuinSlate.Ui.Models;
using QuinSlate.Ui.Services;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace QuinSlate.Tests.Services;

public sealed class BackupServiceTests : IDisposable
{
    private static readonly byte[] PngBytes =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03,
    };

    private static readonly byte[] JpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };

    private static readonly string[] BufferContents = { "one", "two", "three", "four", "five" };

    private readonly string tempDirectory;
    private readonly string appDataDirectory;
    private readonly string imagesDirectory;
    private readonly BackupService backupService;

    public BackupServiceTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        appDataDirectory = Path.Combine(tempDirectory, "app");
        imagesDirectory = Path.Combine(appDataDirectory, "images");
        Directory.CreateDirectory(imagesDirectory);
        backupService = new BackupService(appDataDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            try
            {
                Directory.Delete(tempDirectory, true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task ExportAsync_WritesAllBuffersAndImages()
    {
        File.WriteAllBytes(Path.Combine(imagesDirectory, "a.png"), PngBytes);
        string backupPath = Path.Combine(tempDirectory, "backup.quinslate");

        BackupResult result = await backupService.ExportAsync(backupPath, BufferContents);

        Assert.True(result.Success);
        Assert.Equal(5, result.BufferCount);
        Assert.Equal(1, result.ImageCount);

        using ZipArchive archive = ZipFile.OpenRead(backupPath);
        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.NotNull(archive.GetEntry("images/a.png"));
        Assert.Equal(BufferContents[0], ReadTextEntry(archive, "buffers/buffer-1.txt"));
        Assert.Equal(BufferContents[4], ReadTextEntry(archive, "buffers/buffer-5.txt"));
    }

    [Fact]
    public async Task ExportAsync_RejectsAnIncompleteBufferSet()
    {
        string backupPath = Path.Combine(tempDirectory, "backup.quinslate");

        BackupResult result = await backupService.ExportAsync(backupPath, new[] { "one" });

        Assert.False(result.Success);
        Assert.False(File.Exists(backupPath));
    }

    [Fact]
    public async Task ImportAsync_RestoresBuffersAndImages()
    {
        string sourcePath = Path.Combine(tempDirectory, "source.quinslate");
        File.WriteAllBytes(Path.Combine(imagesDirectory, "source.png"), PngBytes);
        await backupService.ExportAsync(sourcePath, BufferContents);

        File.Delete(Path.Combine(imagesDirectory, "source.png"));
        File.WriteAllBytes(Path.Combine(imagesDirectory, "stale.png"), PngBytes);
        File.WriteAllText(Path.Combine(appDataDirectory, "buffer-1.txt"), "old");

        BackupResult result = await backupService.ImportAsync(sourcePath, new[] { "old", "old", "old", "old", "old" });

        Assert.True(result.Success);
        Assert.Equal(BufferContents[0], File.ReadAllText(Path.Combine(appDataDirectory, "buffer-1.txt")));
        Assert.True(File.Exists(Path.Combine(imagesDirectory, "source.png")));
        Assert.False(File.Exists(Path.Combine(imagesDirectory, "stale.png")));
        Assert.Single(Directory.GetFiles(Path.Combine(appDataDirectory, "backups"), "pre-import-*.quinslate"));
    }

    [Fact]
    public async Task ImportAsync_AcceptsJpegImages()
    {
        string sourcePath = CreateArchive("jpeg.quinslate", archive =>
        {
            WriteManifest(archive, "photo.jpg");
            WriteBuffers(archive);
            WriteBytesEntry(archive, "images/photo.jpg", JpegBytes);
        });

        BackupResult result = await backupService.ImportAsync(sourcePath, BufferContents);

        Assert.True(result.Success);
        Assert.Equal(1, result.ImageCount);
        Assert.True(File.Exists(Path.Combine(imagesDirectory, "photo.jpg")));
    }

    [Fact]
    public async Task ImportAsync_RejectsAFileThatIsNotAZip()
    {
        string path = Path.Combine(tempDirectory, "not-a-backup.quinslate");
        File.WriteAllText(path, "hello");

        BackupResult result = await backupService.ImportAsync(path, BufferContents);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ImportAsync_RejectsAnArchiveWithAMissingBuffer()
    {
        string path = CreateArchive("missing-buffer.quinslate", archive =>
        {
            WriteManifest(archive);
            for (int index = 1; index <= 4; index++)
            {
                WriteTextEntry(archive, "buffers/buffer-" + index + ".txt", BufferContents[index - 1]);
            }
        });

        BackupResult result = await backupService.ImportAsync(path, BufferContents);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ImportAsync_RejectsAnUnexpectedEntry()
    {
        string path = CreateArchive("unexpected.quinslate", archive =>
        {
            WriteManifest(archive);
            WriteBuffers(archive);
            WriteTextEntry(archive, "extra.txt", "extra");
        });

        BackupResult result = await backupService.ImportAsync(path, BufferContents);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ImportAsync_RejectsAnUnsafeImageName()
    {
        string path = CreateArchive("unsafe-name.quinslate", archive =>
        {
            WriteManifest(archive, "../evil.png");
            WriteBuffers(archive);
            WriteBytesEntry(archive, "images/../evil.png", PngBytes);
        });

        BackupResult result = await backupService.ImportAsync(path, BufferContents);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ImportAsync_RejectsAnImageWhoseSignatureDoesNotMatch()
    {
        string path = CreateArchive("bad-image.quinslate", archive =>
        {
            WriteManifest(archive, "bad.png");
            WriteBuffers(archive);
            WriteTextEntry(archive, "images/bad.png", "not an image");
        });

        BackupResult result = await backupService.ImportAsync(path, BufferContents);

        Assert.False(result.Success);
        Assert.False(File.Exists(Path.Combine(imagesDirectory, "bad.png")));
    }

    [Fact]
    public async Task ImportAsync_RejectsInvalidUtf8InABuffer()
    {
        string path = CreateArchive("bad-utf8.quinslate", archive =>
        {
            WriteManifest(archive);
            for (int index = 1; index <= 5; index++)
            {
                if (index == 3)
                {
                    WriteBytesEntry(archive, "buffers/buffer-" + index + ".txt", new byte[] { 0xFF, 0xFE, 0xFD });
                }
                else
                {
                    WriteTextEntry(archive, "buffers/buffer-" + index + ".txt", BufferContents[index - 1]);
                }
            }
        });

        BackupResult result = await backupService.ImportAsync(path, BufferContents);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ImportAsync_KeepsTheThreeNewestSafetyBackups()
    {
        string sourcePath = Path.Combine(tempDirectory, "source.quinslate");
        await backupService.ExportAsync(sourcePath, BufferContents);

        for (int i = 0; i < 4; i++)
        {
            BackupResult result = await backupService.ImportAsync(sourcePath, BufferContents);
            Assert.True(result.Success);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        string[] backups = Directory.GetFiles(Path.Combine(appDataDirectory, "backups"), "pre-import-*.quinslate");
        Assert.Equal(3, backups.Length);
    }

    private string CreateArchive(string fileName, Action<ZipArchive> writeEntries)
    {
        string path = Path.Combine(tempDirectory, fileName);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        writeEntries(archive);
        return path;
    }

    private static void WriteManifest(ZipArchive archive, params string[] imageNames)
    {
        ZipArchiveEntry entry = archive.CreateEntry("manifest.json");
        using Stream stream = entry.Open();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("format", "quinslate-backup");
        writer.WriteNumber("version", 1);
        writer.WriteString("createdUtc", DateTimeOffset.UtcNow.ToString("O"));
        writer.WriteNumber("buffers", 5);
        writer.WriteStartArray("images");
        foreach (string imageName in imageNames)
        {
            writer.WriteStringValue(imageName);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteBuffers(ZipArchive archive)
    {
        for (int index = 1; index <= 5; index++)
        {
            WriteTextEntry(archive, "buffers/buffer-" + index + ".txt", BufferContents[index - 1]);
        }
    }

    private static void WriteTextEntry(ZipArchive archive, string name, string text)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(true));
        writer.Write(text);
    }

    private static void WriteBytesEntry(ZipArchive archive, string name, byte[] data)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using Stream stream = entry.Open();
        stream.Write(data, 0, data.Length);
    }

    private static string ReadTextEntry(ZipArchive archive, string name)
    {
        ZipArchiveEntry entry = archive.GetEntry(name);
        using Stream stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
