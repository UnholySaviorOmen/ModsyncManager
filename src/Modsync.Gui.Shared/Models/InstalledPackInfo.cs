// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Gui.Shared.Models;

/// <summary>
/// Информация об инстансе, найденном в &lt;exeDir&gt;/Instances/.
///
/// Один инстанс = одна папка с modlist.json. Модель — плоская,
/// только для отображения и навигации.
///
/// Display name берётся из manifest.Meta.Name (источник правды),
/// не из имени папки: пользователь мог переименовать папку вручную.
///
/// Состояние «установлен ли MO2» (наличие ModOrganizer.exe) здесь
/// НЕ хранится: это UI-деталь карточки, проверяется в момент клика
/// на Open MO2 — чтобы не было stale-состояния.
/// </summary>
public sealed record InstalledPackInfo
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Game { get; init; }
    public required string GameVersion { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string InstancePath { get; init; }
    public required string ManifestPath { get; init; }
}
