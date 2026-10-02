// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;

namespace Modsync.Install.Downloaders;

/// <summary>
/// Реестр downloaders по типу источника.
///
/// Регистрируется как синглтон. Хранит map "github" → GitHubDownloader, и т.д.
/// При появлении NexusDownloader (12.8) он тоже сюда попадёт.
/// </summary>
public sealed class DownloaderRegistry
{
    private readonly Dictionary<string, IArchiveDownloader> _byType;

    public DownloaderRegistry(IEnumerable<IArchiveDownloader> downloaders)
    {
        _byType = new Dictionary<string, IArchiveDownloader>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var downloader in downloaders)
        {
            if (!_byType.TryAdd(downloader.SourceType, downloader))
            {
                throw new InvalidOperationException(
                    $"Duplicate downloader for source type " +
                    $"'{downloader.SourceType}'. " +
                    $"Already registered: {_byType[downloader.SourceType].GetType().Name}, " +
                    $"attempted: {downloader.GetType().Name}.");
            }
        }
    }

    /// <summary>
    /// Возвращает downloader для типа источника, или null, если такого нет.
    /// </summary>
    public IArchiveDownloader? TryGet(string sourceType)
    {
        return _byType.TryGetValue(sourceType, out var d) ? d : null;
    }

    public IReadOnlyCollection<string> RegisteredTypes
        => _byType.Keys;
}
