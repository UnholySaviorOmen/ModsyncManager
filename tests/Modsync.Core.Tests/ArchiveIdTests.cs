using FluentAssertions;
using Modsync.Core.Identity;

namespace Modsync.Core.Tests;

public class ArchiveIdTests
{
    [Fact]
    public void FromNexus_FormatsCorrectly()
    {
        var id = ArchiveId.FromNexus("skyrimspecialedition", 3863, 1000172397);
        id.Should().Be("nexus_skyrimspecialedition_3863_1000172397");
    }

    [Fact]
    public void FromNexus_NormalizesGameDomainToLowercase()
    {
        var id = ArchiveId.FromNexus("SkyrimSpecialEdition", 3863, 1000172397);
        id.Should().Be("nexus_skyrimspecialedition_3863_1000172397");
    }

    [Fact]
    public void FromNexus_TrimsGameDomain()
    {
        var id = ArchiveId.FromNexus("  skyrimspecialedition  ", 3863, 1000172397);
        id.Should().Be("nexus_skyrimspecialedition_3863_1000172397");
    }

    [Fact]
    public void FromNexus_EmptyGame_Throws()
    {
        var act = () => ArchiveId.FromNexus("", 3863, 1000172397);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(100, 0)]
    [InlineData(100, -5)]
    public void FromNexus_NonPositiveIds_Throws(int modId, int fileId)
    {
        var act = () => ArchiveId.FromNexus("skyrimspecialedition", modId, fileId);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("SkyUI.7z", "local_skyui")]
    [InlineData("Some Mod Name.7z", "local_some-mod-name")]
    [InlineData("Mod-With-Dashes.7z", "local_mod-with-dashes")]
    public void FromLocal_FormatsCorrectly(string fileName, string expected)
    {
        ArchiveId.FromLocal(fileName).Should().Be(expected);
    }

    [Fact]
    public void FromLocal_Empty_Throws()
    {
        var act = () => ArchiveId.FromLocal("");
        act.Should().Throw<ArgumentException>();
    }
}
