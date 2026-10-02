using FluentAssertions;
using Modsync.Core.Validation;

namespace Modsync.Core.Tests;

public class InstancePathValidatorTests
{
    [Theory]
    [InlineData(".")]                    // текущая папка
    [InlineData("NordicUI Overhaul")]    // обычное имя
    [InlineData("sub/instance")]
    [InlineData("./sub/instance")]
    [InlineData("sub\\instance")]
    [InlineData("sub/instance/")]
    public void Validate_ValidPaths_Pass(string path)
    {
        InstancePathValidator.Validate(path).IsValid.Should().BeTrue(
            $"errors: {string.Join("; ", InstancePathValidator.Validate(path).Errors)}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyOrNull_Fail(string? path)
    {
        InstancePathValidator.Validate(path).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("foo/../bar")]
    [InlineData("foo/../../bar")]
    public void Validate_ParentTraversal_Fail(string path)
    {
        InstancePathValidator.Validate(path).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("C:\\absolute")]
    [InlineData("/etc/passwd")]
    [InlineData("\\absolute")]
    public void Validate_Absolute_Fail(string path)
    {
        InstancePathValidator.Validate(path).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("foo//bar")]
    public void Validate_EmptySegmentInMiddle_Fail(string path)
    {
        InstancePathValidator.Validate(path).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_DotSegment_Allowed()
    {
        // В отличие от RelativePathValidator, "." разрешён.
        InstancePathValidator.Validate(".").IsValid.Should().BeTrue();
        InstancePathValidator.Validate("./sub").IsValid.Should().BeTrue();
    }
}
