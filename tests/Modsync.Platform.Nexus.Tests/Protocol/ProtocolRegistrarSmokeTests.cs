using System.Runtime.Versioning;
using FluentAssertions;
using Modsync.Platform.Nexus.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Platform.Nexus.Tests.Protocol;

/// <summary>
/// Один smoke-тест с РЕАЛЬНЫМ реестром HKCU.
///
/// Использует временную ветку "Software\ModsyncManager\Tests\{guid}",
/// которая создаётся в SetUp и удаляется в Dispose.
///
/// Цель: убедиться, что WindowsRegistryAccessor действительно
/// работает с Microsoft.Win32.Registry (fake-тесты это не проверяют).
///
/// ВАЖНО: тест никогда не трогает HKCU\Software\Classes\nxm —
/// только нашу временную ветку. Это гарантирует, что прогон
/// тестов не сломает рабочую среду разработчика.
///
/// Помечен [SupportedOSPlatform("windows")], потому что
/// WindowsRegistryAccessor — Windows-only. На Linux тесты
/// этого класса не запускаются (xUnit их пропустит или CI
/// отфильтрует).
/// </summary>
[SupportedOSPlatform("windows")]
public class ProtocolRegistrarSmokeTests : IDisposable
{
    private readonly string _testKeyPrefix;
    private readonly string _commandKeyPath;
    private readonly WindowsRegistryAccessor _registry;

    public ProtocolRegistrarSmokeTests()
    {
        _testKeyPrefix =
            $@"Software\ModsyncManager\Tests\{Guid.NewGuid():N}";
        _commandKeyPath = _testKeyPrefix + @"\shell\open\command";
        _registry = new WindowsRegistryAccessor();
    }

    public void Dispose()
    {
        try
        {
            if (_registry.KeyExists(_testKeyPrefix))
                _registry.DeleteKey(_testKeyPrefix, recursive: true);
        }
        catch
        {
            // Smoke-тест не должен падать на cleanup.
        }
    }

    [Fact]
    public void WindowsRegistryAccessor_SetGetDelete_Roundtrip()
    {
        // 1. GetValue на несуществующем ключе — null.
        _registry.GetValue(_testKeyPrefix, null).Should().BeNull();
        _registry.KeyExists(_testKeyPrefix).Should().BeFalse();

        // 2. SetValue создаёт ключ.
        _registry.SetValue(_testKeyPrefix, null, "hello");
        _registry.KeyExists(_testKeyPrefix).Should().BeTrue();
        _registry.GetValue(_testKeyPrefix, null).Should().Be("hello");

        // 3. SetValue на подключах.
        _registry.SetValue(
            _commandKeyPath, null, "\"C:\\test.exe\" \"%1\"");
        _registry.GetValue(_commandKeyPath, null)
            .Should().Be("\"C:\\test.exe\" \"%1\"");

        // 4. DeleteValue.
        _registry.DeleteValue(_testKeyPrefix, null);
        _registry.GetValue(_testKeyPrefix, null).Should().BeNull();

        // 5. DeleteKey recursive.
        _registry.DeleteKey(_testKeyPrefix, recursive: true);
        _registry.KeyExists(_testKeyPrefix).Should().BeFalse();
        _registry.KeyExists(_commandKeyPath).Should().BeFalse();
    }

    [Fact]
    public void WindowsRegistryAccessor_DeleteKey_MissingKey_NoOp()
    {
        var act = () => _registry.DeleteKey(
            _testKeyPrefix, recursive: true);

        act.Should().NotThrow();
    }

    [Fact]
    public void WindowsRegistryAccessor_KeyExists_CaseInsensitive()
    {
        _registry.SetValue(_testKeyPrefix, null, "value");

        _registry.KeyExists(_testKeyPrefix.ToUpperInvariant())
            .Should().BeTrue();
    }
}
