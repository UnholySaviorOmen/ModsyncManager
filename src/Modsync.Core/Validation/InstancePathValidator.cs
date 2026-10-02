// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Validation;

/// <summary>
/// Валидация instance.path — относительного пути к корню инстанса.
/// Отличия от RelativePathValidator:
///   - разрешён "." (текущая папка);
///   - запрещён "..";
///   - запрещены абсолютные пути;
///   - пустая строка запрещена (используйте "." явно).
/// </summary>
public static class InstancePathValidator
{
    public static ValidationResult Validate(string? path)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(path))
        {
            errors.Add("instance.path must be non-empty (use \".\" for current directory).");
            return ValidationResult.Fail(errors);
        }

        // Абсолютные пути
        if (Path.IsPathRooted(path))
        {
            errors.Add($"instance.path must be a relative path (got '{path}').");
            return ValidationResult.Fail(errors);
        }

        // Ведущий слэш
        if (path[0] == '/' || path[0] == '\\')
        {
            errors.Add("instance.path must not start with '/' or '\\'.");
            return ValidationResult.Fail(errors);
        }

        // Drive letter
        if (path.Length >= 2 && path[1] == ':')
        {
            errors.Add("instance.path must not contain a drive letter.");
            return ValidationResult.Fail(errors);
        }

        // Нормализация \ → /
        var normalized = path.Replace('\\', '/');

        // Разбиваем на сегменты
        var segments = normalized.Split('/');

        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            var isLast = i == segments.Length - 1;

            if (seg == "..")
            {
                errors.Add("instance.path must not contain '..'.");
                break;
            }
            // "." — разрешён, но только как единственный сегмент или в начале
            // (например, "./subdir"). Не запрещаем.
            if (seg.Length == 0 && !isLast)
            {
                errors.Add("instance.path must not contain empty segments.");
                break;
            }
        }

        // Запрещённые символы Windows
        foreach (var ch in new[] { '<', '>', ':', '"', '|', '?', '*' })
        {
            if (path.Contains(ch))
            {
                errors.Add($"instance.path must not contain '{ch}'.");
                break;
            }
        }

        return errors.Count == 0
            ? ValidationResult.Success
            : ValidationResult.Fail(errors);
    }
}
