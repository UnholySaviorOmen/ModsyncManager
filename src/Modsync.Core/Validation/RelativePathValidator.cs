// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Validation;

/// <summary>
/// Валидация относительных путей в extensions/extras.
/// Запрещено: абсолютные пути, "..", ведущий "/" или "\", пустые сегменты.
/// Разрешено: / и \ как разделители (нормализуются к /).
/// </summary>
public static class RelativePathValidator
{
    public static ValidationResult Validate(string? path, string fieldName)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(path))
        {
            errors.Add($"{fieldName} must be non-empty.");
            return ValidationResult.Fail(errors);
        }

        // Абсолютные пути
        if (Path.IsPathRooted(path))
        {
            errors.Add($"{fieldName} must be a relative path (got '{path}').");
            return ValidationResult.Fail(errors);
        }

        // Ведущий слэш
        if (path[0] == '/' || path[0] == '\\')
        {
            errors.Add($"{fieldName} must not start with '/' or '\\'.");
            return ValidationResult.Fail(errors);
        }

        // Drive letter (C:, D:) — Path.IsPathRooted уже ловит, но дублируем на всякий
        if (path.Length >= 2 && path[1] == ':')
        {
            errors.Add($"{fieldName} must not contain a drive letter.");
            return ValidationResult.Fail(errors);
        }

        // Нормализуем \ → /
        var normalized = path.Replace('\\', '/');

        // Разбиваем на сегменты
        var segments = normalized.Split('/');

        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            var isLast = i == segments.Length - 1;

            if (seg == "..")
            {
                errors.Add($"{fieldName} must not contain '..'.");
                break;
            }
            if (seg == ".")
            {
                errors.Add($"{fieldName} must not contain '.' segments.");
                break;
            }
            if (seg.Length == 0 && !isLast)
            {
                errors.Add($"{fieldName} must not contain empty segments.");
                break;
            }
        }

        // Запрещённые символы Windows
        foreach (var ch in new[] { '<', '>', ':', '"', '|', '?', '*' })
        {
            if (path.Contains(ch))
            {
                errors.Add($"{fieldName} must not contain '{ch}'.");
                break;
            }
        }

        return errors.Count == 0
            ? ValidationResult.Success
            : ValidationResult.Fail(errors);
    }
}
