# /// script
# requires-python = ">=3.12"
# dependencies = []
# ///
"""Regenerates the geo subdivisions seed from pinned sources; run offline by a developer, never by tests.

Sources:
- Debian iso-codes (LGPL-2.1-or-later) data/iso_3166-2.json: the ISO 3166-2 codes and English names.
- Debian iso-codes iso_3166-2/es.po and pt.po: the Spanish and Portuguese names; an untranslated entry keeps the
  English name, as gettext does.

The output is deterministic: rows sorted by code, LF line ends, no timestamps.
"""

# TODO: add subdivisions for another country when Frappé launches there (Argentina only for now)

import json
import pathlib
import re
import urllib.request

ISO_CODES = "https://salsa.debian.org/iso-codes-team/iso-codes/-/raw/v4.20.1"
COUNTRIES = ["AR"]
OUTPUT = (
    pathlib.Path(__file__).resolve().parents[2]
    / "src/Modules/Geo/Frappe.Geo.Infrastructure/Seed/subdivisions.tsv"
)
HEADER = ["subdivision_code", "country_code", "name_en", "name_es", "name_pt"]
ENTRY = re.compile(r'^msgid "(.*)"\nmsgstr "(.*)"$', re.MULTILINE)


def fetch(url: str) -> bytes:
    with urllib.request.urlopen(url, timeout=60) as response:
        return response.read()


def translations(locale: str) -> dict[str, str]:
    text = fetch(f"{ISO_CODES}/iso_3166-2/{locale}.po").decode("utf-8")
    return {msgid: msgstr for msgid, msgstr in ENTRY.findall(text) if msgstr}


def main() -> None:
    subdivisions = json.loads(fetch(f"{ISO_CODES}/data/iso_3166-2.json"))["3166-2"]
    es, pt = translations("es"), translations("pt")
    lines = ["\t".join(HEADER)]
    for subdivision in sorted(subdivisions, key=lambda entry: entry["code"]):
        code, name = subdivision["code"], subdivision["name"]
        country = code.split("-")[0]
        if country not in COUNTRIES:
            continue
        lines.append("\t".join([code, country, name, es.get(name, name), pt.get(name, name)]))
    OUTPUT.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
