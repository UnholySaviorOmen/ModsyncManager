// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using Modsync.Platform.MO2.Models;

namespace Modsync.Platform.MO2.Readers;

/// <summary>
/// Чтение .meta-файлов MO2 (Nexus-формат).
///
/// Формат:
///     [General]
///     gameName=Skyrim
///     modID=3863
///     fileID=1000172397
///
/// Читает только секцию [General] и только ключи gameName, modID, fileID.
/// Регистр ключей — case-sensitive (Wabbajack пишет modID и fileID).
/// </summary>
public static class MetaReader
{
    private const string SectionGeneral = "General";
    private const string KeyGameName = "gameName";
    private const string KeyModId = "modID";
    private const string KeyFileId = "fileID";

    /// <summary>
    /// Пытается прочитать .meta-файл. Если файл не существует, не является
    /// валидным .meta (нет нужных ключей или они не парсятся) — возвращает null.
    /// </summary>
    public static MetaFile? TryRead(string path)
    {
        if (!File.Exists(path))
            return null;

        var lines = File.ReadAllLines(path);
        return Parse(lines);
    }

    /// <summary>
    /// Парсит строки .meta-файла. Если структура невалидна — возвращает null.
    /// </summary>
    public static MetaFile? Parse(IEnumerable<string> lines)
    {
        string? gameName = null;
        int? modId = null;
        int? fileId = null;

        var inGeneral = false;

        foreach (var raw in lines)
        {
            var line = Normalize(raw);

            // Пустые строки и комментарии пропускаем
            if (line.Length == 0) continue;
            if (line[0] == ';' || line[0] == '#') continue;

            // Секция
            if (line[0] == '[')
            {
                var close = line.IndexOf(']');
                if (close < 0) continue;
                var section = line[1..close].Trim();
                inGeneral = section.Equals(SectionGeneral, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inGeneral) continue;

            // key=value
            var eq = line.IndexOf('=');
            if (eq < 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            switch (key)
            {
                case KeyGameName when gameName is null:
                    gameName = value;
                    break;
                case KeyModId when modId is null:
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mi))
                        modId = mi;
                    break;
                case KeyFileId when fileId is null:
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fi))
                        fileId = fi;
                    break;
            }
        }

        if (gameName is null || modId is null || fileId is null)
            return null;

        return new MetaFile
        {
            GameName = gameName,
            ModId = modId.Value,
            FileId = fileId.Value,
        };
    }

    private static string Normalize(string raw)
    {
        var line = raw.TrimEnd('\r', '\n');
        if (line.Length > 0 && line[0] == '\uFEFF')
            line = line[1..];
        return line.Trim();
    }
}
