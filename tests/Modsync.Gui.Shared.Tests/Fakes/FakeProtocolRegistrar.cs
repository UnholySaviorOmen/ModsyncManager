using Modsync.Platform.Nexus.Protocol;

namespace Modsync.Gui.Shared.Tests.Fakes;

public sealed class FakeProtocolRegistrar : IProtocolRegistrar
{
    public ProtocolRegistrationState State { get; set; } =
        ProtocolRegistrationState.NotRegistered;

    public ProtocolRegistrationResult? RegisterResult { get; set; }

    public ProtocolRegistrationResult? RestoreResult { get; set; }

    public int RegisterCallCount { get; private set; }
    public int RestoreCallCount { get; private set; }
    public int GetStateCallCount { get; private set; }

    public ProtocolRegistrationResult Register()
    {
        RegisterCallCount++;
        return RegisterResult ?? ProtocolRegistrationResult.Registered();
    }

    public ProtocolRegistrationResult Restore()
    {
        RestoreCallCount++;
        return RestoreResult ?? ProtocolRegistrationResult.Restored();
    }

    public ProtocolRegistrationState GetState()
    {
        GetStateCallCount++;
        return State;
    }
}
