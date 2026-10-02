using System.Text.Json;
using FluentAssertions;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Core.Models.Pack;
using Modsync.Core.Validation;

namespace Modsync.Core.Tests;

public class PackConfigSamplesTests
{
    private static string SamplesDir
        => Path.Combine(AppContext.BaseDirectory, "samples");

    private static string SamplePath(string fileName)
        => Path.Combine(SamplesDir, fileName);

    [Fact]
    public void SamplesDirectory_Exists()
    {
        Directory.Exists(SamplesDir).Should().BeTrue(
            $"samples directory should be copied to output: {SamplesDir}");
    }

    [Fact]
    public async Task Minimal_LoadsAndValidates()
    {
        var path = SamplePath("modsyncmanager-pack.minimal.json");
        File.Exists(path).Should().BeTrue($"file not found: {path}");

        var config = await PackConfigJson.LoadAsync(path, CancellationToken.None);

        config.Meta.Name.Should().Be("Minimal Pack");
        config.Instance.Path.Should().Be("Minimal Pack");
        config.Mo2.Profile.Should().Be("Default");
        config.Mo2.Source.Should().BeOfType<MirrorSourceRef>();

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeTrue(
            $"errors: {string.Join("; ", result.Errors)}");
    }

    [Fact]
    public async Task Full_LoadsAndValidates()
    {
        var path = SamplePath("modsyncmanager-pack.full.json");
        File.Exists(path).Should().BeTrue($"file not found: {path}");

        var config = await PackConfigJson.LoadAsync(path, CancellationToken.None);

        config.Meta.Name.Should().Be("Nordic UI Overhaul");
        config.Instance.Path.Should().Be("NordicUI Overhaul");
        config.Mo2.Profile.Should().Be("NordicUI");
        config.Mo2.Extensions.Should().HaveCount(3);
        config.ArchiveSources.Should().HaveCount(2);

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeTrue(
            $"errors: {string.Join("; ", result.Errors)}");
    }

    [Fact]
    public async Task InvalidName_LoadsButFailsValidation()
    {
        var config = await PackConfigJson.LoadAsync(
            SamplePath("modsyncmanager-pack.invalid-name.json"), CancellationToken.None);

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("reserved"));
    }

    [Fact]
    public async Task InvalidVersion_LoadsButFailsValidation()
    {
        var config = await PackConfigJson.LoadAsync(
            SamplePath("modsyncmanager-pack.invalid-version.json"), CancellationToken.None);

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("semver"));
    }

    [Fact]
    public async Task InvalidPath_LoadsButFailsValidation()
    {
        var config = await PackConfigJson.LoadAsync(
            SamplePath("modsyncmanager-pack.invalid-path.json"), CancellationToken.None);

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains(".."));
    }

    [Fact]
    public void AllSamples_AreValidJson()
    {
        var files = Directory.GetFiles(SamplesDir, "*.json");
        files.Should().NotBeEmpty();

        foreach (var file in files)
        {
            var act = () =>
            {
                var text = File.ReadAllText(file);
                using var doc = JsonDocument.Parse(text);
            };

            act.Should().NotThrow($"file should be valid JSON: {file}");
        }
    }
}
