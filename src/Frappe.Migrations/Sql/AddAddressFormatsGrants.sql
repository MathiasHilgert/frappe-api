-- Reference data is written only by the migrator's seed, so the app role reads it and nothing more.
GRANT SELECT ON geo.address_formats TO frappe_app;
