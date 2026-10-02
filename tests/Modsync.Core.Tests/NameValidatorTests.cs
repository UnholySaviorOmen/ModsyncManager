using FluentAssertions;
using Modsync.Core.Validation;

namespace Modsync.Core.Tests;

public class NameValidatorTests
{
    [Theory]
    [InlineData("Skyrim Modpack")]
    [InlineData("NordicUI-Overhaul")]
    [InlineData("Firelink")]
    [InlineData("Pack v1")]
    [InlineData("Модпак")]  // кириллица разрешена в имени папки
    public void Validate_ValidNames_Pass(string name)
    {
        NameValidator.Validate(name).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyOrNull_Fail(string? name)
    {
        NameValidator.Validate(name).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Pack<name>")]
    [InlineData("Pack:name")]
    [InlineData("Pack|name")]
    [InlineData("Pack?name")]
    [InlineData("Pack*name")]
    [InlineData("Pack/name")]
    [InlineData("Pack\\name")]
    [InlineData("Pack\"name")]
    public void Validate_ForbiddenChars_Fail(string name)
    {
        NameValidator.Validate(name).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Pack.")]
    [InlineData("Pack ")]
    [InlineData(" Pack")]
    public void Validate_TrailingOrLeadingDotsSpaces_Fail(string name)
    {
        NameValidator.Validate(name).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("aux")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("lpt9")]
    [InlineData("CON.txt")]
    public void Validate_ReservedNames_Fail(string name)
    {
        NameValidator.Validate(name).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ControlChar_Fail()
    {
        NameValidator.Validate("Pack\nName").IsValid.Should().BeFalse();
        NameValidator.Validate("Pack\tName").IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_TooLong_Fail()
    {
        var longName = new string('a', 201);
        NameValidator.Validate(longName).IsValid.Should().BeFalse();
    }
}
