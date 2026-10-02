// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Archives;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest.Sources;

namespace Modsync.Install.Downloaders;

/// <summary>
/// Скачивание архивов по прямой URL-ссылке (mirror).
///
/// HttpClient берётся из IHttpClientFactory по имени "mirror" (таймаут
/// 10 минут). Это позволяет скачивать большие архивы без риска упасть
/// по таймауту.
///
/// Скачивание идёт в TempFileStream (временный файл на диске), а не в
/// MemoryStream: у Nexus есть моды на 3+ ГБ, MemoryStream такой размер
/// не держит (лимит int.MaxValue).
///
/// Hash-проверку делает ArchiveDownloadHelper — не здесь.
/// </summary>
public sealed class MirrorDownloader : IArchiveDownloader
{
    public const string HttpClientName = "mirror";

    private readonly IHttpClientFactory _httpClientFactory;

    public MirrorDownloader(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string SourceType => "mirror";

    public async Task<Stream> DownloadAsync(
        ArchiveSourceRef source, CancellationToken ct)
    {
        if (source is not MirrorSourceRef mirror)
        {
            throw new ArgumentException(
                $"Expected MirrorSourceRef, got {source.GetType().Name}.",
                nameof(source));
        }

        var http = _httpClientFactory.CreateClient(HttpClientName);

        using var response = await http.GetAsync(
            mirror.Url, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Mirror returned {(int)response.StatusCode} " +
                $"({response.ReasonPhrase}) for {mirror.Url}");
        }

        var temp = new TempFileStream();

        try
        {
            await using (var networkStream = await response.Content
                .ReadAsStreamAsync(ct))
            {
                await networkStream.CopyToAsync(temp, ct);
            }

            temp.Position = 0;

            // Проверяем hash сразу, если он указан в mirror-источнике.
            // Это раннее обнаружение битой загрузки до записи в .part.
            var actualHash = XxHash64Value.FromStream(temp);
            temp.Position = 0;

            if (actualHash != mirror.Hash)
            {
                await temp.DisposeAsync();
                throw new InvalidOperationException(
                    $"Mirror hash mismatch for {mirror.Url}: " +
                    $"expected {mirror.Hash}, got {actualHash}.");
            }

            return temp;
        }
        catch
        {
            await temp.DisposeAsync();
            throw;
        }
    }
}
