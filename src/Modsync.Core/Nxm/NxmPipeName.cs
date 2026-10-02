// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Nxm;

/// <summary>
/// Имя named pipe, через который ModsyncManager.NxmHandler.exe
/// передаёт nxm:// URL работающему ModsyncManager.exe.
///
/// Контракт между двумя проектами:
///   - Клиент (writer): ModsyncManager.NxmHandler/PipeClient.cs
///   - Сервер (reader): Modsync.Platform.Nexus/Protocol/NxmUrlReceiver.cs
///
/// Значение — только имя, без префикса "\\.\pipe\". Полный путь
/// строится BCL: NamedPipeClientStream/NamedPipeServerStream
/// сами добавляют префикс.
///
/// Windows: \\.\pipe\modsyncmanager-nxm
/// Linux:   (используется только на Windows, но константа безопасна
///          и на других платформах — BCL создаст UNIX-сокет с этим именем)
///
/// Изменение значения — breaking change для уже зарегистрированных
/// handler-ов. При смене — старая регистрация в реестре перестанет
/// работать до повторного Register.
/// </summary>
public static class NxmPipeName
{
    /// <summary>
    /// Имя pipe. Значение — часть публичного контракта.
    /// </summary>
    public const string Value = "modsyncmanager-nxm";
}
