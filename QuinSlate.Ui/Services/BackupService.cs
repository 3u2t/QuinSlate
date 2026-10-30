using QuinSlate.Ui.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui.Services;

internal sealed class BackupService
{
    private static readonly UTF8Encoding Utf8WithBom = new UTF8Encoding(true);

    private readonly string appDataDirectory;
    private readonly string imagesDirectory;
    private readonly BackupArchiveWriter archiveWriter;
    private readonly BackupArchiveReader archiveReader = new BackupArchiveReader();

    public BackupService(string appDataDirectory)
    {
        if (appDataDirectory == null)
        {
            throw new ArgumentNullException(nameof(appDataDirectory));
        }

        this.appDataDirectory = appDataDirectory;
        imagesDirectory = Path.Combine(appDataDirectory, ImageService.FolderName);
        archiveWriter = new BackupArchiveWriter(imagesDirectory);
    }

    public async Task<BackupResult> ExportAsync(string destinationPath, IReadOnlyList<string> bufferContents)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return BackupResult.Failed("No backup location was chosen.");
        }

        if (bufferContents == null || bufferContents.Count != BackupFormat.BufferCount)
        {
            return BackupResult.Failed("The backup needs all five slates.");
        }

        string tempPath = destinationPath + BackupFormat.TempFileSuffix;

        try
        {
            string directory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrEmpty(directory) == false)
            {
                Directory.CreateDirectory(directory);
            }

            List<string> imageNames = GetImageFileNames();
            await archiveWriter.WriteAsync(tempPath, bufferContents, imageNames).ConfigureAwait(false);
            File.Move(tempPath, destinationPath, overwrite: true);
            return BackupResult.Succeeded(BackupFormat.BufferCount, imageNames.Count);
        }
        catch (Exception ex) when (IsExpectedIoException(ex))
        {
            Log.ForContext<BackupService>().Warning(ex, "Failed to export a QuinSlate backup.");
            TryDeleteFile(tempPath);
            return BackupResult.Failed("The backup could not be written to that location.");
        }
    }

    public async Task<BackupResult> ImportAsync(string sourcePath, IReadOnlyList<string> currentBufferContents)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || File.Exists(sourcePath) == false)
        {
            return BackupResult.Failed("The backup file could not be found.");
        }

        if (currentBufferContents == null || currentBufferContents.Count != BackupFormat.BufferCount)
        {
            return BackupResult.Failed("The current slates could not be read.");
        }

        string stagingDirectory = Path.Combine(appDataDirectory, BackupFormat.StagingFolderName, Guid.NewGuid().ToString("N"));

        try
        {
            BackupContents contents = await archiveReader.ReadAsync(sourcePath, stagingDirectory).ConfigureAwait(false);

            string safetyBackupPath = CreateSafetyBackupPath();
            BackupResult safetyBackup = await ExportAsync(safetyBackupPath, currentBufferContents).ConfigureAwait(false);
            if (safetyBackup.Success == false)
            {
                return BackupResult.Failed("A safety backup could not be created, so the import was cancelled.");
            }

            PruneSafetyBackups();

            Directory.CreateDirectory(imagesDirectory);
            foreach (string imageName in contents.ImageNames)
            {
                string stagedPath = Path.Combine(stagingDirectory, imageName);
                string targetPath = Path.Combine(imagesDirectory, imageName);
                File.Move(stagedPath, targetPath, overwrite: true);
            }

            for (int index = Buffer.MinIndex; index <= Buffer.MaxIndex; index++)
            {
                string bufferPath = Path.Combine(appDataDirectory, BackupFormat.GetBufferFileName(index));
                await AtomicFileWriter.WriteAllTextAsync(bufferPath, contents.BufferTexts[index - Buffer.MinIndex], Utf8WithBom).ConfigureAwait(false);
            }

            DeleteUnlistedImages(contents.ImageNames);
            return BackupResult.Succeeded(BackupFormat.BufferCount, contents.ImageNames.Count);
        }
        catch (Exception ex) when (IsExpectedImportException(ex))
        {
            Log.ForContext<BackupService>().Warning(ex, "Failed to import a QuinSlate backup.");
            return BackupResult.Failed("The backup could not be imported. The file may be damaged or not a QuinSlate backup.");
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private List<string> GetImageFileNames()
    {
        var names = new List<string>();
        if (Directory.Exists(imagesDirectory) == false)
        {
            return names;
        }

        foreach (string path in Directory.EnumerateFiles(imagesDirectory))
        {
            string name = Path.GetFileName(path);
            if (BackupFormat.IsSupportedImageName(name))
            {
                names.Add(name);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private string CreateSafetyBackupPath()
    {
        string directory = Path.Combine(appDataDirectory, BackupFormat.BackupsFolderName);
        Directory.CreateDirectory(directory);
        string stamp = DateTime.Now.ToString(BackupFormat.TimestampFormat, System.Globalization.CultureInfo.InvariantCulture);
        return Path.Combine(directory, BackupFormat.SafetyBackupPrefix + stamp + BackupFormat.FileExtension);
    }

    private void PruneSafetyBackups()
    {
        try
        {
            string directory = Path.Combine(appDataDirectory, BackupFormat.BackupsFolderName);
            if (Directory.Exists(directory) == false)
            {
                return;
            }

            var files = new List<string>(Directory.EnumerateFiles(directory, BackupFormat.SafetyBackupPattern));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            int removeCount = files.Count - BackupFormat.RetainedSafetyBackups;
            for (int i = 0; i < removeCount; i++)
            {
                File.Delete(files[i]);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void DeleteUnlistedImages(IReadOnlyList<string> imageNames)
    {
        string[] files;
        try
        {
            if (Directory.Exists(imagesDirectory) == false)
            {
                return;
            }

            files = Directory.GetFiles(imagesDirectory);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        var keep = new HashSet<string>(imageNames, StringComparer.OrdinalIgnoreCase);
        foreach (string path in files)
        {
            string name = Path.GetFileName(path);
            if (BackupFormat.IsSupportedImageName(name) == false || keep.Contains(name))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool IsExpectedIoException(Exception ex)
    {
        return ex is IOException
            || ex is UnauthorizedAccessException
            || ex is ArgumentException
            || ex is NotSupportedException;
    }

    private static bool IsExpectedImportException(Exception ex)
    {
        return IsExpectedIoException(ex)
            || ex is InvalidDataException
            || ex is JsonException
            || ex is InvalidOperationException;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
