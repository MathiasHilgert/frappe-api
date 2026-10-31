-- Reference data is written only by the migrator's seed, so the app role reads it and nothing more.
GRANT SELECT ON geo.subdivisions, geo.subdivision_names, geo.cities, geo.locales, geo.locale_names TO frappe_app;
