// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Pack;

namespace Modsync.Pack.Models;

/// <summary>
/// Вход для PackConfigBuilder.Build — данные, которые ввёл
/// пользователь в GUI-форме «Create Pack Config».
///
/// mo2-секция НЕ передаётся здесь: она хардкодится в PackConfigBuilder
/// как стандарт для генерируемых манифестов (MO2 2.5.2, официальный
/// GitHub-релиз). Пользователь может изменить profile через форму —
/// он передаётся в Profile.
///
/// meta — обязателен, все 5 полей.
/// extensions/extras — опциональны, могут быть пустыми.
/// archiveSources — список источников для non-nexus архивов. Может
/// быть пустым (если в downloads/ нет unresolved-архивов).
/// </summary>
public sealed record PackConfigBuilderInput
{
    /// <summary>Путь к корню инстанса (папке, содержащей MO2/ и Stock Game/).</summary>
    public required string InstancePath { get; init; }

    /// <summary>
    /// Metadata сборки. Все 5 полей обязательны.
    /// </summary>
    public required PackMeta Meta { get; init; }

    /// <summary>
    /// Имя профиля MO2 в profiles/. Если пусто — по умолчанию "Default".
    /// Список доступных профилей — PackConfigBuilder.ListProfiles.
    /// </summary>
    public required string Profile { get; init; }

    /// <summary>
    /// Пути extensions относительно MO2/. Может быть пустым.
    /// </summary>
    public required IReadOnlyList<string> Extensions { get; init; }

    /// <summary>
    /// Пути extras относительно Stock Game/. Может быть пустым.
    /// </summary>
    public required IReadOnlyList<string> Extras { get; init; }

    /// <summary>
    /// Источники для non-nexus архивов. Может быть пустым, если
    /// в downloads/ нет unresolved-архивов.
    /// </summary>
    public required IReadOnlyList<PackArchiveSource> ArchiveSources { get; init; }
}
