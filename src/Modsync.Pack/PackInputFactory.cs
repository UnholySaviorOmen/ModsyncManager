// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Pack;

/// <summary>
/// Собирает <see cref="PackPipeline.Input"/> из пользовательских данных.
///
/// Единственная точка, где:
///   - configPath нормализуется в полный путь;
///   - ParallelOptions задаётся (или берётся дефолтный).
///
/// Используется CLI и GUI. ViewModel-и не должны знать о Path.GetFullPath
/// и о том, что ParallelOptions обязателен.
/// </summary>
public static class PackInputFactory
{
    /// <summary>
    /// Дефолтные ParallelOptions: столько потоков, сколько ядер CPU.
    /// Так же, как в ModsyncManager.Cli/Program.cs.
    /// </summary>
    public static ParallelOptions DefaultParallelOptions()
        => new() { MaxDegreeOfParallelism = Environment.ProcessorCount };

    /// <summary>
    /// Собрать Input для PackPipeline.
    ///
    /// configPath нормализуется через Path.GetFullPath (с текущей рабочей
    /// директорией процесса). Пользователь может передать относительный путь.
    ///
    /// parallelOptions = null → DefaultParallelOptions().
    /// </summary>
    public static PackPipeline.Input Create(
        string configPath,
        ParallelOptions? parallelOptions = null)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException(
                "Config path must be non-empty.", nameof(configPath));

        return new PackPipeline.Input
        {
            ConfigPath = Path.GetFullPath(configPath),
            ParallelOptions = parallelOptions ?? DefaultParallelOptions(),
        };
    }
}
