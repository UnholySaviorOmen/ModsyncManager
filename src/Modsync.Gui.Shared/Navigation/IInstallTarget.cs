// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Gui.Shared.Navigation;

/// <summary>
/// Реализуется VM экрана Install, который умеет принимать
/// предзаполненные пути (manifest + target) от другого экрана (Home).
///
/// MainWindowVM после NavigateTo(Install) проверяет, реализует ли
/// ActivePane этот интерфейс, и если да — вызывает PrepareForInstall.
///
/// Контракт: PrepareForInstall сбрасывает состояние в Configuration,
/// обнуляет Summary/ErrorMessage, устанавливает ModlistPicker.Path
/// и TargetPicker.Path.
/// </summary>
public interface IInstallTarget
{
    void PrepareForInstall(string manifestPath, string targetPath);
}
