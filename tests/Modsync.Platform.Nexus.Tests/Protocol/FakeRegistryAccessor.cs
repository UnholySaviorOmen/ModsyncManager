using Modsync.Platform.Nexus.Protocol;

namespace Modsync.Platform.Nexus.Tests.Protocol;

/// <summary>
/// Fake IRegistryAccessor для тестов.
///
/// Хранит ключи и значения в памяти. Пути — case-insensitive
/// (как реальный реестр Windows).
///
/// Поддерживает:
///   - GetValue / SetValue / DeleteValue;
///   - KeyExists / DeleteKey (recursive).
///
/// Не эмулирует права доступа и прочие edge-cases —
/// для этого есть отдельный smoke-тест с реальным реестром.
/// </summary>
public sealed class FakeRegistryAccessor : IRegistryAccessor
{
    // keyPath (нормализованный) → valueName ("null" для (Default)) → value
    private readonly Dictionary<string, Dictionary<string, string>> _keys =
        new(StringComparer.OrdinalIgnoreCase);

    // Если задано — GetValue/SetValue/KeyExists бросают эту ошибку.
    public Exception? ReadException { get; set; }
    public Exception? WriteException { get; set; }

    public string? GetValue(string keyPath, string? valueName)
    {
        if (ReadException is not null)
            throw ReadException;

        var key = Normalize(keyPath);

        if (!_keys.TryGetValue(key, out var values))
            return null;

        return values.TryGetValue(NormName(valueName), out var v) ? v : null;
    }

    public void SetValue(string keyPath, string? valueName, string value)
    {
        if (WriteException is not null)
            throw WriteException;

        var key = Normalize(keyPath);

        if (!_keys.TryGetValue(key, out var values))
        {
            values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            _keys[key] = values;
        }

        values[NormName(valueName)] = value;
    }

    public void DeleteKey(string keyPath, bool recursive)
    {
        if (WriteException is not null)
            throw WriteException;

        var key = Normalize(keyPath);

        if (recursive)
        {
            // Удаляем ключ и все, что начинается с "<key>\"
            var prefix = key + "\\";
            var toRemove = _keys.Keys
                .Where(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)
                         || k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var k in toRemove)
                _keys.Remove(k);
        }
        else
        {
            _keys.Remove(key);
        }
    }

    public void DeleteValue(string keyPath, string? valueName)
    {
        if (WriteException is not null)
            throw WriteException;

        var key = Normalize(keyPath);

        if (_keys.TryGetValue(key, out var values))
            values.Remove(NormName(valueName));
    }

    public bool KeyExists(string keyPath)
    {
        if (ReadException is not null)
            throw ReadException;

        return _keys.ContainsKey(Normalize(keyPath));
    }

    // ------------------------------------------------------------------
    //  Test helpers
    // ------------------------------------------------------------------

    /// <summary>Установить значение без проверки WriteException.</summary>
    public void Seed(string keyPath, string? valueName, string value)
    {
        var key = Normalize(keyPath);
        if (!_keys.TryGetValue(key, out var values))
        {
            values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            _keys[key] = values;
        }
        values[NormName(valueName)] = value;
    }

    /// <summary>Убрать все данные (между тестами).</summary>
    public void Clear()
    {
        _keys.Clear();
        ReadException = null;
        WriteException = null;
    }

    private static string Normalize(string keyPath)
        => keyPath.Replace('/', '\\').TrimEnd('\\');

    private static string NormName(string? valueName)
        => valueName ?? "";
}
