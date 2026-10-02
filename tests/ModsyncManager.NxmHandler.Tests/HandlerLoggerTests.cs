// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using ModsyncManager.NxmHandler;

namespace ModsyncManager.NxmHandler.Tests;

public class HandlerLoggerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _logPath;

    public HandlerLoggerTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-nxm-log-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _logPath = Path.Combine(_tempDir, "handler.log");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private HandlerLogger MakeLogger(DateTimeOffset? fixedTime = null)
        => new(
            _logPath,
            () => fixedTime ?? new DateTimeOffset(
                2026, 9, 29, 14, 40, 34, 789, TimeSpan.Zero));

    // ------------------------------------------------------------------
    //  Happy path
    // ------------------------------------------------------------------

    [Fact]
    public void Log_CreatesFile()
    {
        var logger = MakeLogger();
        logger.Log("hello");

        File.Exists(_logPath).Should().BeTrue();
    }

    [Fact]
    public void Log_WritesLineWithTimestamp()
    {
        var logger = MakeLogger();
        logger.Log("hello");

        var content = File.ReadAllText(_logPath);
        content.Should().Contain("[2026-09-29 14:40:34.789] hello");
    }

    [Fact]
    public void Log_AppendsMultipleLines()
    {
        var logger = MakeLogger();
        logger.Log("first");
        logger.Log("second");
        logger.Log("third");

        var lines = File.ReadAllLines(_logPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        lines.Should().HaveCount(3);
        lines[0].Should().Contain("first");
        lines[1].Should().Contain("second");
        lines[2].Should().Contain("third");
    }

    [Fact]
    public void Log_TwoLoggersSameFile_Append()
    {
        var logger1 = MakeLogger();
        var logger2 = MakeLogger();

        logger1.Log("first");
        logger2.Log("second");

        var content = File.ReadAllText(_logPath);
        content.Should().Contain("first");
        content.Should().Contain("second");
    }

    [Fact]
    public void LogFilePath_ReturnsConfiguredPath()
    {
        var logger = MakeLogger();
        logger.LogFilePath.Should().Be(_logPath);
    }

    // ------------------------------------------------------------------
    //  Формат
    // ------------------------------------------------------------------

    [Fact]
    public void Log_NoBom()
    {
        var logger = MakeLogger();
        logger.Log("hello");

        var bytes = File.ReadAllBytes(_logPath);
        var startsWithBom =
            bytes.Length >= 3
            && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        startsWithBom.Should().BeFalse();
    }

    [Fact]
    public void Log_CrlfLineEndings()
    {
        var logger = MakeLogger();
        logger.Log("hello");

        var content = File.ReadAllText(_logPath);
        content.Should().Contain("\r\n");
    }

    // ------------------------------------------------------------------
    //  Устойчивость
    // ------------------------------------------------------------------

    [Fact]
    public void Log_EmptyMessage_NoOp()
    {
        var logger = MakeLogger();
        logger.Log("");

        File.Exists(_logPath).Should().BeFalse();
    }

    [Fact]
    public void Log_LogDirectoryMissing_CreatesIt()
    {
        var nested = Path.Combine(
            _tempDir, "no", "such", "dir", "log.txt");

        var logger = new HandlerLogger(nested, () => DateTimeOffset.Now);

        var act = () => logger.Log("hello");

        act.Should().NotThrow();
        File.Exists(nested).Should().BeTrue();
    }

    [Fact]
    public void Log_LogFileLocked_DoesNotThrow()
    {
        // Занимаем файл эксклюзивно — попытка записи провалится.
        using var handle = new FileStream(
            _logPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var logger = MakeLogger();

        var act = () => logger.Log("hello");

        act.Should().NotThrow();
    }

    // ------------------------------------------------------------------
    //  Default path
    // ------------------------------------------------------------------

    [Fact]
    public void DefaultConstructor_PathEndsWithModsyncManagerLogsHandlerLog()
    {
        var logger = new HandlerLogger();

        logger.LogFilePath.Should().EndWith(
            Path.Combine("ModsyncManager", "logs", "modsyncmanager-nxm-handler.log"));
    }
}
