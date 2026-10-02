using Modsync.Platform.Nexus;

namespace Modsync.Gui.Shared.Tests.Fakes;

public sealed class FakeNexusCredentialValidator : INexusCredentialValidator
{
    public NexusValidationResult ResultToReturn { get; set; } =
        new(NexusKeyStatus.Valid, "testuser", false, null);

    public Exception? ExceptionToThrow { get; set; }

    public Task<NexusValidationResult> ValidateAsync(
        string apiKey, CancellationToken ct)
    {
        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        return Task.FromResult(ResultToReturn);
    }

    public static NexusValidationResult Valid(
        string? userName = "testuser",
        bool isPremium = false)
        => new(NexusKeyStatus.Valid, userName, isPremium, null);

    public static NexusValidationResult Invalid(
        string? message = "Nexus rejected the API key.")
        => new(NexusKeyStatus.Invalid, null, false, message);

    public static NexusValidationResult NetworkError(
        string? message = "Network error.")
        => new(NexusKeyStatus.NetworkError, null, false, message);
}
