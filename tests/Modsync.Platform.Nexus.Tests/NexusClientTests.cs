using System.Net;
using System.Net.Http;
using FluentAssertions;
using Modsync.Platform.Nexus;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Platform.Nexus.Tests;

public class NexusClientTests
{
    private const string TestKey = "test-api-key-12345";

    private static NexusClient MakeClient(
        FakeHttpMessageHandler handler,
        string? key = TestKey)
    {
        var http = new HttpClient(handler);
        var provider = new FakeNexusApiKeyProvider(key);
        return new NexusClient(http, provider, NullLogger<NexusClient>.Instance);
    }

    // ------------------------------------------------------------------
    //  Validate / IsPremiumAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task IsPremiumAsync_PremiumAccount_ReturnsTrue()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"is_premium":true,"name":"testuser"}""");

        var client = MakeClient(handler);

        var result = await client.IsPremiumAsync(CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsPremiumAsync_FreeAccount_ReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"is_premium":false,"name":"testuser"}""");

        var client = MakeClient(handler);

        var result = await client.IsPremiumAsync(CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsPremiumAsync_SendsApiKeyHeader()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"is_premium":true,"name":"testuser"}""");

        var client = MakeClient(handler);

        await client.IsPremiumAsync(CancellationToken.None);

        handler.Requests.Should().HaveCount(1);
        var request = handler.Requests[0];
        request.Headers.GetValues("apikey")
            .Should().ContainSingle().Which.Should().Be(TestKey);
    }

    [Fact]
    public async Task IsPremiumAsync_SendsApplicationHeaders()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"is_premium":true,"name":"testuser"}""");

        var client = MakeClient(handler);

        await client.IsPremiumAsync(CancellationToken.None);

        var request = handler.Requests[0];
        request.Headers.GetValues("Application-Name")
            .Should().ContainSingle().Which.Should().Be("ModsyncManager");
        request.Headers.GetValues("Application-Version")
            .Should().ContainSingle().Which.Should().Be("0.1.0");
        request.Headers.GetValues("User-Agent")
            .Should().ContainSingle().Which.Should().Be("ModsyncManager/0.1.0");
    }

    [Fact]
    public async Task IsPremiumAsync_RequestsCorrectUrl()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"is_premium":true,"name":"testuser"}""");

        var client = MakeClient(handler);

        await client.IsPremiumAsync(CancellationToken.None);

        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api.nexusmods.com/v1/users/validate.json");
    }

    [Fact]
    public async Task IsPremiumAsync_NoKey_ThrowsNexusAuthenticationException()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"is_premium":true,"name":"testuser"}""");

        var client = MakeClient(handler, key: null);

        var act = async () => await client.IsPremiumAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NexusAuthenticationException>();
    }

    [Fact]
    public async Task IsPremiumAsync_EmptyKey_ThrowsNexusAuthenticationException()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"is_premium":true,"name":"testuser"}""");

        var client = MakeClient(handler, key: "   ");

        var act = async () => await client.IsPremiumAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NexusAuthenticationException>();
    }

    [Fact]
    public async Task IsPremiumAsync_401_ThrowsNexusAuthenticationException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized);
        var client = MakeClient(handler);

        var act = async () => await client.IsPremiumAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NexusAuthenticationException>();
    }

    [Fact]
    public async Task IsPremiumAsync_429_Throws()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.TooManyRequests);
        var client = MakeClient(handler);

        var act = async () => await client.IsPremiumAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*rate limit*");
    }

    [Fact]
    public async Task IsPremiumAsync_500_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.InternalServerError);
        var client = MakeClient(handler);

        var act = async () => await client.IsPremiumAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*500*");
    }

    // ------------------------------------------------------------------
    //  GetValidateInfoAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetValidateInfoAsync_ReturnsUserIdAndPremium()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"is_premium":true,"name":"testuser","user_id":123456}""");

        var client = MakeClient(handler);

        var info = await client.GetValidateInfoAsync(CancellationToken.None);

        info.IsPremium.Should().BeTrue();
        info.Name.Should().Be("testuser");
        info.UserId.Should().Be(123456);
    }

    [Fact]
    public async Task GetValidateInfoAsync_Cached_SingleHttpCall()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"is_premium":true,"name":"testuser","user_id":1}""");

        var client = MakeClient(handler);

        await client.GetValidateInfoAsync(CancellationToken.None);
        await client.GetValidateInfoAsync(CancellationToken.None);
        await client.IsPremiumAsync(CancellationToken.None);

        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetValidateInfoAsync_EmptyUserId_ParsedAsZero()
    {
        // Nexus может не отдать user_id для некоторых аккаунтов.
        // Десериализуется в 0 по умолчанию.
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"is_premium":false,"name":"freeuser"}""");

        var client = MakeClient(handler);

        var info = await client.GetValidateInfoAsync(CancellationToken.None);

        info.UserId.Should().Be(0);
    }

    [Fact]
    public async Task GetValidateInfoAsync_NoKey_ThrowsNexusAuthenticationException()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"is_premium":true,"name":"testuser","user_id":1}""");

        var client = MakeClient(handler, key: null);

        var act = async () => await client.GetValidateInfoAsync(
            CancellationToken.None);

        await act.Should().ThrowAsync<NexusAuthenticationException>();
    }

    // ------------------------------------------------------------------
    //  GetDownloadLinksAsync — базовая ветка (без nxm)
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetDownloadLinksAsync_ReturnsAllLinks()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """
            [
              {"name":"CDN","short_name":"cd","URI":"https://cdn1.example.com/file.7z"},
              {"name":"CDN2","short_name":"cd2","URI":"https://cdn2.example.com/file.7z"}
            ]
            """);

        var client = MakeClient(handler);

        var links = await client.GetDownloadLinksAsync(
            "skyrimspecialedition", 3863, 1000172397,
            ct: CancellationToken.None);

        links.Should().HaveCount(2);
        links[0].Name.Should().Be("CDN");
        links[0].Uri!.ToString().Should().Be("https://cdn1.example.com/file.7z");
        links[1].Uri!.ToString().Should().Be("https://cdn2.example.com/file.7z");
    }

    [Fact]
    public async Task GetDownloadLinksAsync_FiltersLinksWithoutUri()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """
            [
              {"name":"CDN","URI":"https://cdn1.example.com/file.7z"},
              {"name":"Broken","URI":null},
              {"name":"AlsoBroken"}
            ]
            """);

        var client = MakeClient(handler);

        var links = await client.GetDownloadLinksAsync(
            "skyrimspecialedition", 3863, 1000172397,
            ct: CancellationToken.None);

        links.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetDownloadLinksAsync_EmptyArray_ReturnsEmpty()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = MakeClient(handler);

        var links = await client.GetDownloadLinksAsync(
            "skyrimspecialedition", 3863, 1000172397,
            ct: CancellationToken.None);

        links.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDownloadLinksAsync_RequestsCorrectUrl()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = MakeClient(handler);

        await client.GetDownloadLinksAsync(
            "skyrimspecialedition", 3863, 1000172397,
            ct: CancellationToken.None);

        handler.Requests[0].RequestUri!.ToString().Should().Be(
            "https://api.nexusmods.com/v1/games/skyrimspecialedition/mods/3863/files/1000172397/download_link.json");
    }

    [Fact]
    public async Task GetDownloadLinksAsync_SendsApiKeyHeader()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = MakeClient(handler);

        await client.GetDownloadLinksAsync(
            "skyrimspecialedition", 3863, 1000172397,
            ct: CancellationToken.None);

        handler.Requests[0].Headers.GetValues("apikey")
            .Should().ContainSingle().Which.Should().Be(TestKey);
    }

    [Fact]
    public async Task GetDownloadLinksAsync_403_ThrowsPremiumRequired()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Forbidden);
        var client = MakeClient(handler);

        var act = async () => await client.GetDownloadLinksAsync(
            "skyrimspecialedition", 3863, 1000172397,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Premium*");
    }

    [Fact]
    public async Task GetDownloadLinksAsync_404_ThrowsNotFound()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.NotFound);
        var client = MakeClient(handler);

        var act = async () => await client.GetDownloadLinksAsync(
            "skyrimspecialedition", 3863, 1000172397,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*")
            .WithMessage("*3863*");
    }

    [Fact]
    public async Task GetDownloadLinksAsync_401_ThrowsNexusAuthenticationException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized);
        var client = MakeClient(handler);

        var act = async () => await client.GetDownloadLinksAsync(
            "skyrimspecialedition", 3863, 1000172397,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NexusAuthenticationException>();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public async Task GetDownloadLinksAsync_NonPositiveIds_Throws(int modId, int fileId)
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = MakeClient(handler);

        var act = async () => await client.GetDownloadLinksAsync(
            "skyrimspecialedition", modId, fileId,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetDownloadLinksAsync_EmptyGame_Throws()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = MakeClient(handler);

        var act = async () => await client.GetDownloadLinksAsync(
            "", 3863, 1000172397,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ------------------------------------------------------------------
    //  GetDownloadLinksAsync — с nxmKey/nxmExpires
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetDownloadLinksAsync_NoNxmKey_UrlWithoutQuery()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = MakeClient(handler);

        await client.GetDownloadLinksAsync(
            "skyrimspecialedition", 3863, 1000172397,
            ct: CancellationToken.None);

        handler.Requests[0].RequestUri!.ToString().Should().Be(
            "https://api.nexusmods.com/v1/games/skyrimspecialedition/mods/3863/files/1000172397/download_link.json");
    }

    [Fact]
    public async Task GetDownloadLinksAsync_WithNxmKey_UrlWithQuery()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = MakeClient(handler);

        await client.GetDownloadLinksAsync(
            "skyrimspecialedition", 3863, 1000172397,
            nxmKey: "abc123def456",
            nxmExpires: 1735689600,
            ct: CancellationToken.None);

        handler.Requests[0].RequestUri!.ToString().Should().Be(
            "https://api.nexusmods.com/v1/games/skyrimspecialedition/mods/3863/files/1000172397/download_link.json?key=abc123def456&expires=1735689600");
    }

    [Fact]
    public async Task GetDownloadLinksAsync_WithNxmKey_SendsApiKeyHeader()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = MakeClient(handler);

        await client.GetDownloadLinksAsync(
            "skyrimspecialedition", 3863, 1000172397,
            nxmKey: "abc", nxmExpires: 100,
            ct: CancellationToken.None);

        handler.Requests[0].Headers.GetValues("apikey")
            .Should().ContainSingle().Which.Should().Be(TestKey);
    }

    [Fact]
    public async Task GetDownloadLinksAsync_NxmKeyWithSpecialChars_UrlEncoded()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = MakeClient(handler);

        // Ключ может содержать +, /, = (base64-подобный).
        await client.GetDownloadLinksAsync(
            "skyrimspecialedition", 3863, 1000172397,
            nxmKey: "abc+def/ghi==",
            nxmExpires: 100,
            ct: CancellationToken.None);

        var url = handler.Requests[0].RequestUri!.ToString();
        url.Should().Contain("key=abc%2Bdef%2Fghi%3D%3D");
        url.Should().NotContain("key=abc+def");
    }

    [Fact]
    public async Task GetDownloadLinksAsync_KeyOnly_Throws()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = MakeClient(handler);

        var act = async () => await client.GetDownloadLinksAsync(
            "skyrimspecialedition", 3863, 1000172397,
            nxmKey: "abc", nxmExpires: null,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*both*");
    }

    [Fact]
    public async Task GetDownloadLinksAsync_ExpiresOnly_Throws()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = MakeClient(handler);

        var act = async () => await client.GetDownloadLinksAsync(
            "skyrimspecialedition", 3863, 1000172397,
            nxmKey: null, nxmExpires: 100,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*both*");
    }

    [Fact]
    public async Task GetDownloadLinksAsync_EmptyNxmKey_TreatedAsNull()
    {
        // Пустой key эквивалентен отсутствию, но expires задан → несоответствие.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = MakeClient(handler);

        var act = async () => await client.GetDownloadLinksAsync(
            "skyrimspecialedition", 3863, 1000172397,
            nxmKey: "", nxmExpires: 100,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetDownloadLinksAsync_WithNxmKey_403_ThrowsPremiumMessage()
    {
        // Даже с key+expires, если user_id не совпадает с apikey,
        // Nexus вернёт 403.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Forbidden);
        var client = MakeClient(handler);

        var act = async () => await client.GetDownloadLinksAsync(
            "skyrimspecialedition", 3863, 1000172397,
            nxmKey: "abc", nxmExpires: 100,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Premium*");
    }
}
