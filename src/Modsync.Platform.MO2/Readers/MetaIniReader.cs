// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using Modsync.Core.Models.Manifest;

namespace Modsync.Platform.MO2.Readers;

/// <summary>
/// Чтение mods/&lt;Name&gt;/meta.ini — файла, который MO2 создаёт для каждого
/// установленного мода.
///
/// Читается только секция [General]. Секция [installedFiles] игнорируется:
/// она содержит абсолютные пути автора и бесполезна при воспроизведении.
///
/// Ключи читаются case-insensitive: MO2 в разных версиях и разных модах
/// пишет modID и modid, fileID и fileid, gameID и gameid. Парсер принимает
/// оба варианта. При записи (MetaIniWriter) используется camelCase —
/// официальный формат MO2.
///
/// Имя секции — case-insensitive ([General], [general], [GENERAL]).
///
/// Поля newestVersion, category, nexusFileStatus, installationFile,
/// nexusDescription, hasCustomURL, lastNexusQuery, lastNexusUpdate,
/// nexusLastModified, nexusCategory, converted, validated, color,
/// endorsed, tracked — не читаются. MO2 сам их регенерирует при первом
/// запуске (запрос к Nexus, timestamps).
///
/// Если файла нет — TryRead возвращает null.
/// Если файл есть, но секции [General] нет — возвращается ModMeta.Empty.
/// </summary>
public static class MetaIniReader
{
    private const string SectionGeneral = "General";

    public static ModMeta? TryRead(string path)
    {
        if (!File.Exists(path))
            return null;

        var lines = File.ReadAllLines(path);
        return Parse(lines);
    }

    public static ModMeta Parse(IEnumerable<string> lines)
    {
        string? gameName = null;
        string? gameId = null;
        int? modId = null;
        int? fileId = null;
        string? version = null;
        string? repository = null;
        string? url = null;
        string? comments = null;
        string? notes = null;

        var inGeneral = false;

        foreach (var raw in lines)
        {
            var line = Normalize(raw);
            if (line.Length == 0) continue;
            if (line[0] == ';' || line[0] == '#') continue;

            if (line[0] == '[')
            {
                var close = line.IndexOf(']');
                if (close < 0) continue;
                var section = line[1..close].Trim();
                inGeneral = section.Equals(
                    SectionGeneral, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inGeneral) continue;

            var eq = line.IndexOf('=');
            if (eq < 0) continue;

            var key = line[..eq].Trim().ToLowerInvariant();
            var value = line[(eq + 1)..].Trim();

            switch (key)
            {
                case "gamename" when gameName is null:
                    gameName = value;
                    break;
                case "gameid" when gameId is null:
                    gameId = value;
                    break;
                case "modid" when modId is null:
                    if (TryParseInt(value, out var mi)) modId = mi;
                    break;
                case "fileid" when fileId is null:
                    if (TryParseInt(value, out var fi)) fileId = fi;
                    break;
                case "version" when version is null:
                    version = value;
                    break;
                case "repository" when repository is null:
                    repository = value;
                    break;
                case "url" when url is null:
                    url = value;
                    break;
                case "comments" when comments is null:
                    comments = value;
                    break;
                case "notes" when notes is null:
                    notes = value;
                    break;
            }
        }

        return new ModMeta
        {
            GameName = gameName,
            GameId = gameId,
            ModId = modId,
            FileId = fileId,
            Version = version,
            Repository = repository,
            Url = url,
            Comments = comments,
            Notes = notes,
        };
    }

    private static bool TryParseInt(string value, out int result)
        => int.TryParse(
            value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static string Normalize(string raw)
    {
        var line = raw.TrimEnd('\r', '\n');
        if (line.Length > 0 && line[0] == '\uFEFF')
            line = line[1..];
        return line.Trim();
    }
}
