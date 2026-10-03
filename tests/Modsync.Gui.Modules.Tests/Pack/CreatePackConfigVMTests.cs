// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Gui.Shared.Services;

namespace Modsync.Gui.Shared.Tests.Fakes;

public sealed class FakeFilePickerService : IFilePickerService
{
    public string? FileToReturn { get; set; }
    public string? FolderToReturn { get; set; }
    public string? SaveFileToReturn { get; set; }
    public int FilePickCalls { get; private set; }
    public int FolderPickCalls { get; private set; }

    public Task<string?> PickFileAsync(string title, string? filterHint = null)
    {
        FilePickCalls++;
        return Task.FromResult(FileToReturn);
    }

    public Task<string?> PickFolderAsync(string title)
    {
        FolderPickCalls++;
        return Task.FromResult(FolderToReturn);
    }

    public Task<string?> SaveFileAsync(
        string title,
        string suggestedName,
        string? filterHint = null)
        => Task.FromResult(SaveFileToReturn);
}
