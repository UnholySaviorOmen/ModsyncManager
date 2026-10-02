// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Models.Pack;

/// <summary>
/// Файл, который не удалось восстановить из архивов.
/// Packer выгружает такие файлы в __ModsyncManager_Output с сохранением структуры,
/// чтобы автор мог увидеть свои правки и решить, делать ли из них патч.
///
/// Используется для unmatched extensions и unmatched extras.
/// Для unmatched модов используется UnmatchedFile (ModName вместо EntryName).
///
/// EntryName:
///   - для extensions — нормализованный relative path из config.Mo2.Extensions[]
///     (например, "plugins/fomod.dll", "tools/BethINI");
///   - для extras — нормализованный relative path из config.StockGame.Extras[].
///
/// RelativePath — путь файла внутри корня (MO2/ или Stock Game/).
/// </summary>
public readonly record struct UnmatchedEntry(
    string EntryName,
    string RelativePath,
    long Size);
