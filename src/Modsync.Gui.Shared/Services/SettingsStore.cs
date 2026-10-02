// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;
using Modsync.Core;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Shared.Services;

/// <summary>
/// Реализация ISettingsStore поверх файла
/// %LOCALAPPDATA%\ModsyncManager\settings.json.
///
/// Формат — JSON, camelCase, indented (для читаемости).
/// Атомарная запись: temp + File.Move(overwrite: true).
/// При ошибке Load — дефолты + warning.
/// При ошибке Save — warning, не бросает.
///
/// Путь — из ModsyncPaths (единый корень %LOCALAPPDATA%\ModsyncManager).
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _filePath;
    private readonly ILogger<SettingsStore> _logger;

    public SettingsStore(ILogger<SettingsStore> logger)
        : this(ModsyncPaths.SettingsFile, logger)
    {
    }

    /// <summary>
    /// Для тестов: явный путь к файлу.
    /// </summary>
    internal SettingsStore(string filePath, ILogger<SettingsStore> logger)
    {
        _filePath = filePath;
        _logger = logger;

        Current = Load();
    }

    public Settings Current { get; private set; }

    public string FilePath => _filePath;

    // ------------------------------------------------------------------
    //  Load
    // ------------------------------------------------------------------

    private Settings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogDebug(
                    "Settings file not found, using defaults: {Path}",
                    _filePath);
                return Settings.Default;
            }

            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<Settings>(json, JsonOptions);

            if (settings is null)
            {
                _logger.LogWarning(
                    "Settings file deserialized to null, using defaults: {Path}",
                    _filePath);
                return Settings.Default;
            }

            _logger.LogDebug("Settings loaded from {Path}", _filePath);
            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to load settings from {Path}, using defaults",
                _filePath);
            return Settings.Default;
        }
    }

    // ------------------------------------------------------------------
    //  Save
    // ------------------------------------------------------------------

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(Current, JsonOptions);
            var tempPath = _filePath + ".tmp";

            File.WriteAllText(tempPath, json);

            // Атомарная замена: либо старый файл, либо новый.
            File.Move(tempPath, _filePath, overwrite: true);

            _logger.LogDebug("Settings saved to {Path}", _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to save settings to {Path}", _filePath);
        }
    }
}
