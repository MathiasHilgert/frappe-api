package domain

// TimeZone is an IANA time zone.
type TimeZone struct {
	// CountryCode is the ISO 3166-1 alpha-2 code of its country, or nil
	// for zones of no country (for example Etc/UTC).
	CountryCode *string
	// ID is the IANA identifier, for example America/Argentina/Cordoba.
	ID string
}
