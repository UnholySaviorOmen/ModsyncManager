# ModsyncManager — состояние проекта и план работ

**Обновлено:** 2026-10-05
**Всего тестов:** 1269, 0 failed
**Текущий блок:** все запланированные блоки закрыты
**Следующий блок:** (не определён)

**Спутные документы:**
- `DOC.md` (v6.1) — формальный справочник: форматы, pipeline,
  обработка ошибок, стек.
- `repo-dump.md` — свежий дамп репозитория.

---

## Что это

ModsyncManager — инструмент для создания и установки
воспроизводимых сборок модов для Mod Organizer 2. C#/.NET 8.

Ключевая идея: манифест `modlist.json` — единственный источник
правды. Файлы восстанавливаются по хешам `xxHash64`.
ModsyncManager работает с **результатом** установки, а не с
процессом.

Один GUI (`ModsyncManager.exe`, Avalonia) + один системный callback
(`ModsyncManager.NxmHandler.exe` для `nxm://`). Логика — в
библиотеках (`Modsync.Pack`, `Modsync.Install`).

---

## Как начать работу в новом чате

**Скопируйте в первое сообщение:**

1. **DEEPSEEK.md** (этот файл) — полностью.
2. **DOC.md** — по запросу (справочник, если работа касается
   форматов/pipeline/ошибок).
3. **repo-dump.md** — свежий.

**Первое сообщение — шаблон:**

Продолжаем проект ModsyncManager. Стиль — пошаговые блоки кода
с тестами.

Прикладываю: DEEPSEEK.md, repo-dump.md (свежий).

Текущее состояние: ~1269 тестов, 0 failed. Закрыты: MVP (packer,
installer, verify), Фаза 2 (общие API для GUI), Фаза 6 (Nexus
Premium), Фаза 3 (GUI, шаги 3.1–3.8), Фаза 3.9 (редизайн GUI +
Home-дашборд), Nexus credential UI, nxm:// handler без WebView2,
v0.2.0 (блоки 1.1, 1.2, 2.1, 3.1, 3.2), блоки 36–40 (patch-архив
для unmatched, Pack UX rework, восстановление .meta для архивов,
полировка, Nexus auth pre-flight).

v0.2.0 закрыт 2026-10-01. Отменены: блок 2.2 (глобальный реестр
`archives.db`), Spectre-прогресс и `--verbose` для CLI, механизм
патчей для inline-файлов. Удалён `Modsync.Cli`.

Стиль ответов:

1. Разбор задачи.
2. Полный код файлов с путями.
3. Инструкция по сборке/тестам.
4. Ожидаемый вывод dotnet test.

DEEPSEEK.md — только по запросу.

Не пиши код, пока я не подтвержу готовность.

---

## Стиль работы

- **Файлы давать целиком**, не патчами.
- **Запускать `dotnet test` сразу** после каждого блока.
- **Присылать полный вывод** тестов при падении (текст).
- **DEEPSEEK.md** — обновлять по запросу.
- **Не менять архитектурные решения без обсуждения.**
- **Не отвечать на китайском.**
- **Разбивать крупные блоки на 12.x.y** (или `<Фаза>.<шаг>.<подшаг>`).
- **Не писать код, пока не подтверждена готовность.**

---

## Стек

- **.NET 8**, C# 12.
- **xUnit + FluentAssertions**.
- **System.Text.Json**.
- **System.IO.Hashing (xxHash64)**.
- **Microsoft.Data.Sqlite** (v0.2.0).
- **Microsoft.Win32.Registry** 5.0.0.
- **Modsync.Logging** — свой file-logger.
- **Microsoft.Extensions.*** — DI, Logging, Http.
- **Polly** (в `Modsync.Core`).
- **7z.exe + 7z.dll**.
- **SharpCompress удалён.**
- **Octokit удалён.**
- **Microsoft.Web.WebView2 удалён** (2026-09-29).
- **Spectre.Console удалён** (2026-10-01, вместе с CLI).

### GUI
- **Avalonia** 11.2.1.
- **CommunityToolkit.Mvvm** 8.4.0.
- **Microsoft.Extensions.DependencyInjection**.
- **Microsoft.Extensions.Logging**.
- **AvaloniaUseCompiledBindingsByDefault=true** во всех
  GUI-проектах.

---

## Текущий статус

### Готово

- **Полный pipeline packer-а (14 шагов).**
- **Полный pipeline installer-а (13 шагов).**
- **Verify** (pipeline + tests + GUI).
- **GUI `ModsyncManager.exe`** — Avalonia, экраны Home/Install/
  Pack/Verify/Logs/Cache/Settings. Home — дашборд инстансов.
- **`ModsyncManager.NxmHandler.exe`** — системный callback для
  `nxm://`.
- **Nexus Premium** (Фаза 6).
- **Фаза 2** — общие API для GUI.
- **Фаза 3** — GUI (3.1–3.8).
- **Фаза 3.9** — редизайн GUI + Home-дашборд (3.9.1–3.9.8).
- **Nexus credential UI** (Блоки 1–3: Core + GUI).
- **nxm:// handler без WebView2** (2026-09-29).
- **v0.2.0, блок 1.1** — file-logging (2026-09-30).
- **v0.2.0, блок 1.2** — SettingsStore + Logs (2026-09-30).
- **v0.2.0, блок 2.1** — persist кеша хешей (2026-09-30).
- **v0.2.0, блок 3.1** — прогресс Install/Pack (2026-10-01).
- **v0.2.0, блок 3.2** — UX-ревизия Settings (2026-10-01).

### Дополнительно
- ✅ **Блок 35.1** — `PackConfigBuilder` в `Modsync.Pack` (сервис
  сканирования downloads/ + сборки config из формы).
- ✅ **Блок 35.2** — `CreatePackConfigVM` + `CreatePackConfigView`
  в `Modsync.Gui.Modules/Pack/` (форма «Create Pack Config»).
- ✅ **Блок 35.3** — интеграция формы в `PackVM` / `PackView`
  (embedded, не overlay) + Skip/Unskip для unresolved-архивов +
  прогресс `BuildArchiveMatcher`.
- ✅ `tools/dump-repo.bat` — заголовок `# Firelink` → `# ModsyncManager`.
- ✅ `Directory.Build.props` — снят BOM (иначе MSB4024 в .NET SDK 10).
- ✅ **Блок 36.1** — `PatchArchiveBuilder` в `Modsync.Pack`
  (сборка patch-архива из `__ModsyncManager_Output/`).
- ✅ **Блок 36.2** — `PatchArchiveDialog` (окно) + `PatchArchiveDialogVM`
  в `Modsync.Gui.Modules/Pack/`.
- ✅ **Блок 36.3** — интеграция через `IPatchDialogService`
  (`Modsync.Gui.Shared`) + `AvaloniaPatchDialogService`
  (`ModsyncManager.Gui`).
- ✅ **Блок 37.1** — переименование `CreatePackConfigVM/View`
  → `PackConfigVM/View`. Свойство `PackVM.ConfigVM` (не
  `PackConfigVM`, чтобы не конфликтовать с типом).
- ✅ **Блок 37.2** — форма Pack Config умеет Load/Save as/Pack,
  пустые дефолты, `_loadedConfigPath`, `_orphanedSources`,
  `SavePathDisplay`, `OverwriteWarning`, `HasLoadedFrom` +
  `LoadedFromPath`. `UnresolvedArchiveRowVM.LoadSources` +
  `AddEmptySource`.
- ✅ **Блок 37.3** — UX rework экрана Pack: `FilePickerView`
  для config и кнопка `Pack` удалены; две кнопки
  `Load config…` / `Create config…` открывают embedded-форму
  `PackConfigVM`. `IPackRunner.RunAsync` удалён.
- ✅ **Блок 38.1** — `MetaReader`/`MetaFile` удалены.
  Парсинг `.meta` унифицирован через `MetaIniReader` (тот же
  формат INI: `[General]` + `[installedFiles]`, что и
  `mods/<Name>/meta.ini`).
- ✅ **Блок 38.2** — `ArchiveEntry.Meta` (`ModMeta?`) —
  опциональное поле. Схема манифеста остаётся `1.0.0`,
  поле не пишется в JSON при `null`.
- ✅ **Блок 38.3** — packer: `IndexArchivesStep` заполняет
  `ArchiveEntry.Meta` для nexus-архивов (читает `.meta`
  через `MetaIniReader`, если `ModId`/`FileId` валидны).
- ✅ **Блок 38.4** — installer: `GenerateArchiveMetaStep`
  восстанавливает `downloads/<archive>.meta` для архивов
  с `Meta != null`. Шаг между `SyncArchivesStep` и
  `ExecuteExtensionsStep`. `InstallPipeline` теперь 12 шагов.
  `InstallSummary` расширен (`ArchiveMetaWritten/Skipped`).
- ✅ **Блок 38.5** — verify: `VerifyPipeline.CheckArchiveMeta`
  проверяет `.meta` для архивов и MO2-архива. Если
  `Meta != null` — файл должен быть и совпадать; если
  `Meta == null` — не ошибка (даже если файл есть).
- ✅ **Блок 39.1** — `DOC.md` §6.6: обратная ссылка на §6.5
  (единый INI-формат `.meta` и `meta.ini`).
- ✅ **Блок 39.2** — интеграционный тест
  `PackInstallArchiveMetaTests`: pack → manifest.Meta →
  install → `.meta` восстановлен → verify.
- ✅ **Блок 39.3** — `VerifyPipeline` тест на битый `.meta`
  (файл есть, содержимое не парсится корректно).
- ✅ **Блок 39.4** — caption-подписи под кнопками формы
  `PackConfigView` (`Load config…`, `Save as…`, `Pack`).
- ✅ **Блок 39.5** — `.meta` для MO2-архива в packer-е:
  `Mo2ArchiveBuilder.Build` читает `downloads/<mo2>.meta`
  и заполняет `ArchiveEntry.Meta`.
- ✅ **Блок 39.6** — `ArchiveMetaWritten` в Success-панели
  `InstallView.axaml`.
- ✅ **Блок 40.1** — Nexus auth: не retry-ить `NexusAuthenticationException`.
  `IArchiveDownloader.IsPermanentFailure(Exception)` — default `false`;
  `NexusDownloader` возвращает `true` для `NexusAuthenticationException`.
  `ArchiveDownloadHelper` через Polly `ShouldHandle` пропускает retry
  для постоянных ошибок. `SyncArchivesStep` при провале всех source-ов
  даёт внятное сообщение про Settings → Nexus.
- ✅ **Блок 40.2** — pre-flight проверка Nexus-ключа.
  `PreflightNexusAuthStep` — новый шаг pipeline (2-й по счёту).
  Если в манифесте есть `NexusSourceRef` и ключа нет — падаем сразу,
  с сообщением «Open Settings → Nexus». Installer стал 13 шагов.

### В работе

(нет)

### Не начато

(нет — все запланированные блоки закрыты)

### Вычеркнуто

- **WebView2 как основа Free-загрузки** — 2026-09-29.
- **Фаза 4 — вариации дистрибутивов** — 2026-09-21.
- **E1 (прогон на большом инстансе 4370 модов)** — отменён.
- **Диалог в GUI «ModsyncManager открыл страницу мода»** — 2026-09-30.
- **Глобальный реестр `archives.db`** — 2026-10-01.
- **`Modsync.Cli`** — 2026-10-01.
- **Спектровский прогресс-бар и `--verbose` в CLI** — 2026-10-01.
- **Механизм патчей для inline-файлов** — 2026-10-01.
- **Clean downloads** — 2026-10-01 (ответственность пользователя,
  как в Wabbajack).
- **`modsyncmanager doctor`** — 2026-10-01 (нечего
  диагностировать).
- **Лимит очереди URL в `NxmUrlReceiver`** — 2026-10-01
  (физически невозможно накликать 1000 URL).
- **Single-instance `NxmUrlReceiver`** — 2026-10-01 (mutex
  достаточно).
- **Локализация GUI (ru/en)** — 2026-10-01 (не критично).

---

## Структура репозитория

```
src/
  Modsync.Core/                  <- ядро: модели, JSON, хеширование,
                                    абстракции, ModsyncPaths,
                                    IHashCache / FileHashCache /
                                    SqliteHashCache, NxmPipeName.
  Modsync.Logging/               <- file-logger (ILoggerProvider).
  Modsync.Platform.MO2/          <- чтение/запись MO2-файлов.
  Modsync.Platform.Nexus/        <- NexusClient, NexusDownloader,
                                    INexusApiKeyProvider,
                                    INexusCredentialValidator,
                                    NexusUrlParser,
                                    NexusFreeNxmProvider +
                                    NullNexusFreeNxmProvider,
                                    IProtocolRegistrar,
                                    IRegistryAccessor,
                                    INxmUrlReceiver, IUrlOpener,
                                    NxmHandlerBackup,
                                    AddModsyncNxm().
  Modsync.Pack/                  <- class library: pipeline packer.
  Modsync.Install/               <- class library: pipeline installer
                                    + verify.
  ModsyncManager.NxmHandler/     <- exe: обработчик nxm://-ссылок.
  Modsync.Gui.Shared/            <- MVVM-инфра для GUI. БЕЗ Avalonia.
  Modsync.Gui.Controls/          <- общие Avalonia-контролы.
  Modsync.Gui.Modules/           <- модули Install / Pack / Verify.
  ModsyncManager.Gui/            <- exe -> ModsyncManager.exe.

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

Детали по проектам, интерфейсам, форматам — в `DOC.md`.

## Прогоны на реальных инстансах

**`C:\ModsyncManager\TestInstance\` (19.09.2026):**
- Install: 82 мода, 68 архивов, 82 meta.ini, профиль `Default`.
- Verify: 4337 passed.

**`C:\ModsyncManager\TestInstance2\` (20.09.2026, OmenRim 7):**
- Pack: 82 мода, 4236 matched, 69 архивов, 49/49 extensions, 2/2 extras.
- Install: 82 мода, 1 extension written, 2 extras written, 82 meta.ini.
- Verify: **4338 passed, 0 failed**.

**`TestInstance5` (22.09.2026, Nexus Premium):**
- 4 nexus-архива скачаны через Premium API, включая USSEP (~250 МБ).

**`OmenTest7` через GUI (25.09.2026, 3.8):**
- Pack: 71 мод, 7853 файла, 4389 директив, 58 архивов.
- Install: 71 created, 58 downloaded, 71 meta.ini.
- Verify: **4522 passed, 0 failed**.

**`OmenRim 7` через GUI (05.10.2026, блоки 36–40):**
- Pack: 81 мод, 4792 файла, 4711 matched, 81 meta.ini,
  69 архивов (плюс patch-архив после первого прогона).
- Install: 81 mod, 66 plugins, 146 loadorder, 68 архивов,
  68 `.meta` восстановлено (блок 38), 1 extension, 5 extras.
- Verify: **5095 passed, 0 failed**.
- Pre-flight Nexus auth (блок 40): сработал при отсутствии ключа,
  показал внятную ошибку в GUI.

**Важно:** `install` без явного target создаёт инстанс в
`<exeDir>/Instances/<meta.name>/`, а не рядом с манифестом.

---

## Ключевые архитектурные решения

Актуальные решения, влияющие на код сейчас. Исторические
решения и обоснования — в «Истории изменений документа» внизу.
Детали форматов — в `DOC.md`.

### Общие принципы

1. **Манифест — единственный источник правды.**
2. **Файлы восстанавливаются по хешам** (`xxHash64`).
3. **ModsyncManager работает с результатом, а не с процессом.**
4. **Одна папка `downloads/` для всех архивов.**
5. **Идентификация архивов — канонический id.**
6. **Глобальный реестр `archives.db` отменён** (2026-10-01).
   `cache.db` закрывает реальную потребность.
7. **Ничего не удаляем** из `downloads/`.
8. **Директивы выполняются последовательно.** `lastWins`.
9. **`[NoDelete]` в имени папки** защищает пользовательские моды.
10. **BSA/BA2 — единые файлы.**
11. **Никаких исполняемых скриптов.**
12. **Installer идемпотентен.**
13. **Installer не работает с игрой.** `Stock Game/` — просто папка.
14. **Шаги pipeline изолированы.** Pipeline — единственный
    оркестратор.
15. **Nexus — один источник, несколько стратегий доступа.**
16. **Параллелизм на уровне pipeline.**
17. **Кеш хешей обязателен** (`IHashCache`: L1 in-memory + L2 SQLite).
18. **Манифест самодостаточен.**
19. **Unmatched → `__ModsyncManager_Output`.**

### Packer

- **`meta.game` = Nexus game domain.**
- **`instance.path`** — относительный.
- **`mo2.profile`** — обязательный.
- **`mo2.source` — обязательно `MirrorSourceRef`.**
- **`mo2.archive.size/hash` — из `mo2.source.hash`.**
- **MO2-архив НЕ попадает в `manifest.Archives[]`.**
- **`mo2.extensions`** — от `MO2/`. **`stockGame.extras`** —
  от `Stock Game/`.
- **`.meta`** — Nexus-формат. Читается через `MetaIniReader`
  (тот же парсер, что для `mods/<Name>/meta.ini`).
- **Канонический id:** `nexus_...` / `local_{slug}`.
- **`archiveSources`** вместо `mirrors`.
- **Slug** — ASCII-only. `Slug.FromFileName` отрезает последнее
  расширение до slug-ификации.
- **Semver** — регулярка semver.org.
- **`Pack*`-модели** — `record`.
- **`ValidationResult`** — накапливает ошибки.
- **Trailing slash** разрешён.
- **Зарезервированные имена Windows** запрещены.
- **`ModlistReader`** читает все строки. `[NoDelete]` — через
  `Contains`.
- **Unmatched модов** → `__ModsyncManager_Output/MO2/mods/<ModName>/<path>`.
- **Unmatched extensions** → `__ModsyncManager_Output/MO2/<path>` (плоско).
- **Unmatched extras** → `__ModsyncManager_Output/Stock Game/<path>`.
- **`PackPipeline`** перед write unmatched чистит соответствующие
  корни (`MO2/` кроме `mods/`; `Stock Game/` целиком).
- **`ArchiveMatcher`** — единый экземпляр на pipeline, `BuildAsync`
  вызывается один раз.
- **`ArchiveMatcher.BuildAsync`** — async, отмена пробрасывается.
- **`MatchStep.Input.Matcher`** — `internal`, не `required`.
- **`ArchiveEntry.Meta`** — `ModMeta?`, заполняется packer-ом
  для nexus-архивов (если рядом есть валидный `.meta`).
- **`Mo2ArchiveBuilder.Build`** читает `downloads/<mo2>.meta`
  для MO2-архива (симметрично `IndexArchivesStep`).

### Installer

- **Инстансы:** `<exeDir>/Instances/<normalize(meta.name)>/`.
- **Копирование манифеста** — `ResolveTargetStep`.
- **`ValidateTargetStep`** — 4 проверки.
- **`[NoDelete]`** — уважаем.
- **`BootstrapInstanceStep`** — создаёт все папки.
- **`IArchiveDownloader`** — абстракция. `DownloaderRegistry` —
  map sourceType → downloader.
- **Hash — источник правды.** Проверка после скачивания обязательна.
- **Скачивание в `.part`**, `File.Move` после проверки.
- **3 попытки + Polly backoff (2 сек).** «Постоянные» ошибки
  (`IsPermanentFailure == true`) не retry-аются.
- **`NexusAuthenticationException`** — постоянная ошибка.
  Retry бессмыслен.
- **`PreflightNexusAuthStep`** — второй шаг pipeline. Проверяет
  наличие ключа до bootstrap-а.
- **`SyncModsStep`** — reconcile `mods/`. Только `FromArchive`.
- **`TempWorkspace` на мод.**
- **Файлы, которых нет в директивах, — удаляются** (recreate).
- **`GenerateMetaIniStep`:** reconcile `meta.ini`.
- **`GenerateArchiveMetaStep`:** reconcile `downloads/<archive>.meta`.
  Пишет, если `ArchiveEntry.Meta != null`. Не удаляет, если
  `Meta == null`.
- **`RegenerateProfileStep`:** сортировка по `Order` ascending,
  **без `Reverse()`**.
- **Порядок pipeline:** ReadManifest → PreflightNexusAuth →
  ResolveTarget → ValidateTarget → BootstrapInstance → BootstrapMo2
  → SyncArchives → GenerateArchiveMeta → ExecuteExtensions →
  ExecuteExtras → SyncMods → GenerateMetaIni → RegenerateProfile.
- **`InstallPipeline.BuildArchivesById`** — включая MO2-архив.
- **`ExecuteExtensionsStep`/`ExecuteExtrasStep` — Skipped**, если
  файлы уже на месте.

### Verify

- **Verify — read-only.**
- **Изоляция (вариант A).**
- **`VerifyPipeline.Execute` — синхронный.**
- **`VerifyContext`** — единый контекст. **`VerifyReport`** —
  `Checks`, `IsOk`, `PassedCount`, `FailedCount`.
- **Регенерация modlist.txt / plugins.txt / loadorder.txt в память.**
- **Сравнение `meta.ini`** — семантическое.
- **Verify проверяет extensions/extras.**
- **Verify проверяет `.meta` архивов** (`CheckArchiveMeta`):
  если `ArchiveEntry.Meta != null` — файл должен быть и совпадать;
  если `Meta == null` — не ошибка.
- **`CheckMod` делает `yield break`** при отсутствии папки мода.

### Cancellation

- **`CancellationHelper.IsCancellation`** в `Modsync.Core`.
- **`catch (Exception ex) when (CancellationHelper.IsCancellation(ex))`**
  → `State = Configuration`, `ErrorMessage = "Cancelled."`.

### Фаза 2 — Общие API

- **`StepProgress` — общий тип в `Modsync.Core.Progress`.**
  `Detail` — `string?`, опциональный.
- **`IProgress<StepProgress>? progress = null` — опциональный
  последний параметр** в `ExecuteAsync`.
- **`PackPipeline.Input` / `InstallPipeline.Input` введены.**
- **`PackInputFactory` / `InstallInputFactory`** — статические.
- **`PackSummary` / `InstallSummary`** — `sealed record` только
  из примитивов. `PackSummaryBuilder` / `InstallSummaryBuilder` —
  статические.
- **`ParallelOptions` остаётся в DI.**

### Фаза 6 — Nexus Premium

- **`NexusDownloader` перебирает CDN-ноды внутри `DownloadAsync`.**
- **`NexusClient` и `NexusDownloader` используют разные именованные
  `HttpClient`-ы.**
- **Nexus API-ключ — plaintext-файл
  `%LOCALAPPDATA%\ModsyncManager\nexus.key`.**
- **Downloader-ы принимают `IHttpClientFactory`.**
- **Скачивание идёт в `TempFileStream`, а не в `MemoryStream`.**
- **`NexusClient.IsPremiumAsync` кешируется на время жизни клиента.**
- **`NexusClient` использует `NexusAuthenticationException` для 401
  и «нет ключа», `InvalidOperationException` — для 403/404/429.**

### GUI

- **Стек:** Avalonia 11.2.1 + CommunityToolkit.Mvvm 8.4.0.
- **`<AssemblyName>ModsyncManager</AssemblyName>`** у
  `ModsyncManager.Gui.csproj`.
- **`ViewLocator`** — перебор по всем загруженным сборкам (решение
  №179).
- **Навигация** — прямой `MainWindowVM.NavigateTo(ScreenType)`.
- **Lazy-резолв панелей через `IScreenFactory`.**
- **`IFilePickerService` в Shared, `AvaloniaFilePickerService` в Gui.**
- **`Modsync.Gui.Shared` — без Avalonia.**
- **`Modsync.Gui.Controls` — отдельный проект для общих контролов.**
- **VM в GUI-модулях зависят от pipeline только через `IRunner`.**
- **`IUiDispatcher` — абстракция UI-диспетчера.**
- **`ObservableLogSink` маршалит мутации через `IUiDispatcher`.**
- **`VerifyPipeline.Execute` синхронный, `IVerifyRunner.RunAsync`
  асинхронный.**
- **`VerifyVM.Rows` + чекбокс `ShowAllChecks` заменяют CLI `--verbose`.**
- **`Directory.Build.props` в корне репо — единый источник версии.**
- **Иконка GUI — `src/ModsyncManager.Gui/Assets/app.ico`.**
- **`tools/build-release.bat` — релизный скрипт.** Публикует два
  exe: `ModsyncManager.exe`, `ModsyncManager.NxmHandler.exe`.
- **`PackConfigVM`** — форма Pack Config. `LoadConfigFileCommand`,
  `SaveAsCommand`, `PackCommand`. `_loadedConfigPath`,
  `_orphanedSources`, `SavePathDisplay`, `OverwriteWarning`,
  `HasLoadedFrom`, `LoadedFromPath`.
- **`PackVM.ConfigVM`** (не `PackConfigVM` — CS0542).
- **Экран Pack** — две кнопки `Load config…` / `Create config…`
  открывают embedded-форму. `FilePickerView` для config и
  кнопка `Pack` с главного экрана удалены (37.3).
- **`PatchArchiveDialog`** — модальное окно после pack, если
  `UnmatchedFiles > 0`. Три кнопки: Ignore / Open output folder /
  Create patch archive.
- **`IPatchDialogService` + `AvaloniaPatchDialogService`** —
  абстракция модального диалога.

### Фаза 3.9 — Дизайн

- **Палитра ModsyncManager в `Application.Resources` (App.axaml).**
  Только тёмная тема. Шрифт Inter.
- **Классы `TextBlock`: `.h1`, `.h2`, `.subtitle`, `.caption`,
  `.muted`.**
- **Все кнопки — secondary.** Класс `accent` не используется.
- **`ScreenIconConverter` живёт в `ModsyncManager.Gui`**
  (namespace-резолвинг в XAML требует той же assembly).
- **Logs — отдельный экран.** Settings — отдельный экран.
  Cache — отдельный DevMode-экран.
- **Sidebar без шапки.**
- **Автоочистка лога убрана.**
- **Home переименован в Dashboard (UI).**
- **Карточка Dashboard — минималистичная:**
  `Install` → `Update` → `Open MO2`, `WarningMessage` inline.
- **`MainWindowVM.NavigateTo(Home)` в конструкторе.**
- **Thumbnail-карточка отменена.**

### Nexus credential UI

- **`INexusApiKeyProvider` умеет Save / Clear.**
- **`NexusAuthenticationException` — специфичное исключение.**
- **`INexusCredentialValidator` — валидация ключа до сохранения.**
- **Ключ маскируется при показе** (`abcd...wxyz`).
- **HTTP-логи HttpClient приглушены.**
- **`Modsync.Gui.Shared` ссылается на `Modsync.Platform.Nexus`.**
- **Секция Nexus — часть `SettingsView`.**
- **`NexusSettingsVM.RefreshAsync` вызывается из View при Loaded.**
- **Поле ввода ключа — `TextBox` с `PasswordChar="•"`.**

### Nexus Free Download — nxm:// handler

- **Отказ от WebView2.** Пользователь работает в своём браузере.
- **`nxm://` регистрируется в `HKCU\Software\Classes\nxm`.**
- **Backup предыдущего handler-а обязателен.**
- **Отдельный exe `ModsyncManager.NxmHandler.exe`.**
- **IPC через named pipe `\\.\pipe\modsyncmanager-nxm`** (константа
  `NxmPipeName.Value` в `Modsync.Core.Nxm`).
- **Постоянная регистрация, не временная.**
- **Handler НЕ стартует ModsyncManager.exe** (принципиально).
- **`NexusFreeNxmProvider`** — открывает браузер, ждёт URL.
  `SemaphoreSlim(1,1)`, timeout 5 минут.
- **`Channel<string>` неограниченного размера.**
- **`AddModsyncNxm()` — DI-extension.** `services.Replace` для
  `INexusFreeNxmProvider`.
- **`&nmm=1` в URL страницы мода** — Nexus открывает в режиме
  Mod Manager.
- **`IRegistryAccessor` регистрируется в DI.** `WindowsRegistryAccessor`
  или `NullRegistryAccessor`.
- **`IProtocolRegistrar` регистрируется через фабрику**
  (короткий конструктор).
- **`NxmUrlReceiver` реализует `IDisposable` и `IAsyncDisposable`.**
- **`NullRegistryAccessor` для не-Windows.**
- **Логи handler-а** — `%LOCALAPPDATA%\ModsyncManager\logs\modsyncmanager-nxm-handler.log`.
- **Handler не зарегистрирован** → `NexusFreeNxmProvider` бросает
  `InvalidOperationException`.
- **Лог-подсказка** «In the browser, click "Mod Manager Download"».

### v0.2.0 — Техдолг

- **Single-instance GUI через `Mutex`.**
- **`MessageBoxW` через P/Invoke — единственный нативный вызов.**
- **`NxmUrlReceiver` принимает имя pipe в конструкторе.**
- **File-logging — свой проект `Modsync.Logging`.**
  `FileLoggerProvider` требует `Func<DateTimeOffset> clock`.
- **Формат времени в файле и в имени — по одной зоне.**
- **`ModsyncPaths` — единый источник правды для путей.**
  Корень: `%LOCALAPPDATA%\ModsyncManager\`.
- **`Settings` — mutable POCO, не record.**
- **Настройки логов — хардкод** (`RetentionDays` = 14,
  `MaxFileSizeBytes` = 10 МБ).
- **`SettingsStore` — атомарная запись** (temp + Move).
- **Ротация по размеру — `.1.log` (перезапись).**
- **`IHashCache`** — `FileHashCache` (L1) + `SqliteHashCache` (L1+L2).
- **SQLite `file_hashes`** в `%LOCALAPPDATA%\ModsyncManager\cache.db`.
  `PRAGMA user_version`, WAL, `busy_timeout`.
- **`hash` — SQLite INTEGER (signed 64-bit).**
- **`ArchiveMatcher` — без persist.** Сознательно (детерминизм
  манифеста + temp-пути).
- **`[ObservableProperty]` не используем в `partial`-классах
  с явными свойствами.**
- **`StepProgress.Detail` — `string?`.**
- **`SyncArchivesStep.Input.DetailProgress`** и
  **`SyncModsStep.Input.DetailProgress`** — `IProgress<(int, int)>?`.
- **`InstallPipeline`** создаёт адаптер `(int, int)` → `Detail`.
- **UI: `Step X of Y · Detail ⟳` — одна строка.**
- **Logs — только экран в DevMode.** «Open logs folder» — в toolbar.
- **Cache — отдельный экран в DevMode.**
- **`HashCacheSettingsVM` удалён,** содержимое в `CacheVM`.
- **`ScreenIconConverter` перенесён** из `Modsync.Gui.Controls`
  в `ModsyncManager.Gui`.

---

## План работ

### MVP ✅ ЗАКРЫТА
### Фаза 1 — Единый CLI ❌ ОТМЕНЕНА (2026-10-01)
### Фаза 2 — Общие API ✅ ЗАКРЫТА
### Фаза 6 — Nexus Premium ✅ ЗАКРЫТА
### Фаза 3 — GUI (Avalonia) ✅ ЗАКРЫТА
### Nexus Free Download — nxm:// handler ✅ ЗАКРЫТА
### v0.2.0 — техдолг ✅ ЗАКРЫТА
### Переименование Firelink → ModsyncManager ✅ ЗАКРЫТА (2026-10-02)

- ✅ **Блок 1.1** — file-logging.
- ✅ **Блок 1.2** — SettingsStore + Logs.
- ✅ **Блок 2.1** — persist кеша хешей (SQLite).
- ✅ **Блок 3.1** — прогресс Install/Pack.
- ✅ **Блок 3.2** — UX-ревизия Settings.
- ❌ **Блок 2.2** — глобальный реестр `archives.db` — отменён.
- ❌ Прогресс-бар Spectre — отменён (вместе с CLI).
- ❌ `--verbose` флаг для CLI — отменён.
- ❌ Механизм патчей для inline-файлов — отменён.

### v0.3.0 (не начато, кандидаты отменены)

Все кандидаты v0.3.0 отменены 2026-10-01 (см. «Вычеркнуто»).
Следующий блок — не определён.

Что уже работает для нескольких игр: `meta.game` — Nexus domain,
поддерживается для любой игры на Nexus (Skyrim SE, Fallout 4,
Cyberpunk 2077, Starfield, Baldur's Gate 3). Ограничение —
конвенции `Stock Game/`, `extras` заточены под Skyrim-семейство;
для других игр возможны иные конвенции. Если появится
реальный сценарий — вернёмся.

---

## Ключевые принципы рефакторинга

- Никаких больших изменений за один шаг.
- Тесты — зелёные на каждом шаге.
- Ручной прогон на OmenRim 7 после каждой фазы.
- Никаких изменений в pipeline, шагах, моделях без причины.
- `TryAddSingleton` в DI-extensions.
- Никаких `Process.Start` для внутренних вызовов.
- GUI — отдельные проекты, ссылаются на pipeline. Обратных
  ссылок нет.
- GUI — единственный пользовательский интерфейс. Pipeline —
  библиотека, тестируется без GUI.
- `Modsync.Gui.Shared` — **без Avalonia**.
- VM в GUI-модулях не зависят от pipeline напрямую — только
  через `IRunner` интерфейсы.

## Что НЕ делать

- Не делать «единый exe через `Process.Start` дочерних процессов».
- Не выносить presentation в pipeline.
- Не делать GUI до Фазы 2 (общие API).
- Не трогать `Modsync.Core`, `Modsync.Platform.*` без причины.
- Не использовать ReactiveUI/DynamicData/MessageBus.
- Не использовать `Avalonia` в `Modsync.Gui.Shared`.
- Не давать `Modsync.Gui.Modules` обратных ссылок на
  `ModsyncManager.Gui`.
- Не делать UI-настроек логов (retention, размер) — хардкод.
- **Не возвращать CLI.**
- **Не вводить `archives.db`.**
- **Не делать sharing архивов между инстансами.**
- **Не делать кеш содержимого архивов (`archive_files`).**
- **Не делать автопатчи / autoPack / inlinePatterns.**
- **Не использовать `Grid.ColumnSpacing` / `Grid.RowSpacing`** —
  этих свойств нет в Avalonia 11.

---

## Грабли и подводные камни

Короткий список симптомов и решений. Детали форматов и матрицы
ошибок — в `DOC.md`.

### Про .NET SDK и `.props`

**BOM в `.props` / `.targets` / `.csproj` ломает .NET SDK 10.**
`MSB4024: Data at the root level is invalid. Line 1, position 1.`
`Directory.Build.props` и `Directory.Packages.props` — **без BOM**.
Проверка: `Format-Hex <path> -Count 8`. Ожидание: `3C 50 72 6F 6A`.

### Про GUI

**`<AssemblyName>ModsyncManager</AssemblyName>` (решение 169).**
Ломает наивный `asm.GetName().Name + ".Views." + shortName` в
`ViewLocator`. Правильный алгоритм — перебор `asm.GetTypes()`.

**`Grid.ColumnSpacing`/`RowSpacing`.** В Avalonia 11.x у `Grid`
нет этих свойств.

**`Color` — неоднозначность.** Есть в `System.Drawing` и в
`Avalonia.Media`. Alias `using AvaloniaColor = Avalonia.Media.Color;`.

**`[RelayCommand]` и публичность.** `private void Clear()`
генерирует `ClearCommand`, но не публичный `Clear()`.

**`ObservableLoggerProvider` и `AddLogging`.** Через
`services.AddSingleton<ILoggerProvider>(...)`.

**`ObservableCollection<T>` в VM.** Мутации из не-UI-потока →
маршалинг через `IUiDispatcher`.

**`Task.Run` в `VerifyRunner`.** `VerifyPipeline.Execute` —
синхронный.

**Производные свойства в CommunityToolkit.** `[ObservableProperty]`
не уведомляет об изменениях computed properties. Ручной
`OnPropertyChanged(nameof(X))`.

**`internal` между проектами.** `internal` виден только внутри
одной сборки. Симптом: `CS0122` на строке регистрации в DI.
Решение: положить файл физически в ту сборку, где он используется.

**`Update` vs `Install` на карточке.** Оба ведут в
`InstallVM.PrepareForInstall(manifestPath, targetPath)`. Installer
сам копирует manifest в target.

**`FakeScreenFactory` в `Modsync.Gui.Shared.Tests`.** Не ссылается
на `Modsync.Gui.Modules` (Avalonia). Фабрика умеет только Home.

**`SettingsVM` — новые зависимости.** Все места с `AddGuiShared()`
должны регистрировать `IProtocolRegistrar`.

**`RefreshAsync` и `ErrorMessage`.** Публичный `RefreshAsync`
**сбрасывает** `ErrorMessage`. Приватный `RefreshStateCore` —
**не трогает**. `finally` в Register/Restore → `RefreshStateCore`.

**`PackVM.ConfigVM`.** Свойство называется `ConfigVM`, не
`PackConfigVM` — иначе конфликт с именем типа (CS0542).

### Про nxm:// handler

**DI и `ProtocolRegistrar`.** Два публичных конструктора. DI
выбирает самый длинный и падает на `string`-параметрах. Решение —
явная фабрика.

**DI и `IAsyncDisposable`.** DI-контейнер синхронный. Реализовать
оба интерфейса. `Dispose()` → `StopAsync().GetAwaiter().GetResult()`.

**Dev-layout ≠ publish-layout.** В dev-сборке три exe в разных
папках. Для прогонов использовать publish (`tools/build-release.bat`).

**`&nmm=1` в URL Nexus.** Без параметра пользователь жмёт Slow
Download, архив падает в системные `Downloads/`.

**Handler — системный callback.** Без окна. `Console.Error.WriteLine`
не виден. Логировать в файл.

**Handler не стартует GUI.** Если pipe недоступен — выход с ошибкой.

**`File.AppendAllText` может бросить.** `HandlerLogger.Log` глотает.

### Про file-logging

**`TryAddEnumerable` требует явного `ImplementationType`.**
`ServiceDescriptor.Singleton<ILoggerProvider, FileLoggerProvider>(factory)`.

**`File.ReadAllText` не читает активно пишущийся файл.** В тестах
закрывать провайдер перед чтением. В прод-логгере — `FileShare.ReadWrite`.

**Одна зона для имени файла и содержимого.** Единый источник —
`now.LocalDateTime`.

**`Directory.CreateDirectory` в конструкторе логгера — плохо.**
Лениво, в `Log`.

### Про `.meta` для архивов в `downloads/`

`.meta`-файлы MO2 (`downloads/<archive>.7z.meta`) — **отдельная
сущность** от `mods/<Name>/meta.ini`. Формат одинаковый
(`[General]` + `[installedFiles]`), но семантика разная:

- `mods/<Name>/meta.ini` — метаданные **мода** (что за мод, откуда).
- `downloads/<archive>.7z.meta` — метаданные **архива** (откуда
  скачан). Без `.meta` packer не может определить, что архив
  с Nexus, и при повторном pack видит его как unresolved.

**До блока 38** installer не восстанавливал `.meta` для архивов.
При повторном pack nexus-архивы становились unmatched. Решение:
`ArchiveEntry.Meta` в манифесте + `GenerateArchiveMetaStep`
в installer-е + `CheckArchiveMeta` в verify.

`.meta` пишется **только** для nexus-архивов (`ModId`/`FileId`
валидны). Для local-архивов `ArchiveEntry.Meta = null` —
нечего восстанавливать.

Не удаляем `.meta`, если он есть, а в манифесте `Meta == null`:
автор мог положить вручную, packer в следующий раз прочитает
и включит в манифест.

### Про `IPatchDialogService` и циклические ссылки

`Modsync.Gui.Shared` и `Modsync.Gui.Modules` **не могут** ссылаться
на `ModsyncManager.Gui` (exe) — это нарушит направление
зависимостей. Поэтому View во `Modsync.Gui.Modules` **не может**
открыть `Window` напрямую. Решение: абстракция `IPatchDialogService`
в `Modsync.Gui.Shared`, реализация — `AvaloniaPatchDialogService`
в `ModsyncManager.Gui`. VM (`PackVM`) резолвит через
`_sp.GetService<IPatchDialogService>()`.

Аналогично для `PackConfigView` — но там View **embedded** (не
модальный), поэтому он **не требует** абстракции.

### Про `instancePath` из `Summary`, а не из `configPath`

Если диалог показывает путь к `__ModsyncManager_Output/`, брать
его из `PackSummary.InstancePath` (это `Snapshot.InstancePath` —
папка инстанса). **Нельзя** использовать `configPath` (путь
к `modsyncmanager-pack.json`) — он даст путь
`C:\OmenRim 7\modsyncmanager-pack.json\__ModsyncManager_Output`,
что неправильно.

### Про копипаст

Были ошибки в коде ассистента. Если билд падает — вероятнее ошибка
в коде ассистента, не в проекте.

### Про BOM в исходниках

`.cs` файлы в репозитории часто с BOM (`\uFEFF`). При перезаписи —
сохранять BOM там, где он был. В `.props` / `.targets` / `.csproj` —
**без BOM**.

### Про samples

`samples/*.json` копируются в output тестов через `PreserveNewest`.

### Про пути

Все проекты в `src/` и `tests/`. Новые проекты — строго в `tests/<Name>/`.

### Про пустой DisabledMod

Мод без восстановимых файлов остаётся в манифесте с пустыми
директивами. Installer создаёт папку, файлов нет. Норма.

### Про xUnit1031

Не использовать `.GetAwaiter().GetResult()`. Async API → async-тест.

### Про verify-счётчики

`CheckMod` делает `yield break` — одна fail-проверка вместо 10+.

### Про `Slug.FromFileName`

`.7z` отрезается до slug-ификации.

### Про `install` без `--target`

`<exeDir>/Instances/<meta.name>/`.

### Про HTTP-логи `HttpClient` в GUI

`builder.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning)`.

### Про `NexusAuthenticationException` vs `InvalidOperationException`

`NexusClient` использует:
- `NexusAuthenticationException` — нет ключа или 401.
- `InvalidOperationException` — 403/404/429.

### Про маскирование API-ключа

`abcd...wxyz`. Не логировать. В GUI — `PasswordChar="•"`.

### Про Fluent-класс `accent`

Не использовать класс `accent`. Задавать цвета через Fluent-ключи.

### Про `MainWindowVM.NavigateTo` в конструкторе

`NavigateTo(ScreenType.Home)` в конструкторе. Не `Home.Refresh()`
напрямую — иначе подписка на `InstallRequested` не произойдёт.

### Про `AvaloniaProperty.RegisterAttached` в статическом классе

Owner-тип не может быть static-классом (CS0718).

### Про регистрацию nxm:// в реестре

**Только HKCU.** `HKCU\Software\Classes\nxm\shell\open\command`.
Не HKLM — там нужны админские права.

**Backup обязателен.** `%LOCALAPPDATA%\ModsyncManager\nxm-handler-backup.json`.

**Restore — по кнопке в Settings.** Не автоматически.

### Про named pipe

**Имя pipe фиксированное** — `NxmPipeName.Value`.

**Очередь URL.** `Channel<string>`.

**ModsyncManager не запущен.** Handler выходит с ошибкой. Не
стартует GUI.

### Про ProjectReference между библиотеками

`Modsync.Gui.Shared.csproj` **явно ссылается** на `Modsync.Core`
и `Modsync.Platform.Nexus`. В Firelink-версии csproj этих
ссылок не было — там они шли транзитивно через exe-проект. В
новом решении — **явные ссылки**. Аналогично `Modsync.Gui.Modules`
явно ссылается на `Modsync.Install` и `Modsync.Pack`.

### Про `Progress<T>` в тестах

`Progress<T>` в BCL маршалит callback через `SynchronizationContext`.
В xUnit-тестах его обычно нет — callback идёт синхронно, но это
**не гарантия**. Если в тесте проверяется точное количество
репортов `IProgress<T>` — использовать **свой** `IProgress<T>`
с `List<T>` под `lock`, а не BCL `Progress<T>`. Иначе флакает.

### Про `Grid.ColumnSpacing` / `Grid.RowSpacing`

В Avalonia 11 у `Grid` **нет** этих свойств. Только `RowDefinitions`
и `ColumnDefinitions`. Для зазоров использовать `StackPanel.Spacing`
или `Margin` у дочерних элементов.

### Про Nexus auth и retry

`ArchiveDownloadHelper` retry-ит все ошибки по умолчанию. Но
`NexusAuthenticationException` — **постоянная** ошибка: ключ не
появится от повторной попытки. Retry только тратит время
(2+4 секунды на каждый архив).

Решение: `IArchiveDownloader.IsPermanentFailure(ex)` — default
`false`, `NexusDownloader` возвращает `true` для
`NexusAuthenticationException`. Polly `ShouldHandle` пропускает
retry.

Pre-flight (`PreflightNexusAuthStep`) ловит это ещё раньше:
если в манифесте есть `NexusSourceRef` и ключа нет — падаем
до bootstrap-а. Пользователь видит ошибку мгновенно.

---

## Технический долг

(пусто)

Всё, что было в этом разделе, либо закрыто, либо перенесено
в «Сознательно не делаем» (см. ниже):

- `SyncModsStep` — `CreateDirectory` / `Delete` не поддерживаются.
  Packer их не создаёт, installer их игнорирует. **Сознательно**
  (мёртвые модели, используем только `FromArchive`).
- nxm:// handler — зарегистрирован **постоянно**, не на время.
  **Сознательно** (один раз с согласия пользователя).

## Сознательно не делаем

- `ConfigureMo2Step`.
- Автопатчи / autoPack / inlinePatterns.
- `repair` в CLI (CLI удалён).
- Обработка `.bsa`/`.ba2` как контейнеров.
- `modsyncmanager index`.
- Фаза 4 — вариации дистрибутивов.
- Nexus Premium API как отдельная подписка.
- Кеширование Nexus download-ссылок.
- `IDialogService` для `Open MO2`.
- Thumbnail на карточке Dashboard.
- WebView2 для Free-загрузки.
- JS-инъекция в WebView2 для перехвата `nxm://`.
- Fixed Version cab WebView2 в дистрибутив.
- `TryLaunch` из handler-а.
- `--pending-nxm` в GUI.
- Диалог в GUI «ModsyncManager открыл страницу мода».
- UI-настройки логов (retention, размер).
- **Глобальный реестр `archives.db` (отменён 2026-10-01).**
- **Sharing архивов между инстансами.**
- **Кеш содержимого архивов (`archive_files`).**
- **`Modsync.Cli` (удалён 2026-10-01).**
- **Спектровский прогресс-бар и `--verbose` в CLI.**
- **Механизм патчей для inline-файлов (дубль autoPatch).**
- **Локализация GUI (ru/en).**
- **Clean downloads в GUI.**
- **`modsyncmanager doctor` / расширенная диагностика.**
- **Лимит очереди URL в `NxmUrlReceiver`.**
- **`CreateDirectory` / `Delete` в `SyncModsStep`** — packer
  не создаёт, installer игнорирует. Используется только
  `FromArchive`.
- **Регистрация nxm:// handler на время** — сейчас постоянно.
  Один раз с согласия пользователя.
- **`LocalSourceRef` для patch-архивов** — patch-архив
  (`ModsyncManager_Output.zip`) не имеет URL, только локальный
  файл. Для воспроизведения он не нужен (пользователь его
  не имеет). Автор добавляет mirror-source, если хочет
  публиковать. Сознательно оставлено как есть.

---

## Окружение

- Windows 10/11.
- .NET 8 SDK.
- .NET SDK 10 (MSBuild — строгий к BOM в `.props`).
- Visual Studio 2022 / VS Code.
- Новый репо: `D:\Code\repos\ModsyncManager`.
- Старый репо (remote `firelink`): `D:\Code\repos\Firelink`.
- `TestInstance/` — MVP прогон.
- `TestInstance2/` — после 12.13.6.
- `OmenRim 7/` — тестовый оригинал.
- `OmenTest7` — GUI-прогон 3.8.
- Большой инстанс — 4370 модов (не используется).

---

## История изменений документа

Записи идут от новых к старым. Каждая запись — что добавилось
в проект. Исторические обоснования решений — здесь же, в тексте
записей.

- **2026-10-05** — Nexus auth UX: pre-flight + no-retry (блок 40).
  - **Проблема:** при install без Nexus-ключа `SyncArchivesStep`
    пытался скачать N nexus-архивов параллельно, каждый падал
    с `NexusAuthenticationException`, Polly делал 3 retry
    (2+4 секунды задержки), и пользователь получал невнятное
    «SkyUI failed: all sources failed» без подсказки, что делать.
  - **40.1** — `ArchiveDownloadHelper` не retry-ит «постоянные»
    ошибки. В `IArchiveDownloader` добавлен метод
    `IsPermanentFailure(Exception) -> bool`, default `false`.
    `NexusDownloader` возвращает `true` для
    `NexusAuthenticationException`. Polly `ShouldHandle`
    пропускает retry. `SyncArchivesStep` при провале всех
    source-ов проверяет: если хоть одна ошибка содержит
    «Nexus API key is not set» или «NexusAuthenticationException» —
    бросает с сообщением «Open Settings → Nexus, paste your
    API key, and retry. If you're a Free user, enable Free
    Download in Settings → Nexus Free Download.»
  - **40.2** — `PreflightNexusAuthStep` — новый шаг installer-а,
    второй по счёту (после `ReadManifestStep`). Проверяет:
    если в манифесте (`archives[]` или `mo2.archive`) есть
    `NexusSourceRef`, а `INexusApiKeyProvider.TryGetApiKey()`
    возвращает null — падает сразу, до всех bootstrap-шагов.
    Installer стал 13 шагов. `StepNames[1] = "PreflightNexusAuth"`,
    все последующие StepIndex сдвинулись на +1.
  - **Тесты:** +6 (`PreflightNexusAuthStepTests` — 6 сценариев:
    no nexus sources, key present, key missing, empty key,
    MO2 archive with nexus, canceled token) + 4 в других
    (SyncArchivesStepTests, NexusDownloaderTests).
  - **Итог:** `dotnet test` — 1269 тестов, 0 failed.
- **2026-10-05** — Полировка после блока 38 (блок 39).
  - **39.1** — `DOC.md` §6.6: обратная ссылка на §6.5.
    Единый INI-парсер `MetaIniReader` для `.meta` архивов
    и `meta.ini` модов.
  - **39.2** — интеграционный тест
    `PackInstallArchiveMetaTests`. Ключевой сценарий:
    у автора `.meta` есть → pack → манифест содержит
    `ArchiveEntry.Meta` → preload, `.meta` удалён из target →
    install восстанавливает `.meta` через
    `GenerateArchiveMetaStep` → verify проходит.
    Второй тест: `.meta` повреждён в target → verify fail.
  - **39.3** — `VerifyPipelineTests`: `.meta` не парсится
    (мусор вместо INI) → `CompareArchiveMeta` даёт fail
    `modID: expected 1, got null`.
  - **39.4** — caption-подписи под кнопками формы Pack Config:
    `Load config…` («Load existing config from disk.»),
    `Save as…` («Save config without running pack.»),
    `Pack` («Save config and run packer.»). Симметрично
    caption-подписям на главном экране Pack.
  - **39.5** — packer читает `.meta` для MO2-архива.
    `Mo2ArchiveBuilder.Build` получил новый параметр
    `downloadsPath`, читает `downloads/<mo2>.meta` через
    `MetaIniReader.TryRead`, заполняет `ArchiveEntry.Meta`
    во всех трёх ветвях (Resolved/Unresolved/Missing).
    Симметрично тому, что делает `IndexArchivesStep` для
    обычных архивов.
  - **39.6** — `InstallView.axaml`: Success-панель показывает
    `Archive .meta` (число записанных `.meta`).
    Поле `InstallSummary.ArchiveMetaWritten` уже было
    добавлено в блоке 38.4, но не отображалось.
  - **Итог:** `dotnet test` — 1259 тестов, 0 failed.
- **2026-10-05** — Восстановление `.meta` для архивов (блок 38).
  - **Проблема:** при install в `downloads/` не восстанавливались
    `.meta`-файлы MO2. При повторном pack (например, для обновления
    сборки) packer видел nexus-архивы как unresolved — моды,
    ссылающиеся на них, становились unmatched. Замкнутый круг.
  - **38.1** — `MetaReader`/`MetaFile` удалены. Парсинг `.meta`
    унифицирован через `MetaIniReader`: формат `.meta` и
    `mods/<Name>/meta.ini` одинаковый (`[General]` +
    `[installedFiles]`), `MetaIniReader` уже умел читать оба.
    Валидация «это nexus-архив» — `ModId.HasValue &&
    FileId.HasValue`. Тесты перенесены в `MetaIniReaderTests`.
  - **38.2** — `ArchiveEntry.Meta` (`ModMeta?`) — опциональное
    поле. В JSON не пишется, если `null`. Схема манифеста
    остаётся `1.0.0` (приложение не в продакшене).
  - **38.3** — packer: `IndexArchivesStep.ProcessOneFile`
    заполняет `ArchiveEntry.Meta` при чтении `.meta`
    (только для nexus-архивов, где `ModId`/`FileId` валидны).
    Для local-архивов `Meta = null`.
  - **38.4** — installer: `GenerateArchiveMetaStep` —
    для каждого `ArchiveEntry` с `Meta != null` пишет
    `downloads/<archive>.meta` через `MetaIniWriter`.
    Идемпотентен (перезапись при повторном запуске).
    `null` — не трогаем (не удаляем, если файл есть).
    `InstallPipeline` — 12 шагов (было 11), `StepNames[6] =
    "GenerateArchiveMeta"`. `InstallSummary` расширен
    (`ArchiveMetaWritten`, `ArchiveMetaSkipped`).
  - **38.5** — verify: `VerifyPipeline.CheckArchiveMeta` —
    отдельная проверка на каждый архив (включая MO2-архив).
    `Meta != null` → файл должен быть и совпадать с манифестом
    (семантическое сравнение полей). `Meta == null` → не ошибка,
    даже если файл есть («not expected (file present, ignored)»).
  - **Итог:** `dotnet test` — 1256 тестов, 0 failed.
- **2026-10-05** — Pack UX rework (блоки 36, 37).
  - **Блок 36** — patch-архив для unmatched файлов.
    - **`PatchArchiveBuilder`** (`Modsync.Pack`) — собирает
      `ModsyncManager_Output.zip` из `__ModsyncManager_Output/`,
      кладёт в `MO2/downloads/`. Структура архива — 1:1 от
      `__ModsyncManager_Output/`, минус `modlist.json`. Не удаляет
      исходную папку.
    - **`PatchArchiveDialog`** — модальное окно (600×400) после
      pack, если `PackSummary.UnmatchedFiles > 0`. Кнопки: Ignore /
      Open output folder / Create patch archive.
    - **`IPatchDialogService`** (`Modsync.Gui.Shared`) +
      `AvaloniaPatchDialogService` (`ModsyncManager.Gui`) —
      абстракция модального диалога. `PackVM` (библиотека) не
      может открыть `Window` напрямую; резолвит через
      `_sp.GetService`.
    - **`instancePath` для диалога** — берётся из
      `Summary.InstancePath` (правильный путь инстанса), **не** из
      `configPath`.
  - **Блок 37.1** — переименование `CreatePackConfigVM/View` →
    `PackConfigVM/View`. Класс, файлы, `x:Class`, `x:DataType`,
    логгер, DI-регистрация. Свойство `PackVM.ConfigVM` (не
    `PackConfigVM`, чтобы не конфликтовать с именем типа —
    CS0542). Тесты переименованы.
  - **Блок 37.2** — форма Pack Config умеет:
    - **`LoadConfigFileCommand`** + `LoadConfigAsync(path)` —
      загрузить существующий `.json`, резолвит `instance.path`
      от папки config, сканирует инстанс, заполняет профиль,
      extensions/extras/archiveSources.
    - **`SaveAsCommand`** — сохранить config в выбранное место,
      не запускать packer.
    - **`PackCommand`** — сохранить config (в `_loadedConfigPath`
      или в `<InstancePath>/modsyncmanager-pack.json`) и поднять
      `ConfigCreated`.
    - **Пустые дефолты** для meta-полей, нейтральные
      плейсхолдеры.
    - **`_loadedConfigPath`**, **`_orphanedSources`**,
      **`SavePathDisplay`**, **`OverwriteWarning`**,
      **`HasLoadedFrom`** + **`LoadedFromPath`**.
    - **`UnresolvedArchiveRowVM`** — `LoadSources(PackArchiveSource)`
      + `AddEmptySource()`, `RemoveSourceInternal` → `RemoveSource`
      (internal).
  - **Блок 37.3** — UX rework экрана Pack.
    - **`FilePickerView` для config** и кнопка `Pack` с главного
      экрана удалены.
    - Две кнопки: **`Load config…`** (приоритет, сверху) и
      **`Create config…`** (снизу). Обе открывают embedded-форму
      `PackConfigVM`.
    - **`IPackRunner.RunAsync(configPath, ...)`** удалён из
      интерфейса и реализации — единственный путь теперь
      `RunFromConfigBuilderAsync`.
    - **`PackVM`** больше не создаёт `FilePickerVM` для config и
      не имеет `PackCommand` на главном экране.
  - **Итог:** `dotnet test ModsyncManager.slnx` — 1244 теста, 0 failed.
- **2026-10-05** — Pack UX rework (блоки 36, 37).
  - **Блок 36** — patch-архив для unmatched файлов.
    - **`PatchArchiveBuilder`** (`Modsync.Pack`) — собирает
      `ModsyncManager_Output.zip` из `__ModsyncManager_Output/`,
      кладёт в `MO2/downloads/`. Структура архива — 1:1 от
      `__ModsyncManager_Output/`, минус `modlist.json`. Не удаляет
      исходную папку.
    - **`PatchArchiveDialog`** — модальное окно (600×400) после
      pack, если `PackSummary.UnmatchedFiles > 0`. Кнопки: Ignore /
      Open output folder / Create patch archive.
    - **`IPatchDialogService`** (`Modsync.Gui.Shared`) +
      `AvaloniaPatchDialogService` (`ModsyncManager.Gui`) —
      абстракция модального диалога. `PackVM` (библиотека) не
      может открыть `Window` напрямую; резолвит через
      `_sp.GetService`.
    - **`instancePath` для диалога** — берётся из
      `Summary.InstancePath` (правильный путь инстанса), **не** из
      `configPath`.
  - **Блок 37.1** — переименование `CreatePackConfigVM/View` →
    `PackConfigVM/View`. Класс, файлы, `x:Class`, `x:DataType`,
    логгер, DI-регистрация. Свойство `PackVM.ConfigVM` (не
    `PackConfigVM`, чтобы не конфликтовать с именем типа —
    CS0542). Тесты переименованы.
  - **Блок 37.2** — форма Pack Config умеет:
    - **`LoadConfigFileCommand`** + `LoadConfigAsync(path)` —
      загрузить существующий `.json`, резолвит `instance.path`
      от папки config, сканирует инстанс, заполняет профиль,
      extensions/extras/archiveSources.
    - **`SaveAsCommand`** — сохранить config в выбранное место,
      не запускать packer.
    - **`PackCommand`** — сохранить config (в `_loadedConfigPath`
      или в `<InstancePath>/modsyncmanager-pack.json`) и поднять
      `ConfigCreated`.
    - **Пустые дефолты** для meta-полей, нейтральные
      плейсхолдеры.
    - **`_loadedConfigPath`**, **`_orphanedSources`**,
      **`SavePathDisplay`**, **`OverwriteWarning`**,
      **`HasLoadedFrom`** + **`LoadedFromPath`**.
    - **`UnresolvedArchiveRowVM`** — `LoadSources(PackArchiveSource)`
      + `AddEmptySource()`, `RemoveSourceInternal` → `RemoveSource`
      (internal).
  - **Блок 37.3** — UX rework экрана Pack.
    - **`FilePickerView` для config** и кнопка `Pack` с главного
      экрана удалены.
    - Две кнопки: **`Load config…`** (приоритет, сверху) и
      **`Create config…`** (снизу). Обе открывают embedded-форму
      `PackConfigVM`.
    - **`IPackRunner.RunAsync(configPath, ...)`** удалён из
      интерфейса и реализации — единственный путь теперь
      `RunFromConfigBuilderAsync`.
    - **`PackVM`** больше не создаёт `FilePickerVM` для config и
      не имеет `PackCommand` на главном экране.
  - **Итог:** `dotnet test ModsyncManager.slnx` — 1244 теста, 0 failed.
- **2026-10-04** — GUI: Pack без config + прогресс индексации.
  - **`PackConfigBuilder`** (`Modsync.Pack`) — новый публичный
    сервис. Сканирует `MO2/downloads/` на предмет non-nexus
    архивов (без `.meta`), читает список профилей MO2, собирает
    `PackConfig` из пользовательского ввода + хардкода
    `mo2`-секции (MO2 2.5.2, официальный GitHub-релиз).
  - **`Create Pack Config`** — форма, встроенная в экран Pack
    (embedded, не overlay). Поля: `meta` (Name, Version, Author,
    Game, GameVersion), Profile (ComboBox реальных профилей),
    Extensions, Extras, список non-nexus архивов.
  - **Skip / Unskip для unresolved-архивов.** Пользователь может
    отказаться указывать источник для архива — тогда архив не
    попадает в `archiveSources`, но остаётся в `downloads/`.
    Packer разберётся: если архив не используется — проигнорирует;
    если используется — мод окажется unmatched.
  - **`Save as template`** — сохранение текущей формы в
    `modsyncmanager-pack.json` (через `IFilePickerService.SaveFileAsync`).
  - **Прогресс `BuildArchiveMatcher`** — репорт `Detail` формата
    `Building index: N / M` через `IProgress<(int, int)>`, как
    `SyncArchivesStep` / `SyncModsStep` в installer-е. Показывается
    в UI: `Step 7 of 14 · Building index: 12 / 68 ⟳`.
  - **`PackVM`** — новое состояние `IsCreatingConfig` (показывает
    форму вместо Configuration). `ExecutePackAsync` — общий метод
    для pack из config-файла и из формы.
  - **Тесты:** +64 теста (PackConfigBuilderTests, CreatePackConfigVMTests,
    UnresolvedArchiveRowVMTests, ArchiveSourceRowVMTests, PackVMTests
    расширен). Итого **1193**.
  - **Итог:** `dotnet test ModsyncManager.slnx` — 1193 теста, 0 failed.
- **2026-10-02** — **Переименование Firelink → ModsyncManager
  завершено.**
  - **Все namespace и assembly:** `Modsync.*` (библиотеки),
    `ModsyncManager.*` (exe-проекты).
  - **Файлы:** `Firelink.Paths` → `ModsyncPaths`, `Firelink.exe`
    → `ModsyncManager.exe`, `firelink-pack.json` →
    `modsyncmanager-pack.json`, `__Firelink_Output` →
    `__ModsyncManager_Output`, `firelink-nxm-handler.log` →
    `modsyncmanager-nxm-handler.log`.
  - **Корень user data:** `%LOCALAPPDATA%\Firelink\` →
    `%LOCALAPPDATA%\ModsyncManager\` (без миграции).
  - **Pipe value:** `firelink-nxm` → `modsyncmanager-nxm`.
  - **Mutex:** `ModsyncManager.Gui.SingleInstance`.
  - **GUI-проекты объединены:** нет отдельных `Modsync.Gui.Install`,
    `Modsync.Gui.Pack`, `Modsync.Gui.Verify` — всё в
    `Modsync.Gui.Modules`.
  - **`Directory.Build.props`** — `<Authors>` / `<Product>` →
    `ModsyncManager`. Снят BOM.
  - **`tools/build-release.bat`** — публикует `ModsyncManager.exe`
    и `ModsyncManager.NxmHandler.exe`. Убрана висячая ссылка
    на CLI.
  - **`ModsyncManager.slnx`** — 21 проект.
  - **Документы обновлены:** `DOC.md` (v6.0), `README.md`,
    `README.ru.md`, `THIRD-PARTY-NOTICES.md`, `LICENSE`,
    `DEEPSEEK.md`.
  - **Итог:** `dotnet build ModsyncManager.slnx` — 21 проект,
    0 errors. `dotnet test ModsyncManager.slnx` — ~1129 тестов,
    0 failed.
- **2026-10-01** — техдолг и чистка GUI:
  - **`WinExe`** для `ModsyncManager.Gui.csproj` — убрано консольное
    окно при запуске GUI.
  - **`AddSimpleConsole`** убран из `App.BuildServices` — логи
    только в файл (`%LOCALAPPDATA%\ModsyncManager\logs\`).
  - **`INxmUrlReceiver.TryDrainPendingUrls()`** — очистка
    очереди URL перед install. URL-ы от прошлых сессий
    (пользователь отменил, URL уже пришёл) не попадают в
    текущий install. `NexusFreeNxmProvider.RequestNxmUrlAsync`
    вызывает drain до открытия браузера.
  - **Отменены кандидаты v0.3.0:** Clean downloads,
    `modsyncmanager doctor`, лимит очереди URL, single-instance
    `NxmUrlReceiver` (mutex достаточно), локализация GUI.
  - Итог: **1168 тестов, 0 failed.**
- **2026-10-01** — реструктуризация документации:
  - **FIRELINK.md** сокращён: убраны детали форматов,
    полные списки проектов, диаграммы навигации (переехали
    в `DOC.md`).
  - **Ключевые решения** сжаты: с ~294 до ~60. Решения
    «удалили X», «переехали Y» консолидированы в записи
    истории.
  - **DOC.md** переименован в справочник (v5.0):
    убраны «Дорожная карта», «Статус реализации»,
    «Ключевые решения», полные JSON-схемы, C# интерфейсы.
  - Общая политика: **FIRELINK.md — журнал проекта,
    DOC.md — справочник.** Нет дублей.
  - Итог: **1155 тестов, 0 failed.**
- **2026-10-01** — удаление CLI, отмена оставшегося техдолга v0.2.0:
  - **Удалён `Modsync.Cli`**: реальных пользователей CLI нет;
    pipeline — библиотека и доказывается структурой проектов и
    тестами, а не exe.
  - **Удалены** `Spectre.Console.Cli` и `Spectre.Console` из
    `Directory.Packages.props`.
  - **`tools/build-release.bat`** — убрана публикация CLI,
    дистрибутив: `ModsyncManager.exe` + `ModsyncManager.NxmHandler.exe`.
  - **Спектровский прогресс-бар и `--verbose` для CLI** — отменены.
  - **Блок 2.2 (`archives.db`)** — отменён ранее в тот же день.
  - **Механизм патчей для inline-файлов** — отменён как дубль
    autoPatch (решение №59).
  - **v0.2.0 закрыт**: все пункты либо закрыты (1.1, 1.2, 2.1,
    3.1, 3.2), либо отменены (2.2, Spectre, --verbose, патчи).
  - **Отменены решения:** №6 (глобальный реестр `archives.db`),
    №144–152 (Фаза 1 — Единый CLI). №293 (CacheVM под несколько
    баз) переформулирован.
  - Итог: **1155 тестов, 0 failed.**
- **2026-10-01** — v0.2.0, блок 3.2 (UX-ревизия Settings):
  - **Logs** убран из Settings; «Open logs folder» переехал
    в toolbar Logs-вкладки (`LogsVM`).
  - **Cache** убран из Settings; теперь — отдельный экран
    DevMode (`CacheVM`, `ScreenType.Cache`).
  - **Sidebar DevMode:** Home, Install, Pack, Verify, Logs,
    Cache, Settings.
  - **`HashCacheSettingsVM`** удалён — содержимое в `CacheVM`.
  - **`ScreenIconConverter`** перенесён из `Modsync.Gui.Controls`
    в `ModsyncManager.Gui` (namespace-резолвинг в XAML).
  - Решения 290–294.
  - Итог: **1155 тестов, 0 failed.**
- **2026-10-01** — v0.2.0, блок 3.1 (прогресс Install/Pack):
  - **`StepProgress.Detail`** — `string?`, опциональный.
  - **`SyncArchivesStep`** репортит `Downloading: N / M` через
    `Input.DetailProgress` (`IProgress<(int, int)>?`).
  - **`SyncModsStep`** репортит `Syncing: N / M mods` (Pass 1).
  - **`InstallPipeline`** — адаптер `(int, int)` → `Detail`.
  - **UI:** «Step 6 of 11 · Downloading: 12 / 891 ⟳».
  - **CLI не показывает `Detail`** (решение неактуально — CLI удалён).
  - Решения 284–289.
  - Итог: **1150 тестов, 0 failed.**
- **2026-09-30** — v0.2.0, блок 2.1 (persist кеша хешей):
  - **`IHashCache`** — интерфейс кеша хешей.
  - **`FileHashCache`** и **`SqliteHashCache`** (L1+L2).
  - SQLite `file_hashes` в `%LOCALAPPDATA%\ModsyncManager\cache.db`.
  - **`ArchiveMatcher` без persist** — сознательно.
  - **Секция Cache** в SettingsView.
  - Решения 275–283.
  - Итог: **1130 тестов, 0 failed.**
- **2026-09-30** — v0.2.0, блок 1.2 (SettingsStore + Logs):
  - **`ModsyncPaths`** в `Modsync.Core` — единый корень.
  - `ISettingsStore` + `Settings` (POCO с `DevMode`).
  - **Ротация по размеру** в `FileLoggerProvider`: `.1.log`.
  - `SettingsVM.IsDevMode` — прокси на `ISettingsStore`.
  - `Modsync.Logging` ссылается на `Modsync.Core`.
  - Решения 268–274.
  - Итог: **1120 тестов, 0 failed.**
- **2026-09-30** — v0.2.0, блок 1.1 (file-logging):
  - Single-instance GUI.
  - Параметризация pipe в NxmUrlReceiver.
  - Modsync.Logging: дневная ротация, retention.
  - ModsyncManager.NxmHandler пишет в
    `%LOCALAPPDATA%\ModsyncManager\logs\`.
  - Итог: **1095 тестов, 0 failed.**
- **2026-09-29** — отказ от WebView2, Вариант C-радикальный.
  Шаги A–I.
- **2026-09-27** — Nexus credential UI, Блок 1–3.
- **2026-09-26** — техдолг + редизайн Dashboard.
- **2026-09-25** — Фаза 3.8, 3.9 (3.9.1–3.9.8).
- **2026-09-24** — Фаза 3, шаги 3.5–3.7.
- **2026-09-23** — Фаза 3, шаги 3.1–3.4.2.
- **2026-09-22** — legacy cleanup, DOC.md v4.2. Фаза 6 закрыта.
- **2026-09-21** — создан `FIRELINK.md`. Фаза 1 закрыта
  (позже отменена 2026-10-01).
