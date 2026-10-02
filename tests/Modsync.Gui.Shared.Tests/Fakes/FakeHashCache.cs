using Modsync.Core.Archives;
using Modsync.Core.Models.Hashing;

namespace Modsync.Gui.Shared.Tests.Fakes;

/// <summary>
/// Fake IHashCache для тестов CacheVM.
/// Не трогает диск. Считает вызовы Clear.
/// </summary>
public sealed class FakeHashCache : IHashCache
{
    private readonly Dictionary<string, XxHash64Value> _store = new();

    public int Count => _store.Count;

    public int ClearCallCount { get; private set; }

    public Exception? ClearException { get; set; }

    public XxHash64Value GetOrCompute(string path)
    {
        if (!_store.TryGetValue(path, out var hash))
        {
            hash = new XxHash64Value((ulong)path.GetHashCode());
            _store[path] = hash;
        }
        return hash;
    }

    public void Clear()
    {
        ClearCallCount++;

        if (ClearException is not null)
            throw ClearException;

        _store.Clear();
    }
}
