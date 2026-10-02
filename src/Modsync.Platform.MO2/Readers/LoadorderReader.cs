// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Mo2;

namespace Modsync.Platform.MO2.Readers;

/// <summary>
/// Чтение loadorder.txt.
/// Просто список имён плагинов. Порядок = порядок загрузки.
/// Комментарии (# ...) игнорируются.
/// </summary>
public static class LoadorderReader
{
    public static LoadorderFile ReadFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"loadorder.txt not found: {path}", path);

        var lines = File.ReadAllLines(path);
        return Parse(lines);
    }

    public static LoadorderFile Parse(IEnumerable<string> lines)
    {
        var plugins = new List<string>();

        foreach (var raw in lines)
        {
            var line = Normalize(raw);
            if (line.Length == 0) continue;
            if (line[0] == '#') continue;

            plugins.Add(line);
        }

        return new LoadorderFile { Plugins = plugins };
    }

    private static string Normalize(string raw)
    {
        var line = raw.TrimEnd('\r', '\n');
        if (line.Length > 0 && line[0] == '\uFEFF')
            line = line[1..];
        return line.Trim();
    }
}
