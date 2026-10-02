// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Models.Mo2;

public sealed class LoadorderFile
{
    /// <summary>Порядок загрузки: сверху — раньше.</summary>
    public required IReadOnlyList<string> Plugins { get; init; }
}
