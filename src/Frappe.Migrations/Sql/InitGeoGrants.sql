-- Reference data is written only by the migrator's seed, so the app role reads it and nothing more.
GRANT SELECT ON geo.currencies, geo.currency_names, geo.countries, geo.country_names TO frappe_app;
