// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Install.Verify;

namespace Modsync.Gui.Modules.Verify.Services;

public sealed class VerifyRunner : IVerifyRunner
{
    private readonly VerifyPipeline _pipeline;

    public VerifyRunner(VerifyPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public Task<VerifyReport> RunAsync(string targetPath, CancellationToken ct)
    {
        // VerifyPipeline.Execute — синхронный. Task.Run уводит работу
        // с UI-потока; ct пробрасывается внутрь Execute, где есть
        // ThrowIfCancellationRequested в начале и в циклах.
        return Task.Run(() => _pipeline.Execute(targetPath, ct), ct);
    }
}
