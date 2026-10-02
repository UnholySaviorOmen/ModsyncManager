// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Validation;

/// <summary>
/// Валидация имени сборки (meta.name).
/// Windows-специфика: запрещённые символы, зарезервированные имена,
/// trailing dot/space.
/// </summary>
public static class NameValidator
{
    private static readonly char[] ForbiddenChars =
        { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5",
        "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
        "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static ValidationResult Validate(string? name)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("meta.name must be non-empty.");
            return ValidationResult.Fail(errors);
        }

        if (name.Length > 200)
            errors.Add($"meta.name must be at most 200 characters (got {name.Length}).");

        // Управляющие символы
        foreach (var ch in name)
        {
            if (ch < 0x20 || ch == 0x7F)
            {
                errors.Add("meta.name must not contain control characters.");
                break;
            }
        }

        // Запрещённые символы
        foreach (var ch in ForbiddenChars)
        {
            if (name.Contains(ch))
            {
                errors.Add($"meta.name must not contain '{ch}'.");
            }
        }

        // Trailing dot или space
        if (name.EndsWith('.') || name.EndsWith(' '))
            errors.Add("meta.name must not end with '.' or space.");

        // Leading space
        if (name.StartsWith(' '))
            errors.Add("meta.name must not start with space.");

        // Зарезервированные имена Windows
        var baseName = name;
        var dot = name.IndexOf('.');
        if (dot > 0) baseName = name[..dot];

        if (ReservedNames.Contains(baseName))
            errors.Add($"meta.name must not be a reserved Windows name ('{baseName}').");

        return errors.Count == 0
            ? ValidationResult.Success
            : ValidationResult.Fail(errors);
    }
}
