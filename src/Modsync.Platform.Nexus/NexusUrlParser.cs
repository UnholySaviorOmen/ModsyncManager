// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace Modsync.Platform.Nexus;

/// <summary>
/// Парсер nxm://-URL.
///
/// Формат:
///   nxm://{game}/mods/{modId}/files/{fileId}
///       ?key={key}&amp;expires={expires}&amp;user_id={userId}
///
/// Правила:
///   - Схема строго "nxm".
///   - Host (game domain) непустой.
///   - Path строго "/mods/{modId}/files/{fileId}" (регистр сохраняется).
///   - modId, fileId — положительные int.
///   - Query-параметры key/expires/user_id — опциональны, но если
///     хоть один из них задан, все три обязаны быть заданы.
///   - expires — Unix timestamp в секундах (положительный).
///   - user_id — положительный int.
///   - Дополнительные query-параметры (file_name, nmm_version и т.п.)
///     игнорируются.
///
/// Метод TryParse никогда не бросает, метод Parse бросает FormatException
/// с понятным текстом.
/// </summary>
public static class NexusUrlParser
{
    private const string Scheme = "nxm";

    /// <summary>
    /// Пытается разобрать URL. Возвращает false, если URL не является
    /// валидным nxm://-URL. Не бросает исключений.
    /// </summary>
    public static bool TryParse(string? url, out NexusNxmUrl? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, Scheme, StringComparison.Ordinal))
            return false;

        // Host — game domain. Uri автоматически lowercase-ит host;
        // для нас это ок (Nexus game domain всегда lowercase).
        if (string.IsNullOrEmpty(uri.Host))
            return false;

        // Path должен быть ровно /mods/{modId}/files/{fileId}.
        // Uri.AbsolutePath уже декодирован от percent-encoding.
        var segments = uri.AbsolutePath.Split(
            '/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length != 4)
            return false;

        if (!string.Equals(segments[0], "mods", StringComparison.Ordinal))
            return false;

        if (!string.Equals(segments[2], "files", StringComparison.Ordinal))
            return false;

        if (!TryParsePositiveInt(segments[1], out var modId))
            return false;

        if (!TryParsePositiveInt(segments[3], out var fileId))
            return false;

        // Query-параметры.
        string? key = null;
        long? expires = null;
        int? userId = null;

        if (!string.IsNullOrEmpty(uri.Query))
        {
            // uri.Query начинается с '?', убираем его и парсим вручную.
            // Не используем HttpUtility.ParseQueryString — лишняя зависимость.
            var query = uri.Query[1..];
            var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);

            foreach (var pair in pairs)
            {
                var eq = pair.IndexOf('=');
                if (eq < 0)
                    continue;

                var name = pair[..eq];
                var value = Uri.UnescapeDataString(pair[(eq + 1)..]);

                switch (name)
                {
                    case "key":
                        key = value;
                        break;
                    case "expires":
                        if (!long.TryParse(
                                value,
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out var exp))
                        {
                            return false;
                        }
                        if (exp <= 0)
                            return false;
                        expires = exp;
                        break;
                    case "user_id":
                        if (!int.TryParse(
                                value,
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out var uid))
                        {
                            return false;
                        }
                        if (uid <= 0)
                            return false;
                        userId = uid;
                        break;
                    default:
                        // Прочие параметры игнорируем (file_name, nmm_version, ...).
                        break;
                }
            }
        }

        // key/expires/user_id — либо все три, либо ни одного.
        bool anyCredential = key is not null
                             || expires is not null
                             || userId is not null;

        if (anyCredential)
        {
            if (key is null || expires is null || userId is null)
                return false;

            if (string.IsNullOrEmpty(key))
                return false;
        }

        result = new NexusNxmUrl
        {
            Game = uri.Host,
            ModId = modId,
            FileId = fileId,
            Key = key,
            Expires = expires,
            UserId = userId,
        };

        return true;
    }

    /// <summary>
    /// Разбирает URL. Бросает FormatException, если URL не валиден.
    /// </summary>
    public static NexusNxmUrl Parse(string url)
    {
        if (TryParse(url, out var result) && result is not null)
            return result;

        throw new FormatException(
            $"Not a valid nxm:// URL: '{url}'. " +
            $"Expected format: nxm://{{game}}/mods/{{modId}}/files/{{fileId}}" +
            $"?key={{key}}&expires={{expires}}&user_id={{userId}} (credentials optional).");
    }

    private static bool TryParsePositiveInt(string value, out int result)
    {
        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result))
        {
            return false;
        }

        return result > 0;
    }
}
