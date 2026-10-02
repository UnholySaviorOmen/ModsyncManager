// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Hashing;

namespace Modsync.Core.Models.Manifest.Directives;

public sealed class FromArchiveDirective : Directive
{
    public required string Archive { get; init; }
    public required string Source { get; init; }
    public required XxHash64Value Hash { get; init; }
    public required long Size { get; init; }
}
