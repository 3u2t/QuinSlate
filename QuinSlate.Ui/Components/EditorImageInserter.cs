using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;
using QuinSlate.Ui.Services;
using Serilog;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace QuinSlate.Ui.Components;

internal sealed class EditorImageInserter
{
    private const string ImageMarkdownFormat = "![]({0})";
    private const string InsertImageCommitText = "Insert";
    private const string PngExtension = ".png";
    private const string JpegExtension = ".jpg";
    private const string JpegAlternateExtension = ".jpeg";
    private const string GifExtension = ".gif";
    private const string WebpExtension = ".webp";
    private const string BmpExtension = ".bmp";

    private readonly EditorHost editorHost;
    private readonly ImageService imageService;
    private readonly Func<WindowId> windowIdProvider;

    public EditorImageInserter(EditorHost editorHost, ImageService imageService, Func<WindowId> windowIdProvider)
    {
        if (editorHost == null)
        {
            throw new ArgumentNullException(nameof(editorHost));
        }

        if (imageService == null)
        {
            throw new ArgumentNullException(nameof(imageService));
        }

        if (windowIdProvider == null)
        {
            throw new ArgumentNullException(nameof(windowIdProvider));
        }

        this.editorHost = editorHost;
        this.imageService = imageService;
        this.windowIdProvider = windowIdProvider;
    }

    public async Task InsertFromPickerAsync()
    {
        WindowId windowId = windowIdProvider();
        if (windowId.Value == 0)
        {
            return;
        }

        var picker = new FileOpenPicker(windowId)
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            CommitButtonText = InsertImageCommitText,
        };
        picker.FileTypeFilter.Add(PngExtension);
        picker.FileTypeFilter.Add(JpegExtension);
        picker.FileTypeFilter.Add(JpegAlternateExtension);
        picker.FileTypeFilter.Add(GifExtension);
        picker.FileTypeFilter.Add(WebpExtension);
        picker.FileTypeFilter.Add(BmpExtension);

        PickFileResult result = await picker.PickSingleFileAsync();
        if (result == null)
        {
            return;
        }

        string src = await imageService.ImportAsync(result.Path);
        if (src == null)
        {
            Log.ForContext<EditorImageInserter>().Warning("The image picked for insertion was not a supported format.");
            return;
        }

        editorHost.InsertText(string.Format(ImageMarkdownFormat, src));
    }

    public async Task PasteAsync()
    {
        DataPackageView dataView;
        try
        {
            dataView = Clipboard.GetContent();
        }
        catch (COMException ex)
        {
            Log.ForContext<EditorImageInserter>().Warning(ex, "Failed to open the clipboard.");
            return;
        }

        if (dataView.Contains(StandardDataFormats.Text))
        {
            string text;
            try
            {
                text = await dataView.GetTextAsync();
            }
            catch (COMException ex)
            {
                Log.ForContext<EditorImageInserter>().Warning(ex, "Failed to read text from the clipboard.");
                return;
            }

            editorHost.InsertText(text);
            return;
        }

        if (dataView.Contains(StandardDataFormats.Bitmap) == false)
        {
            return;
        }

        try
        {
            RandomAccessStreamReference reference = await dataView.GetBitmapAsync();
            using IRandomAccessStreamWithContentType stream = await reference.OpenReadAsync();
            byte[] data = await ReadAllBytesAsync(stream);
            string src = await imageService.SaveAsync(data);
            if (src != null)
            {
                editorHost.InsertText(string.Format(ImageMarkdownFormat, src));
            }
        }
        catch (Exception ex) when (ex is COMException || ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
        {
            Log.ForContext<EditorImageInserter>().Warning(ex, "Failed to read a bitmap from the clipboard.");
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(IRandomAccessStream stream)
    {
        if (stream.Size == 0 || stream.Size > int.MaxValue)
        {
            return Array.Empty<byte>();
        }

        var data = new byte[(int)stream.Size];
        using (var reader = new DataReader(stream.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)data.Length);
            reader.ReadBytes(data);
        }

        return data;
    }
}
