# /// script
# requires-python = ">=3.12"
# dependencies = []
# ///
"""Regenerates the geo currencies seed from pinned sources; run offline by a developer, never by tests.

Sources:
- SIX ISO 4217 List One (active currencies) and List Three (historic currencies); List One wins on a repeated code.
  List One gives the minor unit ("N.A." becomes empty) and the English name.
- CLDR 48 currency display names for es and pt.
- CLDR 48 supplemental currencyData fractions for the minor unit of List Three currencies, which List Three does not
  carry (DEFAULT is 2 digits).

Only the ACTIVE codes are seeded Active; every other List One code is seeded Retired, so it is kept but neither
listed nor selectable.

The output is deterministic: rows sorted by code, LF line ends, no timestamps.
"""

import json
import pathlib
import urllib.request
import xml.etree.ElementTree as ET

SIX = "https://www.six-group.com/dam/download/financial-information/data-center/iso-currrency/lists"
CLDR = "https://raw.githubusercontent.com/unicode-org/cldr-json/48.0.0/cldr-json"
OUTPUT = (
    pathlib.Path(__file__).resolve().parents[2]
    / "src/Modules/Geo/Frappe.Geo.Infrastructure/Seed/currencies.tsv"
)
# TODO: activate more currencies (when Frappé launches in another country or a business needs one)
ACTIVE = {"ARS", "BRL", "EUR", "USD"}
HEADER = ["currency_code", "numeric_code", "minor_unit", "status", "name_en", "name_es", "name_pt"]


def fetch(url: str) -> bytes:
    with urllib.request.urlopen(url, timeout=60) as response:
        return response.read()


def text(entry: ET.Element, tag: str) -> str:
    return (entry.findtext(tag) or "").strip()


def cldr_names(locale: str) -> dict[str, str]:
    data = json.loads(fetch(f"{CLDR}/cldr-numbers-full/main/{locale}/currencies.json"))
    currencies = data["main"][locale]["numbers"]["currencies"]
    return {code: value["displayName"] for code, value in currencies.items() if "displayName" in value}


def main() -> None:
    fractions = json.loads(fetch(f"{CLDR}/cldr-core/supplemental/currencyData.json"))["supplemental"][
        "currencyData"
    ]["fractions"]
    default_digits = fractions["DEFAULT"]["_digits"]
    rows: dict[str, list[str]] = {}

    for entry in ET.fromstring(fetch(f"{SIX}/list-one.xml")).iter("CcyNtry"):
        code = text(entry, "Ccy")
        if not code or code in rows:
            continue
        minor = text(entry, "CcyMnrUnts")
        rows[code] = [
            code,
            text(entry, "CcyNbr"),
            "" if minor == "N.A." else minor,
            "Active" if code in ACTIVE else "Retired",
            text(entry, "CcyNm"),
        ]

    # The latest withdrawal of a code names it; List Three dates sort as text (YYYY-MM).
    historic = sorted(
        ET.fromstring(fetch(f"{SIX}/list-three.xml")).iter("HstrcCcyNtry"),
        key=lambda entry: text(entry, "WthdrwlDt"),
        reverse=True,
    )
    for entry in historic:
        code = text(entry, "Ccy")
        if not code or code in rows:
            continue
        digits = fractions.get(code, {}).get("_digits", default_digits)
        rows[code] = [code, text(entry, "CcyNbr"), digits, "Retired", text(entry, "CcyNm")]

    es, pt = cldr_names("es"), cldr_names("pt")
    lines = ["\t".join(HEADER)]
    for code in sorted(rows):
        lines.append("\t".join([*rows[code], es.get(code, ""), pt.get(code, "")]))
    OUTPUT.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
