// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Models.Mo2;

/// <summary>
/// Строка modlist.txt: имя мода + флаг "включён".
/// Порядок соответствует порядку строк в файле (сверху вниз).
/// </summary>
public sealed record ModlistEntry(string Name, bool Enabled);
