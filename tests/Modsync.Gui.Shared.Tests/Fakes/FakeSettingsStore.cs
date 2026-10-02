using Modsync.Gui.Shared.Services;

namespace Modsync.Gui.Shared.Tests.Fakes;

/// <summary>
/// Fake ISettingsStore для тестов SettingsVM.
///
/// Current — живой Settings, тесты могут его наполнять
/// перед созданием VM (проверка initial-value).
///
/// Save() не пишет на диск — только считает вызовы.
/// Это позволяет проверить: «VM вызвал Save после изменения поля».
/// </summary>
public sealed class FakeSettingsStore : ISettingsStore
{
    public Settings Current { get; } = Settings.Default;

    public string FilePath => "<fake>";

    public int SaveCallCount { get; private set; }

    public void Save() => SaveCallCount++;
}
