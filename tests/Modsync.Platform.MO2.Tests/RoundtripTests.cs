using FluentAssertions;
using Modsync.Core.Models.Mo2;
using Modsync.Platform.MO2.Readers;
using Modsync.Platform.MO2.Writers;

namespace Modsync.Platform.MO2.Tests;

public class RoundtripTests
{
    [Fact]
    public void Modlist_Roundtrip_PreservesData()
    {
        var original = new ModlistFile
        {
            Entries = new[]
            {
                new ModlistEntry("SkyUI", true),
                new ModlistEntry("# \U0001F4C2 Мои моды_separator", false),
                new ModlistEntry("Requiem", true),
                new ModlistEntry("[18+] - OStim Standalone", true),
            }
        };

        var tmp = Path.GetTempFileName();
        try
        {
            ModlistWriter.WriteFile(tmp, original);
            var read = ModlistReader.ReadFile(tmp);

            read.Entries.Should().HaveCount(original.Entries.Count);
            for (int i = 0; i < original.Entries.Count; i++)
                read.Entries[i].Should().Be(original.Entries[i]);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Modlist_WrittenFile_HasBomAndCrlf()
    {
        var file = new ModlistFile
        {
            Entries = new[] { new ModlistEntry("SkyUI", true) }
        };

        var tmp = Path.GetTempFileName();
        try
        {
            ModlistWriter.WriteFile(tmp, file);

            var bytes = File.ReadAllBytes(tmp);
            bytes[0].Should().Be(0xEF);
            bytes[1].Should().Be(0xBB);
            bytes[2].Should().Be(0xBF);

            var text = File.ReadAllText(tmp);
            text.Should().Contain("\r\n");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Plugins_Roundtrip_PreservesData()
    {
        var original = new PluginsFile
        {
            Entries = new[]
            {
                new PluginEntry("Skyrim.esm", true),
                new PluginEntry("Requiem.esp", true),
                new PluginEntry("SomeDisabled.esp", false),
            }
        };

        var tmp = Path.GetTempFileName();
        try
        {
            PluginsWriter.WriteFile(tmp, original);
            var read = PluginsReader.ReadFile(tmp);

            read.Entries.Should().HaveCount(original.Entries.Count);
            for (int i = 0; i < original.Entries.Count; i++)
                read.Entries[i].Should().Be(original.Entries[i]);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Loadorder_Roundtrip_PreservesData()
    {
        var original = new LoadorderFile
        {
            Plugins = new[] { "Skyrim.esm", "Update.esm", "Requiem.esp" }
        };

        var tmp = Path.GetTempFileName();
        try
        {
            LoadorderWriter.WriteFile(tmp, original);
            var read = LoadorderReader.ReadFile(tmp);

            read.Plugins.Should().Equal(original.Plugins);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
