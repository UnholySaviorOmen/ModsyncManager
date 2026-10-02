// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.Input;
using Modsync.Core;
using Modsync.Core.Archives;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Shared.ViewModels;

/// <summary>
/// VM экрана Cache — технические базы Modsync Manager.
///
/// Сейчас: одна база — cache.db (persist хешей файлов).
/// В блоке 2.2 появится archives.db (глобальный реестр архивов).
/// Структура экрана заложена так, чтобы вторая секция добавлялась
/// без переделки.
///
/// Экран виден только в DevMode (NavigationVM фильтрует).
/// </summary>
public sealed partial class CacheVM : ViewModel
{
    private readonly IHashCache _hashCache;
    private readonly ILogger<CacheVM> _logger;

    public string HashCachePath => ModsyncPaths.CacheDbFile;

    public string _hashCacheSizeText = "Unknown.";
    public string HashCacheSizeText
    {
        get => _hashCacheSizeText;
        private set
        {
            if (_hashCacheSizeText == value) return;
            _hashCacheSizeText = value;
            OnPropertyChanged();
        }
    }

    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public CacheVM(
        IHashCache hashCache,
        ILogger<CacheVM> logger)
    {
        _hashCache = hashCache;
        _logger = logger;

        RefreshHashCacheSize();
    }

    [RelayCommand]
    private void ClearHashCache()
    {
        try
        {
            _hashCache.Clear();
            StatusMessage = "Hash cache cleared.";
            _logger.LogInformation("Hash cache cleared by user.");

            RefreshHashCacheSize();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear hash cache.");
            StatusMessage = $"Failed to clear: {ex.Message}";
        }
    }

    private void RefreshHashCacheSize()
    {
        try
        {
            var path = HashCachePath;

            if (!File.Exists(path))
            {
                HashCacheSizeText = "No cache file yet.";
                return;
            }

            var size = new FileInfo(path).Length;
            HashCacheSizeText = FormatSize(size);
        }
        catch
        {
            HashCacheSizeText = "Unknown.";
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
