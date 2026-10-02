// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using ModsyncManager.NxmHandler;

namespace ModsyncManager.NxmHandler.Tests;

public class NxmHandlerArgsTests
{
    private const string ValidUrl =
        "nxm://skyrimspecialedition/mods/3863/files/1000172397" +
        "?key=abc&expires=100&user_id=1";

    // ------------------------------------------------------------------
    //  Happy path
    // ------------------------------------------------------------------

    [Fact]
    public void TryParse_SingleValidUrl_ReturnsArgs()
    {
        var args = NxmHandlerArgs.TryParse(new[] { ValidUrl });

        args.Should().NotBeNull();
        args!.NxmUrl.Should().Be(ValidUrl);
    }

    [Fact]
    public void TryParse_UrlWithoutCredentials_ReturnsArgs()
    {
        const string url = "nxm://skyrimspecialedition/mods/1/files/2";

        var args = NxmHandlerArgs.TryParse(new[] { url });

        args.Should().NotBeNull();
        args!.NxmUrl.Should().Be(url);
    }

    [Fact]
    public void TryParse_UppercaseScheme_ReturnsArgs()
    {
        const string url = "NXM://skyrimspecialedition/mods/1/files/2";

        var args = NxmHandlerArgs.TryParse(new[] { url });

        args.Should().NotBeNull();
        args!.NxmUrl.Should().Be(url);
    }

    [Fact]
    public void TryParse_ExtraArguments_IgnoresThem()
    {
        var args = NxmHandlerArgs.TryParse(new[] { ValidUrl, "extra", "junk" });

        args.Should().NotBeNull();
        args!.NxmUrl.Should().Be(ValidUrl);
    }

    // ------------------------------------------------------------------
    //  Invalid
    // ------------------------------------------------------------------

    [Fact]
    public void TryParse_Null_ReturnsNull()
    {
        NxmHandlerArgs.TryParse(null!).Should().BeNull();
    }

    [Fact]
    public void TryParse_EmptyArray_ReturnsNull()
    {
        NxmHandlerArgs.TryParse(Array.Empty<string>()).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    [InlineData("not-a-url")]
    [InlineData("nxm:")]
    [InlineData("nxm:/")]
    [InlineData("nxm")]
    public void TryParse_InvalidUrl_ReturnsNull(string url)
    {
        NxmHandlerArgs.TryParse(new[] { url }).Should().BeNull();
    }

    // ------------------------------------------------------------------
    //  Тест на устойчивость: TryParse не бросает
    // ------------------------------------------------------------------

    [Fact]
    public void TryParse_NeverThrows()
    {
        var act = () =>
        {
            NxmHandlerArgs.TryParse(null!);
            NxmHandlerArgs.TryParse(Array.Empty<string>());
            NxmHandlerArgs.TryParse(new[] { "" });
            NxmHandlerArgs.TryParse(new[] { "not nxm" });
            NxmHandlerArgs.TryParse(new[] { ValidUrl });
        };

        act.Should().NotThrow();
    }
}
