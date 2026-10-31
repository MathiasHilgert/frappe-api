using System.Buffers;
using System.Text;
using System.Text.Json;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Infrastructure.Serialization;

namespace Frappe.Geo.UnitTests.Serialization;

public sealed class GeoJsonConvertersTests
{
    private static readonly JsonSerializerOptions _options = Options();

    [Fact]
    public void A_CountryCode_is_written_as_its_JSON_primitive()
    {
        CountryCode value = CountryCode.Create("AR").Value;

        string json = JsonSerializer.Serialize(value, _options);

        json.ShouldBe("\"AR\"");
    }

    [Fact]
    public void A_CountryCode_is_read_from_its_JSON_primitive_through_create()
    {
        CountryCode? value = JsonSerializer.Deserialize<CountryCode>("\"ar\"", _options);

        value.ShouldBe(CountryCode.Create("AR").Value);
    }

    [Fact]
    public void An_invalid_CountryCode_JSON_value_is_rejected()
    {
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<CountryCode>("\"ARG\"", _options)
        );
    }

    [Fact]
    public void A_CurrencyCode_is_written_as_its_JSON_primitive()
    {
        CurrencyCode value = CurrencyCode.Create("ARS").Value;

        string json = JsonSerializer.Serialize(value, _options);

        json.ShouldBe("\"ARS\"");
    }

    [Fact]
    public void A_CurrencyCode_is_read_from_its_JSON_primitive_through_create()
    {
        CurrencyCode? value = JsonSerializer.Deserialize<CurrencyCode>("\"ars\"", _options);

        value.ShouldBe(CurrencyCode.Create("ARS").Value);
    }

    [Fact]
    public void An_invalid_CurrencyCode_JSON_value_is_rejected()
    {
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<CurrencyCode>("\"AR\"", _options)
        );
    }

    [Fact]
    public void A_MinorUnit_is_written_as_its_JSON_primitive()
    {
        MinorUnit value = MinorUnit.Create(2).Value;

        string json = JsonSerializer.Serialize(value, _options);

        json.ShouldBe("2");
    }

    [Fact]
    public void A_MinorUnit_is_read_from_its_JSON_primitive_through_create()
    {
        MinorUnit? value = JsonSerializer.Deserialize<MinorUnit>("2", _options);

        value.ShouldBe(MinorUnit.Create(2).Value);
    }

    [Fact]
    public void An_invalid_MinorUnit_JSON_value_is_rejected()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<MinorUnit>("5", _options));
    }

    [Fact]
    public void A_SubdivisionCode_is_written_as_its_JSON_primitive()
    {
        SubdivisionCode value = SubdivisionCode.Create("AR-X").Value;

        string json = JsonSerializer.Serialize(value, _options);

        json.ShouldBe("\"AR-X\"");
    }

    [Fact]
    public void A_SubdivisionCode_is_read_from_its_JSON_primitive_through_create()
    {
        SubdivisionCode? value = JsonSerializer.Deserialize<SubdivisionCode>("\"ar-x\"", _options);

        value.ShouldBe(SubdivisionCode.Create("AR-X").Value);
    }

    [Fact]
    public void An_invalid_SubdivisionCode_JSON_value_is_rejected()
    {
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<SubdivisionCode>("\"AR\"", _options)
        );
    }

    [Fact]
    public void A_PostalCode_is_written_as_its_JSON_primitive()
    {
        PostalCode value = PostalCode.Create("C1425DQF").Value;

        string json = JsonSerializer.Serialize(value, _options);

        json.ShouldBe("\"C1425DQF\"");
    }

    [Fact]
    public void A_PostalCode_is_read_from_its_JSON_primitive_through_create()
    {
        PostalCode? value = JsonSerializer.Deserialize<PostalCode>("\"c1425dqf\"", _options);

        value.ShouldBe(PostalCode.Create("C1425DQF").Value);
    }

    [Fact]
    public void An_invalid_PostalCode_JSON_value_is_rejected()
    {
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<PostalCode>("\"C1425#\"", _options)
        );
    }

    [Fact]
    public void A_StreetLine_is_written_as_its_JSON_primitive()
    {
        StreetLine value = StreetLine.Create("Calle 1").Value;

        string json = JsonSerializer.Serialize(value, _options);

        json.ShouldBe("\"Calle 1\"");
    }

    [Fact]
    public void A_StreetLine_is_read_from_its_JSON_primitive_through_create()
    {
        StreetLine? value = JsonSerializer.Deserialize<StreetLine>("\" Calle 1 \"", _options);

        value.ShouldBe(StreetLine.Create("Calle 1").Value);
    }

    [Fact]
    public void An_invalid_StreetLine_JSON_value_is_rejected()
    {
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<StreetLine>("\" \"", _options)
        );
    }

    [Fact]
    public void A_TimeZoneId_is_written_as_its_JSON_primitive()
    {
        TimeZoneId value = TimeZoneId.Create("America/Argentina/Cordoba").Value;

        string json = JsonSerializer.Serialize(value, _options);

        json.ShouldBe("\"America/Argentina/Cordoba\"");
    }

    [Fact]
    public void A_TimeZoneId_is_read_from_its_JSON_primitive_through_create()
    {
        TimeZoneId? value = JsonSerializer.Deserialize<TimeZoneId>(
            "\"america/argentina/cordoba\"",
            _options
        );

        value.ShouldBe(TimeZoneId.Create("America/Argentina/Cordoba").Value);
    }

    [Fact]
    public void An_invalid_TimeZoneId_JSON_value_is_rejected()
    {
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<TimeZoneId>("\"America/Nowhere\"", _options)
        );
    }

    [Fact]
    public void A_Locale_is_written_as_its_JSON_primitive()
    {
        Locale value = Locale.Create("es-AR").Value;

        string json = JsonSerializer.Serialize(value, _options);

        json.ShouldBe("\"es-AR\"");
    }

    [Fact]
    public void A_Locale_is_read_from_its_JSON_primitive_through_create()
    {
        Locale? value = JsonSerializer.Deserialize<Locale>("\"es_ar\"", _options);

        value.ShouldBe(Locale.Create("es-AR").Value);
    }

    [Fact]
    public void An_invalid_Locale_JSON_value_is_rejected()
    {
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<Locale>("\"en-x-private\"", _options)
        );
    }

    // Minimal APIs read bodies from a PipeReader, so a value may arrive split across buffer segments.
    [Fact]
    public void A_value_split_across_buffer_segments_is_read_whole()
    {
        Utf8JsonReader reader = new(Segmented("\"a", "r\""));

        CountryCode? value = JsonSerializer.Deserialize<CountryCode>(ref reader, _options);

        value.ShouldBe(CountryCode.Create("AR").Value);
    }

    private static JsonSerializerOptions Options()
    {
        JsonSerializerOptions options = new();
        GeoJsonConverters.Add(options);
        return options;
    }

    private static ReadOnlySequence<byte> Segmented(string first, string second)
    {
        Segment head = new(Encoding.UTF8.GetBytes(first), runningIndex: 0);
        Segment tail = head.Append(Encoding.UTF8.GetBytes(second));
        return new ReadOnlySequence<byte>(head, 0, tail, tail.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            Segment next = new(memory, RunningIndex + Memory.Length);
            Next = next;
            return next;
        }
    }
}
