package postgres

// timeZoneColumns selects a time zone.
const timeZoneColumns = `SELECT time_zones.id, time_zones.country_code FROM time_zones`

// listTimeZonesQuery is one page of time zones after the id $1, at most
// $2, in id order.
const listTimeZonesQuery = "-- name: geo.list_time_zones\n" + timeZoneColumns + `
WHERE time_zones.id > $1
ORDER BY time_zones.id
LIMIT $2`

// timeZonesByIDQuery is the time zones with the ids $1.
const timeZonesByIDQuery = "-- name: geo.time_zones_by_id\n" + timeZoneColumns + `
WHERE time_zones.id = ANY($1)`
