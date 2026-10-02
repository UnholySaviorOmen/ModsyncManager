// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Archives;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;
using Modsync.Core.Models.Mo2;
using Modsync.Core.Validation;
using Modsync.Platform.MO2.Readers;
using Modsync.Platform.MO2.Writers;
using Microsoft.Extensions.Logging;

using Mo2ModlistEntry = Modsync.Core.Models.Mo2.ModlistEntry;
using Mo2PluginEntry = Modsync.Core.Models.Mo2.PluginEntry;

namespace Modsync.Install.Verify;

/// <summary>
/// Проверяет соответствие target-инстанса манифесту.
/// Read-only: ничего не пишет, не качает, не удаляет.
///
/// Хеши считаются через IHashCache (L1 + L2). Пути файлов
/// стабильны между прогонами → L2 даёт огромный выигрыш
/// на повторных verify.
/// </summary>
public sealed class VerifyPipeline
{
    private const string ManifestFileName = "modlist.json";
    private const string Mo2DirName = "MO2";
    private const string DownloadsDirName = "downloads";
    private const string ModsDirName = "mods";
    private const string ProfilesDirName = "profiles";
    private const string StockGameDirName = "Stock Game";
    private const string ModOrganizerExeName = "ModOrganizer.exe";
    private const string MetaIniFileName = "meta.ini";

    private readonly IHashCache _hashCache;
    private readonly ILogger<VerifyPipeline> _logger;

    public VerifyPipeline(
        IHashCache hashCache,
        ILogger<VerifyPipeline> logger)
    {
        _hashCache = hashCache;
        _logger = logger;
    }

    public VerifyReport Execute(string targetPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException(
                "Target path must be non-empty.", nameof(targetPath));

        var target = Path.GetFullPath(targetPath);
        _logger.LogInformation("Verifying target: {Path}", target);

        var checks = new List<VerifyCheckResult>();

        if (!Directory.Exists(target))
        {
            checks.Add(VerifyCheckResult.Fail(
                "Target directory exists",
                $"Not found: {target}"));
            return BuildReport(target, checks);
        }

        checks.Add(VerifyCheckResult.Ok(
            "Target directory exists",
            target));

        var manifestPath = Path.Combine(target, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            checks.Add(VerifyCheckResult.Fail(
                "Manifest exists",
                $"Not found: {manifestPath}"));
            return BuildReport(target, checks);
        }

        checks.Add(VerifyCheckResult.Ok("Manifest exists", manifestPath));

        ModlistManifest manifest;
        try
        {
            manifest = ManifestJson.Load(manifestPath);
        }
        catch (Exception ex)
        {
            checks.Add(VerifyCheckResult.Fail(
                "Manifest parses",
                ex.Message));
            return BuildReport(target, checks);
        }

        checks.Add(VerifyCheckResult.Ok(
            "Manifest parses",
            $"'{manifest.Meta.Name}' v{manifest.Meta.Version} " +
            $"({manifest.Meta.Game}), schema {manifest.SchemaVersion}"));

        if (!ManifestSchema.IsSupported(manifest.SchemaVersion))
        {
            var supported = string.Join(
                ", ", ManifestSchema.Supported.OrderBy(s => s));
            checks.Add(VerifyCheckResult.Fail(
                "Manifest schemaVersion supported",
                $"'{manifest.SchemaVersion}' not in: {supported}"));
            return BuildReport(target, checks);
        }

        checks.Add(VerifyCheckResult.Ok(
            "Manifest schemaVersion supported",
            manifest.SchemaVersion));

        var nameResult = NameValidator.Validate(manifest.Meta.Name);
        if (!nameResult.IsValid)
        {
            checks.Add(VerifyCheckResult.Fail(
                "meta.name valid",
                string.Join("; ", nameResult.Errors)));
        }
        else
        {
            checks.Add(VerifyCheckResult.Ok("meta.name valid", manifest.Meta.Name));
        }

        var mo2Path = Path.Combine(target, Mo2DirName);
        var stockGamePath = Path.Combine(target, StockGameDirName);
        var downloadsPath = Path.Combine(mo2Path, DownloadsDirName);
        var modsPath = Path.Combine(mo2Path, ModsDirName);
        var profilesPath = Path.Combine(mo2Path, ProfilesDirName);

        checks.Add(DirectoryOk("MO2/ exists", mo2Path));
        checks.Add(DirectoryOk("Stock Game/ exists", stockGamePath));
        checks.Add(DirectoryOk("MO2/downloads/ exists", downloadsPath));
        checks.Add(DirectoryOk("MO2/mods/ exists", modsPath));
        checks.Add(DirectoryOk("MO2/profiles/ exists", profilesPath));

        var mo2ExePath = Path.Combine(mo2Path, ModOrganizerExeName);
        checks.Add(FileOk("MO2/ModOrganizer.exe exists", mo2ExePath));

        var downloadsByHash = BuildDownloadsIndex(downloadsPath);
        checks.Add(VerifyCheckResult.Ok(
            "Downloads scanned",
            $"{downloadsByHash.Count} unique hashes"));

        var ctx = new VerifyContext
        {
            TargetPath = target,
            Manifest = manifest,
            Mo2Path = mo2Path,
            DownloadsPath = downloadsPath,
            ModsPath = modsPath,
            ProfilesPath = profilesPath,
            StockGamePath = stockGamePath,
            DownloadsByHash = downloadsByHash,
        };

        checks.AddRange(CheckArchive(
            "MO2 archive", manifest.Mo2.Archive, ctx));

        foreach (var archive in manifest.Archives)
            checks.AddRange(CheckArchive($"Archive: {archive.Name}", archive, ctx));

        foreach (var mod in manifest.Mods)
            checks.AddRange(CheckMod(mod, ctx));

        checks.AddRange(CheckProfile(ctx));
        checks.AddRange(CheckExtensions(ctx));
        checks.AddRange(CheckExtras(ctx));

        return BuildReport(target, checks);
    }

    private VerifyReport BuildReport(string target, IReadOnlyList<VerifyCheckResult> checks)
    {
        var report = new VerifyReport
        {
            TargetPath = target,
            Checks = checks,
        };

        _logger.LogInformation(
            "Verify complete: {Passed} passed, {Failed} failed",
            report.PassedCount, report.FailedCount);

        return report;
    }

    private Dictionary<XxHash64Value, string> BuildDownloadsIndex(string downloadsPath)
    {
        var result = new Dictionary<XxHash64Value, string>();

        if (!Directory.Exists(downloadsPath))
            return result;

        foreach (var file in Directory.EnumerateFiles(
            downloadsPath, "*", SearchOption.TopDirectoryOnly))
        {
            if (file.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                continue;
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var hash = _hashCache.GetOrCompute(file);
                result.TryAdd(hash, file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to hash {File} during downloads scan", file);
            }
        }

        return result;
    }

    private static VerifyCheckResult DirectoryOk(string name, string path)
        => Directory.Exists(path)
            ? VerifyCheckResult.Ok(name, path)
            : VerifyCheckResult.Fail(name, $"Not found: {path}");

    private static VerifyCheckResult FileOk(string name, string path)
        => File.Exists(path)
            ? VerifyCheckResult.Ok(name, path)
            : VerifyCheckResult.Fail(name, $"Not found: {path}");

    private static IEnumerable<VerifyCheckResult> CheckArchive(
        string name, ArchiveEntry archive, VerifyContext ctx)
    {
        if (ctx.DownloadsByHash.ContainsKey(archive.Hash))
        {
            yield return VerifyCheckResult.Ok(
                name, $"{archive.Name} ({archive.Hash})");
        }
        else
        {
            yield return VerifyCheckResult.Fail(
                name,
                $"Missing in downloads/: {archive.Name} ({archive.Hash})");
        }
    }

    private VerifyCheckResult CheckDirectiveFile(
        string displayPrefix,
        string rootPath,
        Directive directive)
    {
        if (directive is not FromArchiveDirective fromArchive)
        {
            return VerifyCheckResult.Fail(
                $"{displayPrefix} / {directive.Destination}",
                $"Unsupported directive type: {directive.GetType().Name}");
        }

        var destPath = Path.Combine(
            rootPath,
            fromArchive.Destination.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(destPath))
        {
            return VerifyCheckResult.Fail(
                $"{displayPrefix} / {fromArchive.Destination}",
                "File missing");
        }

        var info = new FileInfo(destPath);
        if (info.Length != fromArchive.Size)
        {
            return VerifyCheckResult.Fail(
                $"{displayPrefix} / {fromArchive.Destination}",
                $"Size mismatch: expected {fromArchive.Size}, got {info.Length}");
        }

        var actualHash = _hashCache.GetOrCompute(destPath);
        if (actualHash != fromArchive.Hash)
        {
            return VerifyCheckResult.Fail(
                $"{displayPrefix} / {fromArchive.Destination}",
                $"Hash mismatch: expected {fromArchive.Hash}, got {actualHash}");
        }

        return VerifyCheckResult.Ok(
            $"{displayPrefix} / {fromArchive.Destination}",
            null);
    }

    private IEnumerable<VerifyCheckResult> CheckMod(ModEntry mod, VerifyContext ctx)
    {
        if (IsSeparator(mod.Name))
        {
            yield return VerifyCheckResult.Ok(
                $"Mod: {mod.Name}", "separator (no files expected)");
            yield break;
        }

        if (mod.Name.Contains("[NoDelete]", StringComparison.OrdinalIgnoreCase))
        {
            yield return VerifyCheckResult.Ok(
                $"Mod: {mod.Name}", "NoDelete (skipped)");
            yield break;
        }

        var modDir = Path.Combine(ctx.ModsPath, mod.Name);

        if (!Directory.Exists(modDir))
        {
            yield return VerifyCheckResult.Fail(
                $"Mod: {mod.Name}",
                $"Mod directory not found: {modDir}");
            yield break;
        }

        yield return CheckModMetaIni(mod, modDir);

        if (mod.Directives.Count == 0)
        {
            yield return VerifyCheckResult.Ok(
                $"Mod: {mod.Name}", "no directives");
        }

        foreach (var directive in mod.Directives)
        {
            yield return CheckDirectiveFile($"Mod: {mod.Name}", modDir, directive);
        }
    }

    private static VerifyCheckResult CheckModMetaIni(ModEntry mod, string modDir)
    {
        var metaIniPath = Path.Combine(modDir, MetaIniFileName);
        var fileExists = File.Exists(metaIniPath);

        if (mod.Meta is null)
        {
            return fileExists
                ? VerifyCheckResult.Fail(
                    $"Mod: {mod.Name} / meta.ini",
                    "File exists on disk, but manifest has no meta")
                : VerifyCheckResult.Ok(
                    $"Mod: {mod.Name} / meta.ini",
                    "not expected");
        }

        if (!fileExists)
        {
            return VerifyCheckResult.Fail(
                $"Mod: {mod.Name} / meta.ini",
                "File missing");
        }

        ModMeta actual;
        try
        {
            actual = MetaIniReader.Parse(File.ReadAllLines(metaIniPath));
        }
        catch (Exception ex)
        {
            return VerifyCheckResult.Fail(
                $"Mod: {mod.Name} / meta.ini",
                $"Failed to parse: {ex.Message}");
        }

        return CompareModMeta(mod.Name, mod.Meta, actual);
    }

    private static VerifyCheckResult CompareModMeta(
        string modName, ModMeta expected, ModMeta actual)
    {
        var diffs = new List<string>();

        CompareString("gameName", expected.GameName, actual.GameName, diffs);
        CompareString("gameID", expected.GameId, actual.GameId, diffs);
        CompareInt("modID", expected.ModId, actual.ModId, diffs);
        CompareInt("fileID", expected.FileId, actual.FileId, diffs);
        CompareString("version", expected.Version, actual.Version, diffs);
        CompareString("repository", expected.Repository, actual.Repository, diffs);
        CompareString("url", expected.Url, actual.Url, diffs);
        CompareString("comments", expected.Comments, actual.Comments, diffs);
        CompareString("notes", expected.Notes, actual.Notes, diffs);

        if (diffs.Count == 0)
            return VerifyCheckResult.Ok($"Mod: {modName} / meta.ini", null);

        return VerifyCheckResult.Fail(
            $"Mod: {modName} / meta.ini",
            $"Content differs: {string.Join("; ", diffs)}");
    }

    private static void CompareString(
        string field, string? expected, string? actual, List<string> diffs)
    {
        var e = expected ?? "";
        var a = actual ?? "";

        if (!string.Equals(e, a, StringComparison.Ordinal))
        {
            diffs.Add($"{field}: expected '{e}', got '{a}'");
        }
    }

    private static void CompareInt(
        string field, int? expected, int? actual, List<string> diffs)
    {
        if (expected != actual)
        {
            var e = expected?.ToString() ?? "null";
            var a = actual?.ToString() ?? "null";
            diffs.Add($"{field}: expected {e}, got {a}");
        }
    }

    private IEnumerable<VerifyCheckResult> CheckExtensions(VerifyContext ctx)
    {
        var entries = ctx.Manifest.Mo2.Extensions;
        if (entries.Count == 0)
            yield break;

        foreach (var entry in entries)
        {
            foreach (var directive in entry.Directives)
            {
                yield return CheckDirectiveFile(
                    $"MO2 extension: {entry.Name}",
                    ctx.Mo2Path,
                    directive);
            }
        }
    }

    private IEnumerable<VerifyCheckResult> CheckExtras(VerifyContext ctx)
    {
        var entries = ctx.Manifest.StockGame.Extras;
        if (entries.Count == 0)
            yield break;

        foreach (var entry in entries)
        {
            foreach (var directive in entry.Directives)
            {
                yield return CheckDirectiveFile(
                    $"Stock Game extra: {entry.Name}",
                    ctx.StockGamePath,
                    directive);
            }
        }
    }

    private static IEnumerable<VerifyCheckResult> CheckProfile(VerifyContext ctx)
    {
        var profile = ctx.Profile;
        if (string.IsNullOrWhiteSpace(profile))
        {
            yield return VerifyCheckResult.Fail(
                "Profile", "manifest.mo2.profile is empty");
            yield break;
        }

        var profileDir = Path.Combine(ctx.ProfilesPath, profile);
        if (!Directory.Exists(profileDir))
        {
            yield return VerifyCheckResult.Fail(
                "Profile directory", $"Not found: {profileDir}");
            yield break;
        }

        yield return VerifyCheckResult.Ok(
            "Profile directory", profileDir);

        yield return CheckProfileFile(
            "modlist.txt",
            Path.Combine(profileDir, "modlist.txt"),
            expected: ModlistWriter.Serialize(new ModlistFile
            {
                Entries = ctx.Manifest.Mods
                    .OrderBy(m => m.Order)
                    .Select(m => new Mo2ModlistEntry(m.Name, m.Enabled))
                    .ToList(),
            }));

        yield return CheckProfileFile(
            "plugins.txt",
            Path.Combine(profileDir, "plugins.txt"),
            expected: PluginsWriter.Serialize(new PluginsFile
            {
                Entries = ctx.Manifest.Plugins
                    .Select(p => new Mo2PluginEntry(p.Name, p.Enabled))
                    .ToList(),
            }));

        yield return CheckProfileFile(
            "loadorder.txt",
            Path.Combine(profileDir, "loadorder.txt"),
            expected: LoadorderWriter.Serialize(new LoadorderFile
            {
                Plugins = ctx.Manifest.Loadorder.ToList(),
            }));
    }

    private static VerifyCheckResult CheckProfileFile(
        string fileName, string actualPath, string expected)
    {
        if (!File.Exists(actualPath))
            return VerifyCheckResult.Fail(
                $"Profile: {fileName}", $"Not found: {actualPath}");

        var actual = File.ReadAllText(actualPath);
        var normalizedActual = NormalizeBomAndEol(actual);
        var normalizedExpected = NormalizeBomAndEol(expected);

        if (normalizedActual != normalizedExpected)
            return VerifyCheckResult.Fail(
                $"Profile: {fileName}",
                "Content differs from manifest");

        return VerifyCheckResult.Ok($"Profile: {fileName}", null);
    }

    private static string NormalizeBomAndEol(string s)
    {
        if (s.Length > 0 && s[0] == '\uFEFF')
            s = s[1..];

        s = s.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

        return s;
    }

    private static bool IsSeparator(string name)
        => name.Length > 0 && name[0] == '#';
}
