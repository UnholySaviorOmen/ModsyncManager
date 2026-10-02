// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;

namespace Modsync.Core.Models.Pack;

/// <summary>
/// Результат MatchStep.
/// </summary>
public sealed class MatchResult
{
    /// <summary>Директивы по каждому моду. Ключ — имя мода.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<Directive>> ModDirectives { get; init; }

    /// <summary>
    /// Файлы, которые не удалось восстановить из архивов.
    /// Пакер выгружает их в __ModsyncManager_Output с сохранением структуры.
    /// meta.ini сюда не попадает — он идёт в ModMetas.
    /// </summary>
    public required IReadOnlyList<UnmatchedFile> Unmatched { get; init; }

    /// <summary>
    /// Structured meta.ini по модам. Ключ — имя мода.
    /// Отсутствие ключа означает, что у мода нет meta.ini.
    /// </summary>
    public required IReadOnlyDictionary<string, ModMeta> ModMetas { get; init; }
}
