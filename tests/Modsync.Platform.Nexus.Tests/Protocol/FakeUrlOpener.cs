using Modsync.Platform.Nexus.Protocol;

namespace Modsync.Platform.Nexus.Tests.Protocol;

public sealed class FakeUrlOpener : IUrlOpener
{
    public List<string> OpenedUrls { get; } = new();
    public Exception? ExceptionToThrow { get; set; }

    public void Open(string url)
    {
        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        OpenedUrls.Add(url);
    }
}
