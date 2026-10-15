// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.CompilerServices;
using CodingSeb.Localization;
using CodingSeb.Localization.Loaders;

namespace Modsync.Gui.Shared.Tests;

internal static class LocalizationFixture
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "Resources", "Strings.loc.json");

        Console.WriteLine($"[Loc] BaseDirectory = {baseDir}");
        Console.WriteLine($"[Loc] Strings path  = {path}");
        Console.WriteLine($"[Loc] File.Exists   = {File.Exists(path)}");

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Localization fixture: Strings.loc.json not found at {path}. " +
                $"Check CopyToOutputDirectory in test .csproj.");
        }

        LocalizationLoader.Instance.FileLanguageLoaders.Add(new JsonFileLoader());
        LocalizationLoader.Instance.AddFile(path);

        Loc.Instance.CurrentLanguage = "en";

        // Проверка: ключ должен найтись.
        var probe = Loc.Tr("Nexus.Premium_Yes");
        Console.WriteLine($"[Loc] Probe Nexus.Premium_Yes = '{probe}'");

        if (probe == "Nexus.Premium_Yes")
        {
            throw new InvalidOperationException(
                "Localization fixture: probe returned key, not value. " +
                "AddFile did not register the file correctly.");
        }
    }
}
