using Serilog;
using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace QuinSlate.Ui.Services;

internal sealed class ImageService
{
    public const string FolderName = "images";

    internal const int MaximumBytes = 40 * 1024 * 1024;
    private const int RandomSuffixBytes = 3;
    private const string RelativeFolderPrefix = FolderName + "/";
    private const string FileNameTimestampFormat = "yyyyMMdd-HHmmss";

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public ImageService(string appDataDirectory)
    {
        if (appDataDirectory == null)
        {
            throw new ArgumentNullException(nameof(appDataDirectory));
        }

        DirectoryPath = Path.Combine(appDataDirectory, FolderName);
        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public async Task<string> SaveAsync(byte[] data)
    {
        if (data == null || data.Length == 0 || data.Length > MaximumBytes)
        {
            return null;
        }

        string extension = DetectExtension(data);
        if (extension == null)
        {
            return null;
        }

        string fileName = CreateFileName(extension);
        string path = Path.Combine(DirectoryPath, fileName);

        try
        {
            await AtomicFileWriter.WriteAllBytesAsync(path, data).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            Log.ForContext<ImageService>().Warning(ex, "Failed to write an editor image ({Bytes} bytes).", data.Length);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.ForContext<ImageService>().Warning(ex, "Access denied writing an editor image ({Bytes} bytes).", data.Length);
            return null;
        }

        return RelativeFolderPrefix + fileName;
    }

    public async Task<string> ImportAsync(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath))
        {
            return null;
        }

        try
        {
            var file = new FileInfo(sourcePath);
            if (file.Exists == false || file.Length <= 0 || file.Length > MaximumBytes)
            {
                return null;
            }

            byte[] data = await File.ReadAllBytesAsync(sourcePath).ConfigureAwait(false);
            return await SaveAsync(data).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            Log.ForContext<ImageService>().Warning(ex, "Failed to read an image for insertion.");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.ForContext<ImageService>().Warning(ex, "Access denied reading an image for insertion.");
            return null;
        }
    }

    internal static string DetectExtension(byte[] data)
    {
        if (StartsWith(data, PngSignature))
        {
            return ".png";
        }

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return ".jpg";
        }

        if (data.Length >= 6
            && data[0] == (byte)'G'
            && data[1] == (byte)'I'
            && data[2] == (byte)'F'
            && data[3] == (byte)'8'
            && (data[4] == (byte)'7' || data[4] == (byte)'9')
            && data[5] == (byte)'a')
        {
            return ".gif";
        }

        if (data.Length >= 12
            && data[0] == (byte)'R'
            && data[1] == (byte)'I'
            && data[2] == (byte)'F'
            && data[3] == (byte)'F'
            && data[8] == (byte)'W'
            && data[9] == (byte)'E'
            && data[10] == (byte)'B'
            && data[11] == (byte)'P')
        {
            return ".webp";
        }

        if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M')
        {
            return ".bmp";
        }

        return null;
    }

    private static bool StartsWith(byte[] data, byte[] prefix)
    {
        if (data.Length < prefix.Length)
        {
            return false;
        }

        for (int i = 0; i < prefix.Length; i++)
        {
            if (data[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    private static string CreateFileName(string extension)
    {
        Span<byte> suffixBytes = stackalloc byte[RandomSuffixBytes];
        RandomNumberGenerator.Fill(suffixBytes);
        string suffix = Convert.ToHexString(suffixBytes).ToLowerInvariant();
        string timestamp = DateTime.Now.ToString(FileNameTimestampFormat, CultureInfo.InvariantCulture);
        return timestamp + "-" + suffix + extension;
    }
}
