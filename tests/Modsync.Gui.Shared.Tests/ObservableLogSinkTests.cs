using FluentAssertions;
using Modsync.Gui.Shared.Logging;
using Modsync.Gui.Shared.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Shared.Tests;

public class ObservableLogSinkDispatcherTests
{
    private static LogEntry Entry(string msg, LogLevel level = LogLevel.Information)
        => new(DateTimeOffset.Now, level, msg);

    [Fact]
    public void WithDispatcher_Add_InvokesDispatcher()
    {
        var dispatcher = new FakeUiDispatcher();
        var sink = new ObservableLogSink(dispatcher);

        sink.Add(Entry("a"));

        sink.Entries.Should().HaveCount(1);
        sink.Entries[0].Message.Should().Be("a");
    }

    [Fact]
    public void WithDispatcher_Clear_InvokesDispatcher()
    {
        var dispatcher = new FakeUiDispatcher();
        var sink = new ObservableLogSink(dispatcher);

        sink.Add(Entry("a"));
        sink.Add(Entry("b"));
        sink.Clear();

        sink.Entries.Should().BeEmpty();
    }

    [Fact]
    public void WithoutDispatcher_Add_Synchronous()
    {
        var sink = new ObservableLogSink();
        sink.Add(Entry("a"));

        sink.Entries.Should().HaveCount(1);
    }

    [Fact]
    public void WithDispatcher_Trim_StillWorks()
    {
        var dispatcher = new FakeUiDispatcher();
        var sink = new ObservableLogSink(dispatcher);

        // MaxEntries = 1000. Добавляем 1200, ожидаем 1000.
        for (int i = 0; i < 1200; i++)
            sink.Add(Entry($"msg-{i}"));

        sink.Entries.Should().HaveCount(1000);
        sink.Entries[0].Message.Should().Be("msg-200");
        sink.Entries[^1].Message.Should().Be("msg-1199");
    }

    [Fact]
    public void WithoutDispatcher_Trim_StillWorks()
    {
        var sink = new ObservableLogSink();

        for (int i = 0; i < 1200; i++)
            sink.Add(Entry($"msg-{i}"));

        sink.Entries.Should().HaveCount(1000);
        sink.Entries[0].Message.Should().Be("msg-200");
        sink.Entries[^1].Message.Should().Be("msg-1199");
    }

    [Fact]
    public void WithoutDispatcher_BelowMax_KeepsAll()
    {
        var sink = new ObservableLogSink();

        for (int i = 0; i < 500; i++)
            sink.Add(Entry($"msg-{i}"));

        sink.Entries.Should().HaveCount(500);
        sink.Entries[0].Message.Should().Be("msg-0");
        sink.Entries[^1].Message.Should().Be("msg-499");
    }

    /// <summary>
    /// Симулирует «настоящий» UI-диспетчер: action не выполняется
    /// синхронно, а копится в очередь. Проверяем, что до Flush
    /// коллекция не тронута — то есть Add реально маршалится.
    /// </summary>
    [Fact]
    public void WithDeferredDispatcher_Add_DoesNotMutateUntilFlush()
    {
        var dispatcher = new DeferredUiDispatcher();
        var sink = new ObservableLogSink(dispatcher);

        sink.Add(Entry("a"));
        sink.Add(Entry("b"));

        // До flush — пусто.
        sink.Entries.Should().BeEmpty();

        dispatcher.Flush();

        sink.Entries.Should().HaveCount(2);
        sink.Entries[0].Message.Should().Be("a");
        sink.Entries[1].Message.Should().Be("b");
    }

    private sealed class DeferredUiDispatcher : IUiDispatcher
    {
        private readonly List<Action> _queue = new();

        public void Post(Action action) => _queue.Add(action);

        public Task InvokeAsync(Action action) =>
            throw new NotSupportedException(
                "DeferredUiDispatcher supports only Post.");

        public Task<T> InvokeAsync<T>(Func<T> func) =>
            throw new NotSupportedException(
                "DeferredUiDispatcher supports only Post.");

        public void Flush()
        {
            var copy = _queue.ToList();
            _queue.Clear();
            foreach (var a in copy) a();
        }
    }
}
