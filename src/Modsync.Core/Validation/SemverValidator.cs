// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.RegularExpressions;

namespace Modsync.Core.Validation;

/// <summary>
/// Валидация semver 2.0.0.
/// Официальный паттерн с semver.org.
/// </summary>
public static class SemverValidator
{
    // Официальный regex с semver.org (https://semver.org/#is-there-a-suggested-regular-expression-regex-to-check-a-semver-string)
    private static readonly Regex SemverRegex = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ValidationResult Validate(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return ValidationResult.Fail("meta.version must be non-empty.");

        if (!SemverRegex.IsMatch(version))
            return ValidationResult.Fail(
                $"meta.version must be a valid semver 2.0.0 (got '{version}').");

        return ValidationResult.Success;
    }
}
