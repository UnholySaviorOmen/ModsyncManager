// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Archives;

/// <summary>
/// Расширения файлов, которые считаются архивами.
/// </summary>
public static class ArchiveExtensions
{
    private static readonly HashSet<string> Known =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z", ".zip", ".rar", ".tar", ".gz", ".tar.gz", ".bz2",
        ".bsa", ".ba2",
    };

    public static bool IsArchive(string fileName)
    {
        // Явно исключаем .meta-файлы MO2.
        if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var ext in Known)
        {
            if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Известные расширения — только для тестов и диагностики.</summary>
    public static IReadOnlyCollection<string> All => Known;
}
