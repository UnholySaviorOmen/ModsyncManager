// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest.Sources;

namespace Modsync.Install.Tests;

/// <summary>
/// Fake-реализация IArchiveDownloader для тестов.
///
/// Хранит map "source identifier" → byte[].
/// Identifier:
///   - MirrorSourceRef  → "mirror:{url}"
///   - NexusSourceRef   → "nexus:{game}:{modId}:{fileId}"
///
/// Если identifier не найден — throws HttpRequestException.
/// Опционально можно настроить "throw on Nth call" для тестов retry.
/// </summary>
public sealed class FakeArchiveDownloader : IArchiveDownloader
{
    private readonly Dictionary<string, byte[]> _content = new(StringComparer.Ordinal);
    private readonly List<string> _requestedIdentifiers = new();

    private int _callCount;
    private int? _failOnCall;

    public FakeArchiveDownloader(string sourceType)
    {
        SourceType = sourceType;
    }

    public string SourceType { get; }

    public IReadOnlyList<string> RequestedIdentifiers => _requestedIdentifiers;

    public int CallCount => _callCount;

    public void SetContent(string identifier, byte[] content)
    {
        _content[identifier] = content;
    }

    public void FailOnCall(int n)
    {
        _failOnCall = n;
    }

    public Task<Stream> DownloadAsync(ArchiveSourceRef source, CancellationToken ct)
    {
        Interlocked.Increment(ref _callCount);
        var currentCall = _callCount;

        if (_failOnCall.HasValue && currentCall == _failOnCall.Value)
        {
            throw new HttpRequestException(
                $"Simulated failure on call #{currentCall}.");
        }

        var identifier = IdentifierOf(source);
        _requestedIdentifiers.Add(identifier);

        if (!_content.TryGetValue(identifier, out var bytes))
        {
            throw new HttpRequestException(
                $"Fake downloader has no content for '{identifier}'.");
        }

        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public static string IdentifierOf(ArchiveSourceRef source) => source switch
    {
        MirrorSourceRef m => $"mirror:{m.Url}",
        NexusSourceRef n => $"nexus:{n.Game}:{n.ModId}:{n.FileId}",
        _ => throw new ArgumentException(
            $"Unknown source type: {source.GetType().Name}"),
    };

    public static byte[] MakeBytes(string content)
        => System.Text.Encoding.UTF8.GetBytes(content);

    public static XxHash64Value HashOf(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        return XxHash64Value.FromStream(ms);
    }
}
