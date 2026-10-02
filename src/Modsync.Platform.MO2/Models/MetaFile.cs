// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.MO2.Models;

public sealed class MetaFile
{
    public required string GameName { get; init; }
    public required int ModId { get; init; }
    public required int FileId { get; init; }
}
