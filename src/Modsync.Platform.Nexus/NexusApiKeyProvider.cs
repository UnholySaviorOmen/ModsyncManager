// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using Modsync.Core;
using Microsoft.Extensions.Logging;

namespace Modsync.Platform.Nexus;

/// <summary>
/// Читает и пишет Nexus API-ключ в %LOCALAPPDATA%\ModsyncManager\nexus.key.
///
/// Формат файла: одна строка с ключом, UTF-8 без BOM, без trailing newline.
/// При чтении BOM и whitespace вокруг ключа обрезаются.
///
/// Никаких исключений наружу из TryGetApiKey: если файла нет,
/// путь недоступен, файл пуст — возвращается null. Это сознательно:
/// отсутствие ключа — валидное состояние, а не ошибка.
///
/// Save и Clear бросают при ошибках I/O: пользователь должен узнать,
/// что его действие не сработало.
///
/// v0.2.0: путь — из ModsyncPaths (единый корень %LOCALAPPDATA%\ModsyncManager).
/// DPAPI-шифрование — в следующих версиях.
/// </summary>
public sealed class NexusApiKeyProvider : INexusApiKeyProvider
{
    private readonly string _keyFilePath;
    private readonly ILogger<NexusApiKeyProvider> _logger;

    public NexusApiKeyProvider(ILogger<NexusApiKeyProvider> logger)
        : this(logger, ModsyncPaths.NexusKeyFile)
    {
    }

    /// <summary>
    /// Для тестов: явный путь к файлу ключа.
    /// </summary>
    internal NexusApiKeyProvider(
        ILogger<NexusApiKeyProvider> logger,
        string keyFilePath)
    {
        _logger = logger;
        _keyFilePath = keyFilePath;
    }

    public string KeyFilePath => _keyFilePath;

    // ------------------------------------------------------------------
    //  Read
    // ------------------------------------------------------------------

    public string? TryGetApiKey()
    {
        try
        {
            if (!File.Exists(_keyFilePath))
            {
                _logger.LogDebug(
                    "Nexus API key not found at {Path}", _keyFilePath);
                return null;
            }

            var raw = File.ReadAllText(_keyFilePath);

            // Убираем BOM, если он есть.
            if (raw.Length > 0 && raw[0] == '\uFEFF')
                raw = raw[1..];

            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                _logger.LogWarning(
                    "Nexus API key file is empty: {Path}", _keyFilePath);
                return null;
            }

            var firstLine = trimmed
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(firstLine))
            {
                _logger.LogWarning(
                    "Nexus API key file has no usable line: {Path}",
                    _keyFilePath);
                return null;
            }

            return firstLine.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to read Nexus API key from {Path}", _keyFilePath);
            return null;
        }
    }

    // ------------------------------------------------------------------
    //  Write
    // ------------------------------------------------------------------

    public void Save(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException(
                "API key must be non-empty.", nameof(apiKey));

        var trimmed = apiKey.Trim();

        var directory = Path.GetDirectoryName(_keyFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        // UTF-8 без BOM, без trailing newline.
        File.WriteAllText(
            _keyFilePath,
            trimmed,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        _logger.LogInformation(
            "Nexus API key saved to {Path}", _keyFilePath);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_keyFilePath))
            {
                File.Delete(_keyFilePath);
                _logger.LogInformation(
                    "Nexus API key removed from {Path}", _keyFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to remove Nexus API key at {Path}", _keyFilePath);
            throw;
        }
    }
}
