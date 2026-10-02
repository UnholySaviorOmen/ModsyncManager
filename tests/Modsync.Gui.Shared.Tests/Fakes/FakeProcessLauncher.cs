using Modsync.Gui.Shared.Services;

namespace Modsync.Gui.Shared.Tests.Fakes;

public sealed class FakeProcessLauncher : IProcessLauncher
{
    public List<string> OpenedPaths { get; } = new();
    public Exception? ExceptionToThrow { get; set; }

    public void OpenFile(string path)
    {
        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        OpenedPaths.Add(path);
    }
}
