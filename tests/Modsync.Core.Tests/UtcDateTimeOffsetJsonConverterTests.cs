using System.Text.Json;
using FluentAssertions;
using Modsync.Core.Models.Manifest;

namespace Modsync.Core.Tests;

public class UtcDateTimeOffsetJsonConverterTests
{
    private static readonly DateTimeOffset Sample =
        new(2026, 9, 16, 21, 30, 17, 310, TimeSpan.Zero);

    // ------------------------------------------------------------------
    //  Write
    // ------------------------------------------------------------------

    [Fact]
    public void Write_UtcOffset_WritesZFormat()
    {
        var json = JsonSerializer.Serialize(Sample, ManifestJson.Options);
        json.Should().Be("\"2026-09-16T21:30:17.310Z\"");
    }

    [Fact]
    public void Write_NonUtcOffset_ConvertsToUtcWithZ()
    {
        // +03:00 → сдвиг в UTC.
        var moscow = new DateTimeOffset(2026, 9, 17, 0, 30, 17, 310, TimeSpan.FromHours(3));
        var json = JsonSerializer.Serialize(moscow, ManifestJson.Options);
        json.Should().Be("\"2026-09-16T21:30:17.310Z\"");
    }

    [Fact]
    public void Write_TruncatesSubMillisecondPrecision()
    {
        // .NET хранит тики; формат — миллисекунды. Проверяем, что не пишем 7 знаков.
        var withTicks = new DateTimeOffset(
            2026, 9, 16, 21, 30, 17, TimeSpan.Zero)
            .AddTicks(1234567); // 0.1234567 сек
        var json = JsonSerializer.Serialize(withTicks, ManifestJson.Options);
        // .fff → три знака после точки
        json.Should().Match("""
            "2026-09-16T21:30:1?.123Z"
            """);
    }

    // ------------------------------------------------------------------
    //  Read
    // ------------------------------------------------------------------

    [Fact]
    public void Read_ZFormat_ReturnsUtcOffset()
    {
        var json = "\"2026-09-16T21:30:17.310Z\"";
        var parsed = JsonSerializer.Deserialize<DateTimeOffset>(json, ManifestJson.Options);

        parsed.Should().Be(Sample);
        parsed.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Read_PlusZeroOffset_ReturnsUtcOffset()
    {
        var json = "\"2026-09-16T21:30:17.310+00:00\"";
        var parsed = JsonSerializer.Deserialize<DateTimeOffset>(json, ManifestJson.Options);

        parsed.Should().Be(Sample);
        parsed.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Read_NonUtcOffset_AdjustsToUtc()
    {
        var json = "\"2026-09-17T00:30:17.310+03:00\"";
        var parsed = JsonSerializer.Deserialize<DateTimeOffset>(json, ManifestJson.Options);

        parsed.Should().Be(Sample);
        parsed.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Read_NoOffset_AssumesUtc()
    {
        var json = "\"2026-09-16T21:30:17.310\"";
        var parsed = JsonSerializer.Deserialize<DateTimeOffset>(json, ManifestJson.Options);

        parsed.Should().Be(Sample);
        parsed.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Read_NoMilliseconds_Parses()
    {
        var json = "\"2026-09-16T21:30:17Z\"";
        var parsed = JsonSerializer.Deserialize<DateTimeOffset>(json, ManifestJson.Options);

        parsed.Should().Be(new DateTimeOffset(2026, 9, 16, 21, 30, 17, TimeSpan.Zero));
    }

    [Fact]
    public void Read_EmptyString_Throws()
    {
        var json = "\"\"";
        var act = () => JsonSerializer.Deserialize<DateTimeOffset>(json, ManifestJson.Options);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_NonString_Throws()
    {
        var json = "12345";
        var act = () => JsonSerializer.Deserialize<DateTimeOffset>(json, ManifestJson.Options);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_InvalidString_Throws()
    {
        var json = "\"not-a-date\"";
        var act = () => JsonSerializer.Deserialize<DateTimeOffset>(json, ManifestJson.Options);
        act.Should().Throw<JsonException>();
    }

    // ------------------------------------------------------------------
    //  Roundtrip
    // ------------------------------------------------------------------

    [Fact]
    public void Roundtrip_UtcInstance_PreservesValue()
    {
        var json = JsonSerializer.Serialize(Sample, ManifestJson.Options);
        var back = JsonSerializer.Deserialize<DateTimeOffset>(json, ManifestJson.Options);
        back.Should().Be(Sample);
    }
}
