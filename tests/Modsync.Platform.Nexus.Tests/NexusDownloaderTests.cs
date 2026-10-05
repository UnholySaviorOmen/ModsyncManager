// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using FluentAssertions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Platform.Nexus;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Platform.Nexus.Tests;

public class NexusDownloaderTests
{
    private const string TestKey = "test-api-key-12345";
    private const int TestUserId = 123456;

    // ------------------------------------------------------------------
    //  Routing handler: API vs CDN
    // ------------------------------------------------------------------

    private sealed class RoutingHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? ApiHandler { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? CdnHandler { get; set; }

        public List<HttpRequestMessage> ApiRequests { get; } = new();
        public List<HttpRequestMessage> CdnRequests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var host = request.RequestUri!.Host;
            if (host == "api.nexusmods.com")
            {
                ApiRequests.Add(request);
                if (ApiHandler is null)
                    throw new InvalidOperationException("Unexpected API request");
                return Task.FromResult(ApiHandler(request));
            }
            else
            {
                CdnRequests.Add(request);
                if (CdnHandler is null)
                    throw new InvalidOperationException("Unexpected CDN request");
                return Task.FromResult(CdnHandler(request));
            }
        }
    }

    private static (NexusDownloader downloader,
                    RoutingHandler handler,
                    FakeNexusFreeNxmProvider nxmProvider)
        MakeDownloader(
            Func<HttpRequestMessage, HttpResponseMessage>? api = null,
            Func<HttpRequestMessage, HttpResponseMessage>? cdn = null,
            string? key = TestKey,
            FakeNexusFreeNxmProvider? nxmProvider = null)
    {
        var handler = new RoutingHandler { ApiHandler = api, CdnHandler = cdn };
        var http = new HttpClient(handler);

        var factory = new FakeHttpClientFactory();
        factory.Register("nexus-api", handler);
        factory.Register("nexus", handler);

        var provider = new FakeNexusApiKeyProvider(key);
        var client = new NexusClient(
            http, provider, NullLogger<NexusClient>.Instance);

        var nxm = nxmProvider ?? new FakeNexusFreeNxmProvider();

        var downloader = new NexusDownloader(
            factory, client, nxm,
            NullLogger<NexusDownloader>.Instance);

        return (downloader, handler, nxm);
    }

    // ------------------------------------------------------------------
    //  Helpers: responses
    // ------------------------------------------------------------------

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code)
        {
            Content = new StringContent(
                body, System.Text.Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage PremiumValidate() =>
        Json(HttpStatusCode.OK,
            $$"""{"is_premium":true,"name":"user","user_id":{{TestUserId}}}""");

    private static HttpResponseMessage FreeValidate() =>
        Json(HttpStatusCode.OK,
            $$"""{"is_premium":false,"name":"user","user_id":{{TestUserId}}}""");

    private static HttpResponseMessage DownloadLinks(params string[] uris)
    {
        var items = uris.Select(u =>
            $$"""{"name":"CDN","short_name":"cd","URI":"{{u}}"}""");
        return Json(HttpStatusCode.OK, "[" + string.Join(",", items) + "]");
    }

    private static NexusSourceRef Source(int modId = 3863, int fileId = 1000172397) =>
        new()
        {
            Game = "skyrimspecialedition",
            ModId = modId,
            FileId = fileId,
        };

    private static HttpResponseMessage BinaryCdn(byte[] content) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
        };

    // ------------------------------------------------------------------
    //  Premium: базовые сценарии
    // ------------------------------------------------------------------

    [Fact]
    public async Task Premium_ValidateThenDownloadLinksThenCdn_Succeeds()
    {
        var content = new byte[] { 1, 2, 3, 4, 5 };
        var apiCall = 0;

        var (downloader, handler, nxm) = MakeDownloader(
            api: _ =>
            {
                apiCall++;
                if (apiCall == 1) return PremiumValidate();
                if (apiCall == 2)
                    return DownloadLinks("https://cdn1.example.com/file.7z");
                throw new InvalidOperationException("Unexpected API call");
            },
            cdn: _ => BinaryCdn(content));

        await using var stream = await downloader.DownloadAsync(
            Source(), CancellationToken.None);

        var buffer = new byte[content.Length];
        await stream.ReadExactlyAsync(buffer);
        buffer.Should().Equal(content);

        // Провайдер не спрашивали.
        nxm.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Premium_FallsBackToSecondCdnNode_WhenFirstFails()
    {
        var content = new byte[] { 42 };
        var cdnCall = 0;
        var apiCall = 0;

        var (downloader, handler, _) = MakeDownloader(
            api: _ =>
            {
                apiCall++;
                if (apiCall == 1) return PremiumValidate();
                return DownloadLinks(
                    "https://cdn1.example.com/file.7z",
                    "https://cdn2.example.com/file.7z");
            },
            cdn: _ =>
            {
                cdnCall++;
                if (cdnCall == 1)
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                return BinaryCdn(content);
            });

        await using var stream = await downloader.DownloadAsync(
            Source(), CancellationToken.None);

        var buffer = new byte[content.Length];
        await stream.ReadExactlyAsync(buffer);
        buffer.Should().Equal(content);
    }

    [Fact]
    public async Task Premium_AllCdnNodesFail_Throws()
    {
        var apiCall = 0;
        var (downloader, _, _) = MakeDownloader(
            api: _ =>
            {
                apiCall++;
                if (apiCall == 1) return PremiumValidate();
                return DownloadLinks(
                    "https://cdn1.example.com/file.7z",
                    "https://cdn2.example.com/file.7z");
            },
            cdn: _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var act = async () => await downloader.DownloadAsync(
            Source(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*All 2 Nexus CDN nodes failed*");
    }

    // ------------------------------------------------------------------
    //  Free: успешный сценарий
    // ------------------------------------------------------------------

    [Fact]
    public async Task Free_UserProvidesNxm_DownloadsViaKeyExpires()
    {
        var content = new byte[] { 10, 20, 30 };
        var apiCall = 0;

        var nxm = new FakeNexusFreeNxmProvider
        {
            ResultToReturn = FakeNexusFreeNxmProvider.MakeNxmUrl(
                "skyrimspecialedition", 3863, 1000172397,
                key: "tmpkey123", expires: 1735689600, userId: TestUserId),
        };

        Uri? downloadLinksUri = null;

        var (downloader, _, _) = MakeDownloader(
            api: req =>
            {
                apiCall++;
                if (apiCall == 1) return FreeValidate();
                if (apiCall == 2)
                {
                    downloadLinksUri = req.RequestUri;
                    return DownloadLinks("https://cdn1.example.com/file.7z");
                }
                throw new InvalidOperationException("Unexpected API call");
            },
            cdn: _ => BinaryCdn(content),
            nxmProvider: nxm);

        await using var stream = await downloader.DownloadAsync(
            Source(), CancellationToken.None);

        var buffer = new byte[content.Length];
        await stream.ReadExactlyAsync(buffer);
        buffer.Should().Equal(content);

        // Провайдер спросили ровно один раз с корректными параметрами.
        nxm.CallCount.Should().Be(1);
        nxm.LastGame.Should().Be("skyrimspecialedition");
        nxm.LastModId.Should().Be(3863);
        nxm.LastFileId.Should().Be(1000172397);

        // download_link вызван с key и expires.
        downloadLinksUri.Should().NotBeNull();
        downloadLinksUri!.ToString().Should().Contain("?key=tmpkey123");
        downloadLinksUri.ToString().Should().Contain("&expires=1735689600");
    }

    // ------------------------------------------------------------------
    //  Free: провайдер вернул null (пользователь закрыл браузер)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Free_UserClosesWebView_Throws()
    {
        var nxm = new FakeNexusFreeNxmProvider { ResultToReturn = null };

        var (downloader, _, _) = MakeDownloader(
            api: _ => FreeValidate(),
            nxmProvider: nxm);

        var act = async () => await downloader.DownloadAsync(
            Source(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cancelled by the user*")
            .WithMessage("*Restart the installation*");
    }

    // ------------------------------------------------------------------
    //  Free: провайдер вернул битый URL
    // ------------------------------------------------------------------

    [Fact]
    public async Task Free_InvalidNxmUrl_Throws()
    {
        var nxm = new FakeNexusFreeNxmProvider
        {
            ResultToReturn = "not-an-nxm-url",
        };

        var (downloader, _, _) = MakeDownloader(
            api: _ => FreeValidate(),
            nxmProvider: nxm);

        var act = async () => await downloader.DownloadAsync(
            Source(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*invalid nxm:// URL*");
    }

    // ------------------------------------------------------------------
    //  Free: провайдер вернул URL для другого файла
    // ------------------------------------------------------------------

    [Fact]
    public async Task Free_NxmUrlForDifferentFile_Throws()
    {
        var nxm = new FakeNexusFreeNxmProvider
        {
            ResultToReturn = FakeNexusFreeNxmProvider.MakeNxmUrl(
                "skyrimspecialedition", 9999, 1000172397,
                key: "k", expires: 100, userId: TestUserId),
        };

        var (downloader, _, _) = MakeDownloader(
            api: _ => FreeValidate(),
            nxmProvider: nxm);

        var act = async () => await downloader.DownloadAsync(
            Source(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different file*")
            .WithMessage("*9999*");
    }

    // ------------------------------------------------------------------
    //  Free: nxm:// без key/expires (Nexus не сгенерил)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Free_NxmWithoutCredentials_Throws()
    {
        var nxm = new FakeNexusFreeNxmProvider
        {
            // URL без query-параметров.
            ResultToReturn = FakeNexusFreeNxmProvider.MakeNxmUrl(
                "skyrimspecialedition", 3863, 1000172397),
        };

        var (downloader, _, _) = MakeDownloader(
            api: _ => FreeValidate(),
            nxmProvider: nxm);

        var act = async () => await downloader.DownloadAsync(
            Source(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*did not generate temporary credentials*")
            .WithMessage("*logged in*");
    }

    // ------------------------------------------------------------------
    //  Free: user_id в nxm:// не совпадает с user_id API-ключа
    // ------------------------------------------------------------------

    [Fact]
    public async Task Free_UserIdMismatch_Throws()
    {
        var nxm = new FakeNexusFreeNxmProvider
        {
            ResultToReturn = FakeNexusFreeNxmProvider.MakeNxmUrl(
                "skyrimspecialedition", 3863, 1000172397,
                key: "k", expires: 100,
                userId: TestUserId + 1),  // другой user_id
        };

        var (downloader, _, _) = MakeDownloader(
            api: _ => FreeValidate(),
            nxmProvider: nxm);

        var act = async () => await downloader.DownloadAsync(
            Source(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{TestUserId}*")
            .WithMessage($"*{TestUserId + 1}*")
            .WithMessage("*same account*");
    }

    // ------------------------------------------------------------------
    //  Free: провайдер бросил исключение
    // ------------------------------------------------------------------

    [Fact]
    public async Task Free_ProviderThrows_PropagatesException()
    {
        var nxm = new FakeNexusFreeNxmProvider
        {
            ExceptionToThrow = new InvalidOperationException(
                "WebView2 Runtime not installed"),
        };

        var (downloader, _, _) = MakeDownloader(
            api: _ => FreeValidate(),
            nxmProvider: nxm);

        var act = async () => await downloader.DownloadAsync(
            Source(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*WebView2 Runtime not installed*");
    }

    // ------------------------------------------------------------------
    //  Free: отмена через ct
    // ------------------------------------------------------------------

    [Fact]
    public async Task Free_CancelledToken_Throws()
    {
        var nxm = new FakeNexusFreeNxmProvider
        {
            Gate = new TaskCompletionSource(),
        };

        var (downloader, _, _) = MakeDownloader(
            api: _ => FreeValidate(),
            nxmProvider: nxm);

        using var cts = new CancellationTokenSource();

        var task = downloader.DownloadAsync(Source(), cts.Token);

        // Даём провайдеру дойти до Gate.
        await Task.Delay(50);
        cts.Cancel();

        var act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ------------------------------------------------------------------
    //  Invalid source type
    // ------------------------------------------------------------------

    [Fact]
    public async Task DownloadAsync_WrongSourceType_Throws()
    {
        var (downloader, _, _) = MakeDownloader();

        var wrongSource = new MirrorSourceRef
        {
            Url = "https://example.com/x.7z",
            Hash = new XxHash64Value(0xabc),
        };

        var act = async () => await downloader.DownloadAsync(
            wrongSource, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*NexusSourceRef*");
    }

    // ------------------------------------------------------------------
    //  IsPermanentFailure
    // ------------------------------------------------------------------

    [Fact]
    public void IsPermanentFailure_NexusAuthException_ReturnsTrue()
    {
        var (downloader, _, _) = MakeDownloader();

        downloader.IsPermanentFailure(
            new NexusAuthenticationException("no key"))
            .Should().BeTrue();
    }

    [Fact]
    public void IsPermanentFailure_HttpRequestException_ReturnsFalse()
    {
        var (downloader, _, _) = MakeDownloader();

        downloader.IsPermanentFailure(
            new HttpRequestException("network"))
            .Should().BeFalse();
    }

    [Fact]
    public void IsPermanentFailure_GenericException_ReturnsFalse()
    {
        var (downloader, _, _) = MakeDownloader();

        downloader.IsPermanentFailure(
            new InvalidOperationException("boom"))
            .Should().BeFalse();
    }
}
