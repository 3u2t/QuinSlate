using QuinSlate.Ui.Services;

namespace QuinSlate.Tests.Services;

public sealed class ImageServiceTests : IDisposable
{
    private static readonly byte[] PngBytes =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03,
    };

    private static readonly byte[] JpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };

    private static readonly byte[] GifBytes = { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x00 };

    private static readonly byte[] WebpBytes =
    {
        0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50,
    };

    private static readonly byte[] BmpBytes = { 0x42, 0x4D, 0x00, 0x00 };

    private readonly string tempDirectory;
    private readonly ImageService imageService;

    public ImageServiceTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        imageService = new ImageService(tempDirectory);
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
    public void Constructor_CreatesImagesDirectory()
    {
        Assert.True(Directory.Exists(Path.Combine(tempDirectory, ImageService.FolderName)));
    }

    [Fact]
    public async Task SaveAsync_WritesPngAndReturnsRelativeSource()
    {
        string src = await imageService.SaveAsync(PngBytes);

        Assert.NotNull(src);
        Assert.StartsWith("images/", src);
        Assert.EndsWith(".png", src);

        string fileName = src.Substring("images/".Length);
        string path = Path.Combine(imageService.DirectoryPath, fileName);
        Assert.True(File.Exists(path));
        Assert.Equal(PngBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task SaveAsync_RecognisesEverySupportedFormat()
    {
        Assert.EndsWith(".png", await imageService.SaveAsync(PngBytes));
        Assert.EndsWith(".jpg", await imageService.SaveAsync(JpegBytes));
        Assert.EndsWith(".gif", await imageService.SaveAsync(GifBytes));
        Assert.EndsWith(".webp", await imageService.SaveAsync(WebpBytes));
        Assert.EndsWith(".bmp", await imageService.SaveAsync(BmpBytes));
    }

    [Fact]
    public async Task SaveAsync_RejectsUnknownFormat()
    {
        Assert.Null(await imageService.SaveAsync(new byte[] { 0x01, 0x02, 0x03 }));
    }

    [Fact]
    public async Task SaveAsync_RejectsEmptyPayload()
    {
        Assert.Null(await imageService.SaveAsync(Array.Empty<byte>()));
    }

    [Fact]
    public async Task SaveAsync_GivesEachImageItsOwnName()
    {
        string first = await imageService.SaveAsync(PngBytes);
        string second = await imageService.SaveAsync(PngBytes);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task ImportAsync_CopiesASupportedFile()
    {
        string sourcePath = Path.Combine(tempDirectory, "source.png");
        File.WriteAllBytes(sourcePath, PngBytes);

        string src = await imageService.ImportAsync(sourcePath);

        Assert.NotNull(src);
        Assert.True(File.Exists(Path.Combine(imageService.DirectoryPath, src.Substring("images/".Length))));
    }

    [Fact]
    public async Task ImportAsync_RejectsMissingFile()
    {
        Assert.Null(await imageService.ImportAsync(Path.Combine(tempDirectory, "missing.png")));
    }
}
