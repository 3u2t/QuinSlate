using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using QuinSlate.Ui.Models;
using QuinSlate.Ui.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui.Components;

internal sealed class BackupController
{
    private const string ExportDialogTitle = "Backup exported";
    private const string ImportDialogTitle = "Backup imported";
    private const string ErrorDialogTitle = "Backup failed";
    private const string ExportCommitText = "Export";
    private const string ImportCommitText = "Import";
    private const string CancelButtonText = "Cancel";
    private const string CloseButtonText = "OK";
    private const string DefaultFileNamePrefix = "QuinSlate-";
    private const string FileNameTimestampFormat = "yyyyMMdd-HHmmss";
    private const string ImportConfirmationTitle = "Import backup?";
    private const string ImportConfirmationMessage = "This replaces all five slates and the images stored with them. A safety backup is created first.";

    private readonly BackupService backupService;
    private readonly BufferService bufferService;
    private readonly Func<Task> flushAsync;
    private readonly Func<Task> reloadAsync;
    private readonly Func<WindowId> windowIdProvider;
    private readonly Func<XamlRoot> xamlRootProvider;
    private readonly Action<bool> setBusy;

    private bool isBusy;

    public BackupController(
        BackupService backupService,
        BufferService bufferService,
        Func<Task> flushAsync,
        Func<Task> reloadAsync,
        Func<WindowId> windowIdProvider,
        Func<XamlRoot> xamlRootProvider,
        Action<bool> setBusy)
    {
        if (backupService == null)
        {
            throw new ArgumentNullException(nameof(backupService));
        }

        if (bufferService == null)
        {
            throw new ArgumentNullException(nameof(bufferService));
        }

        if (flushAsync == null)
        {
            throw new ArgumentNullException(nameof(flushAsync));
        }

        if (reloadAsync == null)
        {
            throw new ArgumentNullException(nameof(reloadAsync));
        }

        if (windowIdProvider == null)
        {
            throw new ArgumentNullException(nameof(windowIdProvider));
        }

        if (xamlRootProvider == null)
        {
            throw new ArgumentNullException(nameof(xamlRootProvider));
        }

        if (setBusy == null)
        {
            throw new ArgumentNullException(nameof(setBusy));
        }

        this.backupService = backupService;
        this.bufferService = bufferService;
        this.flushAsync = flushAsync;
        this.reloadAsync = reloadAsync;
        this.windowIdProvider = windowIdProvider;
        this.xamlRootProvider = xamlRootProvider;
        this.setBusy = setBusy;
    }

    public async Task ExportAsync()
    {
        if (isBusy)
        {
            return;
        }

        WindowId windowId = windowIdProvider();
        if (windowId.Value == 0)
        {
            return;
        }

        SetBusy(true);

        try
        {
            var picker = new FileSavePicker(windowId)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = DefaultFileNamePrefix + DateTime.Now.ToString(FileNameTimestampFormat, CultureInfo.InvariantCulture),
                DefaultFileExtension = BackupFormat.FileExtension,
                CommitButtonText = ExportCommitText,
            };
            picker.FileTypeChoices.Add(BackupFormat.FileTypeDisplayName, new List<string> { BackupFormat.FileExtension });

            PickFileResult result = await picker.PickSaveFileAsync();
            if (result == null || string.IsNullOrEmpty(result.Path))
            {
                return;
            }

            await flushAsync();
            BackupResult export = await backupService.ExportAsync(result.Path, GetBufferContents());
            if (export.Success)
            {
                await ShowMessageAsync(ExportDialogTitle, BuildResultMessage(export, "saved"));
            }
            else
            {
                await ShowMessageAsync(ErrorDialogTitle, export.Error);
            }
        }
        catch (Exception ex)
        {
            Log.ForContext<BackupController>().Error(ex, "The backup export flow failed.");
            await ShowMessageAsync(ErrorDialogTitle, "The backup could not be exported.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    public async Task ImportAsync()
    {
        if (isBusy)
        {
            return;
        }

        WindowId windowId = windowIdProvider();
        if (windowId.Value == 0)
        {
            return;
        }

        SetBusy(true);

        try
        {
            var picker = new FileOpenPicker(windowId)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                CommitButtonText = ImportCommitText,
            };
            picker.FileTypeFilter.Add(BackupFormat.FileExtension);

            PickFileResult result = await picker.PickSingleFileAsync();
            if (result == null || string.IsNullOrEmpty(result.Path))
            {
                return;
            }

            ContentDialogResult confirmation = await ShowConfirmationAsync();
            if (confirmation != ContentDialogResult.Primary)
            {
                return;
            }

            await flushAsync();
            BackupResult import = await backupService.ImportAsync(result.Path, GetBufferContents());
            if (import.Success == false)
            {
                await ShowMessageAsync(ErrorDialogTitle, import.Error);
                return;
            }

            await reloadAsync();
            await ShowMessageAsync(ImportDialogTitle, BuildResultMessage(import, "restored"));
        }
        catch (Exception ex)
        {
            Log.ForContext<BackupController>().Error(ex, "The backup import flow failed.");
            await ShowMessageAsync(ErrorDialogTitle, "The backup could not be imported.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private IReadOnlyList<string> GetBufferContents()
    {
        var contents = new List<string>(BackupFormat.BufferCount);
        for (int index = Buffer.MinIndex; index <= Buffer.MaxIndex; index++)
        {
            Buffer buffer = bufferService.GetBuffer(index);
            contents.Add(buffer == null ? string.Empty : buffer.Content ?? string.Empty);
        }

        return contents;
    }

    private static string BuildResultMessage(BackupResult result, string action)
    {
        string imageWord = result.ImageCount == 1 ? "image" : "images";
        return string.Format(
            CultureInfo.InvariantCulture,
            "All five slates and {0} {1} were {2}.",
            result.ImageCount,
            imageWord,
            action);
    }

    private async Task<ContentDialogResult> ShowConfirmationAsync()
    {
        XamlRoot xamlRoot = xamlRootProvider();
        if (xamlRoot == null)
        {
            return ContentDialogResult.None;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = ImportConfirmationTitle,
            Content = ImportConfirmationMessage,
            PrimaryButtonText = ImportCommitText,
            CloseButtonText = CancelButtonText,
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync();
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        XamlRoot xamlRoot = xamlRootProvider();
        if (xamlRoot == null)
        {
            Log.ForContext<BackupController>().Warning("Could not show a backup dialog because the About window has no XamlRoot.");
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = string.IsNullOrEmpty(message) ? "The backup operation did not finish." : message,
            CloseButtonText = CloseButtonText,
            DefaultButton = ContentDialogButton.Close,
        };

        await dialog.ShowAsync();
    }

    private void SetBusy(bool busy)
    {
        isBusy = busy;
        setBusy(busy);
    }
}
