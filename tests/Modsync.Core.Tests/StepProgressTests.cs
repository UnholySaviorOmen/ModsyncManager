using FluentAssertions;
using Modsync.Core.Progress;

namespace Modsync.Core.Tests;

public class StepProgressTests
{
    [Fact]
    public void Constructor_WithoutDetail_DetailIsNull()
    {
        var p = new StepProgress(1, 11, "SyncArchives");

        p.StepIndex.Should().Be(1);
        p.TotalSteps.Should().Be(11);
        p.StepName.Should().Be("SyncArchives");
        p.Detail.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithDetail_DetailIsSet()
    {
        var p = new StepProgress(6, 11, "SyncArchives", "Downloading: 12 / 891");

        p.Detail.Should().Be("Downloading: 12 / 891");
    }

    [Fact]
    public void Equality_SameValues_Equal()
    {
        var a = new StepProgress(1, 11, "X", "detail");
        var b = new StepProgress(1, 11, "X", "detail");

        a.Should().Be(b);
    }

    [Fact]
    public void Equality_DifferentDetail_NotEqual()
    {
        var a = new StepProgress(1, 11, "X", "detail-a");
        var b = new StepProgress(1, 11, "X", "detail-b");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Equality_NullVsEmptyDetail_NotEqual()
    {
        var a = new StepProgress(1, 11, "X", null);
        var b = new StepProgress(1, 11, "X", "");

        a.Should().NotBe(b);
    }

    [Fact]
    public void With_ModifiesDetail()
    {
        var original = new StepProgress(1, 11, "X");
        var modified = original with { Detail = "hello" };

        original.Detail.Should().BeNull();
        modified.Detail.Should().Be("hello");
    }
}
