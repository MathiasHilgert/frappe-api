using ErrorOr;
using Frappe.Geo.Application.Timezones.ListTimezones;

namespace Frappe.Geo.UnitTests.Timezones;

public sealed class ListTimezonesHandlerTests
{
    [Fact]
    public async Task The_list_is_what_the_read_port_returns()
    {
        TimezoneDto[] zones =
        [
            new("America/Argentina/Cordoba", null),
            new("America/Cordoba", "America/Argentina/Cordoba"),
        ];

        ErrorOr<IReadOnlyList<TimezoneDto>> result = await new ListTimezonesHandler(
            new FixedTimezones(zones)
        ).Handle(new ListTimezonesQuery(), TestContext.Current.CancellationToken);

        result.IsError.ShouldBeFalse();
        result.Value.ShouldBe(zones);
    }

    private sealed class FixedTimezones(IReadOnlyList<TimezoneDto> zones)
        : IListTimezonesQueryService
    {
        public Task<IReadOnlyList<TimezoneDto>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(zones);
    }
}
