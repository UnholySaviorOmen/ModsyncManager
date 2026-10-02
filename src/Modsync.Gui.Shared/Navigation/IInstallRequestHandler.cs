// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Gui.Shared.Navigation;

/// <summary>
/// Реализуется VM, которые могут запросить переход на экран Install
/// с предзаполненными путями (manifest + target). Сейчас — HomeVM.
///
/// MainWindowVM подписывается на событие при навигации на такой экран
/// и сам решает, как переключить ActivePane и передать пути в InstallVM.
///
/// Оба аргумента обязательны:
///   - manifestPath — путь к modlist.json (Install: &lt;InstancePath&gt;/modlist.json,
///                    Update: выбранный юзером через диалог).
///   - targetPath   — куда ставить (всегда InstancePath карточки).
///
/// Install без target (в &lt;exeDir&gt;/Instances/&lt;meta.name&gt;/) — только
/// через CLI --target=null. В GUI всегда явный target: карточка знает,
/// какой инстанс она представляет.
/// </summary>
public interface IInstallRequestHandler
{
    event Action<string, string>? InstallRequested;
}
