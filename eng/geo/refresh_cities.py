# /// script
# requires-python = ">=3.12"
# dependencies = []
# ///
"""Regenerates the geo cities seed from Georef; run offline by a developer, never by tests.

Sources:
- Georef (Servicio de Normalización de Datos Geográficos de Argentina, INDEC; CC BY 4.0) localidades.csv: the INDEC
  id, the official name, the province and the department of every locality.
- Georef provincias.csv: the ISO 3166-2 code of each province.
- ar-province-zones.tsv next to this script: the IANA zone of each province, after the tzdb zone.tab comments;
  Georef carries no zone.

Georef publishes these files without a version, so the retrieval date goes in ATTRIBUTION.txt. The output is
deterministic: rows sorted by INDEC id, LF line ends, no timestamps.
"""

# TODO: add cities for another country when Frappé launches there (Argentina only for now)

import csv
import io
import pathlib
import urllib.request

GEOREF = "https://infra.datos.gob.ar/georef"
HERE = pathlib.Path(__file__).resolve().parent
OUTPUT = HERE.parents[1] / "src/Modules/Geo/Frappe.Geo.Infrastructure/Seed/cities.tsv"
HEADER = ["indec_id", "name", "subdivision_code", "department_name", "time_zone_id"]


def rows(name: str) -> list[dict[str, str]]:
    # The CDN in front of Georef refuses Python's default user agent.
    request = urllib.request.Request(f"{GEOREF}/{name}.csv", headers={"User-Agent": "frappe-geo-refresh"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return list(csv.DictReader(io.StringIO(response.read().decode("utf-8"))))


def main() -> None:
    provinces = {province["id"]: province["iso_id"] for province in rows("provincias")}
    with (HERE / "ar-province-zones.tsv").open(encoding="utf-8") as file:
        zones = {row["subdivision_code"]: row["time_zone_id"] for row in csv.DictReader(file, delimiter="\t")}
    lines = ["\t".join(HEADER)]
    for locality in sorted(rows("localidades"), key=lambda row: int(row["id"])):
        subdivision = provinces[locality["provincia_id"]]
        fields = [
            locality["id"],
            locality["nombre"].strip(),
            subdivision,
            locality["departamento_nombre"].strip(),
            zones[subdivision],
        ]
        if any("\t" in field or "\n" in field for field in fields):
            raise ValueError(f"locality {locality['id']} has a tab or a line break")
        lines.append("\t".join(fields))
    OUTPUT.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
