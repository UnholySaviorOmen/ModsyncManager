using FluentAssertions;
using Modsync.Core.Validation;

namespace Modsync.Core.Tests;

public class SemverValidatorTests
{
    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.0.1")]
    [InlineData("10.20.30")]
    [InlineData("1.0.0-alpha")]
    [InlineData("1.0.0-alpha.1")]
    [InlineData("1.0.0-0.3.7")]
    [InlineData("1.0.0-x.7.z.92")]
    [InlineData("1.0.0+20130313144700")]
    [InlineData("1.0.0-beta+exp.sha.5114f85")]
    [InlineData("1.0.0-alpha.1+build.456")]
    public void Validate_ValidVersions_Pass(string version)
    {
        SemverValidator.Validate(version).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("1.0")]
    [InlineData("1")]
    [InlineData("1.0.0.0")]
    [InlineData("v1.0.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0+")]
    [InlineData("1.0.0-+build")]
    [InlineData("a.b.c")]
    public void Validate_InvalidVersions_Fail(string? version)
    {
        SemverValidator.Validate(version).IsValid.Should().BeFalse();
    }
}
