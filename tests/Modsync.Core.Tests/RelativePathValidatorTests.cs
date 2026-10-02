using FluentAssertions;
using Modsync.Core.Validation;

namespace Modsync.Core.Tests;

public class RelativePathValidatorTests
{
    [Theory]
    [InlineData("plugins/fomod.dll")]
    [InlineData("tools/BethINI")]
    [InlineData("tools/BethINI/")]
    [InlineData("plugins\\fomod.dll")]
    [InlineData("skse64_loader.exe")]
    [InlineData("enbseries/")]
    [InlineData("enbseries")]
    public void Validate_ValidPaths_Pass(string path)
    {
        RelativePathValidator.Validate(path, "field").IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyOrNull_Fail(string? path)
    {
        RelativePathValidator.Validate(path, "field").IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("C:\\absolute\\path")]
    [InlineData("/etc/passwd")]
    [InlineData("\\absolute")]
    [InlineData("C:/absolute")]
    public void Validate_AbsolutePaths_Fail(string path)
    {
        RelativePathValidator.Validate(path, "field").IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("foo/../bar")]
    [InlineData("foo/../../bar")]
    public void Validate_ParentTraversal_Fail(string path)
    {
        RelativePathValidator.Validate(path, "field").IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("./foo")]
    [InlineData("foo/./bar")]
    public void Validate_DotSegments_Fail(string path)
    {
        RelativePathValidator.Validate(path, "field").IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("foo//bar")]
    [InlineData("foo/bar//")]
    public void Validate_EmptySegmentsInMiddle_Fail(string path)
    {
        // trailing slash разрешён, двойной — только в середине — ошибка
        // "foo/bar//" → ["foo", "bar", "", ""] — предпоследний пустой, ошибка
        var result = RelativePathValidator.Validate(path, "field");
        if (path.EndsWith("//"))
            result.IsValid.Should().BeFalse();
        else
            result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("foo<bar")]
    [InlineData("foo:bar")]
    [InlineData("foo|bar")]
    [InlineData("foo?bar")]
    [InlineData("foo*bar")]
    public void Validate_ForbiddenChars_Fail(string path)
    {
        RelativePathValidator.Validate(path, "field").IsValid.Should().BeFalse();
    }
}
