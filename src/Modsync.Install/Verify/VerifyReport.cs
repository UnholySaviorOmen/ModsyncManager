// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Install.Verify;

/// <summary>
/// Полный отчёт verify: путь к target-инстансу + список всех проверок.
/// IsOk == true, если все проверки прошли.
/// </summary>
public sealed class VerifyReport
{
    public required string TargetPath { get; init; }
    public required IReadOnlyList<VerifyCheckResult> Checks { get; init; }

    public bool IsOk => Checks.All(c => c.Passed);
    public int PassedCount => Checks.Count(c => c.Passed);
    public int FailedCount => Checks.Count(c => !c.Passed);

    public IEnumerable<VerifyCheckResult> Failures
        => Checks.Where(c => !c.Passed);
}
