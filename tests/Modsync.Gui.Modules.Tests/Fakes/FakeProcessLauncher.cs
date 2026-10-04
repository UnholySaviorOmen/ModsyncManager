// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Gui.Shared.Services;

namespace Modsync.Gui.Modules.Tests.Fakes;

public sealed class FakeProcessLauncher : IProcessLauncher
{
    public List<string> OpenedPaths { get; } = new();
    public Exception? ExceptionToThrow { get; set; }

    public void OpenFile(string path)
    {
        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        OpenedPaths.Add(path);
    }
}
