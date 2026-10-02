// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Archives.Extraction;

/// <summary>
/// Временная папка для распаковки. Удаляется при Dispose.
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    public string Path { get; }

    public TempWorkspace()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "modsyncmanager-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Игнорируем — временная папка ОС подчистит
        }
    }
}
