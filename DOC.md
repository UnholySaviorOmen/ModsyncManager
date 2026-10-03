# ModsyncManager — Справочник

**Версия документа:** 6.0
**Обновлено:** 2026-10-02

Формальный справочник по форматам, pipeline и обработке ошибок.
Состояние проекта и план работ — в `DEEPSEEK.md`.

## Оглавление

1. [Обзор](#обзор)
2. [Основные принципы](#основные-принципы)
3. [Глоссарий](#глоссарий)
4. [Архитектура](#архитектура)
5. [Структура папок](#структура-папок)
6. [Форматы данных](#форматы-данных)
    - 6.1. [modsyncmanager-pack.json](#modsyncmanager-packjson)
    - 6.2. [modlist.json](#modlistjson)
    - 6.3. [Директивы](#директивы)
    - 6.4. [Источники архивов](#источники-архивов)
    - 6.5. [Формат .meta](#формат-meta)
    - 6.6. [Формат meta.ini мода](#формат-meta-ini-мода)
    - 6.7. [__ModsyncManager_Output](#__modsyncmanager_output)
    - 6.8. [Формат modsyncmanager-nxm-handler.log](#формат-modsyncmanager-nxm-handlerlog)
    - 6.9. [Формат nxm-handler-backup.json](#формат-nxm-handler-backupjson)
    - 6.10. [Структура HKCU\Software\Classes\nxm](#структура-hkcusoftwareclassesnxm)
    - 6.11. [Логи GUI](#логи-gui)
7. [Идентификация архивов](#идентификация-архивов)
8. [Nexus game domain](#nexus-game-domain)
9. [Формат файлов MO2](#формат-файлов-mo2)
10. [Пайплайн: создание сборки](#пайплайн-создание-сборки)
    10.1. [Pack без config](#pack-без-config)
11. [Пайплайн: установка сборки](#пайплайн-установка-сборки)
12. [Пайплайн: обновление сборки](#пайплайн-обновление-сборки)
13. [Работа с Nexus Mods](#работа-с-nexus-mods)
    - 13.1. [Free-загрузка через nxm:// handler](#free-загрузка-через-nxm-handler)
    - 13.2. [Handler nxm:// — exit codes и лог](#handler-nxm--exit-codes-и-лог)
14. [Persist кеша хешей](#persist-кеша-хешей)
15. [Обработка ошибок](#обработка-ошибок)
16. [Технологический стек](#технологический-стек)

---

## Обзор

**ModsyncManager** — инструмент для создания и установки
воспроизводимых сборок модов для Mod Organizer 2. Состоит из:

- **`ModsyncManager.Gui`** (exe → `ModsyncManager.exe`) — Avalonia GUI.
- **`ModsyncManager.NxmHandler`** (exe → `ModsyncManager.NxmHandler.exe`) —
  системный callback для `nxm://`.

Логика — в библиотеках `Modsync.Pack` и `Modsync.Install`.

**Ключевая идея:** манифест — единственный источник правды. Все файлы
восстанавливаются по хешам (`xxHash64`). ModsyncManager работает с
**результатом** установки, а не с процессом.

**Все данные ModsyncManager — в `%LOCALAPPDATA%\ModsyncManager\`.**
Единый корень: `settings.json`, `nexus.key`,
`nxm-handler-backup.json`, `cache.db`, `logs\`.

**Философия packer-а:** снапшот инстанса. Автор готовит инстанс MO2
любым способом. Packer индексирует, что получилось. Всё, что не
восстановимо из архивов, честно складывается в `__ModsyncManager_Output`.

**Философия installer-а:** тупой исполнитель директив. Не проверяет
игру, версии, совместимость. Просто воссоздаёт структуру, которую
сделал автор.

**Целевая платформа:** Windows 10 1809+ / Windows 11.
**Целевая версия MO2:** 2.5.2.

---

## Основные принципы

1. **Манифест — единственный источник правды.** Профиль MO2
   генерируется из манифеста, а не копируется.
2. **Файлы восстанавливаются по хешам.** `xxHash64`, не по именам
   и путям.
3. **ModsyncManager работает с результатом, а не с процессом.**
4. **Одна папка `downloads/` для всех архивов.**
5. **Идентификация архивов — канонический id.**
6. **Глобальный реестр архивов — отменён** (2026-10-01). См.
   `DEEPSEEK.md`.
7. **Ничего не удаляем** (кроме случаев, описанных в reconcile).
8. **Директивы выполняются последовательно.** `lastWins`.
9. **`[NoDelete]` в имени папки** защищает пользовательские моды.
10. **BSA/BA2 — единые файлы.**
11. **Никаких исполняемых скриптов.**
12. **Installer идемпотентен.**
13. **Installer не работает с игрой.**
14. **Шаги pipeline изолированы.**
15. **Nexus — один источник, несколько стратегий доступа.**
16. **Параллелизм на уровне pipeline.**
17. **Кеш хешей обязателен.** `IHashCache`: `FileHashCache` (L1) +
    `SqliteHashCache` (L1+L2, persist).
18. **Манифест самодостаточен.**
19. **Unmatched → `__ModsyncManager_Output`.**
20. **`.mohidden` — часть пути.**
21. **Инстансы в `<exeDir>/Instances/`.**
22. **Сепараторы (`#...`) — часть сборки.**
23. **Один extract архивов на pack pipeline.**
24. **Отмена — не ошибка.** `AggregateException`, все inner
    которого — отмены, трактуется как отмена.
25. **DevMode по умолчанию выключен.**
26. **Free-загрузка с Nexus — через `nxm://` handler, без WebView2.**
27. **Регистрация `nxm://` — в `HKCU`, не в `HKLM`.**
28. **Backup + restore — не автоматически.**
29. **Очередь URL** — `Channel<string>` в `NxmUrlReceiver`.

---

## Глоссарий

| Термин | Определение |
|---|---|
| **Манифест** | `modlist.json` — единственный источник правды. |
| **Инстанс** | Рабочая папка MO2 с подпапками `MO2/`, `Stock Game/`, `__ModsyncManager_Output/` (у автора) или `modlist.json` (у пользователя). |
| **Канонический id** | Идентификатор архива: `nexus_{game}_{modId}_{fileId}` или `local_{slug}`. |
| **Директива** | Декларативное действие: `FromArchive`, `CreateDirectory`, `Delete`. |
| **`FromArchive`** | Директива: взять файл из архива по hash, положить по destination. |
| **Сепаратор** | Строка `#...` в `modlist.txt`. |
| **`[NoDelete]`** | Маркер в имени мода. |
| **Unmatched** | Файл, не найденный в архивах. Идёт в `__ModsyncManager_Output`. |
| **Extensions** | Файлы в корне `MO2/`, кроме модов. |
| **Extras** | Файлы в корне `Stock Game/`. |
| **`.mohidden`** | Часть пути, не отдельный «скрытый» файл. |
| **`__ModsyncManager_Output`** | Каталог автора для unmatched. |
| **BSA/BA2** | Единые файлы. ModsyncManager не разбирает содержимое. |
| **Mirror** | Источник: URL + hash. |
| **Nexus** | Источник: `modId` + `fileId` + `game`. |
| **`ArchiveMatcher`** | Распаковывает архивы один раз, строит hash-индексы. |
| **`manifest.archives[]`** | Все mod-архивы. MO2-архив — в `manifest.mo2.archive`. |
| **`ArchiveDownloadHelper`** | Общий helper скачивания (retry, `.part`, hash-check). |
| **DevMode** | Тумблер в Settings. |
| **InstalledPackInfo** | Модель инстанса из `<exeDir>/Instances/`. |
| **Home-дашборд** | Экран Home. |
| **nxm:// handler** | Обработчик ссылок `nxm://`. |
| **NxmUrlReceiver** | Named pipe server в ModsyncManager. |
| **ProtocolRegistrar** | Регистрация/снятие `nxm://` в HKCU, backup/restore. |
| **Backup handler'а** | JSON-файл с путём к предыдущему handler'у. |
| **`IHashCache`** | Интерфейс кеша хешей. |
| **`SqliteHashCache`** | L1+L2 (SQLite `cache.db`). |
| **`cache.db`** | SQLite-база кеша хешей. Таблица `file_hashes`. |
| **Cache-экран** | DevMode-экран (`ScreenType.Cache`, `CacheVM`). |
| **`StepProgress.Detail`** | Опциональная деталь шага pipeline. |
| **`ModsyncManager.NxmHandler.exe`** | Отдельный exe для `nxm://`. |

---

## Архитектура

### Проекты solution

```
ModsyncManager.slnx
src/
  Modsync.Core               <- ядро: модели, JSON, хеширование,
                                абстракции, ModsyncPaths,
                                IHashCache / FileHashCache /
                                SqliteHashCache, NxmPipeName.
  Modsync.Logging            <- свой file-logger (дневная ротация,
                                retention 14 дней, ротация по
                                размеру 10 МБ, .1.log).
  Modsync.Platform.MO2       <- чтение/запись modlist, plugins,
                                loadorder, meta.ini, meta.
  Modsync.Platform.Nexus     <- NexusClient, NexusDownloader,
                                INexusApiKeyProvider,
                                INexusCredentialValidator,
                                NexusUrlParser, NexusNxmUrl,
                                NexusFreeNxmProvider +
                                NullNexusFreeNxmProvider,
                                IProtocolRegistrar /
                                ProtocolRegistrar,
                                IRegistryAccessor /
                                WindowsRegistryAccessor /
                                NullRegistryAccessor,
                                INxmUrlReceiver / NxmUrlReceiver,
                                IUrlOpener / ShellUrlOpener,
                                NxmHandlerBackup,
                                NxmServices.AddModsyncNxm().
  Modsync.Pack               <- class library: pipeline packer.
  Modsync.Install            <- class library: installer + verify.
  ModsyncManager.NxmHandler  <- exe -> ModsyncManager.NxmHandler.exe.
  Modsync.Gui.Shared         <- MVVM-инфра. БЕЗ Avalonia.
  Modsync.Gui.Controls       <- общие Avalonia-контролы.
  Modsync.Gui.Modules        <- модули Install / Pack / Verify.
  ModsyncManager.Gui         <- exe -> ModsyncManager.exe.
tests/
  Modsync.Core.Tests
  Modsync.Logging.Tests
  Modsync.Platform.MO2.Tests
  Modsync.Platform.Nexus.Tests
  Modsync.Pack.Tests
  Modsync.Install.Tests
  Modsync.Integration.Tests
  ModsyncManager.NxmHandler.Tests
  Modsync.Gui.Shared.Tests
  Modsync.Gui.Modules.Tests
```

### Принципы архитектуры

**Pipeline — единственный оркестратор.** Только pipeline знает
порядок шагов. Шаги не знают друг о друге.

**Шаги изолированы.** Каждый шаг — `IStep<TInput, TOutput>`
(см. `Modsync.Core/Abstractions/IStep.cs`). Получает вход,
возвращает выход. Не вызывает другие шаги.

**Зависимости через DI.** Никаких `ServiceLocator`, никаких
`static` классов.

**Ошибки на уровне pipeline.** Шаг либо успешен, либо бросает
исключение. Pipeline решает, что делать.

### Границы ответственности

**Modsync.Core:** модели, JSON, хеширование, валидаторы,
Slug, ArchiveId, абстракции, `SevenZipExtractor`, `TempWorkspace`,
`ModsyncPaths`, `IHashCache` / `FileHashCache` / `SqliteHashCache`,
`ArchiveDownloadHelper`, `CancellationHelper`, `StepProgress`.

**Modsync.Platform.MO2:** чтение/запись MO2-файлов, `MetaReader`,
`MetaIniReader`, `MetaIniWriter`, `ModlistWriter`, `PluginsWriter`,
`LoadorderWriter`.

**Modsync.Platform.Nexus:** HTTP-клиент к Nexus API
(`NexusClient`), `NexusDownloader`, `INexusApiKeyProvider` +
`NexusApiKeyProvider`, `INexusCredentialValidator` +
`NexusCredentialValidator`, `NexusAuthenticationException`,
`NexusUrlParser`, `NexusNxmUrl`, модели ответов. Free-загрузка:
`INexusFreeNxmProvider` + `NullNexusFreeNxmProvider` +
`NexusFreeNxmProvider`, `IProtocolRegistrar` + `ProtocolRegistrar`,
`IRegistryAccessor` + `WindowsRegistryAccessor` +
`NullRegistryAccessor`, `INxmUrlReceiver` + `NxmUrlReceiver`,
`IUrlOpener` + `ShellUrlOpener`, `NxmHandlerBackup`,
`NxmServices.AddModsyncNxm()`.

**Modsync.Pack:** `PackPipeline` + 13 шагов;
`Modsync.Pack.Matching` (`ArchiveMatcher`, `ArchiveIndexes`,
`Mo2ArchiveBuilder`) — построение индексов архивов и матчинг файлов
по хешу. Один экземпляр `ArchiveMatcher` на весь pipeline.
`PackInputFactory`, `PackSummary` + `PackSummaryBuilder`.
**`PackConfigBuilder`** — сервис для GUI-формы «Create Pack Config»:
сканирует `MO2/downloads/` на non-nexus архивы, читает профили,
собирает `PackConfig` из пользовательского ввода + хардкода
`mo2`-секции. DI-extension: `AddModsyncPack`.

**Modsync.Install:** `InstallPipeline` + 11 шагов; `MirrorDownloader`,
`DownloaderRegistry`; `VerifyPipeline`. `InstallInputFactory`,
`InstallSummary` + `InstallSummaryBuilder`. DI-extension:
`AddModsyncInstall`.

**ModsyncManager.NxmHandler:** exe. `Program.cs`, `NxmHandlerArgs`,
`PipeClient`, `IHandlerLogger` / `HandlerLogger`. Парсит argv,
проверяет схему, подключается к named pipe
`\\.\pipe\modsyncmanager-nxm`, передаёт URL работающему
ModsyncManager. Если pipe недоступен — exit 3. Не стартует GUI.
Без Avalonia.

**Modsync.Gui.Shared:** MVVM-инфра. `ViewModel`,
`ProgressViewModel` (с `Detail`), `HomeVM`, `InstalledPackVM`,
`LogVM`, `LogsVM`, `CacheVM`, `SettingsVM`, `NexusSettingsVM`,
`NexusFreeDownloadSettingsVM`, `MainWindowVM`, `NavigationVM`.
Сервисы: `IFilePickerService`, `IUiDispatcher`,
`IInstalledPackScanner`, `IProcessLauncher`, `ISettingsStore`.
Навигация: `IScreenFactory`, `IInstallRequestHandler`,
`IInstallTarget`, `ScreenType`. Модели: `InstalledPackInfo`,
`Settings`. Состояния: `InstallState`, `PackState`, `VerifyState`.

**Modsync.Gui.Controls:** `LogView`, `FilePickerView`,
`LogLevelToBrushConverter`.

**Modsync.Gui.Modules:** Avalonia-модули Install / Pack / Verify
в одном проекте. `XxxVM`, `XxxView`, `IXxxRunner`.

**ModsyncManager.Gui:** exe. `App.axaml`, `MainWindow.axaml`,
`ViewLocator`, `ScreenFactory`, `AvaloniaFilePickerService`,
`AvaloniaUiDispatcher`, `ShellProcessLauncher`, `NavigationView`,
`HomeView`, `SettingsView`, `LogsView`, `CacheView`,
`ScreenIconConverter`, `SingleInstanceDialog`.

### Интерфейсы

Ключевые интерфейсы — в коде:

- `IStep<TInput, TOutput>` — `Modsync.Core/Abstractions/IStep.cs`.
- `IArchiveDownloader` — `Modsync.Core/Abstractions/IArchiveDownloader.cs`.
- `IArchiveExtractor` — `Modsync.Core/Archives/Extraction/IArchiveExtractor.cs`.
- `IHashCache` — `Modsync.Core/Archives/IHashCache.cs`.
- `INexusApiKeyProvider` — `Modsync.Platform.Nexus/INexusApiKeyProvider.cs`.
- `INexusCredentialValidator` — `Modsync.Platform.Nexus/NexusCredentialValidator.cs`.
- `INexusFreeNxmProvider` — `Modsync.Platform.Nexus/INexusFreeNxmProvider.cs`.
- `IProtocolRegistrar` — `Modsync.Platform.Nexus/Protocol/IProtocolRegistrar.cs`.
- `IRegistryAccessor` — `Modsync.Platform.Nexus/Protocol/IRegistryAccessor.cs`.
- `INxmUrlReceiver` — `Modsync.Platform.Nexus/Protocol/INxmUrlReceiver.cs`.
- `IUrlOpener` — `Modsync.Platform.Nexus/Protocol/IUrlOpener.cs`.
- `IHandlerLogger` — `ModsyncManager.NxmHandler/IHandlerLogger.cs`.
- `IInstallRunner` / `IPackRunner` / `IVerifyRunner` — в GUI-модулях.

### Навигация GUI
```
MainWindow (Grid: sidebar + content)
├── NavigationView (DataContext = NavigationVM)
└── ContentControl (Content = MainWindowVM.ActivePane)
└── ViewLocator → View
```
`MainWindowVM.NavigateTo(ScreenType)` резолвит VM через
`IScreenFactory`, обновляет `ActivePane`, синхронизирует sidebar.
В конструкторе — `NavigateTo(ScreenType.Home)`.

`IScreenFactory` (в `ModsyncManager.Gui`) маппит `ScreenType` на VM:
Home, Install, Pack, Verify, Logs, Cache, Settings.

`ViewLocator` ищет `Control` по `FullName` VM: сначала прямая
замена `.ViewModels.` → `.Views.`, потом перебор всех загруженных
сборок.

### DevMode

`SettingsVM.IsDevMode` — прокси на `ISettingsStore.Current.DevMode`,
persist в `%LOCALAPPDATA%\ModsyncManager\settings.json`. Default `false`.

Влияет на `NavigationVM.Items`:

- DevMode=false → `[Home, Settings]`
- DevMode=true  → `[Home, Install, Pack, Verify, Logs, Cache, Settings]`

При смене `IsDevMode` — `RebuildItems()`. Если текущий
`SelectedItem` скрывается — переход на Home.

### Версия приложения

Единый источник — `Directory.Build.props` в корне репо
(`<VersionPrefix>`). MSBuild генерирует `AssemblyVersion`,
`FileVersion`, `InformationalVersion`.
`<IncludeSourceRevisionInInformationalVersion>false</...>`.

GUI читает `InformationalVersion` из entry assembly
(`SettingsVM.GetVersion`). Хардкода версии в коде нет.

### Сборка релиза

`tools/build-release.bat`:

1. Читает `<VersionPrefix>` из `Directory.Build.props`.
2. `dotnet publish` GUI и NxmHandler в одну папку
   `build_artifacts/ModsyncManager-<version>-win-x64/`
   (`-c Release -r win-x64 --self-contained false`).
3. `Compress-Archive` →
   `build_artifacts/ModsyncManager-<version>-win-x64.zip`.

Раскладка — «как есть» (managed-сборки, нативные DLL
Avalonia/Skia, `Assets/7z/`).

---

## Структура папок

### Рабочая папка автора

```
C:\Mods\Dev\
  modsyncmanager-pack.json        <- конфиг packer-а
  NordicUI Overhaul\              <- инстанс MO2
    MO2\
      ModOrganizer.exe
      portable.txt
      plugins\                    <- extensions (fomod_plus_installer.dll)
      tools\                      <- extensions (BethINI)
      downloads\                  <- архивы (моды + MO2 + extras)
      mods\
        SkyUI\
          meta.ini
        [NoDelete]UserMod\        <- пользовательский мод
      profiles\NordicUI\          <- modlist.txt, plugins.txt, loadorder.txt
    Stock Game\                   <- extras (SKSE, ENB)
    __ModsyncManager_Output\
      modlist.json                <- манифест (WriteManifestStep)
      MO2\
        mods\<ModName>\<path>     <- unmatched модов
        <path>                    <- unmatched extensions
      Stock Game\
        <path>                    <- unmatched extras
```

### Рабочая папка пользователя (после установки)

```
D:\Games\ModsyncManager\
  ModsyncManager.exe
  ModsyncManager.NxmHandler.exe
  Modsync.Core.dll
  Modsync.Pack.dll
  Modsync.Install.dll
  Modsync.Platform.MO2.dll
  ...
  Assets\7z\ (7z.exe, 7z.dll)
  Instances\
    Nordic UI Overhaul\           <- имя из meta.name
      modlist.json                <- копия манифеста (ResolveTargetStep)
      MO2\
        ModOrganizer.exe
        portable.txt
        plugins\
        tools\
        downloads\
          Mod.Organizer-2.5.2.7z
          SkyUI.7z
        mods\
          SkyUI\
            meta.ini
          [NoDelete]UserMod\      <- пользовательский мод, installer не трогает
        profiles\Default\
          modlist.txt / plugins.txt / loadorder.txt
      Stock Game\
        skse64_loader.exe
        enbseries\
```

### Глобальные данные

```
%LOCALAPPDATA%\ModsyncManager\
  settings.json                   <- настройки (DevMode)
  nexus.key                       <- API-ключ (plaintext; v0.2.0+ - DPAPI)
  nxm-handler-backup.json         <- backup предыдущего nxm:// handler-а
  cache.db                        <- SQLite: кеш хешей файлов (file_hashes)
  logs\
    modsyncmanager-yyyy-MM-dd.log     <- логи GUI (дневная ротация)
    modsyncmanager-yyyy-MM-dd.1.log   <- ротация по размеру (10 МБ)
    modsyncmanager-nxm-handler.log    <- логи handler-а
```

---

## Форматы данных

### modsyncmanager-pack.json

Конфиг автора сборки. Лежит рядом с инстансом MO2.

Пример — `samples/modsyncmanager-pack.full.json`.
Модель — `Modsync.Core/Models/Pack/PackConfig.cs` (и связанные
`PackMeta`, `PackInstance`, `PackMo2`, `PackStockGame`,
`PackArchiveSource`).

**Поля:**

| Поле | Описание |
|---|---|
| `meta.name` | Имя сборки. `NameValidator`. |
| `meta.version` | Semver 2.0.0. |
| `meta.author` | Автор. |
| `meta.game` | Nexus game domain. |
| `meta.gameVersion` | Целевая версия игры. |
| `instance.path` | Относительный путь к корню инстанса. `InstancePathValidator`. |
| `mo2.version` | Версия MO2. |
| `mo2.profile` | Имя профиля MO2. `NameValidator`. |
| `mo2.archive` | Имя архива MO2 (только имя, не путь). |
| `mo2.source` | `MirrorSourceRef` (с hash). |
| `mo2.extensions` | Пути от `MO2/` (файлы или папки). `RelativePathValidator`. |
| `stockGame.extras` | Пути от `Stock Game/`. |
| `archiveSources` | Источники для архивов без `.meta`. Уникальные имена. |

**Валидация:** `PackConfigValidator`.

### modlist.json

Манифест. Единственный источник правды для installer-а.

Пример — `samples/modsyncmanager-pack.json` (минимальный),
`Modsync.Core/Models/Manifest/ModlistManifest.cs` (модель).

**Секции:**

| Секция | Описание |
|---|---|
| `schemaVersion` | Версия формата. `ManifestSchema.IsSupported`. |
| `manifestVersion` | Версия сборки (= `meta.version`). |
| `createdAt` | `yyyy-MM-ddTHH:mm:ss.fffZ`. |
| `createdBy` | `modsyncmanager-pack/0.1.0`. |
| `meta` | Метаданные сборки. |
| `execution` | `directives: "sequential"`, `onConflict: "lastWins"`. |
| `mo2` | Версия MO2, профиль, MO2-архив, extensions. |
| `stockGame` | extras. |
| `archives` | Все mod-архивы. MO2-архив сюда НЕ попадает. |
| `mods` | Моды с директивами и `ModMeta`. |
| `plugins` | Плагины с флагами. |
| `loadorder` | Порядок загрузки. |

**`ModMeta`** (поле `mods[].meta`): все поля опциональны.
`ModMeta.Empty` — валидное состояние. Поля: `GameName`, `GameId`,
`ModId`, `FileId`, `Version`, `Repository`, `Url`, `Comments`,
`Notes`. Без `Category`.

### Директивы

Полиморфизм: `type` — дискриминатор.

**`FromArchive`** — взять файл из архива по hash:
- `archive` — id из `manifest.archives[]` или `manifest.mo2.archive.id`.
- `source` — путь внутри архива.
- `destination` — путь относительно корня мода / MO2 / Stock Game.
- `hash`, `size` — проверка после распаковки.

**`CreateDirectory`** — создать папку:
- `destination` — путь.

**`Delete`** — удалить файл (модель есть, packer не создаёт):
- `destination` — путь.

### Источники архивов

**`mirror`** — URL + hash:
- `url` — HTTPS-ссылка.
- `hash` — обязателен.

**`nexus`** — Nexus API:
- `modId`, `fileId` — положительные.
- `game` — Nexus game domain.

**GitHub source** — удалён (12.10.1). Всё через `mirror`.

### Формат .meta

Файл рядом с архивом в `downloads/`: `SkyUI.7z.meta`.

```
[General]
gameName=Skyrim
modID=3863
fileID=1000172397
```

Правила:
- Ключи `modID` / `fileID` — case-sensitive (заглавные ID).
- `gameName` — опционально.
- Секция `[General]` — case-insensitive.
- `.meta` без `modID`/`fileID` (или с lowercase) → `MetaReader.TryRead`
  вернёт null → fallback на `archiveSources[]`.
- `.meta` не считается архивом.

### Формат meta.ini мода

Файл `mods/<Name>/meta.ini`. Создаётся MO2.

```
[General]
gameName=Skyrim Special Edition
gameID=skyrimspecialedition
modID=32349
fileID=795423
version=1.7.0
repository=Nexus
url=https://www.nexusmods.com/skyrimspecialedition/mods/32349
comments=
notes=
```

**Чтение (`MetaIniReader`):**
- Секция `[General]`. Ключи case-insensitive.
- `[installedFiles]` игнорируется.
- `category`, `newestVersion`, `nexusFileStatus`, timestamps —
  игнорируются.
- Если `[General]` пуста → `ModMeta.Empty`.

**Запись (`MetaIniWriter`):**
- `[General]`, camelCase.
- UTF-8 без BOM, CRLF.
- Не пишет `[installedFiles]`, `category`, `newestVersion`.
- Null-поля не пишутся. Пустые строки — `key=`.
- Порядок полей: `gameName`, `gameID`, `modID`, `fileID`,
  `version`, `repository`, `url`, `comments`, `notes`.

### __ModsyncManager_Output

Каталог автора сборки. Создаётся packer-ом в корне инстанса.

```
__ModsyncManager_Output/
modlist.json ← манифест (WriteManifestStep)
MO2/
mods/
<ModName>/<path> ← unmatched модов (MatchStep)
<path> ← unmatched extensions (плоско)
Stock Game/
<path> ← unmatched extras (плоско)
```

**Три категории unmatched:**

- **Моды (`MatchStep`)** — `MO2/mods/<ModName>/<path>`. Сохраняется
  имя мода. `meta.ini` в корне мода сюда не попадает.
- **Extensions (`MatchExtensionsStep`)** — `MO2/<path>`. Плоско.
- **Extras (`MatchExtrasStep`)** — `Stock Game/<path>`. Плоско.

**Почему плоско для extensions/extras:** пути в
`__ModsyncManager_Output` совпадают с реальными путями в инстансе.

**Чистка:**
- `MatchStep` перед прогоном удаляет `__ModsyncManager_Output/MO2/mods/`.
- `PackPipeline` перед write unmatched extensions удаляет
  содержимое `__ModsyncManager_Output/MO2/`, кроме `mods/`.
- `PackPipeline` перед write unmatched extras удаляет содержимое
  `__ModsyncManager_Output/Stock Game/`.
- `modlist.json` перезаписывается с предупреждением.

**Создание папок:**
- `__ModsyncManager_Output/MO2/mods/` — всегда.
- `__ModsyncManager_Output/MO2/` (без `mods/`) — только если unmatched
  extensions непусты.
- `__ModsyncManager_Output/Stock Game/` — только если unmatched extras
  непусты.

### Формат modsyncmanager-nxm-handler.log

Файл диагностики `ModsyncManager.NxmHandler.exe`.

**Расположение:**
`%LOCALAPPDATA%\ModsyncManager\logs\modsyncmanager-nxm-handler.log`.

**Запись:** append, без ротации. UTF-8 без BOM, CRLF.

**Формат строки:** `[yyyy-MM-dd HH:mm:ss.fff] message`.

**События:** `argv`, `parsed`, `argv invalid`, `pipe: OK`,
`pipe: FAILED`, `unexpected error`, `exit`.

**Устойчивость:** `HandlerLogger.Log` глотает ошибки I/O.
Handler не падает из-за лога.

### Формат nxm-handler-backup.json

Файл backup предыдущего `nxm://` handler-а.

**Расположение:** `%LOCALAPPDATA%\ModsyncManager\nxm-handler-backup.json`.

**Формат:** JSON, UTF-8 без BOM, indented.

**Поля:**
- `previousCommand` — значение `(Default)` в `shell\open\command`.
- `previousDefaultValue` — значение `(Default)` в самом ключе `nxm`.
- `previousUrlProtocolValue` — значение `URL Protocol`.
- `backupTimeUtc` — timestamp сохранения.
- `previousHandlerPath` — путь к exe из `previousCommand`.

Все `previous*` могут быть null.

**Жизненный цикл:**
- `ProtocolRegistrar.Register()`: если чужой handler — сохраняет
  backup.
- `ProtocolRegistrar.Restore()`: читает backup, восстанавливает,
  удаляет файл. Если файла нет — просто удаляет нашу регистрацию.

**Класс:** `NxmHandlerBackup`.

### Структура HKCU\Software\Classes\nxm

**Только `HKCU`**, не `HKLM`.

```
HKEY_CURRENT_USER\Software\Classes\nxm
(Default) REG_SZ URL:NXM Protocol
URL Protocol REG_SZ (пустая строка)

HKEY_CURRENT_USER\Software\Classes\nxm\shell\open\command
(Default) REG_SZ "<path>\ModsyncManager.NxmHandler.exe" "%1"
```


`%1` — плейсхолдер Windows. Кавычки вокруг `%1` обязательны.

**Определение состояния (`ProtocolRegistrationState`):**
- `NotRegistered` — ключ отсутствует.
- `RegisteredToUs` — `command` указывает на наш handler
  (case-insensitive).
- `RegisteredToOther` — `command` указывает на другое приложение.
- `Unknown` — ошибка чтения реестра.

**Backup:** см. §6.9.

**Регистрация — постоянная.** Один раз с согласия.

### Логи GUI

Файловые логи GUI пишутся в `%LOCALAPPDATA%\ModsyncManager\logs\`
через `Modsync.Logging`.

`ModsyncManager.Gui.csproj` — `<OutputType>WinExe</OutputType>`. GUI
запускается без консольного окна. `AddSimpleConsole` в GUI
не регистрируется — stdout пустой. Логи — только в файл.

**Файл:** `modsyncmanager-yyyy-MM-dd.log`. Дневная ротация.

**Ротация по размеру:** при 10 МБ — `modsyncmanager-yyyy-MM-dd.1.log`
(перезапись). Один бэкап на день.

**Retention:** 14 дней.

**Формат строки:**
`[yyyy-MM-dd HH:mm:ss.fff] [Level] Category: message`

**Кодировка:** UTF-8 без BOM, CRLF. `FileShare.ReadWrite`.

**Настройки — хардкод.** Retention, MaxFileSize — константы в
`FileLoggerOptions`.

**Сбои I/O молча игнорируются.**

**Открыть папку с логами:** кнопка «Open logs folder» на экране
Logs (DevMode).

---

## Идентификация архивов

Канонический id:

- **`nexus_{game_domain}_{modId}_{fileId}`** — для Nexus-архивов.
  Пример: `nexus_skyrimspecialedition_3863_1000172397`.
- **`local_{slug}`** — для архивов без `.meta`. Slug от имени
  файла без расширения.

**Slug (`Slug.From`):**
- ASCII-only, lowercase.
- Разделитель — дефис.
- Обрезка до 80 символов.
- `.7z` отрезается до slug-ификации.

**Дубликаты:** `IndexArchivesStep` бросает `InvalidOperationException`
при дубликате id.

**`archives[]` vs `mo2.archive`:** MO2-архив никогда не попадает
в `manifest.archives[]`. Он живёт в `manifest.mo2.archive`.

**BSA/BA2.** `.bsa`/`.ba2` считаются архивами
(`ArchiveExtensions.IsArchive`), но на практике они всегда упакованы
внутри `.7z`/`.zip`. ModsyncManager не разбирает `.bsa`/`.ba2` на
содержимое — принципиальное отличие от Wabbajack.

---

## Nexus game domain

`meta.game` — Nexus game domain, не человекочитаемое имя игры.

Примеры: `skyrimspecialedition`, `fallout4`, `starfield`, `skyrim`.

Используется в:
- `ArchiveId.FromNexus(game, modId, fileId)` — game domain попадает
  в id.
- `NexusSourceRef.Game` — при скачивании через Nexus API.
- `manifest.meta.game` — для installer-а (диагностика).

При смене игры — `meta.game` меняется, id архивов становятся
другими.

---

## Формат файлов MO2

**`modlist.txt`** (`profiles/<Name>/modlist.txt`):
- Строка: `+Name` (enabled) или `-Name` (disabled).
- Комментарии `#...` игнорируются. Но сепараторы `-#` — данные,
  не комментарии: `ModlistReader` включает их в `Entries` с
  `Enabled = false`.
- UTF-8 с BOM, CRLF.
- Порядок строк сохраняется.
- Заголовок пишется `ModlistWriter`.

**`plugins.txt`** (`profiles/<Name>/plugins.txt`):
- Строка: `*Name.esp` (enabled) или `Name.esp` (disabled).
- Комментарии `#...` игнорируются.
- UTF-8 с BOM, CRLF.
- Заголовок пишется `PluginsWriter`.

**`loadorder.txt`** (`profiles/<Name>/loadorder.txt`):
- Строка: имя плагина.
- Порядок = порядок загрузки.
- UTF-8 с BOM, CRLF.
- Заголовок пишется `LoadorderWriter`.

**Сепараторы** — `#...` в `modlist.txt`. Packer сохраняет;
installer пропускает; `RegenerateProfileStep` пишет.

---

## Пайплайн: создание сборки

### Этап 1.1. Подготовка окружения

Автор:
- Ставит портативный MO2 2.5.2.
- Устанавливает моды через MO2 любым способом.
- Кладёт архивы модов в `MO2/downloads/`.
- Устанавливает плагины MO2 в `MO2/plugins/`, `MO2/tools/`.
- Устанавливает extras в `Stock Game/`.
- Настраивает профиль.

### Этап 1.2. Конфиг и инстанс

Автор кладёт `modsyncmanager-pack.json` рядом с инстансом MO2.
`instance.path` — относительный путь к корню инстанса.

### Этап 1.3. Запуск packer-а

Packer запускается через `PackPipeline.ExecuteAsync`. В GUI —
экран Pack (`PackVM` → `PackRunner` → `PackPipeline`).

**Pipeline (14 шагов, включая `WriteUnmatchedExtensionsExtras`):**

1. `ReadConfigStep` — читает `modsyncmanager-pack.json`.
2. `ReadInstanceStep` — читает `modlist.txt`, `plugins.txt`,
   `loadorder.txt`. Возвращает `InstanceSnapshot`.
3. `IndexArchivesStep` — индексирует `MO2/downloads/`, определяет
   источник через `.meta` или `archiveSources[]`. Возвращает
   `ArchiveIndex`.
4. `ScanModsStep` — сканирует моды (кроме `[NoDelete]`).
   Возвращает `ModScanResult`.
5. `ScanExtensionsStep` — сканирует `config.Mo2.Extensions[]`.
   `EntryScanResult`.
6. `ScanExtrasStep` — симметричен, для `config.StockGame.Extras[]`.
7. `BuildArchiveMatcher` — `ArchiveMatcher.BuildAsync(ct, progress)` —
   один раз на pipeline. Репортит `IProgress<(int, int)>` после
   каждого архива. `PackPipeline` оборачивает это в
   `StepProgress.Detail` формата `Building index: N / M`
   (аналогично `SyncArchives` и `SyncMods` в installer-е).
8. `MatchStep` — сопоставляет файлы модов с архивами. Unmatched
   модов → `__ModsyncManager_Output/MO2/mods/<ModName>/<path>`.
   `meta.ini` мода → `ModMetas`. Возвращает `MatchResult`.
9. `MatchExtensionsStep` — сопоставляет extensions. Принимает
   общий `ArchiveMatcher` через `Input`. Возвращает
   `MatchEntriesResult`. Unmatched **не пишет**.
10. `MatchExtrasStep` — симметричен.
11. `WriteUnmatchedExtensionsExtras` — пишет unmatched в
    `__ModsyncManager_Output/MO2/<path>` и
    `__ModsyncManager_Output/Stock Game/<path>` (плоско). Перед
    записью чистит соответствующие корни.
12. `BuildManifestStep` — собирает `ModlistManifest`.
13. `ValidateManifestStep` — валидирует.
14. `WriteManifestStep` — пишет `modlist.json` в
    `__ModsyncManager_Output/`.

**`ArchiveMatcher`:**
- Один экземпляр создаётся в `PackPipeline.ExecuteAsync` перед
  `MatchExtensionsStep`, `BuildAsync(ct)` вызывается один раз.
- Передаётся в `MatchStep`, `MatchExtensionsStep`,
  `MatchExtrasStep` через `Input.Matcher` (`internal`).
- Детерминирован: при дубликатах `(hash, path)` или `hash` между
  архивами побеждает минимальный `archiveId` (Ordinal).
- Отмена: `BuildAsync` пробрасывает `OperationCanceledException`.
  Прочие ошибки — skip с логированием.

### 10.1. Pack без config

Помимо обычного пути (готовый `modsyncmanager-pack.json`), Packer
поддерживает сценарий «Pack без config»: пользователь заполняет
форму в GUI, config генерируется программно.

**Форма «Create Pack Config»** (встроена в экран Pack):

- **Instance folder** — выбор папки инстанса (через `IFilePickerService`).
- **Metadata** — `Name`, `Version`, `Author`, `Game`, `GameVersion` (все поля обязательны, валидация через `NameValidator` / `SemverValidator`).
- **MO2 profile** — ComboBox реальных профилей из `MO2/profiles/`. Если профилей нет — поле пустое, используется `Default`.
- **MO2 archive** — секция с хардкодом: MO2 2.5.2, официальный GitHub-релиз, `xxh64:E574E05EB6C470AD`. Read-only.
- **Extensions / Extras** — опциональные списки путей. Формат — строки. Кнопка «+ Add».
- **Archives without metadata** — список non-nexus архивов из `downloads/` (без валидного `.meta`). Для каждого — поля `URL` + `Hash` (поддерживается несколько зеркал через «+ Add mirror»). Кнопки **Skip / Unskip** — если пользователь не может указать источник.

**Что происходит при нажатии «Create Config & Pack»:**

1. Форма собирает `PackConfigBuilderInput` (все поля + отфильтрованные non-skipped архивы).
2. `PackRunner.RunFromConfigBuilderAsync` собирает `PackConfig` через `PackConfigBuilder.Build`.
3. Сериализует через `PackConfigJson.Serialize` в временный файл в **корне инстанса** (`modsyncmanager-pack.temp.json`) — не в `%TEMP%`, потому что `config.instance.path == "."` и pipeline ищет `MO2/`, `downloads/` относительно папки config.
4. Запускает обычный `PackPipeline.ExecuteAsync`.
5. Удаляет temp-файл в `finally`.

**Хардкод `mo2`-секции** (стандарт для генерируемых манифестов):

| Поле | Значение |
|---|---|
| `mo2.version` | `2.5.2` |
| `mo2.archive` | `Mod.Organizer-2.5.2.7z` |
| `mo2.source.type` | `mirror` |
| `mo2.source.url` | `https://github.com/ModOrganizer2/modorganizer/releases/download/v2.5.2/Mod.Organizer-2.5.2.7z` |
| `mo2.source.hash` | `xxh64:E574E05EB6C470AD` |

**`Save as template…`** — сохранение формы в `modsyncmanager-pack.json` (через `IFilePickerService.SaveFileAsync`). Если пользователь передумал сохранять — форма остаётся открытой.

### Этап 1.4. Итог

- `__ModsyncManager_Output/modlist.json` — манифест.
- `__ModsyncManager_Output/MO2/mods/<ModName>/<path>` — unmatched
  модов.
- `__ModsyncManager_Output/MO2/<path>` — unmatched extensions.
- `__ModsyncManager_Output/Stock Game/<path>` — unmatched extras.

### Этап 1.5. Публикация

Автор публикует `modlist.json`. Зеркала для архивов (mirror) —
опционально.

---

## Пайплайн: установка сборки

### Этап 2.1. Получение манифеста

Пользователь кладёт `modlist.json` куда угодно.

### Этап 2.2. Запуск installer-а

Installer запускается через `InstallPipeline.ExecuteAsync`. В GUI —
экран Install (`InstallVM` → `InstallRunner` → `InstallPipeline`).

**Расположение инстанса:** `<exeDir>/Instances/<normalize(meta.name)>/`.

**Pipeline (11 шагов):**

1. `ReadManifestStep` — читает `modlist.json`, валидирует
   `schemaVersion`.
2. `ResolveTargetStep` — вычисляет `instancePath`, копирует
   манифест в `<instancePath>/modlist.json`.
3. `ValidateTargetStep` — 4 проверки (корень диска, системные
   папки, папка exe, права записи).
4. `BootstrapInstanceStep` — создаёт `MO2/`, `MO2/downloads/`,
   `MO2/mods/`, `MO2/profiles/`, `MO2/plugins/`, `MO2/tools/`,
   `Stock Game/`.
5. `BootstrapMo2Step` — самодостаточный: скачивает MO2-архив
   (если нет по хешу), распаковывает в `MO2/`.
6. `SyncArchivesStep` — сканирует `downloads/` → hash → path,
   для каждого mod-архива скачивает или находит локально.
7. `ExecuteExtensionsStep` — раскладывает `mo2.extensions[]`
   в `MO2/`.
8. `ExecuteExtrasStep` — раскладывает `stockGame.extras[]` в
   `Stock Game/`.
9. `SyncModsStep` — reconcile `mods/`.
10. `GenerateMetaIniStep` — reconcile `meta.ini`.
11. `RegenerateProfileStep` — генерирует `modlist.txt`/`plugins.txt`/
    `loadorder.txt`.

**Разделение ответственности:**
- **MO2-логика** — в `BootstrapMo2Step`.
- **Логика `mods/`** — в `SyncArchivesStep` + `SyncModsStep`.
- **Логика extensions** — в `ExecuteExtensionsStep`.
- **Логика extras** — в `ExecuteExtrasStep`.

### Этап 2.3. Запуск игры

Пользователь:
- Копирует игру в `instance/Stock Game/` (вручную).
- Открывает `instance/MO2/ModOrganizer.exe`.
- Выбирает профиль.
- Запускает игру через SKSE.

**ModsyncManager не работает с игрой.** `Stock Game/` — просто
папка для extras.

---

## Пайплайн: обновление сборки

**Реализовано через GUI (3.9.8).**

1. Пользователь кликает `Update` на карточке.
2. Открывается диалог выбора файла (`IFilePickerService.PickFileAsync`).
3. Если отменил — no-op.
4. Если выбрал — `MainWindowVM` переключается на Install и вызывает
   `InstallVM.PrepareForInstall(<выбранный>, <InstancePath>)`.
5. Пользователь нажимает `Install`.
6. `InstallPipeline`:
   - `ResolveTargetStep.CopyManifestIfNeeded` копирует манифест в
     `<InstancePath>/modlist.json` (перезапись).
   - Дальше — обычный пайплайн.

**Про идемпотентность:** если манифест тот же — install
идемпотентен (всё `Skipped`).

---

## Работа с Nexus Mods

**Реализовано (Фаза 6).**

### Философия

Nexus — один источник (`type: "nexus"`). Способы доступа —
стратегии внутри `NexusDownloader`.

### Что делает

`NexusDownloader : IArchiveDownloader` (`SourceType => "nexus"`):

1. Проверяет аккаунт через `GET /v1/users/validate.json`.
   Результат кешируется на время жизни `NexusClient`.
2. Запрашивает список CDN-ссылок через
   `GET /v1/games/{game}/mods/{modId}/files/{fileId}/download_link.json`.
3. Перебирает ссылки по очереди: первая успешная выигрывает.
4. Возвращает `Stream` с содержимым архива.

Скачивание идёт в `TempFileStream` (не `MemoryStream`).

Retry, `.part`, hash-check — на стороне `ArchiveDownloadHelper`.

### Аутентификация

API-ключ хранится в `%LOCALAPPDATA%\ModsyncManager\nexus.key` —
plaintext, одна строка, UTF-8 без BOM, без trailing newline.
Отправляется как HTTP-заголовок `apikey`.

Дополнительные заголовки:
- `Application-Name: ModsyncManager`
- `Application-Version: 0.1.0`
- `User-Agent: ModsyncManager/0.1.0`

**Управление ключом:**

- **Запись.** `INexusApiKeyProvider.Save(apiKey)` создаёт папку,
  пишет ключ. Обрезает whitespace.
- **Чтение.** `TryGetApiKey()` — читает, обрезает BOM и whitespace,
  берёт первую непустую строку. Никогда не бросает.
- **Удаление.** `Clear()` — удаляет файл. Не бросает, если файла
  нет.
- **Валидация.** `INexusCredentialValidator.ValidateAsync(apiKey, ct)` —
  `GET /v1/users/validate.json`. Возвращает `NexusValidationResult`.

**Обработка ошибок аутентификации:**

`NexusClient` бросает `NexusAuthenticationException`, когда:
- ключа нет,
- ключ отозван (HTTP 401).

HTTP 403 (Premium required) — `InvalidOperationException`.

**Управление ключом в GUI:** секция Nexus в `SettingsView`
(`NexusSettingsVM`).

**v0.2.0+:** DPAPI-шифрование файла ключа, SQLite-хранилище,
OAuth-логин.

### HttpClients

Именованные клиенты:
- `"nexus-api"` — 2 минуты.
- `"nexus"` — 10 минут (CDN).
- `"mirror"` — 10 минут.

### Обработка ошибок

| HTTP | Значение |
|---|---|
| Нет ключа | `NexusAuthenticationException` |
| 401 | `NexusAuthenticationException` |
| 403 | `InvalidOperationException` — нужен Premium |
| 404 | `InvalidOperationException` — мод/файл не найден |
| 429 | `InvalidOperationException` — rate limit |
| Прочее | `HttpRequestException` |

### Что не делаем

- OAuth-логин (только API-key).
- Кеширование download-ссылок.
- Автоматический retry внутри `NexusDownloader`.

---

## 13.1. Free-загрузка через nxm:// handler

**Начиная с 2026-09-29 Free-загрузка с Nexus работает через
зарегистрированный `nxm://` handler, а не через WebView2.**

### Зачем отказались от WebView2

`LaunchingExternalUriScheme` требует Runtime ≥ 1.0.2210.55.
У пользователя — Fixed Version `100.0.1185.36` (2022 год).
Evergreen Bootstrapper падает с `0x8004070c`. Standalone требует
прав администратора. Другие API не работают:
- `WebResourceRequested` покрывает только известные схемы.
- `NavigationStarting` для `nxm://` не срабатывает.

**Решение.** Отказаться от WebView2. Пользователь работает в
своём браузере; `nxm://` регистрируется в HKCU; отдельный exe
принимает URL и передаёт в pipeline.

### Архитектура

1. **`NexusDownloader` видит, что аккаунт не Premium.**
   `NexusClient.IsPremiumAsync` возвращает `false`.

2. **`NexusFreeNxmProvider.RequestNxmUrlAsync(...)`:**
   - Проверяет `IProtocolRegistrar.GetState()`. Если состояние
     не `RegisteredToUs` — бросает `InvalidOperationException`.
     Install прерывается до открытия браузера.
   - Открывает страницу мода через `IUrlOpener.Open`:
     `https://www.nexusmods.com/{game}/mods/{modId}?tab=files&file_id={fileId}&nmm=1`.
     Параметр `&nmm=1` — Nexus открывает в режиме Mod Manager.
   - Логирует подсказку: «In the browser, click "Mod Manager
     Download" (not Slow Download)».
   - Ждёт URL из `INxmUrlReceiver.WaitForUrlAsync` (timeout 5 мин).

3. **Пользователь кликает «Mod Manager Download».** Nexus
   генерирует `nxm://...?key=...&expires=...&user_id=...`.
   Windows запускает `ModsyncManager.NxmHandler.exe`.

4. **Handler получает URL в argv.** См. §13.2 и §6.8.

5. **ModsyncManager принимает URL через `INxmUrlReceiver`.**
   URL попадает в `Channel<string>`, откуда его забирает
   `NexusFreeNxmProvider`.

6. **Если ModsyncManager не запущен** — handler выходит с exit 3.
   **Handler НЕ запускает ModsyncManager.exe.**

7. **`NexusDownloader` парсит nxm://.**
   - `NexusUrlParser.Parse` → `game`, `modId`, `fileId`, `key`,
     `expires`, `userId`.
   - Сверяет `game`/`modId`/`fileId` с запрошенным.
   - Сверяет `userId` с `userId` из `/users/validate.json`.
   - `GET /v1/games/{game}/mods/{modId}/files/{fileId}/download_link.json?key=...&expires=...`.
   - Качает первую успешную CDN-ноду.

### Регистрация handler'а

**Только HKCU.** Структура реестра — см. §6.10.

**Состояния (`ProtocolRegistrationState`)** — см. §6.10.

**Статусы (`ProtocolRegistrationStatus`)** — результат операции:
- `Registered`, `AlreadyRegistered`, `Restored`, `AlreadyRestored`,
  `BackupFailed`, `RegistryWriteFailed`, `RegistryReadFailed`,
  `HandlerNotFound`.

**Register:**
1. `File.Exists(handlerPath)`. Нет — `HandlerNotFound`.
2. Читает текущий `HKCU\Software\Classes\nxm`.
3. Уже наш — `AlreadyRegistered`.
4. Чужой — сохраняет backup. Ошибка — `BackupFailed`.
5. Пишет нашу регистрацию. Ошибка — `RegistryWriteFailed`.

**Restore:**
1. Читает backup-файл.
2. Нет файла — удаляет нашу регистрацию, `AlreadyRestored`.
3. Есть — восстанавливает, удаляет backup-файл, `Restored`.

**Restore — только вручную.** Через кнопку в Settings.

### Очередь URL

`NxmUrlReceiver` держит `Channel<string>` (неограниченный, FIFO).

**Lifecycle в GUI** (`App.axaml.cs`):
- `StartAsync` — в `OnFrameworkInitializationCompleted`,
  fire-and-forget.
- `StopAsync` — в `desktop.ShutdownRequested`, с таймаутом 2 сек.

**Отмена:** `WaitForUrlAsync(ct)` бросает `OperationCanceledException`.

**Очистка:** `TryDrainPendingUrls()` — не блокирующий метод,
выбрасывает все URL из очереди. Вызывается
`NexusFreeNxmProvider.RequestNxmUrlAsync` перед началом ожидания
нового URL. Причина: URL-ы от прошлых сессий install
(пользователь отменил, URL уже пришёл) не должны попасть
в текущий install.

**Ограничения:**
- Очередь неограниченная. Лимит не нужен — накликать 1000 URL
  физически невозможно (один клик — один URL, каждый URL
  забирается сразу после появления).
- Single-instance GUI — есть (mutex).

### Что удалено (Шаг E + Шаг G)

- `Modsync.WebViewHost` (проект).
- `Modsync.Gui.Nexus` (проект).
- `Modsync.Gui.Nexus.Tests` (проект).
- `WebView2Runtime/*`, `WebViewHost/*`, `WebView2RuntimeDialog`.
- `tools/fetch-webview2-runtime.bat`.
- `src/ModsyncManager.Gui/Assets/WebView2/` (130 МБ).
- `Microsoft.Web.WebView2` из `Directory.Packages.props`.
- `ModsyncLauncher`, `IProcessStarter`, `DefaultProcessStarter`.
- `ModsyncLauncherTests`.

---

## 13.2. Handler nxm:// — exit codes и лог

`ModsyncManager.NxmHandler.exe` — системный callback Windows для
`nxm://`. Запускается без консоли, `stderr` пользователь не видит.
Диагностика — `%LOCALAPPDATA%\ModsyncManager\logs\modsyncmanager-nxm-handler.log`
(§6.8).

### Что делает

1. Разбирает argv (`NxmHandlerArgs.TryParse`).
2. Проверяет схему `nxm://`.
3. Подключается к named pipe `\\.\pipe\modsyncmanager-nxm`
   (`PipeClient.TrySendAsync`, timeout 2 секунды).
4. Пишет URL одной строкой, закрывает соединение.
5. Выходит с кодом 0/2/3.

**Handler НЕ запускает ModsyncManager.exe.**

### Exit codes

| Код | Значение |
|---|---|
| `0` | URL успешно передан в pipe. |
| `2` | Невалидный argv. |
| `3` | Pipe недоступен / любая другая ошибка. |

### Логирование

Каждый запуск пишет в
`%LOCALAPPDATA%\ModsyncManager\logs\modsyncmanager-nxm-handler.log`:
`argv`, `parsed`, `pipe: OK` / `pipe: FAILED`, `exit`.

Формат, устойчивость — см. §6.8.

### Что handler НЕ делает

- Не открывает браузер.
- Не стартует ModsyncManager.exe.
- Не проверяет свою регистрацию в реестре.
- Не валидирует `nxm://` через `NexusUrlParser`.
- Не пытается повторно отправить URL.

### Контракт

**Имя pipe** — `NxmPipeName.Value = "modsyncmanager-nxm"` из
`Modsync.Core.Nxm`. Единственный источник правды и для handler-а
(`PipeClient.PipeName`), и для receiver-а (`NxmUrlReceiver`).

---

## Persist кеша хешей

**Реализовано** (2026-09-30, блок 2.1).

### Назначение

ModsyncManager считает `xxHash64` часто: при pack (сканирование
модов, индексация архивов, распаковка), при install (проверка
архивов и файлов модов), при verify. На больших инстансах
(400–600 ГБ) это десятки минут. Кеш сохраняет результаты между
запусками.

### Расположение

`%LOCALAPPDATA%\ModsyncManager\cache.db`

### Схема

```sql
CREATE TABLE file_hashes (
    path      TEXT    PRIMARY KEY,
    length    INTEGER NOT NULL,
    mtime_ms  INTEGER NOT NULL,
    hash      INTEGER NOT NULL
);
```

### Логика

- **L1** — `FileHashCache` (in-memory). Мгновенно.
- **L2** — SQLite. `SELECT` по `path`. Если `length` и `mtime_ms`
  совпали — хеш валиден.
- **Compute** — `XxHash64Value.FromFile`. Запись в L1+L2.

Ключ валидации: `(path, length, mtime_ms)`.

### Где используется

Persist включён в:
- `IndexArchivesStep`.
- `ScanModsStep`, `ScanExtensionsStep`, `ScanExtrasStep`.
- `SyncArchivesStep`, `SyncModsStep`, `BootstrapMo2Step`.
- `VerifyPipeline`.

**`ArchiveMatcher` — без persist.** Сознательно: работает с
temp-папками (пути меняются), плюс манифест должен быть
детерминированным.

### Ошибки

Повреждённый файл БД, locked, нет прав — warning, работа без
persist (L1 продолжает работать). Кеш — оптимизация, не критичный
ресурс.

### Управление

Экран Cache (DevMode) → секция Hash cache → кнопка «Clear hash
cache». Очищает таблицу `file_hashes`. `VACUUM` возвращает место.

### Классы

- `IHashCache` — интерфейс.
- `FileHashCache` — L1 (in-memory).
- `SqliteHashCache` — L1 + L2.

---

## Обработка ошибок

### Матрица packer-а

| Ситуация | Поведение |
|---|---|
| `modsyncmanager-pack.json` не найден | `FileNotFoundException` |
| Невалидный JSON | `InvalidOperationException` («Failed to parse») |
| `meta.name` не проходит `NameValidator` | `InvalidOperationException` |
| `meta.version` не semver | `InvalidOperationException` |
| `instance.path` не проходит `InstancePathValidator` | `InvalidOperationException` |
| `mo2.source` не `MirrorSourceRef` | `InvalidOperationException` |
| `instance.path` не существует | `DirectoryNotFoundException` |
| `MO2/` не существует | `DirectoryNotFoundException` |
| `downloads/` не существует | `DirectoryNotFoundException` |
| `mods/` не существует | `DirectoryNotFoundException` |
| Профиль не найден | `DirectoryNotFoundException` |
| Мод enabled, папки нет | `DirectoryNotFoundException` |
| Мод disabled, папки нет | Warning, пропустить |
| Сепаратор без папки | `LogDebug`, пропустить |
| Дубликат `archiveId` | `InvalidOperationException` |
| `.meta` без `modID`/`fileID` | Warning, fallback на `archiveSources` |
| Архив без `.meta` и без `archiveSources` | `UnresolvedArchive`, warning |
| `mo2.archive` не найден в `downloads/` | Size = 0, hash из `mo2.source.hash` |
| Hash MO2-архива mismatch | `InvalidOperationException` |
| Entry в `mo2.extensions[]` не найден | `FileNotFoundException` |
| Entry в `stockGame.extras[]` не найден | `FileNotFoundException` |
| Файл мода не найден в архивах | Unmatched → `__ModsyncManager_Output` |
| Файл extension/extra не найден | Unmatched → `__ModsyncManager_Output` |
| `FromArchiveDirective.Archive` не существует | `InvalidOperationException` |
| Отмена во время pack | `OperationCanceledException` |

### Матрица installer-а

| Ситуация | Поведение |
|---|---|
| `schemaVersion` не поддерживается | Ошибка |
| `meta.name` не проходит `NameValidator` | Ошибка |
| Target — корень / системная папка / папка exe | Ошибка |
| Нет прав записи | Ошибка |
| Инстанс не существует | Создать |
| Архив есть локально с нужным хешем | Пропустить |
| Архив с другим именем, но тем же хешем | Использовать |
| Архив отсутствует | Скачать по sources |
| Hash не совпадает | Удалить `.part`, следующий источник |
| Все источники провалились | Ошибка |
| Мод `[NoDelete]` | Пропустить |
| Мод-сепаратор (`#...`) | Пропустить |
| `mods[].meta != null` | `MetaIniWriter.WriteFile` |
| `mods[].meta == null`, файл есть | Удалить |
| `mods[].meta == null`, файла нет | Ничего не делать |
| MO2: локальный архив с нужным хешем | Использовать |
| MO2: локальный архив с другим хешем | Перекачать |
| MO2: архива нет | Скачать |
| MO2: hash mismatch после скачивания | `.part` удалён, следующий источник |
| MO2: все источники провалились | Ошибка |
| MO2: распаковка | Всегда, с заменой |
| Отмена во время install / verify | `OperationCanceledException` |
| nexus, ключ отсутствует | `NexusAuthenticationException` |
| nexus, ключ отозван (401) | `NexusAuthenticationException` |
| nexus, аккаунт Premium | `NexusDownloader` → CDN-ноды |
| nexus, аккаунт Free, handler зарегистрирован на нас | `NexusFreeNxmProvider` → браузер → pipe → URL |
| nexus, аккаунт Free, handler НЕ зарегистрирован | `InvalidOperationException` |
| nexus, все CDN-ноды упали | `InvalidOperationException` |

### Матрица Free-загрузки (Nexus)

| Ситуация | Поведение |
|---|---|
| Аккаунт Premium | Обычный Premium-путь |
| Аккаунт Free, handler зарегистрирован | `NexusFreeNxmProvider` открывает браузер, ждёт URL |
| Аккаунт Free, handler НЕ зарегистрирован | `InvalidOperationException` **до** открытия браузера |
| `IUrlOpener.Open` бросил | `InvalidOperationException` из `ShellUrlOpener` |
| Пользователь закрыл браузер, URL не пришёл | Timeout 5 мин → `null` → `InvalidOperationException` |
| Пользователь отменил install во время ожидания URL | `OperationCanceledException` |
| `INxmUrlReceiver.StopAsync` вызван | `WaitForUrlAsync` возвращает `null` |
| URL пришёл, `TryParse` вернул `false` | `InvalidOperationException` |
| URL для другого файла | `InvalidOperationException` |
| URL без `key`/`expires`/`user_id` | `InvalidOperationException` |
| `user_id` в URL ≠ API-ключа | `InvalidOperationException` |
| `/download_link.json` пустой массив | `InvalidOperationException` |
| Все CDN-ноды упали | `InvalidOperationException` |

**Сериализация:** `NexusFreeNxmProvider` использует
`SemaphoreSlim(1,1)`.

### Матрица GUI

| Ситуация | Поведение |
|---|---|
| Install: отмена | `State = Configuration`, `ErrorMessage = "Cancelled."` |
| Install: pipeline бросил | `State = Failure`, `ErrorMessage = ex.Message` |
| Install: успех | `State = Success`, `Summary` заполнен |
| FilePicker: отмена | Path не меняется |
| FilePicker: невалидный путь | `IsValid = false`, `Error` |
| Home: `Open MO2`, `ModOrganizer.exe` нет | `WarningMessage`, launcher не вызывается |
| Home: `Open MO2`, `Process.Start` бросил | `WarningMessage` с текстом |
| Home: `Open MO2`, успех | `WarningMessage = null` |
| Home: `Update`, юзер отменил | no-op |
| Home: `Install` / `Update` | `MainWindowVM.OnInstallRequested` → `NavigateTo(Install)` → `InstallVM.PrepareForInstall` |
| Home: пустой список | Пустой экран, заголовок остаётся |
| Settings: переключение DevMode | `NavigationVM.RebuildItems()` |

### Матрица ModsyncManager.NxmHandler.exe

| Ситуация | Поведение | Exit code |
|---|---|---|
| Норма | URL передан в pipe | `0` |
| `argv` пуст | «expected a single nxm:// URL argument» | `2` |
| URL не начинается с `nxm://` | То же | `2` |
| Pipe недоступен | «ModsyncManager is not running…» | `3` |
| Pipe OK, но запись упала | `pipe: FAILED` в лог | `3` |
| Непредвиденная ошибка | `unexpected error` в лог | `3` |

---

## Технологический стек

| Компонент | Технология |
|---|---|
| Платформа | .NET 8 (net8.0), C# 12 |
| GUI | Avalonia 11.2.1 + CommunityToolkit.Mvvm 8.4.0 |
| Тип выходного файла GUI | `WinExe` (без консольного окна) |
| JSON | System.Text.Json |
| Registry | Microsoft.Win32.Registry (nxm:// handler) |
| IPC | Named pipes (System.IO.Pipes) |
| Хеширование | System.IO.Hashing (xxHash64) |
| Распаковка | 7z.exe + 7z.dll (Assets/7z/) |
| База данных | Microsoft.Data.Sqlite |
| Шифрование | System.Security.Cryptography.ProtectedData (DPAPI, v0.2.0+) |
| DI | Microsoft.Extensions.DependencyInjection |
| Логирование | Microsoft.Extensions.Logging (+ .Console), Modsync.Logging |
| Retry | Polly 8 (в Modsync.Core) |
| HTTP | Microsoft.Extensions.Http |
| Тесты | xUnit + FluentAssertions |
| Целевая ОС | Windows 10 1809+ / Windows 11 |

**Удалено:** SharpCompress (заменён на 7z), Octokit (GitHub
cleanup), Microsoft.Web.WebView2 (отказ от WebView2, 2026-09-29),
`ModsyncLauncher` / `IProcessStarter` / `DefaultProcessStarter`
(handler не стартует GUI), `Modsync.Cli` (2026-10-01),
Spectre.Console (2026-10-01).
