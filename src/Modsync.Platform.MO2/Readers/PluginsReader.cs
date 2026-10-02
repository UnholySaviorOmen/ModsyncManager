// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Mo2;

namespace Modsync.Platform.MO2.Readers;

/// <summary>
/// Чтение plugins.txt.
/// Формат строки: [*]ИмяПлагина.esp|esm|esl
/// Комментарии (# ...) игнорируются.
/// Порядок строк сохраняется как есть (не является порядком загрузки).
/// </summary>
public static class PluginsReader
{
    public static PluginsFile ReadFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"plugins.txt not found: {path}", path);

        var lines = File.ReadAllLines(path);
        return Parse(lines);
    }

    public static PluginsFile Parse(IEnumerable<string> lines)
    {
        var entries = new List<PluginEntry>();

        foreach (var raw in lines)
        {
            var line = Normalize(raw);
            if (line.Length == 0) continue;

            if (line[0] == '#') continue;

            var enabled = line[0] == '*';
            var name = (enabled ? line[1..] : line).Trim();
            if (name.Length == 0) continue;

            entries.Add(new PluginEntry(name, enabled));
        }

        return new PluginsFile { Entries = entries };
    }

    private static string Normalize(string raw)
    {
        var line = raw.TrimEnd('\r', '\n');
        if (line.Length > 0 && line[0] == '\uFEFF')
            line = line[1..];
        return line.Trim();
    }
}
