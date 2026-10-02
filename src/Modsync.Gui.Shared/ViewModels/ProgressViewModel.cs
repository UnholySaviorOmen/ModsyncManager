// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;

namespace Modsync.Gui.Shared.ViewModels;

/// <summary>
/// Базовая VM для экранов с прогрессом (Install, Pack).
///
/// Хранит состояние шага: номер, всего, имя, процент, деталь.
/// Detail — опциональная строка от pipeline («Downloading: 12 / 891»).
/// HasDetail — удобное производное для видимости спиннера в XAML.
///
/// Свойства — с [ObservableProperty]. Явные свойства дали бы больше
/// контроля, но здесь нет конфликтов с генератором (нет явных свойств
/// с теми же именами), а CommunityToolkit.Mvvm работает.
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

    partial void OnDetailChanged(string? value)
    {
        // CommunityToolkit.Mvvm не отслеживает computed properties —
        // после смены Detail уведомляем HasDetail вручную.
        OnPropertyChanged(nameof(HasDetail));
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
