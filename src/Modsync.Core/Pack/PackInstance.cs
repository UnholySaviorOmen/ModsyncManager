// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Models.Pack;

/// <summary>
/// Информация об инстансе MO2 в modsyncmanager-pack.json.
/// </summary>
public sealed record PackInstance
{
    /// <summary>
    /// Путь к корню инстанса (папке, содержащей MO2/ и Stock Game/)
    /// относительно modsyncmanager-pack.json.
    /// Пример: "NordicUI Overhaul".
    /// </summary>
    public required string Path { get; init; }
}
