// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using CodingSeb.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Core.Models.Pack;
using Modsync.Pack.Models;

namespace Modsync.Gui.Modules.Pack.ViewModels;

/// <summary>
/// Строка non-nexus архива в форме Pack Config.
///
/// Локализация: StatusText через Loc.Tr. Смена языка — при
/// перезапуске приложения.
/// </summary>
public sealed partial class UnresolvedArchiveRowVM : ObservableObject
{
    public string FileName { get; }
    public string SizeText { get; }
    public string HashText { get; }

    public ObservableCollection<ArchiveSourceRowVM> Sources { get; } = new();

    [ObservableProperty]
    private bool _isExcluded;

    public event EventHandler? ValidationChanged;

    public UnresolvedArchiveRowVM(UnresolvedArchiveInfo info)
    {
        FileName = info.FileName;
        SizeText = FormatSize(info.Size);
        HashText = info.Hash.ToString();

        AddInitialSource(info.Hash.ToString());
    }

    [RelayCommand]
    private void AddSource()
    {
        if (IsExcluded) return;

        var source = new ArchiveSourceRowVM(
            onRemove: RemoveSource,
            canRemove: () => Sources.Count > 1)
        {
            Url = "",
            Hash = HashText,
        };

        source.ValidationChanged += OnSourceValidationChanged;

        Sources.Add(source);
        NotifyRemoveCommands();
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleExclude()
    {
        IsExcluded = !IsExcluded;
    }

    partial void OnIsExcludedChanged(bool value)
    {
        OnPropertyChanged(nameof(BorderOpacity));
        OnPropertyChanged(nameof(StatusText));
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }

    public double BorderOpacity => IsExcluded ? 0.5 : 1.0;

    public string StatusText => IsExcluded
        ? Loc.Tr("Pack.Config.Archives_SkippedStatus")
        : "";

    // ------------------------------------------------------------------
    //  Источники
    // ------------------------------------------------------------------

    private void AddInitialSource(string hash)
    {
        var source = new ArchiveSourceRowVM(
            onRemove: RemoveSource,
            canRemove: () => Sources.Count > 1)
        {
            Url = "",
            Hash = hash,
        };

        source.ValidationChanged += OnSourceValidationChanged;

        Sources.Add(source);
    }

    internal void AddEmptySource()
    {
        var source = new ArchiveSourceRowVM(
            onRemove: RemoveSource,
            canRemove: () => Sources.Count > 1)
        {
            Url = "",
            Hash = HashText,
        };

        source.ValidationChanged += OnSourceValidationChanged;

        Sources.Add(source);
        NotifyRemoveCommands();
    }

    internal void RemoveSource(ArchiveSourceRowVM source)
    {
        if (Sources.Count <= 1) return;
        if (!Sources.Contains(source)) return;

        source.ValidationChanged -= OnSourceValidationChanged;

        Sources.Remove(source);
        NotifyRemoveCommands();
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSourceValidationChanged(object? sender, EventArgs e)
    {
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyRemoveCommands()
    {
        foreach (var s in Sources)
            s.NotifyCanRemoveChanged();
    }

    // ------------------------------------------------------------------
    //  Валидация
    // ------------------------------------------------------------------

    public bool IsValid =>
        IsExcluded || (Sources.Count > 0 && Sources.All(s => s.IsValid));

    public PackArchiveSource ToPackArchiveSource()
    {
        if (IsExcluded)
            throw new InvalidOperationException(
                $"Cannot build archive source for excluded archive '{FileName}'.");

        var sourceRefs = Sources
            .Select(s => (ArchiveSourceRef)s.ToSourceRef())
            .ToList();

        return new PackArchiveSource
        {
            Archive = FileName,
            Sources = sourceRefs,
        };
    }

    // ------------------------------------------------------------------
    //  Load from config
    // ------------------------------------------------------------------

    public void LoadSources(PackArchiveSource source)
    {
        foreach (var s in Sources)
            s.ValidationChanged -= OnSourceValidationChanged;

        Sources.Clear();

        foreach (var srcRef in source.Sources)
        {
            if (srcRef is not MirrorSourceRef mirror)
                continue;

            var row = new ArchiveSourceRowVM(
                onRemove: RemoveSource,
                canRemove: () => Sources.Count > 1)
            {
                Url = mirror.Url,
                Hash = mirror.Hash.ToString(),
            };

            row.ValidationChanged += OnSourceValidationChanged;

            Sources.Add(row);
        }

        if (Sources.Count == 0)
            AddEmptySource();

        NotifyRemoveCommands();
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
