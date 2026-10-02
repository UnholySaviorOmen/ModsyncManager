# ModsyncManager

> Tool for creating and installing reproducible mod packs
> for Mod Organizer 2.

[Русская версия →](README.md)

## What is ModsyncManager

ModsyncManager is a packer and installer for MO2 mod packs. A pack
author sets up an MO2 instance however they like, and
ModsyncManager produces a manifest (`modlist.json`). A user
receives the manifest, and ModsyncManager reconstructs the instance
byte-for-byte by hashes.

ModsyncManager works with the **result** of mod installation, not
the process. How exactly the author installed the mods is
irrelevant.

## Why ModsyncManager

- **Manifest is the only source of truth.** Profile state is
  generated from the manifest, not copied.
- **Files are restored by `xxHash64` hashes.** Not by names and
  paths — byte-for-byte.
- **ModsyncManager does not touch the game.** `Stock Game/` is
  just a folder for extras. No game detection, no version checks.
- **Everything not recoverable from archives goes to
  `__ModsyncManager_Output`.** The author decides whether to make
  a patch.
- **Free Nexus downloads via `nxm://` handler.** No WebView2, no
  embedded browser. The user works in their own browser.
- **No executable scripts.** Only declarative directives.
- **Idempotent installer.** Can be run repeatedly.

## Requirements

- Windows 10 1809+ / Windows 11.
- .NET 8 Runtime.
- Mod Organizer 2.5.2 (ModsyncManager reads and writes MO2
  instances).

## Installation

1. Download the latest release from [Releases](../../releases).
2. Extract to any folder.
3. Run `ModsyncManager.exe`.

## Usage

### Creating a pack (packer)

1. Prepare an MO2 instance with mods, plugins, and extras.
2. Create `modsyncmanager-pack.json` next to the instance
   (see `samples/modsyncmanager-pack.full.json` for the full
   schema).
3. Open ModsyncManager → Pack → select the config → **Pack**.

Result: `__ModsyncManager_Output/modlist.json` — the pack manifest.

### Installing a pack (installer)

1. Open ModsyncManager → Install.
2. Select the path to `modlist.json`.
3. (Optional) Select a target folder. Default:
   `<exeDir>/Instances/<meta.name>/`.
4. **Install**.

### Verifying an instance (verify)

ModsyncManager → Verify → select the instance folder.
ModsyncManager compares the instance against the manifest and
reports discrepancies.

## Screenshots

_Coming soon._

## Documentation

- **[DOC.md](DOC.md)** — data formats, pipeline, error handling.
  Reference.
- **[DEEPSEEK.md](DEEPSEEK.md)** — project state, key decisions,
  pitfalls. Internal notes.

## Contributing

Contributions are welcome. Before opening a pull request:

1. Read [CONTRIBUTING.md](CONTRIBUTING.md) — build instructions,
   code style, and PR guidelines.
2. Read [CLA.md](CLA.md) — Contributor License Agreement.
   By submitting a pull request, you agree to its terms.

Found a bug? [Open an issue](../../issues).
Have an idea? [Start a discussion](../../discussions).
 
## License

ModsyncManager is licensed under the **GNU General Public License
v3.0** (GPL-3.0.0). See [LICENSE](LICENSE) for the full text.

Copyright (C) 2026 UnholySaviorOmen.

### Manifests (modlist.json)

Manifests created with ModsyncManager are **not** covered by
GPL-3.0.0. They are data files produced by the tool, not code
derived from it. The author of each manifest is free to license it
under any terms — including commercial — or to keep it unlicensed.

ModsyncManager does not grant you rights to:

- Mod files. They remain under their respective authors' licenses.
- Third-party platforms (Nexus Mods, GitHub, etc.).
- Mod authors' rights.

Responsibility for compliance with these terms lies with the
manifest author, not with ModsyncManager.

This is different from [Wabbajack](https://www.wabbajack.org/),
which licenses its modlists under CC BY-NC-SA 4.0 (non-commercial).
Both are legitimate choices for tool authors. ModsyncManager
chooses neutrality.

### Third-party components

ModsyncManager bundles and depends on third-party components. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the full list
of licenses.
