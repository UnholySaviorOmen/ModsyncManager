using FluentAssertions;
using Modsync.Platform.MO2.Readers;

namespace Modsync.Platform.MO2.Tests;

public class PluginsReaderTests
{
    [Fact]
    public void Parse_StarMeansEnabled()
    {
        var lines = new[]
        {
            "*Skyrim.esm",
            "*Update.esm",
            "SomeDisabled.esp",
        };

        var file = PluginsReader.Parse(lines);

        file.Entries.Should().HaveCount(3);
        file.Entries[0].Enabled.Should().BeTrue();
        file.Entries[0].Name.Should().Be("Skyrim.esm");
        file.Entries[2].Enabled.Should().BeFalse();
        file.Entries[2].Name.Should().Be("SomeDisabled.esp");
    }

    [Fact]
    public void Parse_SkipsCommentsAndBom()
    {
        var lines = new[]
        {
            "\uFEFF# This file is used by Skyrim to keep track of your downloaded content.",
            "# Please do not modify this file.",
            "*Skyrim.esm",
        };

        var file = PluginsReader.Parse(lines);

        file.Entries.Should().HaveCount(1);
        file.Entries[0].Name.Should().Be("Skyrim.esm");
    }

    [Fact]
    public void Parse_RealWorldSample_PreservesCase()
    {
        // Отрывок из реального plugins.txt: имена с разным регистром и .esl/.esm/.esp
        var lines = new[]
        {
            "*unofficial skyrim special edition patch.esp",
            "*unofficial skyrim creation club content patch.esl",
            "*WraithguardVaultFix - USCCCP Patch.esp",
            "*Requiem.esp",
            "*Requiem for the Indifferent.esp",
        };

        var file = PluginsReader.Parse(lines);

        file.Entries.Should().HaveCount(5);
        file.Entries[0].Name.Should().Be("unofficial skyrim special edition patch.esp");
        file.Entries[1].Name.Should().Be("unofficial skyrim creation club content patch.esl");
        file.Entries[2].Name.Should().Be("WraithguardVaultFix - USCCCP Patch.esp");
    }

    [Fact]
    public void Parse_SkipsEmptyLines()
    {
        var lines = new[] { "*Skyrim.esm", "", "  ", "*Update.esm" };
        var file = PluginsReader.Parse(lines);
        file.Entries.Should().HaveCount(2);
    }
}
