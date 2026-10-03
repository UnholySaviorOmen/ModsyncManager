// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modsync.Core.Validation;

namespace Modsync.Gui.Modules.Pack.ViewModels;

/// <summary>
/// Строка в списке extensions или extras.
///
/// Пользователь вводит относительный путь (например, "plugins/fomod.dll"
/// или "tools/BethINI/"). Валидация — через RelativePathValidator.
///
/// RemoveCommand — self-remove: вызывает callback родительской VM,
/// чтобы та удалила этот элемент из коллекции. Так работает
/// binding внутри DataTemplate без гимнастики с $parent.
/// </summary>
public sealed partial class PathEntryVM : ObservableObject
{
    private readonly Action<PathEntryVM>? _onRemove;

    [ObservableProperty]
    private string _value = "";

    public string? Error { get; private set; }

    public bool IsValid { get; private set; }

    public event EventHandler? ValidationChanged;

    public PathEntryVM(
        string value = "",
        Action<PathEntryVM>? onRemove = null)
    {
        _value = value;
        _onRemove = onRemove;
        Validate();
    }

    partial void OnValueChanged(string value)
    {
        Validate();
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Remove()
    {
        _onRemove?.Invoke(this);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            IsValid = false;
            Error = null;    // пустое поле — не ошибка, просто невалидно
        }
        else
        {
            var result = RelativePathValidator.Validate(Value, "path");
            IsValid = result.IsValid;
            Error = result.IsValid ? null : result.Errors.FirstOrDefault();
        }

        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(IsValid));
    }
}
