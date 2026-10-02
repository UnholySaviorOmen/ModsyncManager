// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Models.Pack;

public sealed record PackStockGame
{
    /// <summary>
    /// Пути к файлам/папкам extras относительно корня Stock Game/.
    /// Например: "skse64_loader.exe", "enbseries/".
    /// </summary>
    public required IReadOnlyList<string> Extras { get; init; }
}
