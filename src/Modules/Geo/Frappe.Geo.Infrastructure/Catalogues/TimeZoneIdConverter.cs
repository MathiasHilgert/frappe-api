using Frappe.Geo.Contracts.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Frappe.Geo.Infrastructure.Catalogues;

/// <summary>Stores a <see cref="TimeZoneId"/> as its id, read back as stored: a link such as Europe/Kiev stays itself.</summary>
internal sealed class TimeZoneIdConverter()
    : ValueConverter<TimeZoneId, string>(
        static zone => zone.Value,
        static id => TimeZoneId.Create(id).Value
    );
