using Modsync.Platform.Nexus.Protocol;

namespace Modsync.Platform.Nexus.Tests.Protocol;

public sealed class FakeProtocolRegistrar : IProtocolRegistrar
{
    public ProtocolRegistrationState State { get; set; } =
        ProtocolRegistrationState.RegisteredToUs;

    public ProtocolRegistrationResult? RegisterResult { get; set; }

    public ProtocolRegistrationResult? RestoreResult { get; set; }

    public ProtocolRegistrationResult Register()
        => RegisterResult ?? ProtocolRegistrationResult.Registered();

    public ProtocolRegistrationResult Restore()
        => RestoreResult ?? ProtocolRegistrationResult.Restored();

    public ProtocolRegistrationState GetState() => State;
}
