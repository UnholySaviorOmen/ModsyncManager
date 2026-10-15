// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CodingSeb.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Modsync.Gui.Shared.ViewModels;

/// <summary>
/// Базовая VM для экранов с прогрессом (Install, Pack, Verify).
///
/// Хранит состояние шага: номер, всего, имя, процент, деталь.
/// Detail — опциональная строка от pipeline («Downloading: 12 / 891»).
/// HasDetail — удобное производное для видимости спиннера в XAML.
///
/// StepLabel — локализованная строка «Шаг X из Y». Обновляется
/// при изменении CurrentStep / TotalSteps. Ключ Common.Step_Label
/// общий для Install, Pack, Verify.
/// </summary>
public abstract partial class ProgressViewModel : ViewModel
{
    [ObservableProperty]
    private int _currentStep;

    [ObservableProperty]
    private int _totalSteps;

    [ObservableProperty]
    private string _stepName = "";

    [ObservableProperty]
    private double _percent;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _detail;

    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    public string StepLabel =>
        string.Format(
            Loc.Tr("Common.Step_Label"),
            CurrentStep,
            TotalSteps);

    partial void OnDetailChanged(string? value)
    {
        OnPropertyChanged(nameof(HasDetail));
    }

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(StepLabel));
    }

    partial void OnTotalStepsChanged(int value)
    {
        OnPropertyChanged(nameof(StepLabel));
    }

    public void Report(
        int step,
        int total,
        string name,
        string? detail = null)
    {
        CurrentStep = step;
        TotalSteps = total;
        StepName = name;
        Detail = detail;
        Percent = total > 0 ? (double)step / total * 100 : 0;
    }

    public void Reset()
    {
        CurrentStep = 0;
        TotalSteps = 0;
        StepName = "";
        Detail = null;
        Percent = 0;
        IsBusy = false;
    }
}
