using System.Net;
using FluentAssertions;
using Modsync.Platform.Nexus;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Platform.Nexus.Tests;

public class NexusCredentialValidatorTests
{
    private static NexusCredentialValidator MakeValidator(
        FakeHttpMessageHandler handler)
    {
        var factory = new FakeHttpClientFactory();
        factory.Register("nexus-api", handler);

        return new NexusCredentialValidator(
            factory, NullLogger<NexusCredentialValidator>.Instance);
    }

    private static NexusCredentialValidator MakeValidator(
        HttpStatusCode status, string? body = null)
    {
        return MakeValidator(new FakeHttpMessageHandler(
            status,
            body ?? "",
            "application/json"));
    }

    // ------------------------------------------------------------------
    //  Valid
    // ------------------------------------------------------------------

    [Fact]
    public async Task Validate_ValidPremiumKey_ReturnsValid()
    {
        var validator = MakeValidator(
            HttpStatusCode.OK,
            """{"is_premium":true,"name":"testuser"}""");

        var result = await validator.ValidateAsync(
            "abcdef123456", CancellationToken.None);

        result.Status.Should().Be(NexusKeyStatus.Valid);
        result.UserName.Should().Be("testuser");
        result.IsPremium.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Validate_ValidFreeKey_ReturnsValidNotPremium()
    {
        var validator = MakeValidator(
            HttpStatusCode.OK,
            """{"is_premium":false,"name":"testuser"}""");

        var result = await validator.ValidateAsync(
            "abcdef", CancellationToken.None);

        result.Status.Should().Be(NexusKeyStatus.Valid);
        result.IsPremium.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_SendsCorrectHeaders()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"is_premium":true,"name":"testuser"}""");
        var validator = MakeValidator(handler);

        await validator.ValidateAsync("abcdef123456", CancellationToken.None);

        handler.Requests.Should().HaveCount(1);
        var request = handler.Requests[0];
        request.Headers.GetValues("apikey")
            .Should().ContainSingle().Which.Should().Be("abcdef123456");
        request.Headers.GetValues("Application-Name")
            .Should().ContainSingle().Which.Should().Be("ModsyncManager");
        request.Headers.GetValues("User-Agent")
            .Should().ContainSingle().Which.Should().Be("ModsyncManager/0.1.0");
        request.RequestUri!.ToString().Should()
            .Be("https://api.nexusmods.com/v1/users/validate.json");
    }

    [Fact]
    public async Task Validate_TrimsKeyBeforeSending()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """{"is_premium":true,"name":"testuser"}""");
        var validator = MakeValidator(handler);

        await validator.ValidateAsync("  abcdef  ", CancellationToken.None);

        handler.Requests[0].Headers.GetValues("apikey")
            .Should().ContainSingle().Which.Should().Be("abcdef");
    }

    // ------------------------------------------------------------------
    //  Invalid
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Validate_EmptyKey_ReturnsInvalidWithoutRequest(
        string? key)
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "");
        var validator = MakeValidator(handler);

        var result = await validator.ValidateAsync(
            key!, CancellationToken.None);

        result.Status.Should().Be(NexusKeyStatus.Invalid);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_401_ReturnsInvalid()
    {
        var validator = MakeValidator(HttpStatusCode.Unauthorized);

        var result = await validator.ValidateAsync(
            "bad-key", CancellationToken.None);

        result.Status.Should().Be(NexusKeyStatus.Invalid);
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Validate_403_ReturnsInvalid()
    {
        var validator = MakeValidator(HttpStatusCode.Forbidden);

        var result = await validator.ValidateAsync(
            "some-key", CancellationToken.None);

        result.Status.Should().Be(NexusKeyStatus.Invalid);
    }

    [Fact]
    public async Task Validate_EmptyBody_ReturnsInvalid()
    {
        var validator = MakeValidator(HttpStatusCode.OK, "");

        var result = await validator.ValidateAsync(
            "abcdef", CancellationToken.None);

        // ReadFromJsonAsync на пустом body возвращает null → Invalid.
        result.Status.Should().Be(NexusKeyStatus.Invalid);
    }

    // ------------------------------------------------------------------
    //  NetworkError
    // ------------------------------------------------------------------

    [Fact]
    public async Task Validate_429_ReturnsNetworkError()
    {
        var validator = MakeValidator(HttpStatusCode.TooManyRequests);

        var result = await validator.ValidateAsync(
            "abcdef", CancellationToken.None);

        result.Status.Should().Be(NexusKeyStatus.NetworkError);
        result.ErrorMessage.Should().Contain("rate limit");
    }

    [Fact]
    public async Task Validate_500_ReturnsNetworkError()
    {
        var validator = MakeValidator(HttpStatusCode.InternalServerError);

        var result = await validator.ValidateAsync(
            "abcdef", CancellationToken.None);

        result.Status.Should().Be(NexusKeyStatus.NetworkError);
    }

    [Fact]
    public async Task Validate_NetworkException_ReturnsNetworkError()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            throw new HttpRequestException("DNS failure"));
        var validator = MakeValidator(handler);

        var result = await validator.ValidateAsync(
            "abcdef", CancellationToken.None);

        result.Status.Should().Be(NexusKeyStatus.NetworkError);
        result.ErrorMessage.Should().Contain("DNS failure");
    }

    [Fact]
    public async Task Validate_CallerCancelled_Throws()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            throw new OperationCanceledException());
        var validator = MakeValidator(handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await validator.ValidateAsync("abcdef", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
