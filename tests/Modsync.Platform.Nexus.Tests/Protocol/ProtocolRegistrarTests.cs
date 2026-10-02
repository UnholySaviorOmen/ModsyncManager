using FluentAssertions;
using Modsync.Platform.Nexus.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Platform.Nexus.Tests.Protocol;

public class ProtocolRegistrarTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _backupPath;
    private readonly string _handlerPath;
    private readonly FakeRegistryAccessor _registry;

    public ProtocolRegistrarTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-registrar-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);

        _backupPath = Path.Combine(_tempDir, "backup.json");
        _handlerPath = Path.Combine(_tempDir, "ModsyncManager.NxmHandler.exe");

        // Создаём фейковый handler, чтобы File.Exists() вернул true.
        File.WriteAllText(_handlerPath, "fake handler");

        _registry = new FakeRegistryAccessor();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private ProtocolRegistrar MakeRegistrar()
        => new(
            _registry,
            _backupPath,
            _handlerPath,
            NullLogger<ProtocolRegistrar>.Instance);

    // ------------------------------------------------------------------
    //  GetState
    // ------------------------------------------------------------------

    [Fact]
    public void GetState_EmptyRegistry_ReturnsNotRegistered()
    {
        var registrar = MakeRegistrar();
        registrar.GetState().Should().Be(
            ProtocolRegistrationState.NotRegistered);
    }

    [Fact]
    public void GetState_OurHandler_ReturnsRegisteredToUs()
    {
        _registry.Seed(
            ProtocolRegistrar.NxmKeyPath, null, "URL:NXM Protocol");
        _registry.Seed(
            ProtocolRegistrar.NxmKeyPath, "URL Protocol", "");
        _registry.Seed(
            ProtocolRegistrar.CommandKeyPath, null,
            $"\"{_handlerPath}\" \"%1\"");

        var registrar = MakeRegistrar();
        registrar.GetState().Should().Be(
            ProtocolRegistrationState.RegisteredToUs);
    }

    [Fact]
    public void GetState_OtherHandler_ReturnsRegisteredToOther()
    {
        _registry.Seed(
            ProtocolRegistrar.NxmKeyPath, null, "URL:NXM Protocol");
        _registry.Seed(
            ProtocolRegistrar.NxmKeyPath, "URL Protocol", "");
        _registry.Seed(
            ProtocolRegistrar.CommandKeyPath, null,
            "\"C:\\Vortex\\vortex.exe\" \"%1\"");

        var registrar = MakeRegistrar();
        registrar.GetState().Should().Be(
            ProtocolRegistrationState.RegisteredToOther);
    }

    [Fact]
    public void GetState_ReadException_ReturnsUnknown()
    {
        _registry.ReadException = new IOException("boom");

        var registrar = MakeRegistrar();
        registrar.GetState().Should().Be(
            ProtocolRegistrationState.Unknown);
    }

    // ------------------------------------------------------------------
    //  Register — happy path
    // ------------------------------------------------------------------

    [Fact]
    public void Register_EmptyRegistry_Succeeds()
    {
        var registrar = MakeRegistrar();
        var result = registrar.Register();

        result.Status.Should().Be(ProtocolRegistrationStatus.Registered);
        result.IsSuccess.Should().BeTrue();
        result.PreviousHandlerPath.Should().BeNull();

        // Проверяем, что записалось.
        _registry.GetValue(ProtocolRegistrar.NxmKeyPath, null)
            .Should().Be("URL:NXM Protocol");
        _registry.GetValue(ProtocolRegistrar.NxmKeyPath, "URL Protocol")
            .Should().Be("");
        _registry.GetValue(ProtocolRegistrar.CommandKeyPath, null)
            .Should().Be($"\"{_handlerPath}\" \"%1\"");
    }

    [Fact]
    public void Register_EmptyRegistry_NoBackupFile()
    {
        var registrar = MakeRegistrar();
        registrar.Register();

        File.Exists(_backupPath).Should().BeFalse();
    }

    [Fact]
    public void Register_AlreadyOurHandler_ReturnsAlreadyRegistered()
    {
        // Первый register.
        MakeRegistrar().Register();

        // Второй register — должен увидеть наш handler.
        var result = MakeRegistrar().Register();

        result.Status.Should().Be(
            ProtocolRegistrationStatus.AlreadyRegistered);
    }

    [Fact]
    public void Register_AlreadyOurHandler_DoesNotOverwriteBackup()
    {
        // Симулируем: у нас уже зарегистрирован handler,
        // и backup-файл лежит от прошлого раза.
        var existingBackup = new NxmHandlerBackup
        {
            PreviousCommand = "old-command",
            BackupTimeUtc = DateTimeOffset.UtcNow.AddDays(-1),
        };
        existingBackup.SaveToFile(_backupPath);

        // Регистрируем наш handler напрямую.
        _registry.Seed(
            ProtocolRegistrar.NxmKeyPath, null, "URL:NXM Protocol");
        _registry.Seed(
            ProtocolRegistrar.CommandKeyPath, null,
            $"\"{_handlerPath}\" \"%1\"");

        var result = MakeRegistrar().Register();
        result.Status.Should().Be(
            ProtocolRegistrationStatus.AlreadyRegistered);

        // Backup не должен быть перезаписан.
        var loaded = NxmHandlerBackup.LoadFromFile(_backupPath);
        loaded!.PreviousCommand.Should().Be("old-command");
    }

    // ------------------------------------------------------------------
    //  Register — handler not found
    // ------------------------------------------------------------------

    [Fact]
    public void Register_HandlerNotFound_ReturnsHandlerNotFound()
    {
        File.Delete(_handlerPath);

        var result = MakeRegistrar().Register();

        result.Status.Should().Be(
            ProtocolRegistrationStatus.HandlerNotFound);
        result.IsSuccess.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  Register — чужой handler + backup
    // ------------------------------------------------------------------

    [Fact]
    public void Register_OtherHandler_SavesBackupAndOverwrites()
    {
        _registry.Seed(
            ProtocolRegistrar.NxmKeyPath, null, "URL:NXM Protocol");
        _registry.Seed(
            ProtocolRegistrar.NxmKeyPath, "URL Protocol", "");
        _registry.Seed(
            ProtocolRegistrar.CommandKeyPath, null,
            "\"C:\\Vortex\\vortex.exe\" \"%1\"");

        var result = MakeRegistrar().Register();

        result.Status.Should().Be(ProtocolRegistrationStatus.Registered);
        result.PreviousHandlerPath.Should().Be("C:\\Vortex\\vortex.exe");

        // Backup-файл создан.
        File.Exists(_backupPath).Should().BeTrue();
        var backup = NxmHandlerBackup.LoadFromFile(_backupPath);
        backup!.PreviousCommand.Should().Be(
            "\"C:\\Vortex\\vortex.exe\" \"%1\"");
        backup.PreviousHandlerPath.Should().Be("C:\\Vortex\\vortex.exe");

        // Реестр перезаписан нашим handler-ом.
        _registry.GetValue(ProtocolRegistrar.CommandKeyPath, null)
            .Should().Be($"\"{_handlerPath}\" \"%1\"");
    }

    // ------------------------------------------------------------------
    //  Register — backup fails
    // ------------------------------------------------------------------

    [Fact]
    public void Register_BackupFails_DoesNotOverwriteRegistry()
    {
        _registry.Seed(
            ProtocolRegistrar.NxmKeyPath, null, "URL:NXM Protocol");
        _registry.Seed(
            ProtocolRegistrar.CommandKeyPath, null,
            "\"C:\\Vortex\\vortex.exe\" \"%1\"");

        // Делаем backup-путь недоступным: создаём директорию
        // с таким же именем, что и файл.
        Directory.CreateDirectory(_backupPath);

        try
        {
            var result = MakeRegistrar().Register();

            result.Status.Should().Be(ProtocolRegistrationStatus.BackupFailed);
            result.IsSuccess.Should().BeFalse();

            // Реестр не перезаписан — остался Vortex.
            _registry.GetValue(ProtocolRegistrar.CommandKeyPath, null)
                .Should().Be("\"C:\\Vortex\\vortex.exe\" \"%1\"");
        }
        finally
        {
            Directory.Delete(_backupPath, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    //  Register — registry write fails
    // ------------------------------------------------------------------

    [Fact]
    public void Register_RegistryWriteFails_ReturnsRegistryWriteFailed()
    {
        _registry.WriteException = new IOException("access denied");

        var result = MakeRegistrar().Register();

        result.Status.Should().Be(
            ProtocolRegistrationStatus.RegistryWriteFailed);
        result.IsSuccess.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  Restore — happy path
    // ------------------------------------------------------------------

    [Fact]
    public void Restore_WithBackup_RestoresPreviousHandler()
    {
        // Сначала регистрируем (создаём backup).
        _registry.Seed(
            ProtocolRegistrar.NxmKeyPath, null, "URL:NXM Protocol");
        _registry.Seed(
            ProtocolRegistrar.NxmKeyPath, "URL Protocol", "");
        _registry.Seed(
            ProtocolRegistrar.CommandKeyPath, null,
            "\"C:\\Vortex\\vortex.exe\" \"%1\"");

        MakeRegistrar().Register();

        // Restore.
        var result = MakeRegistrar().Restore();

        result.Status.Should().Be(ProtocolRegistrationStatus.Restored);
        result.PreviousHandlerPath.Should().Be("C:\\Vortex\\vortex.exe");

        // Реестр вернулся к Vortex.
        _registry.GetValue(ProtocolRegistrar.CommandKeyPath, null)
            .Should().Be("\"C:\\Vortex\\vortex.exe\" \"%1\"");
        _registry.GetValue(ProtocolRegistrar.NxmKeyPath, null)
            .Should().Be("URL:NXM Protocol");
        _registry.GetValue(ProtocolRegistrar.NxmKeyPath, "URL Protocol")
            .Should().Be("");

        // Backup-файл удалён.
        File.Exists(_backupPath).Should().BeFalse();
    }

    [Fact]
    public void Restore_NoBackupFile_RemovesOurRegistration()
    {
        // Регистрируем без предварительного чужого handler-а
        // (backup не создаётся).
        MakeRegistrar().Register();
        File.Exists(_backupPath).Should().BeFalse();

        var result = MakeRegistrar().Restore();

        result.Status.Should().Be(
            ProtocolRegistrationStatus.AlreadyRestored);

        // Реестр очищен.
        _registry.KeyExists(ProtocolRegistrar.NxmKeyPath).Should().BeFalse();
    }

    [Fact]
    public void Restore_NothingRegistered_NoOp()
    {
        // Ничего не зарегистрировано — Restore не падает.
        var result = MakeRegistrar().Restore();

        result.Status.Should().Be(
            ProtocolRegistrationStatus.AlreadyRestored);
    }

    [Fact]
    public void Restore_MalformedBackupFile_FallsBackToRemoval()
    {
        // Регистрируем наш handler.
        MakeRegistrar().Register();

        // Пишем битый backup.
        File.WriteAllText(_backupPath, "{ garbage }");

        var result = MakeRegistrar().Restore();

        // Fallback: удаляем нашу регистрацию.
        result.Status.Should().Be(
            ProtocolRegistrationStatus.AlreadyRestored);
        _registry.KeyExists(ProtocolRegistrar.NxmKeyPath).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  Idempotency: register-restore-register
    // ------------------------------------------------------------------

    [Fact]
    public void RegisterRestoreRegister_FullCycle()
    {
        // Чужой handler.
        _registry.Seed(
            ProtocolRegistrar.NxmKeyPath, null, "URL:NXM Protocol");
        _registry.Seed(
            ProtocolRegistrar.CommandKeyPath, null,
            "\"C:\\Vortex\\vortex.exe\" \"%1\"");

        // 1. Register.
        var r1 = MakeRegistrar().Register();
        r1.Status.Should().Be(ProtocolRegistrationStatus.Registered);

        // 2. Restore.
        var r2 = MakeRegistrar().Restore();
        r2.Status.Should().Be(ProtocolRegistrationStatus.Restored);

        // 3. Register снова — снова чужой handler, backup перезапишется.
        var r3 = MakeRegistrar().Register();
        r3.Status.Should().Be(ProtocolRegistrationStatus.Registered);
        r3.PreviousHandlerPath.Should().Be("C:\\Vortex\\vortex.exe");
    }
}
