using Modsync.Gui.Modules.Verify.Services;
using Modsync.Install.Verify;

namespace Modsync.Gui.Modules.Tests.Verify;

public sealed class FakeVerifyRunner : IVerifyRunner
{
    public VerifyReport? ResultToReturn { get; set; }
    public Exception? ExceptionToThrow { get; set; }
    public TaskCompletionSource? Gate { get; set; }
    public CancellationToken LastToken { get; private set; }
    public string? LastTargetPath { get; private set; }

    public async Task<VerifyReport> RunAsync(
        string targetPath,
        CancellationToken ct)
    {
        LastTargetPath = targetPath;
        LastToken = ct;

        if (Gate is not null)
        {
            using var reg = ct.Register(() => Gate.TrySetCanceled(ct));
            await Gate.Task;
        }

        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        if (ResultToReturn is null)
            throw new InvalidOperationException(
                "FakeVerifyRunner.ResultToReturn is null.");

        return ResultToReturn;
    }

    // ------------------------------------------------------------------
    //  Билдеры отчёта
    // ------------------------------------------------------------------

    public static VerifyReport MakeReport(
        string targetPath = "/test/instance",
        params VerifyCheckResult[] checks)
    {
        return new VerifyReport
        {
            TargetPath = targetPath,
            Checks = checks,
        };
    }

    public static VerifyReport OkReport(
        string targetPath = "/test/instance",
        int passedCount = 10)
    {
        var checks = Enumerable.Range(0, passedCount)
            .Select(i => VerifyCheckResult.Ok($"Check {i}"))
            .ToArray();

        return MakeReport(targetPath, checks);
    }

    public static VerifyReport FailingReport(
        string targetPath = "/test/instance",
        int passedCount = 5,
        params (string name, string message)[] failures)
    {
        var checks = new List<VerifyCheckResult>();

        for (int i = 0; i < passedCount; i++)
            checks.Add(VerifyCheckResult.Ok($"Check {i}"));

        foreach (var (name, message) in failures)
            checks.Add(VerifyCheckResult.Fail(name, message));

        return MakeReport(targetPath, checks.ToArray());
    }
}
