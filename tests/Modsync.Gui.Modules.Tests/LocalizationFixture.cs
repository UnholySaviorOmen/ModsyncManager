// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.CompilerServices;
using CodingSeb.Localization;
using CodingSeb.Localization.Loaders;

namespace Modsync.Gui.Modules.Tests;

/// <summary>
/// Инициализация локализации для тестов: грузит Strings.loc.json
/// и устанавливает английский язык.
///
/// Без этого Loc.Tr("key") вернёт сам ключ, и тесты на строки
/// будут сравнивать ключ с ожидаемым текстом.
///
/// Использование — через [ModuleInitializer], чтобы сработало
/// один раз на весь тестовый процесс до всех тестов.
/// </summary>
internal static class LocalizationFixture
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "Resources", "Strings.loc.json");

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Localization fixture: Strings.loc.json not found at {path}. " +
                $"Check CopyToOutputDirectory in Modsync.Gui.Modules.Tests.csproj.");
        }

        LocalizationLoader.Instance.FileLanguageLoaders
            .Add(new JsonFileLoader());
        LocalizationLoader.Instance.AddFile(path);

        Loc.Instance.CurrentLanguage = "en";
    }
}
