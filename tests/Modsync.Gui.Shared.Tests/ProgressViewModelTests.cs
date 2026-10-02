using FluentAssertions;
using Modsync.Gui.Shared.ViewModels;

namespace Modsync.Gui.Shared.Tests;

public class ProgressViewModelTests
{
    private sealed class TestProgressVM : ProgressViewModel { }

    // ------------------------------------------------------------------
    //  Report — базовая функциональность
    // ------------------------------------------------------------------

    [Fact]
    public void Report_SetsAllFields()
    {
        var vm = new TestProgressVM();
        vm.Report(3, 11, "SyncMods");

        vm.CurrentStep.Should().Be(3);
        vm.TotalSteps.Should().Be(11);
        vm.StepName.Should().Be("SyncMods");
        vm.Percent.Should().BeApproximately(300.0 / 11.0, 0.01);
    }

    [Fact]
    public void Report_TotalZero_PercentIsZero()
    {
        var vm = new TestProgressVM();
        vm.Report(0, 0, "");

        vm.Percent.Should().Be(0);
    }

    // ------------------------------------------------------------------
    //  Report — Detail
    // ------------------------------------------------------------------

    [Fact]
    public void Report_WithoutDetail_DetailIsNull()
    {
        var vm = new TestProgressVM();
        vm.Report(1, 11, "X");

        vm.Detail.Should().BeNull();
        vm.HasDetail.Should().BeFalse();
    }

    [Fact]
    public void Report_WithDetail_DetailIsSet()
    {
        var vm = new TestProgressVM();
        vm.Report(6, 11, "SyncArchives", "Downloading: 12 / 891");

        vm.Detail.Should().Be("Downloading: 12 / 891");
        vm.HasDetail.Should().BeTrue();
    }

    [Fact]
    public void Report_WithEmptyDetail_HasDetailIsFalse()
    {
        var vm = new TestProgressVM();
        vm.Report(6, 11, "SyncArchives", "");

        vm.Detail.Should().Be("");
        vm.HasDetail.Should().BeFalse();
    }

    [Fact]
    public void Report_DetailChanges_UpdatesHasDetail()
    {
        var vm = new TestProgressVM();

        vm.Report(6, 11, "X", "detail");
        vm.HasDetail.Should().BeTrue();

        vm.Report(7, 11, "Y");
        vm.HasDetail.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  PropertyChanged на HasDetail
    // ------------------------------------------------------------------

    [Fact]
    public void Report_DetailChanged_RaisesHasDetailPropertyChanged()
    {
        var vm = new TestProgressVM();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Report(6, 11, "X", "detail");

        changed.Should().Contain(nameof(ProgressViewModel.HasDetail));
    }

    // ------------------------------------------------------------------
    //  Reset
    // ------------------------------------------------------------------

    [Fact]
    public void Reset_ClearsAllFields()
    {
        var vm = new TestProgressVM();
        vm.Report(5, 10, "X", "detail");
        vm.IsBusy = true;
        vm.Reset();

        vm.CurrentStep.Should().Be(0);
        vm.TotalSteps.Should().Be(0);
        vm.StepName.Should().Be("");
        vm.Detail.Should().BeNull();
        vm.HasDetail.Should().BeFalse();
        vm.Percent.Should().Be(0);
        vm.IsBusy.Should().BeFalse();
    }
}
