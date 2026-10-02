// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using Modsync.Core.Nxm;
using Modsync.Platform.Nexus.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Modsync.Platform.Nexus;

/// <summary>
/// DI-extension для модуля nxm:// handler + Free-загрузки.
///
/// Регистрирует:
///   - IRegistryAccessor    → WindowsRegistryAccessor (Windows)
///                          → NullRegistryAccessor   (иначе)
///   - INxmUrlReceiver      → NxmUrlReceiver      (Singleton, через фабрику)
///   - IUrlOpener           → ShellUrlOpener      (Singleton)
///   - IProtocolRegistrar   → ProtocolRegistrar   (Singleton, через фабрику)
///   - INexusFreeNxmProvider→ NexusFreeNxmProvider (Replace!)
///
/// Replace — потому что AddFirelinkInstall (в Modsync.Install)
/// регистрирует NullNexusFreeNxmProvider через TryAddSingleton.
/// Клиенты, которые поддерживают Free-загрузку (GUI), вызывают
/// AddModsyncNxm() после AddModsyncInstall(), чтобы перезаписать
/// default на реальную реализацию.
///
/// Про IProtocolRegistrar: у ProtocolRegistrar два публичных
/// конструктора. DI выбирает самый длинный и упрётся в
/// string backupFilePath / string handlerPath. Поэтому регистрируем
/// через явную фабрику, которая вызывает «короткий» конструктор
/// (registry + logger) — тот, что сам вычисляет backupPath и handlerPath
/// от AppContext.BaseDirectory.
///
/// Про INxmUrlReceiver: у NxmUrlReceiver теперь тоже два
/// конструктора (с pipeName и без). Регистрируем через фабрику,
/// чтобы явно передать NxmPipeName.Value.
/// </summary>
public static class NxmServices
{
    public static IServiceCollection AddModsyncNxm(
        this IServiceCollection services)
    {
        // --- Реестр: Windows-only ---
        // На Windows — реальный WindowsRegistryAccessor.
        // На других платформах — заглушка, чтобы граф собирался.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            services.TryAddSingleton<IRegistryAccessor, WindowsRegistryAccessor>();
        }
        else
        {
            services.TryAddSingleton<IRegistryAccessor, NullRegistryAccessor>();
        }

        // --- INxmUrlReceiver через фабрику ---
        // Явная фабрика гарантирует, что имя pipe берётся из
        // единственного источника правды — NxmPipeName.Value.
        // Без фабрики DI выберет конструктор с string pipeName
        // и попытается резолвить его как сервис → упадёт.
        services.TryAddSingleton<INxmUrlReceiver>(sp =>
            new NxmUrlReceiver(
                NxmPipeName.Value,
                sp.GetRequiredService<ILogger<NxmUrlReceiver>>()));

        services.TryAddSingleton<IUrlOpener, ShellUrlOpener>();

        // --- IProtocolRegistrar через фабрику ---
        // Явная фабрика гарантирует выбор «короткого» конструктора
        // ProtocolRegistrar(IRegistryAccessor, ILogger<ProtocolRegistrar>).
        // Без неё DI выберет самый длинный конструктор и упадёт
        // на string-параметрах (backupFilePath, handlerPath).
        services.TryAddSingleton<IProtocolRegistrar>(sp =>
            new ProtocolRegistrar(
                sp.GetRequiredService<IRegistryAccessor>(),
                sp.GetRequiredService<ILogger<ProtocolRegistrar>>()));

        // Replace, не TryAdd — перезаписываем Null-реализацию.
        services.Replace(
            ServiceDescriptor.Singleton<
                INexusFreeNxmProvider,
                NexusFreeNxmProvider>());

        return services;
    }
}
