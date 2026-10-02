using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Modsync.Logging.Tests;

public class FileLoggerProviderTests : IDisposable
{
    private readonly string _tempDir;

    public FileLoggerProviderTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-logging-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private FileLoggerOptions MakeOptions(int retentionDays = 14)
        => new()
        {
            LogDirectory = _tempDir,
            RetentionDays = retentionDays,
            FilePrefix = "modsyncmanager",
        };

    private FileLoggerProvider MakeProvider(
        DateTimeOffset? fixedNow = null,
        int retentionDays = 14)
    {
        var now = fixedNow ?? LocalTime(
            2026, 9, 29, 14, 30, 15, 123);

        return new FileLoggerProvider(
            MakeOptions(retentionDays),
            () => now);
    }

    /// <summary>
    /// Строит DateTimeOffset в локальной зоне системы так, чтобы
    /// <c>LocalDateTime</c> давал ровно указанные значения.
    /// </summary>
    private static DateTimeOffset LocalTime(
        int year, int month, int day,
        int hh, int mm, int ss, int ms = 0)
    {
        var local = new DateTime(
            year, month, day, hh, mm, ss, ms,
            DateTimeKind.Unspecified);

        var offset = TimeZoneInfo.Local.GetUtcOffset(local);

        return new DateTimeOffset(local, offset);
    }

    private static string FilePath(string dir, DateOnly date)
        => Path.Combine(dir, $"modsyncmanager-{date:yyyy-MM-dd}.log");

    private static string BackupPath(string dir, DateOnly date)
        => Path.Combine(dir, $"modsyncmanager-{date:yyyy-MM-dd}.1.log");

    // ------------------------------------------------------------------
    //  Basic write
    // ------------------------------------------------------------------

    [Fact]
    public void Log_WritesLineToFile()
    {
        var provider = MakeProvider();
        var logger = provider.CreateLogger("Test.Category");

        logger.LogInformation("hello world");

        var path = provider.CurrentFilePath;
        provider.Dispose();

        File.Exists(path).Should().BeTrue();

        var text = File.ReadAllText(path);
        text.Should().Contain("[Information] Test.Category: hello world");
    }

    [Fact]
    public void Log_IncludesTimestamp()
    {
        var provider = MakeProvider(
            LocalTime(2026, 9, 29, 14, 30, 15, 123));

        var logger = provider.CreateLogger("X");
        logger.LogInformation("msg");

        var path = provider.CurrentFilePath;
        provider.Dispose();

        var text = File.ReadAllText(path);
        text.Should().Contain("2026-09-29 14:30:15.123");
    }

    [Fact]
    public void Log_MultipleLines_Appended()
    {
        var provider = MakeProvider();
        var logger = provider.CreateLogger("X");

        logger.LogInformation("first");
        logger.LogInformation("second");
        logger.LogInformation("third");

        var path = provider.CurrentFilePath;
        provider.Dispose();

        var lines = File.ReadAllLines(path);
        lines.Should().HaveCount(3);
        lines[0].Should().Contain("first");
        lines[2].Should().Contain("third");
    }

    [Fact]
    public void Log_BelowMinimumLevel_NotWritten()
    {
        var provider = MakeProvider();
        var logger = provider.CreateLogger("X");

        logger.LogDebug("debug msg");
        logger.LogTrace("trace msg");
        logger.LogInformation("info msg");

        var path = provider.CurrentFilePath;
        provider.Dispose();

        var lines = File.ReadAllLines(path);
        lines.Should().HaveCount(1);
        lines[0].Should().Contain("info msg");
    }

    [Fact]
    public void Log_Exception_AppendsFullExceptionText()
    {
        var provider = MakeProvider();
        var logger = provider.CreateLogger("X");

        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed");
        }

        var path = provider.CurrentFilePath;
        provider.Dispose();

        var text = File.ReadAllText(path);
        text.Should().Contain("failed");
        text.Should().Contain("InvalidOperationException");
        text.Should().Contain("boom");
    }

    [Fact]
    public void Log_NoBom()
    {
        var provider = MakeProvider();
        var logger = provider.CreateLogger("X");
        logger.LogInformation("hello");

        var path = provider.CurrentFilePath;
        provider.Dispose();

        var bytes = File.ReadAllBytes(path);
        var startsWithBom =
            bytes.Length >= 3
            && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        startsWithBom.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  Rotation (daily)
    // ------------------------------------------------------------------

    [Fact]
    public void Rotation_NewDay_NewFile()
    {
        var currentTime = LocalTime(2026, 9, 29, 23, 59, 59);

        var provider = new FileLoggerProvider(
            MakeOptions(), () => currentTime);

        var logger = provider.CreateLogger("X");
        logger.LogInformation("day 1");

        currentTime = LocalTime(2026, 9, 30, 0, 0, 1);
        logger.LogInformation("day 2");

        provider.Dispose();

        var path1 = FilePath(_tempDir, new DateOnly(2026, 9, 29));
        var path2 = FilePath(_tempDir, new DateOnly(2026, 9, 30));

        File.Exists(path1).Should().BeTrue();
        File.Exists(path2).Should().BeTrue();

        File.ReadAllText(path1).Should().Contain("day 1");
        File.ReadAllText(path1).Should().NotContain("day 2");

        File.ReadAllText(path2).Should().Contain("day 2");
        File.ReadAllText(path2).Should().NotContain("day 1");
    }

    // ------------------------------------------------------------------
    //  Rotation (size)
    // ------------------------------------------------------------------

    [Fact]
    public void SizeRotation_ExceedingMax_RenamesToBackup()
    {
        // 200 байт — это ~3 строки по ~50-60 байт.
        // Дизайн: один .1.log на день, перезапись при каждой ротации.
        var options = new FileLoggerOptions
        {
            LogDirectory = _tempDir,
            RetentionDays = 14,
            FilePrefix = "modsyncmanager",
            MaxFileSizeBytes = 200,
        };

        var now = LocalTime(2026, 9, 30, 12, 0, 0);
        var provider = new FileLoggerProvider(options, () => now);
        var logger = provider.CreateLogger("X");

        for (int i = 0; i < 20; i++)
            logger.LogInformation($"line-{i:D3}");

        provider.Dispose();

        var mainPath = FilePath(_tempDir, new DateOnly(2026, 9, 30));
        var backupPath = BackupPath(_tempDir, new DateOnly(2026, 9, 30));

        File.Exists(mainPath).Should().BeTrue();
        File.Exists(backupPath).Should().BeTrue();

        var backupLines = File.ReadAllLines(backupPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        var mainLines = File.ReadAllLines(mainPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        backupLines.Should().NotBeEmpty();
        mainLines.Should().NotBeEmpty();

        (backupLines.Length + mainLines.Length)
            .Should().BeLessThanOrEqualTo(20);
    }

    [Fact]
    public void SizeRotation_BackupOverwritten_OnSecondRotation()
    {
        var options = new FileLoggerOptions
        {
            LogDirectory = _tempDir,
            RetentionDays = 14,
            FilePrefix = "modsyncmanager",
            MaxFileSizeBytes = 200,
        };

        var now = LocalTime(2026, 9, 30, 12, 0, 0);
        var provider = new FileLoggerProvider(options, () => now);
        var logger = provider.CreateLogger("X");

        for (int i = 0; i < 100; i++)
            logger.LogInformation($"line-{i:D3}");

        provider.Dispose();

        var backupPath = BackupPath(_tempDir, new DateOnly(2026, 9, 30));
        File.Exists(backupPath).Should().BeTrue();

        // .2.log не создаётся.
        File.Exists(Path.Combine(_tempDir, "modsyncmanager-2026-09-30.2.log"))
            .Should().BeFalse();
    }

    [Fact]
    public void SizeRotation_ZeroDisablesRotation()
    {
        var options = new FileLoggerOptions
        {
            LogDirectory = _tempDir,
            RetentionDays = 14,
            FilePrefix = "modsyncmanager",
            MaxFileSizeBytes = 0,
        };

        var now = LocalTime(2026, 9, 30, 12, 0, 0);
        var provider = new FileLoggerProvider(options, () => now);
        var logger = provider.CreateLogger("X");

        for (int i = 0; i < 100; i++)
            logger.LogInformation($"line-{i:D3}");

        provider.Dispose();

        var backupPath = BackupPath(_tempDir, new DateOnly(2026, 9, 30));
        File.Exists(backupPath).Should().BeFalse();

        var mainPath = FilePath(_tempDir, new DateOnly(2026, 9, 30));
        var mainLines = File.ReadAllLines(mainPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        mainLines.Should().HaveCount(100);
    }

    // ------------------------------------------------------------------
    //  Retention
    // ------------------------------------------------------------------

    [Fact]
    public void Retention_OldFilesDeleted()
    {
        var old1 = FilePath(_tempDir, new DateOnly(2026, 9, 1));
        var old2 = FilePath(_tempDir, new DateOnly(2026, 9, 10));
        var recent = FilePath(_tempDir, new DateOnly(2026, 9, 28));

        File.WriteAllText(old1, "old1");
        File.WriteAllText(old2, "old2");
        File.WriteAllText(recent, "recent");

        var provider = MakeProvider(
            LocalTime(2026, 9, 29, 12, 0, 0),
            retentionDays: 14);

        provider.Dispose();

        File.Exists(old1).Should().BeFalse();
        File.Exists(old2).Should().BeFalse();
        File.Exists(recent).Should().BeTrue();
    }

    [Fact]
    public void Retention_Zero_KeepsEverything()
    {
        var old = FilePath(_tempDir, new DateOnly(2020, 1, 1));
        File.WriteAllText(old, "very old");

        var provider = MakeProvider(retentionDays: 0);
        provider.Dispose();

        File.Exists(old).Should().BeTrue();
    }

    [Fact]
    public void Retention_IgnoresUnrelatedFiles()
    {
        var unrelated = Path.Combine(_tempDir, "not-a-log.txt");
        File.WriteAllText(unrelated, "keep me");

        var malformed = Path.Combine(_tempDir, "modsyncmanager-garbage.log");
        File.WriteAllText(malformed, "keep me too");

        var provider = MakeProvider(
            LocalTime(2030, 1, 1, 0, 0, 0),
            retentionDays: 1);

        provider.Dispose();

        File.Exists(unrelated).Should().BeTrue();
        File.Exists(malformed).Should().BeTrue();
    }

    [Fact]
    public void Retention_DoesNotDeleteBackupSeparately()
    {
        var oldMain = FilePath(_tempDir, new DateOnly(2026, 8, 1));
        var oldBackup = BackupPath(_tempDir, new DateOnly(2026, 8, 1));
        File.WriteAllText(oldMain, "old main");
        File.WriteAllText(oldBackup, "old backup");

        var provider = new FileLoggerProvider(
            new FileLoggerOptions
            {
                LogDirectory = _tempDir,
                RetentionDays = 14,
                FilePrefix = "modsyncmanager",
            },
            () => LocalTime(2026, 9, 30, 12, 0, 0));

        provider.Dispose();

        File.Exists(oldMain).Should().BeFalse();
        File.Exists(oldBackup).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  Robustness
    // ------------------------------------------------------------------

    [Fact]
    public void Log_ReadOnlyDirectory_DoesNotThrow()
    {
        var options = new FileLoggerOptions
        {
            LogDirectory = Path.Combine(_tempDir, "nope", "\0bad"),
        };

        var provider = new FileLoggerProvider(
            options, () => LocalTime(2026, 9, 29, 12, 0, 0));
        var logger = provider.CreateLogger("X");

        var act = () => logger.LogInformation("hello");
        act.Should().NotThrow();

        provider.Dispose();
    }

    [Fact]
    public async Task Log_ConcurrentWrites_AllLinesWritten()
    {
        var provider = MakeProvider();
        var logger = provider.CreateLogger("X");

        var tasks = Enumerable.Range(0, 10).Select(i =>
            Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                    logger.LogInformation($"thread-{i}-line-{j}");
            }));

        await Task.WhenAll(tasks);

        var path = provider.CurrentFilePath;
        provider.Dispose();

        var lines = File.ReadAllLines(path);
        lines.Should().HaveCount(1000);
        lines.Distinct().Should().HaveCount(1000);
    }

    [Fact]
    public void Dispose_Idempotent()
    {
        var provider = MakeProvider();
        provider.Dispose();
        var act = () => provider.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Log_AfterDispose_DoesNotThrow()
    {
        var provider = MakeProvider();
        var logger = provider.CreateLogger("X");
        provider.Dispose();

        var act = () => logger.LogInformation("after dispose");
        act.Should().NotThrow();
    }

    // ------------------------------------------------------------------
    //  Constructor validation
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new FileLoggerProvider(
            null!, () => DateTimeOffset.Now);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullClock_Throws()
    {
        var act = () => new FileLoggerProvider(MakeOptions(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_EmptyLogDirectory_Throws()
    {
        var options = new FileLoggerOptions { LogDirectory = "" };
        var act = () => new FileLoggerProvider(
            options, () => DateTimeOffset.Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EmptyFilePrefix_Throws()
    {
        var options = new FileLoggerOptions
        {
            LogDirectory = _tempDir,
            FilePrefix = "",
        };
        var act = () => new FileLoggerProvider(
            options, () => DateTimeOffset.Now);
        act.Should().Throw<ArgumentException>();
    }
}
