// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using System.Text;
using Modsync.Core.Archives;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Sources;

namespace Modsync.Install.Tests;

/// <summary>
/// Общие хелперы для тестов installer-а.
///
/// Не используется в Modsync.Pack.Tests / Modsync.Core.Tests —
/// там свои локальные копии. Это сознательное решение:
/// рефакторить старые тесты ради общего хелпера не хотим.
/// </summary>
public static class TestArchives
{
    // ------------------------------------------------------------------
    //  Создание zip-архивов
    // ------------------------------------------------------------------

    /// <summary>
    /// Создаёт zip-архив с бинарным содержимым.
    /// Возвращает полный путь к архиву.
    /// </summary>
    public static string CreateZip(
        string directory,
        string fileName,
        params (string path, byte[] content)[] files)
    {
        Directory.CreateDirectory(directory);

        var archivePath = Path.Combine(directory, fileName);

        using var fs = File.Create(archivePath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        foreach (var (path, content) in files)
        {
            var entry = zip.CreateEntry(path);
            using var entryStream = entry.Open();
            entryStream.Write(content, 0, content.Length);
        }

        return archivePath;
    }

    /// <summary>
    /// Создаёт zip-архив с текстовым содержимым (UTF-8).
    /// </summary>
    public static string CreateZip(
        string directory,
        string fileName,
        params (string path, string content)[] files)
    {
        var binary = files
            .Select(f => (f.path, Encoding.UTF8.GetBytes(f.content)))
            .ToArray();

        return CreateZip(directory, fileName, binary);
    }

    // ------------------------------------------------------------------
    //  ArchiveEntry
    // ------------------------------------------------------------------

    /// <summary>
    /// Строит ArchiveEntry по zip-файлу.
    /// Id — канонический id (например, "local_testmod").
    /// Hash — xxHash64 архива целиком.
    /// Sources — пустой (в тестах installer-а не используются).
    /// </summary>
    public static ArchiveEntry MakeArchiveEntry(
        string archivePath,
        string id,
        FileHashCache hashCache)
    {
        var hash = hashCache.GetOrCompute(archivePath);
        var size = new FileInfo(archivePath).Length;

        return new ArchiveEntry
        {
            Id = id,
            Name = Path.GetFileName(archivePath),
            Size = size,
            Hash = hash,
            Sources = Array.Empty<ArchiveSourceRef>(),
        };
    }

    // ------------------------------------------------------------------
    //  Хеши
    // ------------------------------------------------------------------

    /// <summary>
    /// xxHash64 содержимого как байтов.
    /// </summary>
    public static XxHash64Value HashOf(byte[] content)
    {
        using var ms = new MemoryStream(content, writable: false);
        return XxHash64Value.FromStream(ms);
    }

    /// <summary>
    /// xxHash64 строки в UTF-8.
    /// </summary>
    public static XxHash64Value HashOf(string content)
        => HashOf(Encoding.UTF8.GetBytes(content));

    // ------------------------------------------------------------------
    //  Чтение с диска
    // ------------------------------------------------------------------

    public static string ReadText(string path)
        => File.ReadAllText(path);

    public static byte[] ReadBytes(string path)
        => File.ReadAllBytes(path);
}
