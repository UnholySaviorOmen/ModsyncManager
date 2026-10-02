// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Serialization;

namespace Modsync.Core.Models.Manifest.Sources;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NexusSourceRef), "nexus")]
[JsonDerivedType(typeof(MirrorSourceRef), "mirror")]
public abstract class ArchiveSourceRef { }
