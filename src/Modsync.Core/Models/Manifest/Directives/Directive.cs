// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Serialization;

namespace Modsync.Core.Models.Manifest.Directives;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FromArchiveDirective), "FromArchive")]
[JsonDerivedType(typeof(CreateDirectoryDirective), "CreateDirectory")]
[JsonDerivedType(typeof(DeleteDirective), "Delete")]
public abstract class Directive
{
    public required string Destination { get; init; }
}
