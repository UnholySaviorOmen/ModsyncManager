using FluentAssertions;
using Modsync.Platform.Nexus;

namespace Modsync.Platform.Nexus.Tests;

public class NexusUrlParserTests
{
    // ------------------------------------------------------------------
    //  Happy path: без credentials (Premium-сценарий)
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_BareUrl_ReturnsGameModFile()
    {
        var url = "nxm://skyrimspecialedition/mods/3863/files/1000172397";

        var parsed = NexusUrlParser.Parse(url);

        parsed.Game.Should().Be("skyrimspecialedition");
        parsed.ModId.Should().Be(3863);
        parsed.FileId.Should().Be(1000172397);
        parsed.Key.Should().BeNull();
        parsed.Expires.Should().BeNull();
        parsed.UserId.Should().BeNull();
        parsed.HasFreeCredentials.Should().BeFalse();
    }

    [Fact]
    public void Parse_UrlWithTrailingSlash_Accepted()
    {
        var url = "nxm://skyrimspecialedition/mods/3863/files/1000172397/";

        var parsed = NexusUrlParser.Parse(url);

        parsed.ModId.Should().Be(3863);
        parsed.FileId.Should().Be(1000172397);
    }

    // ------------------------------------------------------------------
    //  Happy path: с credentials (Free-сценарий)
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_UrlWithCredentials_ReturnsAllFields()
    {
        var url = "nxm://skyrimspecialedition/mods/3863/files/1000172397" +
                  "?key=abc123def456&expires=1735689600&user_id=123456";

        var parsed = NexusUrlParser.Parse(url);

        parsed.Game.Should().Be("skyrimspecialedition");
        parsed.ModId.Should().Be(3863);
        parsed.FileId.Should().Be(1000172397);
        parsed.Key.Should().Be("abc123def456");
        parsed.Expires.Should().Be(1735689600);
        parsed.UserId.Should().Be(123456);
        parsed.HasFreeCredentials.Should().BeTrue();
    }

    [Fact]
    public void Parse_UrlWithExtraQueryParams_IgnoresThem()
    {
        // Nexus иногда добавляет file_name и nmm_version.
        var url = "nxm://skyrimspecialedition/mods/3863/files/1000172397" +
                  "?key=abc&expires=100&user_id=1" +
                  "&file_name=SkyUI.7z&nmm_version=0.16.0";

        var parsed = NexusUrlParser.Parse(url);

        parsed.Key.Should().Be("abc");
        parsed.Expires.Should().Be(100);
        parsed.UserId.Should().Be(1);
    }

    [Fact]
    public void Parse_QueryParamsInAnyOrder_Accepted()
    {
        var url = "nxm://skyrimspecialedition/mods/3863/files/1000172397" +
                  "?user_id=1&expires=100&key=abc";

        var parsed = NexusUrlParser.Parse(url);

        parsed.Key.Should().Be("abc");
        parsed.Expires.Should().Be(100);
        parsed.UserId.Should().Be(1);
    }

    // ------------------------------------------------------------------
    //  Регистр host
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_HostWithUppercase_LowercasedByUri()
    {
        // Uri нормализует host в lowercase — нам это ок,
        // Nexus game domain всегда lowercase.
        var url = "nxm://SkyrimSpecialEdition/mods/1/files/2";

        var parsed = NexusUrlParser.Parse(url);

        parsed.Game.Should().Be("skyrimspecialedition");
    }

    // ------------------------------------------------------------------
    //  Invalid: схема / host / path
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("http://skyrimspecialedition/mods/1/files/2")]
    [InlineData("https://skyrimspecialedition/mods/1/files/2")]
    [InlineData("ftp://skyrimspecialedition/mods/1/files/2")]
    public void Parse_WrongScheme_Throws(string url)
    {
        var act = () => NexusUrlParser.Parse(url);
        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("nxm:///mods/1/files/2")]
    [InlineData("nxm://mods/1/files/2")]
    public void Parse_MissingHost_Throws(string url)
    {
        var act = () => NexusUrlParser.Parse(url);
        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("nxm://game/mods/1/files/2/extra")]
    [InlineData("nxm://game/mods/1/files")]
    [InlineData("nxm://game/mods/1")]
    [InlineData("nxm://game/files/2")]
    [InlineData("nxm://game/mods/1/files/2/3")]
    public void Parse_WrongPathShape_Throws(string url)
    {
        var act = () => NexusUrlParser.Parse(url);
        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("nxm://game/modes/1/files/2")]
    [InlineData("nxm://game/mods/1/file/2")]
    [InlineData("nxm://game/Mods/1/Files/2")]  // регистр важен
    public void Parse_WrongPathKeywords_Throws(string url)
    {
        var act = () => NexusUrlParser.Parse(url);
        act.Should().Throw<FormatException>();
    }

    // ------------------------------------------------------------------
    //  Invalid: modId / fileId
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("nxm://game/mods/0/files/2")]
    [InlineData("nxm://game/mods/-1/files/2")]
    [InlineData("nxm://game/mods/abc/files/2")]
    [InlineData("nxm://game/mods//files/2")]
    [InlineData("nxm://game/mods/1.5/files/2")]
    public void Parse_InvalidModId_Throws(string url)
    {
        var act = () => NexusUrlParser.Parse(url);
        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("nxm://game/mods/1/files/0")]
    [InlineData("nxm://game/mods/1/files/-5")]
    [InlineData("nxm://game/mods/1/files/xyz")]
    public void Parse_InvalidFileId_Throws(string url)
    {
        var act = () => NexusUrlParser.Parse(url);
        act.Should().Throw<FormatException>();
    }

    // ------------------------------------------------------------------
    //  Invalid: неполные credentials
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_KeyOnly_Throws()
    {
        var url = "nxm://game/mods/1/files/2?key=abc";
        var act = () => NexusUrlParser.Parse(url);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_KeyAndExpires_NoUserId_Throws()
    {
        var url = "nxm://game/mods/1/files/2?key=abc&expires=100";
        var act = () => NexusUrlParser.Parse(url);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_ExpiresOnly_Throws()
    {
        var url = "nxm://game/mods/1/files/2?expires=100";
        var act = () => NexusUrlParser.Parse(url);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_UserIdOnly_Throws()
    {
        var url = "nxm://game/mods/1/files/2?user_id=1";
        var act = () => NexusUrlParser.Parse(url);
        act.Should().Throw<FormatException>();
    }

    // ------------------------------------------------------------------
    //  Invalid: некорректные значения credentials
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("?key=&expires=100&user_id=1")]                 // пустой key
    [InlineData("?key=abc&expires=0&user_id=1")]                // expires = 0
    [InlineData("?key=abc&expires=-1&user_id=1")]               // expires < 0
    [InlineData("?key=abc&expires=notanumber&user_id=1")]       // expires не число
    [InlineData("?key=abc&expires=100&user_id=0")]              // user_id = 0
    [InlineData("?key=abc&expires=100&user_id=-1")]             // user_id < 0
    [InlineData("?key=abc&expires=100&user_id=abc")]            // user_id не число
    public void Parse_InvalidCredentialValues_Throws(string query)
    {
        var url = "nxm://game/mods/1/files/2" + query;
        var act = () => NexusUrlParser.Parse(url);
        act.Should().Throw<FormatException>();
    }

    // ------------------------------------------------------------------
    //  Invalid: null / empty / мусор
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url at all")]
    [InlineData("nxm:")]
    [InlineData("nxm://")]
    public void Parse_EmptyOrGarbage_Throws(string? url)
    {
        var act = () => NexusUrlParser.Parse(url!);
        act.Should().Throw<FormatException>();
    }

    // ------------------------------------------------------------------
    //  TryParse
    // ------------------------------------------------------------------

    [Fact]
    public void TryParse_ValidUrl_ReturnsTrue()
    {
        var ok = NexusUrlParser.TryParse(
            "nxm://skyrimspecialedition/mods/1/files/2", out var result);

        ok.Should().BeTrue();
        result.Should().NotBeNull();
        result!.ModId.Should().Be(1);
    }

    [Fact]
    public void TryParse_InvalidUrl_ReturnsFalse_NoException()
    {
        var ok = NexusUrlParser.TryParse("http://example.com", out var result);

        ok.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryParse_Null_ReturnsFalse_NoException()
    {
        var ok = NexusUrlParser.TryParse(null, out var result);

        ok.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryParse_Empty_ReturnsFalse_NoException()
    {
        var ok = NexusUrlParser.TryParse("", out var result);

        ok.Should().BeFalse();
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    //  HasFreeCredentials
    // ------------------------------------------------------------------

    [Fact]
    public void HasFreeCredentials_TrueWhenAllThreePresent()
    {
        var parsed = NexusUrlParser.Parse(
            "nxm://g/mods/1/files/2?key=k&expires=100&user_id=1");
        parsed.HasFreeCredentials.Should().BeTrue();
    }

    [Fact]
    public void HasFreeCredentials_FalseWhenAbsent()
    {
        var parsed = NexusUrlParser.Parse("nxm://g/mods/1/files/2");
        parsed.HasFreeCredentials.Should().BeFalse();
    }
}
