// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.IO.Hashing;
using System.Text.Json.Serialization;

namespace Modsync.Core.Models.Hashing;

/// <summary>
/// Хеш xxHash64 в формате "xxh64:hex".
/// Hex-часть — ровно 16 символов (fixed-width ulong), без пробелов.
/// </summary>
[JsonConverter(typeof(XxHash64ValueJsonConverter))]
public readonly record struct XxHash64Value(ulong Value)
{
    public const string Prefix = "xxh64:";

    private const int HexLength = 16;

    public override string ToString() =>
        Prefix + Value.ToString("x16", CultureInfo.InvariantCulture);

    public static XxHash64Value FromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return FromStream(stream);
    }

    public static XxHash64Value FromStream(Stream stream)
    {
        var hasher = new XxHash64();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            hasher.Append(buffer.AsSpan(0, read));
        return new XxHash64Value(hasher.GetCurrentHashAsUInt64());
    }

    public static XxHash64Value Parse(string s)
    {
        if (string.IsNullOrEmpty(s))
            throw new FormatException("Hash must not be empty.");

        if (!s.StartsWith(Prefix, StringComparison.Ordinal))
            throw new FormatException($"Hash must start with '{Prefix}': {s}");

        var hex = s.AsSpan(Prefix.Length);

        if (hex.Length != HexLength)
            throw new FormatException(
                $"Hash hex part must be exactly {HexLength} characters " +
                $"(got {hex.Length}): {s}");

        // ulong.Parse с HexNumber принимает пробелы — отклоняем их явно.
        foreach (var ch in hex)
        {
            if (!Uri.IsHexDigit(ch))
                throw new FormatException(
                    $"Hash contains non-hex character '{ch}': {s}");
        }

        return new XxHash64Value(
            ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }
}
