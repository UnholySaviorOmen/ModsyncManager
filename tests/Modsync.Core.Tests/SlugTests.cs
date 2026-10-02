using FluentAssertions;
using Modsync.Core.Identity;

namespace Modsync.Core.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("SkyUI", "skyui")]
    [InlineData("SkyUI_5_1-3863-5-1", "skyui-5-1-3863-5-1")]
    [InlineData("[NoDelete]SkyUI", "nodelete-skyui")]
    [InlineData("Mod Name", "mod-name")]
    [InlineData("Mod   With   Spaces", "mod-with-spaces")]
    [InlineData("UPPER_CASE", "upper-case")]
    [InlineData("123abc", "123abc")]
    [InlineData("a-b-c", "a-b-c")]
    public void From_CommonCases(string input, string expected)
    {
        Slug.From(input).Should().Be(expected);
    }

    [Fact]
    public void From_TrimsLeadingAndTrailingDashes()
    {
        Slug.From("---SkyUI---").Should().Be("skyui");
        Slug.From("___SkyUI___").Should().Be("skyui");
    }

    [Fact]
    public void From_CollapsesRepeatedSeparators()
    {
        Slug.From("a---b___c   d").Should().Be("a-b-c-d");
    }

    [Fact]
    public void From_OnlyCyrillic_Throws()
    {
        // Кириллица не транслитерируется, а превращается в дефисы,
        // которые потом обрезаются. Результат — пустая строка → исключение.
        var act = () => Slug.From("Мод");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_CyrillicWithLatin_KeepsLatinParts()
    {
        // Кириллица выбрасывается, латиница остаётся.
        Slug.From("Мод SkyUI").Should().Be("skyui");
        Slug.From("SkyUI Мод").Should().Be("skyui");
        Slug.From("МодSkyUIМод").Should().Be("skyui");
    }

    [Fact]
    public void From_OnlyInvalidChars_Throws()
    {
        var act = () => Slug.From("___");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_Empty_Throws()
    {
        var act = () => Slug.From("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_WhitespaceOnly_Throws()
    {
        var act = () => Slug.From("   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_LongInput_TruncatesToMaxLength()
    {
        var longInput = new string('a', 200);
        var result = Slug.From(longInput);
        result.Length.Should().Be(Slug.MaxLength);
    }

    [Fact]
    public void From_LongInputWithTrailingDashAfterTruncate_RemovesDash()
    {
        // "a" * 79 + "-" + "b" → обрезка на 80 даст "a"*79 + "-",
        // TrimEnd('-') уберёт дефис.
        var input = new string('a', 79) + "-b";
        var result = Slug.From(input);
        result.Length.Should().Be(79);
        result.Should().Be(new string('a', 79));
    }

    [Theory]
    [InlineData("SkyUI.7z", "skyui")]
    [InlineData("SkyUI_5_1-3863-5-1.7z", "skyui-5-1-3863-5-1")]
    [InlineData("Mod.tar.gz", "mod-tar")]
    [InlineData(".hidden", "hidden")]
    [InlineData("no-extension", "no-extension")]
    public void FromFileName_StripsExtension(string input, string expected)
    {
        Slug.FromFileName(input).Should().Be(expected);
    }

    [Fact]
    public void FromFileName_Empty_Throws()
    {
        var act = () => Slug.FromFileName("");
        act.Should().Throw<ArgumentException>();
    }
}
