// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Gui.Shared.Services;

namespace Modsync.Gui.Modules.Tests.Fakes;

public sealed class FakePatchDialogService : IPatchDialogService
{
    public int CallCount { get; private set; }
    public string? LastInstancePath { get; private set; }
    public int LastUnmatchedCount { get; private set; }

    public Task ShowAsync(
        string instancePath,
        int unmatchedCount,
        CancellationToken ct = default)
    {
        CallCount++;
        LastInstancePath = instancePath;
        LastUnmatchedCount = unmatchedCount;
        return Task.CompletedTask;
    }
}
