// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Gui.Modules.Verify.ViewModels;

/// <summary>
/// Строка таблицы проверок.
///
/// Обёртка над VerifyCheckResult — плоская, с двумя вычисляемыми
/// полями для удобства биндинга:
///   - StatusGlyph — "✓" или "×";
///   - StatusColor — hex для Foreground.
///
/// VerifyCheckResult — sealed class с required-свойствами,
/// поэтому в XAML напрямую биндить нельзя без конвертеров.
/// </summary>
public sealed class VerifyRowVM
{
    public required string Name { get; init; }
    public required bool Passed { get; init; }
    public string? Message { get; init; }

    public string StatusGlyph => Passed ? "✓" : "×";

    public string StatusColor => Passed ? "#7fc98a" : "#d97777";
}
