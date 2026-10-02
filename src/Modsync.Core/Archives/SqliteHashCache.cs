// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Hashing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Modsync.Core.Archives;

/// <summary>
/// Кеш хешей файлов с persist в SQLite.
///
/// Два уровня:
///   L1 — FileHashCache (in-memory, мгновенно).
///   L2 — SQLite-таблица file_hashes (между запусками).
///
/// Логика GetOrCompute:
///   1. L1 попадание → возврат.
///   2. L2 попадание (path + length + mtime_ms совпали) → L1 прогрев, возврат.
///   3. Compute → запись в L2 и L1 → возврат.
///
/// Ключ в L2: path (PRIMARY KEY). Проверка совпадения length и mtime
/// делается отдельным SELECT.
///
/// Потокобезопасность: lock вокруг любых операций с connection.
/// SQLite не потокобезопасен на один connection.
///
/// Ошибки SQLite (повреждённый файл, locked, нет прав) — warning,
/// работаем без persist (L1 продолжает работать).
///
/// WAL-режим включён при открытии: одновременное чтение + запись
/// из другого процесса (CLI + GUI) не блокируются.
/// </summary>
public sealed class SqliteHashCache : IHashCache, IDisposable
{
    private const int SchemaVersion = 1;
    private const int BusyTimeoutMs = 5000;

    private readonly FileHashCache _l1;
    private readonly SqliteConnection _connection;
    private readonly ILogger<SqliteHashCache> _logger;
    private readonly object _lock = new();

    private bool _disposed;

    /// <summary>
    /// Продакшн-конструктор: использует ModsyncPaths.CacheDbFile.
    /// </summary>
    public SqliteHashCache(ILogger<SqliteHashCache> logger)
        : this(ModsyncPaths.CacheDbFile, logger)
    {
    }

    /// <summary>
    /// Для тестов: явный путь к БД.
    /// </summary>
    public SqliteHashCache(string dbPath, ILogger<SqliteHashCache> logger)
    {
        _l1 = new FileHashCache();
        _logger = logger;

        try
        {
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            _connection = new SqliteConnection($"Data Source={dbPath}");
            _connection.Open();

            InitializeSchema();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to open hash cache database at {Path}. " +
                "Working without persistent cache.",
                dbPath);

            // Fallback: connection = null, работаем только с L1.
            _connection = null!;
        }
    }

    // ------------------------------------------------------------------
    //  Public API
    // ------------------------------------------------------------------

    public XxHash64Value GetOrCompute(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException($"File not found: {path}", path);

        // 1. L1 — мгновенно.
        // FileHashCache не умеет проверять «а не изменился ли файл»
        // без обращения к диску. Поэтому мы всегда делаем FileInfo
        // и передаём ключ — L1 сам решит.
        try
        {
            return _l1.GetOrCompute(path);
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch
        {
            // Внутренняя ошибка L1 — не критично, идём в L2/Compute.
        }

        // 2. L2 — SQLite.
        if (_connection is not null && TryGetFromL2(path, info, out var hash))
        {
            // Прогрев L1.
            try { _l1.GetOrCompute(path); } catch { }
            return hash;
        }

        // 3. Compute.
        var computed = XxHash64Value.FromFile(path);

        // 4. Запись в L2.
        if (_connection is not null)
            TryWriteToL2(path, info, computed);

        // L1 уже содержит запись после успешного GetOrCompute или мы её туда не положили.
        // На всякий случай — прогрев.
        try { _l1.GetOrCompute(path); } catch { }

        return computed;
    }

    public int Count => _l1.Count;

    public void Clear()
    {
        _l1.Clear();

        if (_connection is null)
            return;

        lock (_lock)
        {
            if (_disposed)
                return;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM file_hashes;";
                cmd.ExecuteNonQuery();

                // VACUUM не делает авто-коммит внутри транзакции, но мы
                // вне транзакции. Освобождает место на диске.
                using var vacuum = _connection.CreateCommand();
                vacuum.CommandText = "VACUUM;";
                vacuum.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to clear hash cache database.");
            }
        }
    }

    // ------------------------------------------------------------------
    //  Schema
    // ------------------------------------------------------------------

    private void InitializeSchema()
    {
        lock (_lock)
        {
            // WAL — одновременное чтение + запись из разных процессов.
            ExecuteNonQuery("PRAGMA journal_mode = WAL;");
            ExecuteNonQuery($"PRAGMA busy_timeout = {BusyTimeoutMs};");

            var currentVersion = GetUserVersion();

            if (currentVersion == SchemaVersion)
                return;

            // Либо новая БД (0), либо чужая/старая — пересоздаём.
            // Кеш не критичен, потери данных допустимы.
            _logger.LogInformation(
                "Recreating hash cache schema: {Current} → {Target}",
                currentVersion, SchemaVersion);

            ExecuteNonQuery("DROP TABLE IF EXISTS file_hashes;");
            ExecuteNonQuery(
                """
                CREATE TABLE file_hashes (
                    path      TEXT    PRIMARY KEY,
                    length    INTEGER NOT NULL,
                    mtime_ms  INTEGER NOT NULL,
                    hash      INTEGER NOT NULL
                );
                """);

            SetUserVersion(SchemaVersion);
        }
    }

    private int GetUserVersion()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var result = cmd.ExecuteScalar();
        return Convert.ToInt32(result);
    }

    private void SetUserVersion(int version)
    {
        using var cmd = _connection.CreateCommand();
        // PRAGMA не поддерживает параметры — подставляем как литерал.
        // version — int, injection невозможна.
        cmd.CommandText = $"PRAGMA user_version = {version};";
        cmd.ExecuteNonQuery();
    }

    private void ExecuteNonQuery(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------
    //  L2: read
    // ------------------------------------------------------------------

    private bool TryGetFromL2(
        string path,
        FileInfo info,
        out XxHash64Value hash)
    {
        hash = default;

        lock (_lock)
        {
            if (_disposed)
                return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText =
                    "SELECT length, mtime_ms, hash FROM file_hashes WHERE path = $path;";
                cmd.Parameters.AddWithValue("$path", path);

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return false;

                var length = reader.GetInt64(0);
                var mtimeMs = reader.GetInt64(1);
                var hashRaw = reader.GetInt64(2);

                if (length != info.Length)
                    return false;

                var currentMtimeMs = new DateTimeOffset(info.LastWriteTimeUtc)
                    .ToUnixTimeMilliseconds();
                if (mtimeMs != currentMtimeMs)
                    return false;

                hash = new XxHash64Value(unchecked((ulong)hashRaw));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to read from hash cache database.");
                return false;
            }
        }
    }

    // ------------------------------------------------------------------
    //  L2: write
    // ------------------------------------------------------------------

    private void TryWriteToL2(string path, FileInfo info, XxHash64Value hash)
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            try
            {
                var mtimeMs = new DateTimeOffset(info.LastWriteTimeUtc)
                    .ToUnixTimeMilliseconds();

                using var cmd = _connection.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT OR REPLACE INTO file_hashes (path, length, mtime_ms, hash)
                    VALUES ($path, $length, $mtime, $hash);
                    """;
                cmd.Parameters.AddWithValue("$path", path);
                cmd.Parameters.AddWithValue("$length", info.Length);
                cmd.Parameters.AddWithValue("$mtime", mtimeMs);
                cmd.Parameters.AddWithValue("$hash", unchecked((long)hash.Value));
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to write to hash cache database.");
            }
        }
    }

    // ------------------------------------------------------------------
    //  IDisposable
    // ------------------------------------------------------------------

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_connection is null)
                return;

            try
            {
                _connection.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to dispose hash cache database connection.");
            }
        }
    }
}
