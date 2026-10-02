// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Hashing;

namespace Modsync.Core.Models.Manifest.Sources;

public sealed class MirrorSourceRef : ArchiveSourceRef
{
    public required string Url { get; init; }
    public required XxHash64Value Hash { get; init; }
}
