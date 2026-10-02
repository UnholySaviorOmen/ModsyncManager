using Modsync.Gui.Shared.Logging;

namespace Modsync.Gui.Shared.Tests.Fakes;

public sealed class FakeUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();

    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
}
