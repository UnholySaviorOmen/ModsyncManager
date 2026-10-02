using Modsync.Platform.Nexus;

namespace Modsync.Platform.Nexus.Tests;

/// <summary>
/// Fake INexusFreeNxmProvider для тестов NexusDownloader.
///
/// Режимы:
///   - ResultToReturn: URL или null.
///   - ExceptionToThrow: пробросить исключение.
///   - Gate: TaskCompletionSource для проверки отмены.
///
/// Запоминает последний вызов — для проверки, что провайдер спросили
/// ровно один раз с нужными параметрами.
/// </summary>
public sealed class FakeNexusFreeNxmProvider : INexusFreeNxmProvider
{
    public string? ResultToReturn { get; set; }
    public Exception? ExceptionToThrow { get; set; }
    public TaskCompletionSource? Gate { get; set; }

    public int CallCount { get; private set; }
    public string? LastGame { get; private set; }
    public int LastModId { get; private set; }
    public int LastFileId { get; private set; }
    public string? LastDisplayName { get; private set; }

    public async Task<string?> RequestNxmUrlAsync(
        string game,
        int modId,
        int fileId,
        string displayName,
        CancellationToken ct)
    {
        CallCount++;
        LastGame = game;
        LastModId = modId;
        LastFileId = fileId;
        LastDisplayName = displayName;

        if (Gate is not null)
        {
            using var reg = ct.Register(() => Gate.TrySetCanceled(ct));
            await Gate.Task;
        }

        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        return ResultToReturn;
    }

    /// <summary>
    /// Строит валидный nxm:// URL для заданных параметров.
    /// </summary>
    public static string MakeNxmUrl(
        string game,
        int modId,
        int fileId,
        string? key = null,
        long? expires = null,
        int? userId = null)
    {
        var url = $"nxm://{game}/mods/{modId}/files/{fileId}";

        if (key is not null && expires.HasValue && userId.HasValue)
        {
            url += $"?key={key}&expires={expires}&user_id={userId}";
        }

        return url;
    }
}
