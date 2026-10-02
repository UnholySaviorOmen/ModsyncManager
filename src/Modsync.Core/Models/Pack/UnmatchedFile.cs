// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Models.Pack;

/// <summary>
/// Файл мода, который не удалось восстановить из архивов.
/// Пакер выгружает такие файлы в __ModsyncManager_Output с сохранением структуры,
/// чтобы автор мог увидеть свои правки и решить, делать ли из них патч.
/// </summary>
public readonly record struct UnmatchedFile(
    string ModName,
    string RelativePath,
    long Size);
