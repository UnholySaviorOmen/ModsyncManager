// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Progress;

/// <summary>
/// Прогресс одного шага pipeline.
///
/// StepIndex — 1-based, для отображения «шаг 3 из 12».
/// TotalSteps — общее число шагов в pipeline.
/// StepName — короткое стабильное имя шага, для логов и UI.
/// Detail — опциональная деталь шага, для UI (например,
///   «Downloading: 12 / 891»). Null для шагов без деталей.
///
/// Общий тип для packer-а и installer-а. Не привязан ни к одной
/// из библиотек — поэтому живёт в Core.
/// </summary>
public readonly record struct StepProgress(
    int StepIndex,
    int TotalSteps,
    string StepName,
    string? Detail = null);
