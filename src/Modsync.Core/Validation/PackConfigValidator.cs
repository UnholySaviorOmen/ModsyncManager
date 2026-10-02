// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Manifest.Sources;
using Modsync.Core.Models.Pack;

namespace Modsync.Core.Validation;

public static class PackConfigValidator
{
    public static ValidationResult Validate(PackConfig config)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));

        var errors = new List<string>();

        // meta.name
        var nameResult = NameValidator.Validate(config.Meta.Name);
        if (!nameResult.IsValid)
            errors.AddRange(nameResult.Errors);

        // meta.version
        var versionResult = SemverValidator.Validate(config.Meta.Version);
        if (!versionResult.IsValid)
            errors.AddRange(versionResult.Errors);

        // meta.author
        if (string.IsNullOrWhiteSpace(config.Meta.Author))
            errors.Add("meta.author must be non-empty.");

        // meta.game
        if (string.IsNullOrWhiteSpace(config.Meta.Game))
            errors.Add("meta.game must be non-empty (Nexus game domain).");

        // instance.path
        if (config.Instance is null)
        {
            errors.Add("instance section is required.");
        }
        else
        {
            var instResult = InstancePathValidator.Validate(config.Instance.Path);
            if (!instResult.IsValid)
                errors.AddRange(instResult.Errors);
        }

        // mo2.version
        if (string.IsNullOrWhiteSpace(config.Mo2.Version))
            errors.Add("mo2.version must be non-empty.");

        // mo2.profile
        var profileResult = NameValidator.Validate(config.Mo2.Profile);
        if (!profileResult.IsValid)
            errors.AddRange(profileResult.Errors.Select(e =>
                e.Replace("meta.name", "mo2.profile")));

        // mo2.archive
        if (string.IsNullOrWhiteSpace(config.Mo2.Archive))
            errors.Add("mo2.archive must be non-empty.");
        else if (config.Mo2.Archive.Contains('/') || config.Mo2.Archive.Contains('\\'))
            errors.Add("mo2.archive must be a file name, not a path.");

        // mo2.source — обязательно mirror (с hash).
        // Packer берёт hash MO2-архива из source.hash, а не считает из файла
        // (файла может не быть в downloads/). Значит, source должен уметь
        // предоставить hash. У github/nexus source поля hash нет.
        if (config.Mo2.Source is null)
        {
            errors.Add("mo2.source is required.");
        }
        else if (config.Mo2.Source is not MirrorSourceRef)
        {
            errors.Add(
                "mo2.source must be a mirror source (with url and hash). " +
                "github/nexus sources are not supported for MO2 archive.");
        }

        // mo2.extensions
        for (int i = 0; i < config.Mo2.Extensions.Count; i++)
        {
            var ext = config.Mo2.Extensions[i];
            var r = RelativePathValidator.Validate(ext, $"mo2.extensions[{i}]");
            if (!r.IsValid) errors.AddRange(r.Errors);
        }

        // stockGame.extras
        for (int i = 0; i < config.StockGame.Extras.Count; i++)
        {
            var ext = config.StockGame.Extras[i];
            var r = RelativePathValidator.Validate(ext, $"stockGame.extras[{i}]");
            if (!r.IsValid) errors.AddRange(r.Errors);
        }

        // archiveSources
        var seenArchiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < config.ArchiveSources.Count; i++)
        {
            var src = config.ArchiveSources[i];
            var prefix = $"archiveSources[{i}]";

            if (string.IsNullOrWhiteSpace(src.Archive))
                errors.Add($"{prefix}.archive must be non-empty.");
            else
            {
                if (!seenArchiveNames.Add(src.Archive))
                    errors.Add($"{prefix}.archive is a duplicate: '{src.Archive}'.");

                if (src.Archive.Contains('/') || src.Archive.Contains('\\'))
                    errors.Add($"{prefix}.archive must be a file name, not a path.");
            }

            if (src.Sources.Count == 0)
                errors.Add($"{prefix}.sources must contain at least one source.");
        }

        return errors.Count == 0
            ? ValidationResult.Success
            : ValidationResult.Fail(errors);
    }
}
