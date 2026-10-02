// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Install.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Install.Tests;

public class RegenerateProfileStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _profilesDir;
    private readonly RegenerateProfileStep _step;

    public RegenerateProfileStepTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-install-rp-" + Guid.NewGuid());
        _profilesDir = Path.Combine(_tempDir, "profiles");
        Directory.CreateDirectory(_profilesDir);

        _step = new RegenerateProfileStep(
            NullLogger<RegenerateProfileStep>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------
    //  Хелперы
    // ------------------------------------------------------------------

    private static ModEntry MakeMod(string name, bool enabled, int order)
        => new()
        {
            Name = name,
            Enabled = enabled,
            Order = order,
            Directives = Array.Empty<Modsync.Core.Models.Manifest.Directives.Directive>(),
        };

    private static ModlistManifest MakeManifest(
        string profile = "Default",
        IReadOnlyList<ModEntry>? mods = null,
        IReadOnlyList<PluginEntry>? plugins = null,
        IReadOnlyList<string>? loadorder = null)
    {
        return new ModlistManifest
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
                Profile = profile,
                Archive = new ArchiveEntry
                {
                    Id = "mo2",
                    Name = "MO2.7z",
                    Size = 0,
                    Hash = new XxHash64Value(0),
                    Sources = Array.Empty<ArchiveSourceRef>(),
                },
                Extensions = Array.Empty<ExtensionEntry>(),
            },
            StockGame = new StockGameSection
            {
                Extras = Array.Empty<ExtensionEntry>(),
            },
            Archives = Array.Empty<ArchiveEntry>(),
            Mods = mods ?? Array.Empty<ModEntry>(),
            Plugins = plugins ?? Array.Empty<PluginEntry>(),
            Loadorder = loadorder ?? Array.Empty<string>(),
        };
    }

    private RegenerateProfileStep.Input MakeInput(ModlistManifest manifest)
        => new()
        {
            Manifest = manifest,
            ProfilesPath = _profilesDir,
        };

    // ------------------------------------------------------------------
    //  Happy path
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_EmptyManifest_CreatesEmptyFiles()
    {
        var manifest = MakeManifest();
        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.ProfilePath.Should().Be(Path.Combine(_profilesDir, "Default"));
        Directory.Exists(output.ProfilePath).Should().BeTrue();

        File.Exists(output.ModlistPath).Should().BeTrue();
        File.Exists(output.PluginsPath).Should().BeTrue();
        File.Exists(output.LoadorderPath).Should().BeTrue();

        output.ModlistCount.Should().Be(0);
        output.PluginsCount.Should().Be(0);
        output.LoadorderCount.Should().Be(0);
    }

    [Fact]
    public async Task Execute_CustomProfile_UsesProfileName()
    {
        var manifest = MakeManifest(profile: "NordicUI");
        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.ProfilePath.Should().Be(Path.Combine(_profilesDir, "NordicUI"));
        Directory.Exists(output.ProfilePath).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  Порядок модов (сохраняется как в манифесте, по Order ascending)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_ModsOrder_PreservedAsInManifest()
    {
        // order: 0, 1, 2 → в файле: 0, 1, 2 (сверху вниз).
        // Order = 0 — первая строка (верх оригинала).
        var manifest = MakeManifest(
            mods: new[]
            {
                MakeMod("First", enabled: true, order: 0),
                MakeMod("Second", enabled: false, order: 1),
                MakeMod("Third", enabled: true, order: 2),
            });

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        var lines = File.ReadAllLines(output.ModlistPath)
            .Where(l => !l.StartsWith("#"))
            .Where(l => l.Length > 0)
            .ToList();

        lines.Should().Equal("+First", "-Second", "+Third");
    }

    [Fact]
    public async Task Execute_ModsWithGapsInOrder_SortedByOrder()
    {
        // Order с дырками (из-за [NoDelete], исключённых из манифеста).
        var manifest = MakeManifest(
            mods: new[]
            {
                MakeMod("First", enabled: true, order: 0),
                MakeMod("Third", enabled: true, order: 2),
                MakeMod("Fifth", enabled: true, order: 4),
            });

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        var lines = File.ReadAllLines(output.ModlistPath)
            .Where(l => !l.StartsWith("#"))
            .Where(l => l.Length > 0)
            .ToList();

        lines.Should().Equal("+First", "+Third", "+Fifth");
    }

    [Fact]
    public async Task Execute_EnabledFlagWritten()
    {
        var manifest = MakeManifest(
            mods: new[]
            {
                MakeMod("A", enabled: true, order: 0),
                MakeMod("B", enabled: false, order: 1),
            });

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        var text = File.ReadAllText(output.ModlistPath);
        text.Should().Contain("+A");
        text.Should().Contain("-B");
    }

    // ------------------------------------------------------------------
    //  Сепараторы остаются
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_Separator_PreservedInModlist()
    {
        var manifest = MakeManifest(
            mods: new[]
            {
                MakeMod("Normal", enabled: true, order: 0),
                MakeMod("# \U0001F4C2 Мои моды_separator", enabled: false, order: 1),
                MakeMod("Another", enabled: true, order: 2),
            });

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        var text = File.ReadAllText(output.ModlistPath);
        text.Should().Contain("-# \U0001F4C2 Мои моды_separator");
        output.ModlistCount.Should().Be(3);
    }

    // ------------------------------------------------------------------
    //  Plugins
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_Plugins_AllWritten()
    {
        var manifest = MakeManifest(
            plugins: new[]
            {
                new PluginEntry { Name = "Skyrim.esm", Enabled = true, Order = 0 },
                new PluginEntry { Name = "Disabled.esp", Enabled = false, Order = 1 },
            });

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.PluginsCount.Should().Be(2);

        var text = File.ReadAllText(output.PluginsPath);
        text.Should().Contain("*Skyrim.esm");
        text.Should().Contain("Disabled.esp");
        text.Should().NotContain("*Disabled.esp");
    }

    // ------------------------------------------------------------------
    //  Loadorder
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_Loadorder_WrittenVerbatim()
    {
        var manifest = MakeManifest(
            loadorder: new[] { "Skyrim.esm", "Update.esm", "SkyUI.esp" });

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.LoadorderCount.Should().Be(3);

        var lines = File.ReadAllLines(output.LoadorderPath)
            .Where(l => !l.StartsWith("#"))
            .Where(l => l.Length > 0)
            .ToList();

        lines.Should().Equal("Skyrim.esm", "Update.esm", "SkyUI.esp");
    }

    // ------------------------------------------------------------------
    //  Перезапись
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_ExistingFiles_Overwritten()
    {
        var profileDir = Path.Combine(_profilesDir, "Default");
        Directory.CreateDirectory(profileDir);
        File.WriteAllText(Path.Combine(profileDir, "modlist.txt"), "old modlist");
        File.WriteAllText(Path.Combine(profileDir, "plugins.txt"), "old plugins");
        File.WriteAllText(Path.Combine(profileDir, "loadorder.txt"), "old loadorder");

        var manifest = MakeManifest(
            mods: new[] { MakeMod("Mod", enabled: true, order: 0) });

        await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        File.ReadAllText(Path.Combine(profileDir, "modlist.txt"))
            .Should().Contain("+Mod");
        File.ReadAllText(Path.Combine(profileDir, "modlist.txt"))
            .Should().NotContain("old modlist");
    }

    // ------------------------------------------------------------------
    //  Errors
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_EmptyProfileName_Throws()
    {
        var manifest = MakeManifest(profile: "");

        var act = async () => await _step.ExecuteAsync(
            MakeInput(manifest), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Profile*");
    }

    [Fact]
    public async Task Execute_CanceledToken_Throws()
    {
        var manifest = MakeManifest();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _step.ExecuteAsync(MakeInput(manifest), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
