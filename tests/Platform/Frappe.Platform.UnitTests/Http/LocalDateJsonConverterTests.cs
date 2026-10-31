using System.Buffers;
using System.Text;
using System.Text.Json;
using Frappe.Platform.Http;
using NodaTime;

namespace Frappe.Platform.UnitTests.Http;

public sealed class LocalDateJsonConverterTests
{
    private static readonly JsonSerializerOptions _options = new()
    {
        Converters = { new LocalDateJsonConverter() },
    };

    [Fact]
    public void A_date_is_written_as_an_iso_string()
    {
        JsonSerializer.Serialize(new LocalDate(2026, 4, 2), _options).ShouldBe("\"2026-04-02\"");
    }

    [Fact]
    public void A_date_split_across_segments_is_read()
    {
        Utf8JsonReader reader = new(Segmented("\"2026-", "04-02\""));
        reader.Read();

        LocalDate date = new LocalDateJsonConverter().Read(ref reader, typeof(LocalDate), _options);

        date.ShouldBe(new LocalDate(2026, 4, 2));
    }

    [Theory]
    [InlineData("\"02/04/2026\"")]
    [InlineData("\"2026-02-30\"")]
    [InlineData("20260402")]
    public void A_malformed_date_fails_to_read(string json)
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<LocalDate>(json, _options));
    }

    private static ReadOnlySequence<byte> Segmented(string first, string second)
    {
        Segment start = new(Encoding.UTF8.GetBytes(first), 0);
        Segment end = start.Append(Encoding.UTF8.GetBytes(second));
        return new ReadOnlySequence<byte>(start, 0, end, end.Memory.Length);
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
