using System.Collections.Generic;

namespace QuinSlate.Ui.Models;

internal sealed class BackupContents
{
    public BackupContents(IReadOnlyList<string> bufferTexts, IReadOnlyList<string> imageNames)
    {
        BufferTexts = bufferTexts;
        ImageNames = imageNames;
    }

    public IReadOnlyList<string> BufferTexts { get; }

    public IReadOnlyList<string> ImageNames { get; }
}
