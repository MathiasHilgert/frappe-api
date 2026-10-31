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

Georef lists some census localities twice: as the locality (8-digit INDEC id) and as an entity of it (10 digits)
with the same name, province and department. Only the shorter id is kept, so no two cities share a label.

Georef publishes these files without a version, so the retrieval date goes in ATTRIBUTION.txt. The output is
deterministic: rows sorted by INDEC id, LF line ends, no timestamps.
"""

# TODO: add cities for another country when Frappé launches there (Argentina only for now)

import csv
import io
import pathlib
import unicodedata
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


# The seed's search folding: accents dropped, lower case, whitespace collapsed.
def fold(text: str) -> str:
    decomposed = unicodedata.normalize("NFD", text)
    plain = "".join(character for character in decomposed if unicodedata.category(character) != "Mn")
    return " ".join(plain.lower().split())


# One locality per folded name, province and department: the shortest INDEC id, then the lexically smaller one.
def deduplicated(localities: list[dict[str, str]], provinces: dict[str, str]) -> list[dict[str, str]]:
    kept: dict[tuple[str, str, str], dict[str, str]] = {}
    for locality in sorted(localities, key=lambda row: (len(row["id"]), row["id"])):
        key = (
            fold(locality["nombre"]),
            provinces[locality["provincia_id"]],
            locality["departamento_nombre"].strip(),
        )
        kept.setdefault(key, locality)
    return list(kept.values())


def main() -> None:
    provinces = {province["id"]: province["iso_id"] for province in rows("provincias")}
    with (HERE / "ar-province-zones.tsv").open(encoding="utf-8") as file:
        zones = {row["subdivision_code"]: row["time_zone_id"] for row in csv.DictReader(file, delimiter="\t")}
    lines = ["\t".join(HEADER)]
    for locality in sorted(deduplicated(rows("localidades"), provinces), key=lambda row: int(row["id"])):
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
