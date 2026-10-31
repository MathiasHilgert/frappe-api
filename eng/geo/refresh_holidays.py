# /// script
# requires-python = ">=3.12"
# dependencies = ["holidays==0.107"]
# ///
"""Regenerates the geo holidays seed from pinned sources; run offline by a developer, never by tests.

Sources:
- vacanza/holidays 0.107 (MIT), Argentina, public category: the national holidays and those of the 24 subdivisions
  (ISO 3166-2 AR-<letter>), Spanish names (es) and their English translations (en_US catalogue), one row per holiday, never merged by date.
- eng/geo/holiday-names-pt.tsv: hand-translated Portuguese names keyed by holiday_key.

vacanza gives names only, so class and origin are mapped here from the legal category of each national holiday
(Law 27.399, the yearly bridge-day decree, DNU 1103/2026); check the result by hand against the yearly decree.

The output is deterministic: rows sorted by date, national first, then subdivision and key; LF line ends.
"""

# TODO: add holidays for another country when Frappé launches there (Argentina only for now)

import csv
import gettext
import pathlib
import re
import unicodedata

import holidays

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "src/Modules/Geo/Frappe.Geo.Infrastructure/Seed/holidays.tsv"
NAMES_PT = ROOT / "eng/geo/holiday-names-pt.tsv"
COUNTRY = "AR"
YEARS = (2026, 2027)
HEADER = [
    "country_code",
    "subdivision_code",
    "date",
    "holiday_key",
    "class",
    "origin",
    "name_en",
    "name_es",
    "name_pt",
]

FIXED = ("Holiday", "Fixed")
MOVABLE = ("Holiday", "Movable")

# The legal category of each national holiday, by its vacanza Spanish name.
NATIONAL = {
    "Año Nuevo": FIXED,
    "Lunes de Carnaval": FIXED,
    "Martes de Carnaval": FIXED,
    "Día Nacional de la Memoria por la Verdad y la Justicia": FIXED,
    "Día del Veterano y de los Caidos en la Guerra de Malvinas": FIXED,
    "Jueves Santo": ("NonWorkingDay", "Fixed"),
    "Viernes Santo": FIXED,
    "Día del Trabajo": FIXED,
    "Día de la Revolución de Mayo": FIXED,
    "Paso a la Inmortalidad del General Don Martín Miguel de Güemes": MOVABLE,
    "Paso a la Inmortalidad del General Don Manuel Belgrano": FIXED,
    "Día de la Independencia": FIXED,
    "Paso a la Inmortalidad del General Don José de San Martín": MOVABLE,
    "Día del Respeto a la Diversidad Cultural": MOVABLE,
    "Día de la Soberanía Nacional": MOVABLE,
    "Inmaculada Concepción de María": FIXED,
    "Navidad": FIXED,
    "Feriado con fines turísticos": ("Holiday", "Bridge"),
    "Visita de Su Santidad el Papa León XIV": ("Holiday", "Decreed"),
}


def slug(name: str) -> str:
    ascii_name = unicodedata.normalize("NFKD", name).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]+", "-", ascii_name.lower()).strip("-")


# vacanza's names are Spanish message ids; its en_US catalogue translates each one. Pairing the two calendars by
# position instead would mismatch days with two holidays, since each language lists them in its own order.
ENGLISH = gettext.translation(
    COUNTRY, pathlib.Path(holidays.__file__).parent / "locale", languages=["en_US"]
)


def calendar(year: int, subdivision: str | None):
    return holidays.country_holidays(
        COUNTRY, subdiv=subdivision, years=year, language="es", categories=("public",)
    )


def rows_of(year: int, subdivision: str | None) -> list[tuple[str, ...]]:
    es, national = calendar(year, subdivision), calendar(year, None)
    rows = []
    for day in sorted(es):
        for name_es in es.get_list(day):
            name_en = ENGLISH.gettext(name_es)
            if name_en == name_es:
                raise SystemExit(f"No English name for {name_es}.")
            if subdivision is None:
                kind = NATIONAL[name_es]
            elif name_es in national.get_list(day):
                continue
            else:
                # TODO: map provincial holiday class/origin from each provincial law (vacanza gives names only;
                # revisit when a province is onboarded)
                kind = NATIONAL.get(name_es, FIXED)
            code = f"{COUNTRY}-{subdivision}" if subdivision else ""
            rows.append((code, day.isoformat(), slug(name_en), *kind, name_en, name_es))
    return rows


def main() -> None:
    with NAMES_PT.open(encoding="utf-8", newline="") as file:
        portuguese = {row["holiday_key"]: row["name_pt"] for row in csv.DictReader(file, delimiter="\t")}
    rows = [
        row
        for year in YEARS
        for subdivision in (None, *holidays.AR.subdivisions)
        for row in rows_of(year, subdivision)
    ]
    keys = [(code, day, key) for code, day, key, *_ in rows]
    if len(keys) != len(set(keys)):
        raise SystemExit("Two holidays share a subdivision, date and key.")
    missing = sorted({row[2] for row in rows} - portuguese.keys())
    if missing:
        raise SystemExit(f"No Portuguese name in {NAMES_PT.name} for: {', '.join(missing)}")
    rows.sort(key=lambda row: (row[1], row[0] != "", row[0], row[2]))
    lines = ["\t".join(HEADER)]
    lines += ["\t".join((COUNTRY, *row, portuguese[row[2]])) for row in rows]
    OUTPUT.write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
