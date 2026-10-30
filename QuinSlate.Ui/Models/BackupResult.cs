namespace QuinSlate.Ui.Models;

internal sealed class BackupResult
{
    private BackupResult(bool success, int bufferCount, int imageCount, string error)
    {
        Success = success;
        BufferCount = bufferCount;
        ImageCount = imageCount;
        Error = error;
    }

    public bool Success { get; }

    public int BufferCount { get; }

    public int ImageCount { get; }

    public string Error { get; }

    public static BackupResult Succeeded(int bufferCount, int imageCount)
    {
        return new BackupResult(true, bufferCount, imageCount, null);
    }

    public static BackupResult Failed(string error)
    {
        return new BackupResult(false, 0, 0, error);
    }
}
