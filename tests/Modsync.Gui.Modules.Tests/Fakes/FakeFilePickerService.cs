// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Gui.Shared.Services;

namespace Modsync.Gui.Modules.Tests.Fakes;

public sealed class FakeFilePickerService : IFilePickerService
{
    public string? FileToReturn { get; set; }
    public string? FolderToReturn { get; set; }
    public string? SaveFileToReturn { get; set; }

    public Task<string?> PickFileAsync(string title, string? filterHint = null)
        => Task.FromResult(FileToReturn);

    public Task<string?> PickFolderAsync(string title)
        => Task.FromResult(FolderToReturn);

    public Task<string?> SaveFileAsync(
        string title,
        string suggestedName,
        string? filterHint = null)
        => Task.FromResult(SaveFileToReturn);
}
