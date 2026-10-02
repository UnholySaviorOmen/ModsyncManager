// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Models.Manifest;

/// <summary>
/// Поддерживаемые версии схемы манифеста.
/// Единственная точка правды: packer пишет SchemaVersion из Supported,
/// installer (ReadManifestStep) и verify (VerifyPipeline) читают
/// через IsSupported.
/// </summary>
public static class ManifestSchema
{
    /// <summary>Текущая версия, которую пишет packer.</summary>
    public const string Current = "1.0.0";

    /// <summary>
    /// Все версии схемы, которые умеют читать эта сборка Modsync Manager.
    /// Включает Current. При добавлении новой версии — добавить сюда,
    /// сохранив старые (для обратной совместимости).
    /// </summary>
    public static readonly IReadOnlySet<string> Supported =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "1.0.0",
        };

    public static bool IsSupported(string schemaVersion)
        => Supported.Contains(schemaVersion);
}
