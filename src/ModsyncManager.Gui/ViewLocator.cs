// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Modsync.Gui.Shared.ViewModels;

namespace ModsyncManager.Gui;

public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null) return null;

        var vmType = param.GetType();
        var shortName = vmType.Name.Replace("VM", "View", StringComparison.Ordinal);

        // 1. Точное совпадение по FullName с заменой .ViewModels. → .Views.
        //    Работает только если View в той же сборке и namespace без .Controls
        //    в середине. Часто не срабатывает между проектами.
        var directName = vmType.FullName!
            .Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("VM", "View", StringComparison.Ordinal);

        var direct = Type.GetType(directName);
        if (direct is not null && typeof(Control).IsAssignableFrom(direct))
            return (Control)Activator.CreateInstance(direct)!;

        // 2. Поиск по всем загруженным сборкам: перебираем все типы и ищем
        //    тот, чей FullName заканчивается на ".Views.<ShortName>".
        //
        //    Это надёжнее, чем `asm.GetType($"{asmName}.Views.{ShortName}")`,
        //    потому что:
        //      - assembly name ≠ root namespace (ModsyncManager.Gui → "ModsyncManager");
        //      - namespace View может быть вложенным (Modsync.Gui.Modules.Install.Views);
        //      - .NET может ещё не загрузить сборку — на всякий случай
        //        форсируем загрузку через GetReferencedAssemblies.
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var candidate = FindViewInAssembly(asm, shortName);
            if (candidate is not null)
                return (Control)Activator.CreateInstance(candidate)!;
        }

        return new TextBlock { Text = $"View not found: {vmType.FullName}" };
    }

    private static Type? FindViewInAssembly(System.Reflection.Assembly asm, string shortName)
    {
        Type[] types;
        try
        {
            types = asm.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            // Часть типов не загрузилась — работаем с тем, что есть.
            types = ex.Types.Where(t => t is not null).ToArray()!;
        }

        foreach (var type in types)
        {
            if (type is null) continue;
            if (!typeof(Control).IsAssignableFrom(type)) continue;
            if (type.IsAbstract) continue;

            // FullName = "ModsyncManager.Gui.Views.HomeView" или
            //           "Modsync.Gui.Modules.Install.Views.InstallView".
            // Ищем совпадение последних двух сегментов: ".Views.<ShortName>".
            var fullName = type.FullName;
            if (fullName is null) continue;

            if (fullName.EndsWith("." + shortName, StringComparison.Ordinal)
                && fullName.Contains(".Views.", StringComparison.Ordinal))
            {
                return type;
            }
        }

        return null;
    }

    public bool Match(object? data) => data is ViewModel;
}
