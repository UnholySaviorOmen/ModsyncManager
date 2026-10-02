// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Install.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Install.Tests;

public class ResolveTargetStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _manifestDir;
    private readonly ResolveTargetStep _step;

    public ResolveTargetStepTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-install-rt-" + Guid.NewGuid());
        _manifestDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_manifestDir);
        _step = new ResolveTargetStep(NullLogger<ResolveTargetStep>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string WriteManifest(string name = "modlist.json")
    {
        var manifest = MakeManifest(name: "Test Pack");
        var path = Path.Combine(_manifestDir, name);
        File.WriteAllText(path, ManifestJson.Serialize(manifest));
        return path;
    }

    private static ModlistManifest MakeManifest(string name)
        => new()
        {
            SchemaVersion = "1.0.0",
            ManifestVersion = "1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "modsyncmanager-pack/0.1.0",
            Meta = new ManifestMeta
            {
                Name = name,
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

    // ------------------------------------------------------------------
    //  --target
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_WithExplicitTarget_UsesIt()
    {
        var manifestPath = WriteManifest();
        var manifest = ManifestJson.Deserialize(File.ReadAllText(manifestPath));

        var target = Path.Combine(_tempDir, "custom-target");

        var output = await _step.ExecuteAsync(
            new ResolveTargetStep.Input
            {
                Manifest = manifest,
                ManifestPath = manifestPath,
                Target = target,
            }, CancellationToken.None);

        output.InstancePath.Should().Be(Path.GetFullPath(target));
        Directory.Exists(target).Should().BeTrue();
        File.Exists(Path.Combine(target, "modlist.json")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  Auto-resolve
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_WithoutTarget_ResolvesToInstancesFolder()
    {
        var manifestPath = WriteManifest();
        var manifest = ManifestJson.Deserialize(File.ReadAllText(manifestPath));

        var output = await _step.ExecuteAsync(
            new ResolveTargetStep.Input
            {
                Manifest = manifest,
                ManifestPath = manifestPath,
                Target = null,
            }, CancellationToken.None);

        output.InstancePath.Should().Contain(Path.Combine(
            AppContext.BaseDirectory, "Instances", "Test Pack"));
        Directory.Exists(output.InstancePath).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  Копирование манифеста
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_CopiesManifestToInstance()
    {
        var manifestPath = WriteManifest();
        var manifest = ManifestJson.Deserialize(File.ReadAllText(manifestPath));
        var target = Path.Combine(_tempDir, "target");

        var output = await _step.ExecuteAsync(
            new ResolveTargetStep.Input
            {
                Manifest = manifest,
                ManifestPath = manifestPath,
                Target = target,
            }, CancellationToken.None);

        var copiedPath = Path.Combine(target, "modlist.json");
        File.Exists(copiedPath).Should().BeTrue();
        File.Exists(manifestPath).Should().BeTrue();

        var original = File.ReadAllText(manifestPath);
        var copied = File.ReadAllText(copiedPath);
        copied.Should().Be(original);
    }

    [Fact]
    public async Task Execute_ManifestAlreadyInPlace_DoesNotThrow()
    {
        var manifestPath = WriteManifest();
        var manifest = ManifestJson.Deserialize(File.ReadAllText(manifestPath));

        var output = await _step.ExecuteAsync(
            new ResolveTargetStep.Input
            {
                Manifest = manifest,
                ManifestPath = manifestPath,
                Target = _manifestDir,
            }, CancellationToken.None);

        output.InstancePath.Should().Be(Path.GetFullPath(_manifestDir));
        File.Exists(manifestPath).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_ExistingManifestInTarget_Overwrites()
    {
        var manifestPath = WriteManifest();
        var manifest = ManifestJson.Deserialize(File.ReadAllText(manifestPath));
        var target = Path.Combine(_tempDir, "target");
        Directory.CreateDirectory(target);

        File.WriteAllText(Path.Combine(target, "modlist.json"), "old content");

        await _step.ExecuteAsync(
            new ResolveTargetStep.Input
            {
                Manifest = manifest,
                ManifestPath = manifestPath,
                Target = target,
            }, CancellationToken.None);

        var overwritten = File.ReadAllText(Path.Combine(target, "modlist.json"));
        overwritten.Should().NotBe("old content");
        overwritten.Should().Contain("Test Pack");
    }

    // ------------------------------------------------------------------
    //  Валидация meta.name
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_InvalidMetaName_Throws()
    {
        var badManifest = MakeManifest(name: "CON");
        var manifestPath = Path.Combine(_manifestDir, "bad.json");
        File.WriteAllText(manifestPath, ManifestJson.Serialize(badManifest));

        var act = async () => await _step.ExecuteAsync(
            new ResolveTargetStep.Input
            {
                Manifest = badManifest,
                ManifestPath = manifestPath,
                Target = null,
            }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*meta.name*");
    }

    [Fact]
    public async Task Execute_CanceledToken_Throws()
    {
        var manifestPath = WriteManifest();
        var manifest = ManifestJson.Deserialize(File.ReadAllText(manifestPath));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _step.ExecuteAsync(
            new ResolveTargetStep.Input
            {
                Manifest = manifest,
                ManifestPath = manifestPath,
                Target = Path.Combine(_tempDir, "t"),
            }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
