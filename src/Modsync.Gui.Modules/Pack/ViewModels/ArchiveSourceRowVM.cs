// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest.Sources;

namespace Modsync.Gui.Modules.Pack.ViewModels;

/// <summary>
/// Один источник (url + hash) для архива без .meta.
///
/// Hash нормализуется: пользователь может ввести "E574E05EB6C470AD"
/// или "xxh64:E574E05EB6C470AD" — оба варианта принимаются.
///
/// RemoveCommand — self-remove: вызывает callback родительской
/// UnresolvedArchiveRowVM, чтобы та удалила этот элемент.
/// </summary>
public sealed partial class ArchiveSourceRowVM : ObservableObject
{
    private readonly Action<ArchiveSourceRowVM>? _onRemove;
    private readonly Func<bool>? _canRemove;

    [ObservableProperty]
    private string? _url;

    [ObservableProperty]
    private string? _hash;

    public string? UrlError { get; private set; }

    public string? HashError { get; private set; }

    public bool IsValid { get; private set; }

    public event EventHandler? ValidationChanged;

    public ArchiveSourceRowVM(
        Action<ArchiveSourceRowVM>? onRemove = null,
        Func<bool>? canRemove = null)
    {
        _onRemove = onRemove;
        _canRemove = canRemove;
    }

    partial void OnUrlChanged(string? value)
    {
        Validate();
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnHashChanged(string? value)
    {
        Validate();
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        _onRemove?.Invoke(this);
    }

    private bool CanRemove() => _canRemove?.Invoke() ?? true;

    /// <summary>
    /// Уведомить UI, что CanExecute мог измениться (например,
    /// в родителе изменилось количество источников).
    /// </summary>
    public void NotifyCanRemoveChanged()
    {
        RemoveCommand.NotifyCanExecuteChanged();
    }

    public void Validate()
    {
        // URL
        if (string.IsNullOrWhiteSpace(Url))
        {
            UrlError = null;
        }
        else if (!Uri.TryCreate(Url, UriKind.Absolute, out _))
        {
            UrlError = "Not a valid URL";
        }
        else
        {
            UrlError = null;
        }

        // Hash
        if (string.IsNullOrWhiteSpace(Hash))
        {
            HashError = null;
        }
        else
        {
            try
            {
                XxHash64Value.Parse(NormalizeHash(Hash));
                HashError = null;
            }
            catch (FormatException ex)
            {
                HashError = ex.Message;
            }
        }

        IsValid =
            !string.IsNullOrWhiteSpace(Url) && UrlError is null &&
            !string.IsNullOrWhiteSpace(Hash) && HashError is null;

        OnPropertyChanged(nameof(UrlError));
        OnPropertyChanged(nameof(HashError));
        OnPropertyChanged(nameof(IsValid));
    }

    /// <summary>
    /// Приводит hash к формату "xxh64:...". Если префикса нет — добавляет.
    /// </summary>
    public static string NormalizeHash(string hash)
    {
        var trimmed = hash.Trim();
        if (trimmed.StartsWith(XxHash64Value.Prefix, StringComparison.OrdinalIgnoreCase))
            return XxHash64Value.Prefix + trimmed[XxHash64Value.Prefix.Length..];

        return XxHash64Value.Prefix + trimmed;
    }

    public MirrorSourceRef ToSourceRef()
    {
        return new MirrorSourceRef
        {
            Url = Url!.Trim(),
            Hash = XxHash64Value.Parse(NormalizeHash(Hash!)),
        };
    }
}
