// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Manifest.Sources;

namespace Modsync.Core.Models.Pack;

/// <summary>
/// Явное указание источников для архива, у которого нет .meta-файла.
/// </summary>
public sealed record PackArchiveSource
{
    /// <summary>Имя файла в downloads/ (например, "SomeMod.7z").</summary>
    public required string Archive { get; init; }

    /// <summary>Источники в порядке приоритета. Непустой.</summary>
    public required IReadOnlyList<ArchiveSourceRef> Sources { get; init; }
}
