// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modsync.Gui.Shared.Services;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Modsync.Gui.Shared.ViewModels.Controls;

public sealed partial class FilePickerVM : ObservableObject
{
    private readonly IFilePickerService _picker;

    [ObservableProperty]
    private string? _path;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isValid;

    public string Placeholder { get; init; } = "Select a file...";
    public string? FilterHint { get; init; }
    public bool MustExist { get; init; } = true;
    public bool Folder { get; init; }

    public FilePickerVM(IFilePickerService picker)
    {
        _picker = picker;
    }

    public void SetPath(string? path)
    {
        Path = path;
        Validate();
    }

    public void Clear()
    {
        Path = null;
        Error = null;
        IsValid = false;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Path))
        {
            IsValid = false;
            Error = null;
            return;
        }

        if (MustExist)
        {
            var exists = Folder ? Directory.Exists(Path) : File.Exists(Path);
            if (!exists)
            {
                IsValid = false;
                Error = Folder ? "Folder not found" : "File not found";
                return;
            }
        }

        IsValid = true;
        Error = null;
    }

    [RelayCommand]
    private async Task PickAsync()
    {
        var picked = Folder
            ? await _picker.PickFolderAsync(Placeholder)
            : await _picker.PickFileAsync(Placeholder, FilterHint);

        if (picked is not null)
            SetPath(picked);
    }
}
