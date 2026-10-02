using FluentAssertions;
using Modsync.Core.Models.Hashing;

namespace Modsync.Core.Tests;

public class XxHash64ValueParseTests
{
    [Theory]
    [InlineData("xxh64:0000000000000000")]
    [InlineData("xxh64:ffffffffffffffff")]
    [InlineData("xxh64:FFFFFFFFFFFFFFFF")]
    [InlineData("xxh64:b48aa9bea422799e")]
    [InlineData("xxh64:B48AA9BEA422799E")]
    [InlineData("xxh64:00000000B48AA9BE")]
    public void Parse_ValidHashes_Succeeds(string input)
    {
        var act = () => XxHash64Value.Parse(input);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("xxh64:")]
    [InlineData("xxh64: B48AA9BEA422799E")]      // пробел после :
    [InlineData("xxh64:B48AA9BEA422799E ")]      // пробел в конце
    [InlineData("xxh64:  B48AA9BEA422799E")]     // два пробела
    [InlineData("xxh64:B48AA9BEA422799")]        // 15 символов
    [InlineData("xxh64:B48AA9BEA422799E00")]     // 17 символов
    [InlineData("xxh64:B48AA9BEA422799G")]       // не-hex символ 'G'
    [InlineData("xxh64-B48AA9BEA422799E")]       // дефис
    [InlineData("b48aa9bea422799e")]              // нет префикса
    [InlineData("xxh32:B48AA9BEA422799E")]       // неверный префикс
    public void Parse_InvalidHashes_Throws(string? input)
    {
        var act = () => XxHash64Value.Parse(input!);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_Roundtrip_Works()
    {
        var original = new XxHash64Value(0xB48AA9BEA422799E);
        var text = original.ToString();
        var parsed = XxHash64Value.Parse(text);
        parsed.Should().Be(original);
    }

    [Fact]
    public void ToString_Always16HexChars()
    {
        var h1 = new XxHash64Value(0x1);
        var h2 = new XxHash64Value(ulong.MaxValue);

        h1.ToString().Should().Be("xxh64:0000000000000001");
        h2.ToString().Should().Be("xxh64:ffffffffffffffff");
    }
}
