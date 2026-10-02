// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Archives;

/// <summary>
/// Поток во временный файл на диске. Создаёт файл в системной temp-папке,
/// позволяет писать в него и читать с начала. При Dispose файл удаляется.
///
/// Зачем: скачивание архива через MemoryStream не работает для файлов
/// больше ~2 ГБ (лимит MemoryStream — int.MaxValue). У Nexus есть моды
/// по 3, 5, 10 ГБ, поэтому downloader-ы пишут во временный файл.
///
/// Альтернатива — расширить IArchiveDownloader до «скачай сразу в файл»,
/// но это ломает существующий контракт и ArchiveDownloadHelper.
/// TempFileStream даёт тот же эффект без ломки контракта.
///
/// Двойная запись: downloader → temp → .part (в ArchiveDownloadHelper).
/// На SSD это почти незаметно, на HDD — стоимость, которую принимаем.
/// </summary>
public sealed class TempFileStream : Stream
{
    private readonly string _path;
    private readonly FileStream _inner;
    private bool _disposed;

    public TempFileStream()
    {
        _path = Path.Combine(
            Path.GetTempPath(),
            "modsyncmanager-dl-" + Guid.NewGuid().ToString("N") + ".tmp");

        _inner = new FileStream(
            _path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.SequentialScan | FileOptions.DeleteOnClose);
    }

    /// <summary>Путь временного файла. Для диагностики.</summary>
    public string TempPath => _path;

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken ct)
        => _inner.FlushAsync(ct);

    public override int Read(byte[] buffer, int offset, int count)
        => _inner.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken ct = default)
        => _inner.ReadAsync(buffer, ct);

    public override int Read(Span<byte> buffer)
        => _inner.Read(buffer);

    public override Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken ct)
        => _inner.ReadAsync(buffer, offset, count, ct);

    public override long Seek(long offset, SeekOrigin origin)
        => _inner.Seek(offset, origin);

    public override void SetLength(long value)
        => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
        => _inner.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer)
        => _inner.Write(buffer);

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        => _inner.WriteAsync(buffer, ct);

    public override Task WriteAsync(
        byte[] buffer, int offset, int count, CancellationToken ct)
        => _inner.WriteAsync(buffer, offset, count, ct);

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            base.Dispose(disposing);
            return;
        }

        _disposed = true;

        if (disposing)
        {
            try
            {
                _inner.Dispose();
            }
            catch
            {
                // FileOptions.DeleteOnClose обычно сам удаляет файл.
                // Если что-то пошло не так — игнорируем, ОС подчистит.
            }

            // На случай, если DeleteOnClose не сработал.
            try
            {
                if (File.Exists(_path))
                    File.Delete(_path);
            }
            catch
            {
                // Игнорируем.
            }
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            await base.DisposeAsync();
            return;
        }

        _disposed = true;

        try
        {
            await _inner.DisposeAsync();
        }
        catch
        {
        }

        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch
        {
        }

        await base.DisposeAsync();
    }
}
