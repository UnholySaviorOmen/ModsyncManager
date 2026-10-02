// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Modsync.Platform.Nexus.Protocol;

/// <summary>
/// Backup предыдущего nxm:// handler-а.
///
/// Сохраняется перед тем, как мы впервые перезаписываем
/// HKCU\Software\Classes\nxm. Нужен для Restore — вернуть
/// пользователю Vortex / MO2 / что угодно.
///
/// Формат — JSON (UTF-8 без BOM, indented — для читаемости
/// пользователем при отладке).
///
/// Все три поля (PreviousCommand, PreviousDefaultValue,
/// PreviousUrlProtocolValue) могут быть null — это валидное
/// состояние: ключ существовал, но конкретного значения не было.
///
/// PreviousHandlerPath — вычисляется из PreviousCommand, если
/// это возможно. Хранится для удобства UI («Backup from: Vortex»).
/// </summary>
public sealed record NxmHandlerBackup
{
    /// <summary>
    /// Значение (Default) в HKCU\Software\Classes\nxm\shell\open\command.
    /// Обычно — "\"C:\path\to\handler.exe\" \"%1\"".
    /// </summary>
    public string? PreviousCommand { get; init; }

    /// <summary>
    /// Значение (Default) в HKCU\Software\Classes\nxm.
    /// Обычно — "URL:NXM Protocol".
    /// </summary>
    public string? PreviousDefaultValue { get; init; }

    /// <summary>
    /// Значение "URL Protocol" в HKCU\Software\Classes\nxm.
    /// Обычно — пустая строка "".
    /// </summary>
    public string? PreviousUrlProtocolValue { get; init; }

    /// <summary>
    /// Когда был сохранён backup.
    /// </summary>
    public required DateTimeOffset BackupTimeUtc { get; init; }

    /// <summary>
    /// Путь к предыдущему handler-у, извлечённый из PreviousCommand
    /// (если получилось). Для UI. Может быть null.
    /// </summary>
    public string? PreviousHandlerPath { get; init; }

    // ------------------------------------------------------------------
    //  JSON
    // ------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Сериализует backup в JSON-строку.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Парсит backup из JSON-строки.
    /// Бросает JsonException при ошибке.
    /// </summary>
    public static NxmHandlerBackup FromJson(string json)
    {
        var backup = JsonSerializer.Deserialize<NxmHandlerBackup>(
            json, JsonOptions);

        if (backup is null)
            throw new JsonException("Backup JSON deserialized to null.");

        return backup;
    }

    /// <summary>
    /// Сохраняет backup в файл. Перезаписывает существующий.
    /// Бросает IOException при ошибке записи.
    /// </summary>
    public void SaveToFile(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, ToJson());
    }

    /// <summary>
    /// Загружает backup из файла.
    /// Возвращает null, если файла нет.
    /// Бросает IOException / JsonException при ошибке чтения/парсинга.
    /// </summary>
    public static NxmHandlerBackup? LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        return FromJson(json);
    }

    /// <summary>
    /// Извлекает путь к exe из command-строки вида "\"C:\path\app.exe\" \"%1\"".
    /// Возвращает null, если command не соответствует формату.
    /// </summary>
    public static string? ExtractExePathFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var trimmed = command.TrimStart();

        // Ожидаем: "path" ...
        if (trimmed.Length < 2 || trimmed[0] != '"')
            return null;

        var endQuote = trimmed.IndexOf('"', 1);
        if (endQuote < 0)
            return null;

        var path = trimmed[1..endQuote];
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}
