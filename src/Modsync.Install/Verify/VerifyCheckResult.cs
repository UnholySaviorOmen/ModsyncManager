// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Install.Verify;

/// <summary>
/// Результат одной проверки verify.
///
/// Name — короткое описание ("Manifest exists", "Archive: TestMod.7z",
/// "Mod file: TestMod/file.txt"). Message заполняется только при провале
/// или когда есть что сказать (например, для skip-случая).
/// </summary>
public sealed class VerifyCheckResult
{
    public required string Name { get; init; }
    public required bool Passed { get; init; }
    public string? Message { get; init; }

    public static VerifyCheckResult Ok(string name, string? message = null)
        => new() { Name = name, Passed = true, Message = message };

    public static VerifyCheckResult Fail(string name, string message)
        => new() { Name = name, Passed = false, Message = message };
}
