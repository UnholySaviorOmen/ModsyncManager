// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Validation;

/// <summary>
/// Результат валидации. Содержит список сообщений об ошибках.
/// Пустой список = валидация прошла.
/// </summary>
public sealed class ValidationResult
{
    public static readonly ValidationResult Success = new(Array.Empty<string>());

    public IReadOnlyList<string> Errors { get; }

    public bool IsValid => Errors.Count == 0;

    private ValidationResult(IReadOnlyList<string> errors)
    {
        Errors = errors;
    }

    public static ValidationResult Fail(params string[] errors)
    {
        if (errors.Length == 0)
            throw new ArgumentException("At least one error required.", nameof(errors));
        return new ValidationResult(errors);
    }

    public static ValidationResult Fail(IEnumerable<string> errors)
    {
        var list = errors.ToList();
        if (list.Count == 0)
            throw new ArgumentException("At least one error required.", nameof(errors));
        return new ValidationResult(list);
    }

    public override string ToString()
        => IsValid ? "OK" : string.Join("; ", Errors);
}
