// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Install;

/// <summary>
/// Собирает <see cref="InstallPipeline.Input"/> из пользовательских данных.
///
/// Единственная точка, где:
///   - manifestPath нормализуется в полный путь;
///   - target нормализуется в полный путь (если задан);
///   - ParallelOptions задаётся (или берётся дефолтный).
///
/// Target = null — валидное состояние: ResolveTargetStep сам вычислит
/// instancePath = &lt;exeDir&gt;/Instances/&lt;normalize(meta.name)&gt;/.
/// </summary>
public static class InstallInputFactory
{
    /// <summary>
    /// Дефолтные ParallelOptions: столько потоков, сколько ядер CPU.
    /// </summary>
    public static ParallelOptions DefaultParallelOptions()
        => new() { MaxDegreeOfParallelism = Environment.ProcessorCount };

    /// <summary>
    /// Собрать Input для InstallPipeline.
    ///
    /// manifestPath нормализуется через Path.GetFullPath.
    /// target: если не null/пусто — нормализуется; иначе остаётся null,
    /// и ResolveTargetStep сам разрулит.
    ///
    /// parallelOptions = null → DefaultParallelOptions().
    /// </summary>
    public static InstallPipeline.Input Create(
        string manifestPath,
        string? target = null,
        ParallelOptions? parallelOptions = null)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException(
                "Manifest path must be non-empty.", nameof(manifestPath));

        string? normalizedTarget = null;
        if (!string.IsNullOrWhiteSpace(target))
            normalizedTarget = Path.GetFullPath(target);

        return new InstallPipeline.Input
        {
            ManifestPath = Path.GetFullPath(manifestPath),
            Target = normalizedTarget,
            ParallelOptions = parallelOptions ?? DefaultParallelOptions(),
        };
    }
}
