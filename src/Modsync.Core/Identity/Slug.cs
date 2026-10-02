// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text;

namespace Modsync.Core.Identity;

/// <summary>
/// Генератор slug из произвольных имён.
/// Slug — ASCII-only, lower-case, разделитель — дефис.
/// </summary>
public static class Slug
{
    /// <summary>Максимальная длина slug.</summary>
    public const int MaxLength = 80;

    /// <summary>
    /// Преобразует произвольную строку в slug.
    /// Бросает ArgumentException, если результат получается пустым.
    /// </summary>
    public static string From(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Slug input must be non-empty.", nameof(input));

        var sb = new StringBuilder(input.Length);
        var lastWasDash = true; // чтобы обрезать ведущие дефисы

        foreach (var ch in input)
        {
            if (ch >= 'a' && ch <= 'z')
            {
                sb.Append(ch);
                lastWasDash = false;
            }
            else if (ch >= 'A' && ch <= 'Z')
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasDash = false;
            }
            else if (ch >= '0' && ch <= '9')
            {
                sb.Append(ch);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                sb.Append('-');
                lastWasDash = true;
            }
        }

        var result = sb.ToString().TrimEnd('-');

        if (result.Length == 0)
            throw new ArgumentException(
                $"Slug input produced an empty result: '{input}'", nameof(input));

        if (result.Length > MaxLength)
            result = result[..MaxLength].TrimEnd('-');

        return result;
    }

    /// <summary>
    /// Преобразует имя файла (с расширением) в slug без расширения.
    /// </summary>
    public static string FromFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name must be non-empty.", nameof(fileName));

        var dot = fileName.LastIndexOf('.');
        var nameWithoutExt = dot > 0 ? fileName[..dot] : fileName;
        return From(nameWithoutExt);
    }
}
