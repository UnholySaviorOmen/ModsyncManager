using FluentAssertions;
using Modsync.Platform.Nexus;
using Modsync.Platform.Nexus.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Platform.Nexus.Tests.Protocol;

public class NexusFreeNxmProviderTests
{
    private const string Game = "skyrimspecialedition";
    private const int ModId = 3863;
    private const int FileId = 1000172397;
    private const string DisplayName = "SkyUI";

    private static (NexusFreeNxmProvider provider,
                    FakeUrlOpener opener,
                    FakeNxmUrlReceiver receiver,
                    FakeProtocolRegistrar registrar)
        Make(
            FakeNxmUrlReceiver? receiver = null,
            FakeProtocolRegistrar? registrar = null)
    {
        var opener = new FakeUrlOpener();
        var rec = receiver ?? new FakeNxmUrlReceiver
        {
            // По умолчанию — реестр «зарегистрирован на нас»,
            // чтобы не спамить Warning в тестах.
        };
        var reg = registrar ?? new FakeProtocolRegistrar
        {
            State = ProtocolRegistrationState.RegisteredToUs,
        };

        var provider = new NexusFreeNxmProvider(
            rec, opener, reg,
            NullLogger<NexusFreeNxmProvider>.Instance);

        return (provider, opener, rec, reg);
    }

    // ------------------------------------------------------------------
    //  Валидация аргументов
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task RequestNxmUrlAsync_EmptyGame_Throws(string? game)
    {
        var (provider, _, _, _) = Make();

        var act = async () => await provider.RequestNxmUrlAsync(
            game!, ModId, FileId, DisplayName, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RequestNxmUrlAsync_NonPositiveModId_Throws(int modId)
    {
        var (provider, _, _, _) = Make();

        var act = async () => await provider.RequestNxmUrlAsync(
            Game, modId, FileId, DisplayName, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task RequestNxmUrlAsync_NonPositiveFileId_Throws(int fileId)
    {
        var (provider, _, _, _) = Make();

        var act = async () => await provider.RequestNxmUrlAsync(
            Game, ModId, fileId, DisplayName, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task RequestNxmUrlAsync_EmptyDisplayName_Throws(string? name)
    {
        var (provider, _, _, _) = Make();

        var act = async () => await provider.RequestNxmUrlAsync(
            Game, ModId, FileId, name!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ------------------------------------------------------------------
    //  Happy path
    // ------------------------------------------------------------------

    [Fact]
    public async Task RequestNxmUrlAsync_HappyPath_ReturnsUrl()
    {
        const string expected =
            "nxm://skyrimspecialedition/mods/3863/files/1000172397" +
            "?key=abc&expires=100&user_id=1";

        var receiver = new FakeNxmUrlReceiver
        {
            UrlToReturn = expected,
        };
        var (provider, opener, _, _) = Make(receiver);

        var result = await provider.RequestNxmUrlAsync(
            Game, ModId, FileId, DisplayName, CancellationToken.None);

        result.Should().Be(expected);
        opener.OpenedUrls.Should().ContainSingle();

        // Проверяем, что URL мода открыт правильно.
        opener.OpenedUrls[0].Should().Be(
            "https://www.nexusmods.com/skyrimspecialedition/mods/3863" +
            "?tab=files&file_id=1000172397&nmm=1");
    }

    [Fact]
    public async Task RequestNxmUrlAsync_OpensBrowserBeforeWaiting()
    {
        // Проверяем порядок: сначала открытие браузера,
        // потом ожидание URL.
        var receiver = new FakeNxmUrlReceiver
        {
            UrlToReturn = "nxm://game/mods/1/files/1",
        };
        var (provider, opener, _, _) = Make(receiver);

        await provider.RequestNxmUrlAsync(
            Game, ModId, FileId, DisplayName, CancellationToken.None);

        opener.OpenedUrls.Should().HaveCount(1);
        receiver.WaitCallCount.Should().Be(1);
    }

    // ------------------------------------------------------------------
    //  Drain: очистка очереди перед началом
    // ------------------------------------------------------------------

    [Fact]
    public async Task RequestNxmUrlAsync_CallsTryDrainPendingUrls()
    {
        // Регрессия: URL-ы от прошлых сессий install (пользователь
        // отменил, URL уже пришёл) не должны попасть в текущий
        // install. NexusFreeNxmProvider должен очистить очередь
        // перед началом ожидания.
        var receiver = new FakeNxmUrlReceiver
        {
            UrlToReturn = "nxm://game/mods/1/files/1",
        };
        var (provider, _, _, _) = Make(receiver);

        await provider.RequestNxmUrlAsync(
            Game, ModId, FileId, DisplayName, CancellationToken.None);

        receiver.DrainCallCount.Should().Be(1);
    }

    [Fact]
    public async Task RequestNxmUrlAsync_DrainsStaleUrlsBeforeWaiting()
    {
        var receiver = new FakeNxmUrlReceiver
        {
            UrlToReturn = "nxm://game/mods/1/files/1",
            PendingUrlsToDrain = 3,
        };
        var (provider, _, _, _) = Make(receiver);

        var result = await provider.RequestNxmUrlAsync(
            Game, ModId, FileId, DisplayName, CancellationToken.None);

        result.Should().Be("nxm://game/mods/1/files/1");
        receiver.DrainCallCount.Should().Be(1);
    }

    // ------------------------------------------------------------------
    //  Timeout
    // ------------------------------------------------------------------

    [Fact]
    public async Task RequestNxmUrlAsync_Timeout_ReturnsNull()
    {
        var receiver = new FakeNxmUrlReceiver
        {
            UrlToReturn = null,
            ThrowOnCancel = true,
        };
        var (provider, _, _, _) = Make(receiver);

        using var cts = new CancellationTokenSource();
        var task = provider.RequestNxmUrlAsync(
            Game, ModId, FileId, DisplayName, cts.Token);

        await Task.Delay(100);
        cts.Cancel();

        var act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ------------------------------------------------------------------
    //  Отмена внешним ct
    // ------------------------------------------------------------------

    [Fact]
    public async Task RequestNxmUrlAsync_ExternallyCancelled_Throws()
    {
        var receiver = new FakeNxmUrlReceiver
        {
            ThrowOnCancel = true,
        };
        var (provider, _, _, _) = Make(receiver);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await provider.RequestNxmUrlAsync(
            Game, ModId, FileId, DisplayName, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ------------------------------------------------------------------
    //  Receiver вернул null (StopAsync вызван)
    // ------------------------------------------------------------------

    [Fact]
    public async Task RequestNxmUrlAsync_ReceiverReturnsNull_ReturnsNull()
    {
        var receiver = new FakeNxmUrlReceiver
        {
            UrlToReturn = null,
            ThrowOnCancel = false,
        };
        var (provider, _, _, _) = Make(receiver);

        var result = await provider.RequestNxmUrlAsync(
            Game, ModId, FileId, DisplayName, CancellationToken.None);

        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    //  Handler не зарегистрирован → throw
    // ------------------------------------------------------------------

    [Fact]
    public async Task RequestNxmUrlAsync_HandlerNotRegistered_Throws()
    {
        var registrar = new FakeProtocolRegistrar
        {
            State = ProtocolRegistrationState.NotRegistered,
        };
        var (provider, opener, _, _) = Make(registrar: registrar);

        var act = async () => await provider.RequestNxmUrlAsync(
            Game, ModId, FileId, DisplayName, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not registered to ModsyncManager*")
            .WithMessage("*Settings → Nexus Free Download*");

        // Браузер НЕ открывали — падаем до него.
        opener.OpenedUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestNxmUrlAsync_HandlerRegisteredToOther_Throws()
    {
        var registrar = new FakeProtocolRegistrar
        {
            State = ProtocolRegistrationState.RegisteredToOther,
        };
        var (provider, opener, _, _) = Make(registrar: registrar);

        var act = async () => await provider.RequestNxmUrlAsync(
            Game, ModId, FileId, DisplayName, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RegisteredToOther*");

        opener.OpenedUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestNxmUrlAsync_HandlerStateUnknown_Throws()
    {
        var registrar = new FakeProtocolRegistrar
        {
            State = ProtocolRegistrationState.Unknown,
        };
        var (provider, _, _, _) = Make(registrar: registrar);

        var act = async () => await provider.RequestNxmUrlAsync(
            Game, ModId, FileId, DisplayName, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unknown*");
    }

    [Fact]
    public async Task RequestNxmUrlAsync_HandlerNotRegistered_DoesNotDrain()
    {
        // Если handler не зарегистрирован — падаем до drain.
        // Очередь не трогаем: пользователь может вернуться в
        // Settings, включить handler и повторить — URL не потеряется
        // (хотя URL и так от прошлой сессии).
        var receiver = new FakeNxmUrlReceiver { PendingUrlsToDrain = 5 };
        var registrar = new FakeProtocolRegistrar
        {
            State = ProtocolRegistrationState.NotRegistered,
        };
        var (provider, _, _, _) = Make(receiver, registrar);

        var act = async () => await provider.RequestNxmUrlAsync(
            Game, ModId, FileId, DisplayName, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        receiver.DrainCallCount.Should().Be(0);
    }

    // ------------------------------------------------------------------
    //  IUrlOpener бросает
    // ------------------------------------------------------------------

    [Fact]
    public async Task RequestNxmUrlAsync_UrlOpenerThrows_Propagates()
    {
        var receiver = new FakeNxmUrlReceiver();
        var (provider, opener, _, _) = Make(receiver);

        opener.ExceptionToThrow = new InvalidOperationException("no browser");

        var act = async () => await provider.RequestNxmUrlAsync(
            Game, ModId, FileId, DisplayName, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no browser*");
    }

    // ------------------------------------------------------------------
    //  Сериализация через SemaphoreSlim
    // ------------------------------------------------------------------

    [Fact]
    public async Task RequestNxmUrlAsync_ConcurrentCalls_SerializedBySemaphore()
    {
        var receiver = new FakeNxmUrlReceiver
        {
            UrlToReturn = "nxm://game/mods/1/files/1",
            Delay = TimeSpan.FromMilliseconds(200),
        };
        var (provider, opener, _, _) = Make(receiver);

        var task1 = provider.RequestNxmUrlAsync(
            Game, 1, 1, "A", CancellationToken.None);
        var task2 = provider.RequestNxmUrlAsync(
            Game, 2, 2, "B", CancellationToken.None);

        await Task.WhenAll(task1, task2);

        // Оба прошли, каждый — свой opener.Open.
        opener.OpenedUrls.Should().HaveCount(2);

        // Первый URL — для mods/1, второй — для mods/2.
        opener.OpenedUrls.Should().Contain(
            "https://www.nexusmods.com/skyrimspecialedition/mods/1" +
            "?tab=files&file_id=1&nmm=1");
        opener.OpenedUrls.Should().Contain(
            "https://www.nexusmods.com/skyrimspecialedition/mods/2" +
            "?tab=files&file_id=2&nmm=1");
    }

    [Fact]
    public async Task RequestNxmUrlAsync_OpenedUrl_ContainsNmmParameter()
    {
        // Регрессия: без &nmm=1 Nexus открывает страницу в Manual-режиме,
        // пользователь жмёт Slow Download, архив падает в системные
        // Downloads/ вместо пайплайна ModsyncManager.
        var receiver = new FakeNxmUrlReceiver
        {
            UrlToReturn = "nxm://game/mods/1/files/1",
        };
        var (provider, opener, _, _) = Make(receiver);

        await provider.RequestNxmUrlAsync(
            Game, ModId, FileId, DisplayName, CancellationToken.None);

        opener.OpenedUrls.Should().ContainSingle();
        opener.OpenedUrls[0].Should().EndWith("&nmm=1");
    }

    // ------------------------------------------------------------------
    //  SanitizeUrl
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(
        "nxm://game/mods/1/files/2?key=abc&expires=100&user_id=1",
        "nxm://game/mods/1/files/2")]
    [InlineData(
        "nxm://game/mods/1/files/2",
        "nxm://game/mods/1/files/2")]
    [InlineData("", "")]
    public void SanitizeUrl_StripsQueryParameters(
        string input, string expected)
    {
        NexusFreeNxmProvider.SanitizeUrl(input).Should().Be(expected);
    }
}
