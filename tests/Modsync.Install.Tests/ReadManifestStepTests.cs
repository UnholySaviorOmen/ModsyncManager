// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Install.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Install.Tests;

public class ReadManifestStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ReadManifestStep _step;

    public ReadManifestStepTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-install-rm-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _step = new ReadManifestStep(NullLogger<ReadManifestStep>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string WriteManifest(string name, string json)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, json);
        return path;
    }

    private static ModlistManifest MakeManifest(string schema = "1.0.0")
        => new()
        {
            SchemaVersion = schema,
            ManifestVersion = "1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "modsyncmanager-pack/0.1.0",
            Meta = new ManifestMeta
            {
                Name = "Test Pack",
                Version = "1.0.0",
                Author = "tester",
                Game = "skyrimspecialedition",
                GameVersion = "1.6.1170",
            },
            Execution = new ExecutionPolicy(),
            Mo2 = new Mo2Section
            {
                Version = "2.5.2",
                Profile = "Default",
                Archive = new ArchiveEntry
                {
                    Id = "mo2",
                    Name = "MO2.7z",
                    Size = 1,
                    Hash = new XxHash64Value(0xabc),
                    Sources = Array.Empty<Core.Models.Manifest.Sources.ArchiveSourceRef>(),
                },
                Extensions = Array.Empty<ExtensionEntry>(),
            },
            StockGame = new StockGameSection
            {
                Extras = Array.Empty<ExtensionEntry>(),
            },
            Archives = Array.Empty<ArchiveEntry>(),
            Mods = Array.Empty<ModEntry>(),
            Plugins = Array.Empty<PluginEntry>(),
            Loadorder = Array.Empty<string>(),
        };

    [Fact]
    public async Task Execute_ValidManifest_ReturnsOutput()
    {
        var manifest = MakeManifest();
        var path = WriteManifest("modlist.json", ManifestJson.Serialize(manifest));

        var output = await _step.ExecuteAsync(path, CancellationToken.None);

        output.Manifest.Meta.Name.Should().Be("Test Pack");
        output.Manifest.SchemaVersion.Should().Be("1.0.0");
        output.ManifestPath.Should().Be(Path.GetFullPath(path));
    }

    [Fact]
    public async Task Execute_NonExistentFile_Throws()
    {
        var path = Path.Combine(_tempDir, "missing.json");

        var act = async () => await _step.ExecuteAsync(path, CancellationToken.None);

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
        var path = WriteManifest("bad.json", "{ not json }");

        var act = async () => await _step.ExecuteAsync(path, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Failed to parse*");
    }

    [Fact]
    public async Task Execute_UnsupportedSchema_Throws()
    {
        var manifest = MakeManifest(schema: "99.0.0");
        var path = WriteManifest("future.json", ManifestJson.Serialize(manifest));

        var act = async () => await _step.ExecuteAsync(path, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported schemaVersion*");
    }

    [Fact]
    public async Task Execute_CanceledToken_Throws()
    {
        var manifest = MakeManifest();
        var path = WriteManifest("modlist.json", ManifestJson.Serialize(manifest));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _step.ExecuteAsync(path, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
