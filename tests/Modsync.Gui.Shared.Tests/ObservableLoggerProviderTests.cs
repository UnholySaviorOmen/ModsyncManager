using FluentAssertions;
using Modsync.Gui.Shared.Logging;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Shared.Tests;

public class ObservableLoggerProviderTests
{
    [Fact]
    public void Logger_WritesToSink()
    {
        var sink = new ObservableLogSink();
        var provider = new ObservableLoggerProvider(sink, LogLevel.Information);
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("hello");

        sink.Entries.Should().HaveCount(1);
        sink.Entries[0].Message.Should().Be("hello");
        sink.Entries[0].Level.Should().Be(LogLevel.Information);
    }

    [Fact]
    public void IsEnabled_RespectsMinLevel()
    {
        var sink = new ObservableLogSink();
        var provider = new ObservableLoggerProvider(sink, LogLevel.Warning);
        var logger = provider.CreateLogger("Test");

        logger.IsEnabled(LogLevel.Debug).Should().BeFalse();
        logger.IsEnabled(LogLevel.Information).Should().BeFalse();
        logger.IsEnabled(LogLevel.Warning).Should().BeTrue();
        logger.IsEnabled(LogLevel.Error).Should().BeTrue();
    }

    [Fact]
    public void Logger_BelowMinLevel_NotWritten()
    {
        var sink = new ObservableLogSink();
        var provider = new ObservableLoggerProvider(sink, LogLevel.Warning);
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("nope");
        logger.LogWarning("yes");

        sink.Entries.Should().HaveCount(1);
        sink.Entries[0].Message.Should().Be("yes");
    }

    [Fact]
    public void Logger_Exception_AppendedToMessage()
    {
        var sink = new ObservableLogSink();
        var provider = new ObservableLoggerProvider(sink, LogLevel.Information);
        var logger = provider.CreateLogger("Test");

        logger.LogError(new InvalidOperationException("boom"), "failed");

        sink.Entries.Should().HaveCount(1);
        sink.Entries[0].Message.Should().Contain("failed");
        sink.Entries[0].Message.Should().Contain("InvalidOperationException");
        sink.Entries[0].Message.Should().Contain("boom");
    }

    [Fact]
    public void BeginScope_ReturnsNull()
    {
        var sink = new ObservableLogSink();
        var provider = new ObservableLoggerProvider(sink);
        var logger = provider.CreateLogger("Test");

        using var scope = logger.BeginScope("scope");
        scope.Should().BeNull();
    }
}
