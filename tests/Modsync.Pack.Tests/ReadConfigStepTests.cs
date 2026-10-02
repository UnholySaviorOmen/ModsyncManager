// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Pack.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Pack.Tests;

public class ReadConfigStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ReadConfigStep _step;

    public ReadConfigStepTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "modsyncmanager-rc-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _step = new ReadConfigStep(NullLogger<ReadConfigStep>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string WriteConfig(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private const string ValidJson = """
    {
      "meta": {
        "name": "Test Pack",
        "version": "1.0.0",
        "author": "tester",
        "game": "skyrimspecialedition",
        "gameVersion": "1.6.1170"
      },
      "instance": { "path": "Test Pack" },
      "mo2": {
        "version": "2.5.2",
        "profile": "Default",
        "archive": "Mod.Organizer-2.5.2.7z",
        "source": {
          "type": "mirror",
          "url": "https://example.com/Mod.Organizer-2.5.2.7z",
          "hash": "xxh64:0000000000000001"
        },
        "extensions": []
      },
      "stockGame": { "extras": [] },
      "archiveSources": []
    }
    """;

    [Fact]
    public async Task Execute_ValidConfig_ReturnsConfig()
    {
        var path = WriteConfig("valid.json", ValidJson);

        var config = await _step.ExecuteAsync(path, CancellationToken.None);

        config.Meta.Name.Should().Be("Test Pack");
        config.Instance.Path.Should().Be("Test Pack");
        config.Mo2.Profile.Should().Be("Default");
    }

    [Fact]
    public async Task Execute_NonExistentFile_Throws()
    {
        var act = async () => await _step.ExecuteAsync(
            Path.Combine(_tempDir, "missing.json"), CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Execute_EmptyPath_Throws()
    {
        var act = async () => await _step.ExecuteAsync("", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Execute_MalformedJson_Throws()
    {
        var path = WriteConfig("bad.json", "{ this is not json }");

        var act = async () => await _step.ExecuteAsync(path, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Failed to parse*");
    }

    [Fact]
    public async Task Execute_InvalidConfig_ReportsAllErrors()
    {
        var json = ValidJson
            .Replace("\"Test Pack\"", "\"CON\"")     // имя
            .Replace("\"1.0.0\"", "\"v1.0\"");        // версия

        var path = WriteConfig("invalid.json", json);

        var act = async () => await _step.ExecuteAsync(path, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();

        ex.Which.Message.Should().Contain("reserved");
        ex.Which.Message.Should().Contain("semver");
    }
}
