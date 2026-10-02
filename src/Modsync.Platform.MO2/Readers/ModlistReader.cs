// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Mo2;

namespace Modsync.Platform.MO2.Readers;

/// <summary>
/// Чтение modlist.txt.
/// Формат строки: [+|-]ИмяМода
/// Комментарии (# ...) игнорируются.
/// BOM и пустые строки обрабатываются.
/// </summary>
public static class ModlistReader
{
    public static ModlistFile ReadFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"modlist.txt not found: {path}", path);

        var lines = File.ReadAllLines(path);
        return Parse(lines);
    }

    public static ModlistFile Parse(IEnumerable<string> lines)
    {
        var entries = new List<ModlistEntry>();

        foreach (var raw in lines)
        {
            var line = Normalize(raw);
            if (line.Length == 0) continue;

            // Комментарии
            if (line[0] == '#') continue;

            // Префикс обязателен
            var prefix = line[0];
            if (prefix != '+' && prefix != '-') continue;

            // Имя обрезаем по краям — на случай "+  SkyUI  " и подобного
            var name = line[1..].Trim();
            if (name.Length == 0) continue;

            entries.Add(new ModlistEntry(name, prefix == '+'));
        }

        return new ModlistFile { Entries = entries };
    }

    private static string Normalize(string raw)
    {
        var line = raw.TrimEnd('\r', '\n');
        if (line.Length > 0 && line[0] == '\uFEFF')
            line = line[1..];
        return line.Trim();
    }
}
