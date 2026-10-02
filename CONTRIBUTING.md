# Contributing to ModsyncManager

Thank you for your interest in contributing to ModsyncManager!
This document explains how to set up the project, run tests, and
submit changes.

By submitting a pull request or any other form of contribution,
you agree to the terms of the [Contributor License Agreement
(CLA)](CLA.md). Please read it before making your first
contribution.

---

## Table of contents

1. [Code of Conduct](#code-of-conduct)
2. [Prerequisites](#prerequisites)
3. [Getting started](#getting-started)
4. [Building and testing](#building-and-testing)
5. [Project structure](#project-structure)
6. [Code style](#code-style)
7. [Submitting changes](#submitting-changes)
8. [Reporting bugs](#reporting-bugs)
9. [Requesting features](#requesting-features)

---

## Code of Conduct

Be respectful. Disagreements happen — focus on technical merits,
not individuals. Harassment, discrimination, and personal attacks
are not tolerated.

---

## Prerequisites

- **Windows 10 1809+ or Windows 11.** ModsyncManager is
  Windows-only (uses `Microsoft.Win32.Registry`, P/Invoke, and
  Windows-specific paths).
- **.NET 8 SDK.** Download: https://dotnet.microsoft.com/download/dotnet/8.0
- **.NET SDK 10 (optional but recommended).** Recent MSBuild
  versions are stricter about XML files (see [BOM
  warning](#bom-warning) below).
- **Mod Organizer 2.5.2** (for manual testing against a real
  instance).
- **Visual Studio 2022** or **VS Code** with the C# extension.

---

## Getting started

1. **Fork the repository** on GitHub.
2. **Clone your fork:**
   ```cmd
   git clone https://github.com/<your-username>/ModsyncManager.git
   cd ModsyncManager
   ```
3. **Add the upstream remote:**
   ```cmd
   git remote add upstream https://github.com/UnholySaviorOmen/ModsyncManager.git
   ```
4. **Create a feature branch:**
   ```cmd
   git checkout -b feature/your-feature-name
   ```

---

## Building and testing

### Build everything

```cmd
dotnet build ModsyncManager.slnx
```

### Run all tests

```cmd
dotnet test ModsyncManager.slnx
```

### Run tests for a single project

```cmd
dotnet test tests\Modsync.Core.Tests\Modsync.Core.Tests.csproj
```

### Run the GUI locally

```cmd
dotnet run --project src\ModsyncManager.Gui\ModsyncManager.Gui.csproj
```

### Build a release artifact

```cmd
tools\build-release.bat
```

Output: `build_artifacts\ModsyncManager-<version>-win-x64.zip`.

**All tests must pass before you submit a pull request.** If you
add new functionality, add tests for it. If you fix a bug, add a
test that would have caught it.

---

## Project structure

```
src/
  Modsync.Core/               <- Core: models, JSON, hashing,
                                 abstractions, paths, hash cache.
  Modsync.Logging/            <- File logger.
  Modsync.Platform.MO2/       <- MO2 file readers/writers.
  Modsync.Platform.Nexus/     <- Nexus API client, nxm:// handling.
  Modsync.Pack/               <- Packer pipeline.
  Modsync.Install/            <- Installer + verify pipeline.
  ModsyncManager.NxmHandler/  <- nxm:// system callback exe.
  Modsync.Gui.Shared/         <- MVVM infrastructure (no Avalonia).
  Modsync.Gui.Controls/       <- Shared Avalonia controls.
  Modsync.Gui.Modules/        <- Install / Pack / Verify GUI modules.
  ModsyncManager.Gui/         <- Main GUI exe.

tests/
  Modsync.Core.Tests/
  Modsync.Logging.Tests/
  Modsync.Platform.MO2.Tests/
  Modsync.Platform.Nexus.Tests/
  Modsync.Pack.Tests/
  Modsync.Install.Tests/
  Modsync.Integration.Tests/
  ModsyncManager.NxmHandler.Tests/
  Modsync.Gui.Shared.Tests/
  Modsync.Gui.Modules.Tests/
```

For a detailed architecture reference, see [DOC.md](DOC.md). For
project state, decisions, and pitfalls, see
[DEEPSEEK.md](DEEPSEEK.md).

### Where does my change belong?

- **Manifest format, validation, hashing** → `Modsync.Core`.
- **Reading/writing `modlist.txt`, `plugins.txt`, `meta.ini`** →
  `Modsync.Platform.MO2`.
- **Nexus API calls, `nxm://` URL parsing, registry
  registration** → `Modsync.Platform.Nexus`.
- **Packer pipeline steps** → `Modsync.Pack`.
- **Installer or verify pipeline steps** → `Modsync.Install`.
- **`nxm://` handler exe** → `ModsyncManager.NxmHandler`.
- **ViewModels, services, navigation** → `Modsync.Gui.Shared`
  (if Avalonia-free) or `Modsync.Gui.Modules` (if Avalonia-specific).
- **Avalonia controls, converters** → `Modsync.Gui.Controls`.
- **App entry point, MainWindow, DI composition** →
  `ModsyncManager.Gui`.

---

## Code style

- **C# 12, .NET 8.** Language features: file-scoped namespaces,
  `required` members, primary constructors, pattern matching.
- **4-space indentation** for `.cs` files. 2-space for `.csproj`,
  `.props`, `.json`, `.axaml`, `.yml`.
- **CRLF line endings.** UTF-8. See [.editorconfig](.editorconfig).
- **Nullable reference types enabled.** `#nullable enable` is
  implicit via `Directory.Build.props`.
- **`var`** for built-in types when the type is obvious.
- **File-scoped namespaces** preferred:
  ```csharp
  namespace Modsync.Core;
  ```
- **XML documentation** on public APIs where the intent is not
  obvious from the name.
- **No `Console.WriteLine` for logging.** Use
  `Microsoft.Extensions.Logging`.
- **No `async void`** except event handlers.
- **Comments in Russian** are OK — this is a bilingual project.

### ⚠️ BOM warning

`.cs`, `.axaml`, and `.md` files may have a UTF-8 BOM. Preserve
it when editing.

**`.props`, `.targets`, `.csproj`, and `.slnx` files must NOT
have a BOM.** Recent .NET SDK versions (10.0+) fail with
`MSB4024: Data at the root level is invalid` if a BOM is present.

To strip a BOM from a project file:

```powershell
$path = 'path\to\file.props'
$content = [System.IO.File]::ReadAllText($path) -replace "^\uFEFF", ''
[System.IO.File]::WriteAllText($path, $content, (New-Object System.Text.UTF8Encoding $false))
```

---

## Submitting changes

### Before you submit

1. **Rebase on top of the latest `main`:**
   ```cmd
   git fetch upstream
   git rebase upstream/main
   ```
2. **Run the full test suite:**
   ```cmd
   dotnet test ModsyncManager.slnx
   ```
3. **Ensure all tests pass.** Zero failures, zero warnings.
4. **Add tests** for new functionality or bug fixes.
5. **Update documentation** if your change affects public
   behavior:
   - `DOC.md` — if you change formats, pipelines, or error
     handling.
   - `DEEPSEEK.md` — if you change architecture or add new
     decisions.
   - `README.md` / `README.ru.md` — if you change user-facing
     behavior.

### Opening a pull request

1. **Push your branch:**
   ```cmd
   git push origin feature/your-feature-name
   ```
2. **Open a pull request** against `main`.
3. **Fill out the PR template.** By opening the PR, you confirm
   that you have read and agree to the [CLA](CLA.md).
4. **Keep PRs focused.** One feature or one bug fix per PR. If
   you have multiple unrelated changes, open multiple PRs.
5. **Reference issues** in the PR description: `Fixes #123`.

### What to expect

- **Review within a week, usually sooner.** This is a hobby
  project; please be patient.
- **Feedback may request changes.** Push additional commits to
  the same branch — no need to force-push during review.
- **If a PR is rejected,** the reason will be explained. Not
  every idea fits the project's direction. See the "What NOT to
  do" section in `DEEPSEEK.md` for things that are explicitly
  out of scope.

### What not to submit

- **Refactoring PRs without a functional change.** Unless the
  refactoring fixes a bug or unblocks a specific feature.
- **Formatting-only PRs** (whitespace, brace style, line
  endings). `.editorconfig` handles this; if it doesn't, open an
  issue.
- **Dependencies without justification.** Every new NuGet package
  is a long-term liability. Open an issue first if you need one.
- **Features from the "What NOT to do" list** in `DEEPSEEK.md`.
  These are explicitly out of scope: CLI, `archives.db`, patches,
  autoPack, WebView2, etc.
- **AI-generated code without understanding.** If you can't
  explain your changes in the PR description, don't submit them.

---

## Reporting bugs

Open an issue using the [Bug Report template](.github/ISSUE_TEMPLATE/bug_report.md).

Include:

- **Steps to reproduce.** Minimal example if possible.
- **Expected behavior.**
- **Actual behavior.**
- **Version.** From the Settings screen in the GUI, or from
  `ModsyncManager.exe` file properties.
- **OS version.** Windows 10/11, build number.
- **Logs.** From `%LOCALAPPDATA%\ModsyncManager\logs\`. Attach
  the relevant log file.
- **Manifest / config** if relevant. Redact API keys and
  personal paths.

---

## Requesting features

Open an issue using the [Feature Request template](.github/ISSUE_TEMPLATE/feature_request.md).

Include:

- **What problem are you trying to solve?** Not "add feature X",
  but "I want to do Y and currently I can't because Z".
- **Proposed solution.** If you have one.
- **Alternatives considered.**
- **Would you be willing to implement it?** If yes, mention it.
  If no, that's fine too.

**Check `DEEPSEEK.md` first.** The "What NOT to do" section
lists features that have been explicitly rejected. If your idea
is there, please explain what changed since the rejection.

---

## Questions

For general questions:

- Open a [discussion](https://github.com/UnholySaviorOmen/ModsyncManager/discussions),
  if discussions are enabled.
- Or open an issue with the `question` label.

For security issues, **do not open a public issue.** Contact the
maintainer directly.

---

Thank you for contributing!
