using QuinSlate.Ui.Constants;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui.Services;

internal static class BackupFormat
{
    public const string FileExtension = ".quinslate";
    public const string FileTypeDisplayName = "QuinSlate backup";
    public const int BufferCount = Buffer.MaxIndex - Buffer.MinIndex + 1;

    public const string ManifestEntryName = "manifest.json";
    public const string ManifestFormat = "quinslate-backup";
    public const int ManifestVersion = 1;
    public const string BuffersFolderName = "buffers";
    public const string ImagesEntryPrefix = ImageService.FolderName + "/";

    public const string BackupsFolderName = "backups";
    public const string StagingFolderName = "backup-staging";
    public const string SafetyBackupPrefix = "pre-import-";
    public const string SafetyBackupPattern = SafetyBackupPrefix + "*" + FileExtension;
    public const int RetainedSafetyBackups = 3;
    public const string TimestampFormat = "yyyyMMdd-HHmmss-fff";

    public const int MaximumManifestBytes = 1024 * 1024;
    public const int MaximumImageCount = 5000;
    public const long MaximumArchiveBytes = 1024L * 1024L * 1024L;
    public const long MaximumBufferBytes = ((long)AppConstants.MaxBufferLength * 4) + 4;
    public const int ImageHeaderBytes = 16;
    public const int CopyBufferSize = 81920;
    public const string TempFileSuffix = ".tmp";

    private const string PngExtension = ".png";
    private const string JpegExtension = ".jpg";
    private const string JpegAlternateExtension = ".jpeg";
    private const string GifExtension = ".gif";
    private const string WebpExtension = ".webp";
    private const string BmpExtension = ".bmp";

    private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        PngExtension,
        JpegExtension,
        JpegAlternateExtension,
        GifExtension,
        WebpExtension,
        BmpExtension,
    };

    public static string GetBufferFileName(int index)
    {
        return string.Format(CultureInfo.InvariantCulture, BufferService.BufferFileNameFormat, index);
    }

    public static string GetBufferEntryName(int index)
    {
        return BuffersFolderName + "/" + GetBufferFileName(index);
    }

    public static bool IsSupportedImageName(string name)
    {
        if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name))
        {
            return false;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        return SupportedExtensions.Contains(Path.GetExtension(name));
    }

    public static bool ExtensionMatches(string fileName, string detectedExtension)
    {
        string extension = Path.GetExtension(fileName);
        if (string.Equals(extension, detectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return detectedExtension == JpegExtension
            && string.Equals(extension, JpegAlternateExtension, StringComparison.OrdinalIgnoreCase);
    }
}
