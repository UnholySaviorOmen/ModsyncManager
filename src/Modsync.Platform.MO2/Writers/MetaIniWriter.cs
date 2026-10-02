// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text;
using Modsync.Core.Models.Manifest;

namespace Modsync.Platform.MO2.Writers;

/// <summary>
/// Запись mods/&lt;Name&gt;/meta.ini — файла метаданных мода MO2.
///
/// Формат:
///   [General]
///   gameName=Skyrim Special Edition
///   gameID=skyrimspecialedition
///   modID=32349
///   fileID=795423
///   version=1.7.0
///   repository=Nexus
///   url=https://www.nexusmods.com/skyrimspecialedition/mods/32349
///   comments=
///   notes=
///
/// Ключи пишутся в camelCase — официальный формат MO2. MO2 читает
/// case-insensitive, поэтому modID и modid для него эквивалентны.
///
/// Секция [installedFiles] НЕ пишется. MO2 сам её перестроит при первом запуске.
/// Поле newestVersion НЕ пишется (зависит от времени проверки апдейтов).
/// Поле category НЕ пишется (MO2 хранит его как строку "7,15,"; список
/// категорий не нужен для воспроизведения).
///
/// Кодировка: UTF-8 без BOM. Перевод строк: CRLF.
///
/// Null-поля (например, modID для не-Nexus мода) — строка не пишется вообще.
/// Пустые строки ("" — например, comments="") — пишутся как key=.
/// </summary>
public static class MetaIniWriter
{
    private const string Header = "[General]";

    /// <summary>
    /// Записать meta.ini по указанному пути. Файл перезаписывается.
    /// </summary>
    public static void WriteFile(string path, ModMeta meta)
    {
        if (meta is null)
            throw new ArgumentNullException(nameof(meta));

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var content = Serialize(meta);

        // UTF-8 без BOM, CRLF.
        File.WriteAllText(
            path,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Сериализовать ModMeta в текст meta.ini. Для тестов и диагностики.
    /// </summary>
    public static string Serialize(ModMeta meta)
    {
        if (meta is null)
            throw new ArgumentNullException(nameof(meta));

        var sb = new StringBuilder();
        sb.Append(Header).Append("\r\n");

        AppendString(sb, "gameName", meta.GameName);
        AppendString(sb, "gameID", meta.GameId);
        AppendInt(sb, "modID", meta.ModId);
        AppendInt(sb, "fileID", meta.FileId);
        AppendString(sb, "version", meta.Version);
        AppendString(sb, "repository", meta.Repository);
        AppendString(sb, "url", meta.Url);
        AppendString(sb, "comments", meta.Comments);
        AppendString(sb, "notes", meta.Notes);

        return sb.ToString();
    }

    private static void AppendString(StringBuilder sb, string key, string? value)
    {
        if (value is null)
            return;

        sb.Append(key).Append('=').Append(value).Append("\r\n");
    }

    private static void AppendInt(StringBuilder sb, string key, int? value)
    {
        if (value is null)
            return;

        sb.Append(key).Append('=')
          .Append(value.Value.ToString(CultureInfo.InvariantCulture))
          .Append("\r\n");
    }
}
