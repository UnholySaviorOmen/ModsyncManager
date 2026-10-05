// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Install.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Install.Tests;

public class PreflightNexusAuthStepTests
{
    private static ModlistManifest MakeManifest(
        IReadOnlyList<ArchiveEntry>? archives = null,
        IReadOnlyList<ArchiveSourceRef>? mo2Sources = null)
        => new()
        {
            SchemaVersion = "1.0.0",
            ManifestVersion = "1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "modsyncmanager-pack/0.1.0",
            Meta = new ManifestMeta
            {
                Name = "Test",
                Version = "1.0.0",
                Author = "t",
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
                    Size = 0,
                    Hash = new XxHash64Value(0),
                    Sources = mo2Sources ?? new ArchiveSourceRef[]
                    {
                        new MirrorSourceRef
                        {
                            Url = "https://example.com/MO2.7z",
                            Hash = new XxHash64Value(0),
                        },
                    },
                },
                Extensions = Array.Empty<ExtensionEntry>(),
            },
            StockGame = new StockGameSection
            {
                Extras = Array.Empty<ExtensionEntry>(),
            },
            Archives = archives ?? Array.Empty<ArchiveEntry>(),
            Mods = Array.Empty<ModEntry>(),
            Plugins = Array.Empty<PluginEntry>(),
            Loadorder = Array.Empty<string>(),
        };

    private static ArchiveEntry MakeNexusArchive() => new()
    {
        Id = "nexus_skyrimspecialedition_1_1",
        Name = "SkyUI.7z",
        Size = 100,
        Hash = new XxHash64Value(0x1234),
        Sources = new ArchiveSourceRef[]
        {
            new NexusSourceRef
            {
                Game = "skyrimspecialedition",
                ModId = 1,
                FileId = 1,
            },
        },
    };

    private static ArchiveEntry MakeMirrorArchive() => new()
    {
        Id = "local_somemod",
        Name = "SomeMod.7z",
        Size = 100,
        Hash = new XxHash64Value(0x1234),
        Sources = new ArchiveSourceRef[]
        {
            new MirrorSourceRef
            {
                Url = "https://example.com/SomeMod.7z",
                Hash = new XxHash64Value(0x1234),
            },
        },
    };

    private static PreflightNexusAuthStep MakeStep(string? key)
        => new(
            new NexusApiKeyProviderStub(key),
            NullLogger<PreflightNexusAuthStep>.Instance);

    [Fact]
    public async Task Execute_NoNexusSources_DoesNotNeedKey()
    {
        var step = MakeStep(key: null);
        var manifest = MakeManifest(archives: new[] { MakeMirrorArchive() });

        var output = await step.ExecuteAsync(
            new PreflightNexusAuthStep.Input { Manifest = manifest },
            CancellationToken.None);

        output.NeedsNexus.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_NexusSource_KeyPresent_Passes()
    {
        var step = MakeStep(key: "test-key");
        var manifest = MakeManifest(archives: new[] { MakeNexusArchive() });

        var output = await step.ExecuteAsync(
            new PreflightNexusAuthStep.Input { Manifest = manifest },
            CancellationToken.None);

        output.NeedsNexus.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_NexusSource_KeyMissing_Throws()
    {
        var step = MakeStep(key: null);
        var manifest = MakeManifest(archives: new[] { MakeNexusArchive() });

        var act = async () => await step.ExecuteAsync(
            new PreflightNexusAuthStep.Input { Manifest = manifest },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Settings → Nexus*")
            .WithMessage("*API key*");
    }

    [Fact]
    public async Task Execute_EmptyKey_TreatedAsMissing()
    {
        var step = MakeStep(key: "   ");
        var manifest = MakeManifest(archives: new[] { MakeNexusArchive() });

        var act = async () => await step.ExecuteAsync(
            new PreflightNexusAuthStep.Input { Manifest = manifest },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Execute_Mo2ArchiveWithNexusSource_KeyMissing_Throws()
    {
        var step = MakeStep(key: null);
        var manifest = MakeManifest(
            mo2Sources: new ArchiveSourceRef[]
            {
                new NexusSourceRef
                {
                    Game = "skyrimspecialedition",
                    ModId = 1,
                    FileId = 1,
                },
            });

        var act = async () => await step.ExecuteAsync(
            new PreflightNexusAuthStep.Input { Manifest = manifest },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Execute_CanceledToken_Throws()
    {
        var step = MakeStep(key: "test-key");
        var manifest = MakeManifest();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await step.ExecuteAsync(
            new PreflightNexusAuthStep.Input { Manifest = manifest },
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
