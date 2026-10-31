# /// script
# requires-python = ">=3.12"
# dependencies = []
# ///
"""Regenerates the geo countries seed from pinned sources; run offline by a developer, never by tests.

Sources:
- CLDR 48 supplemental codeMappings: the two-letter territories with an alpha-3 and a numeric code, without the
  ISO 3166-1 user-assigned codes (AA, QM-QZ, XA-XZ, ZZ), the CLDR groupings (EU, EZ, QO, UN), XK, and the deprecated
  codes CLDR no longer names (AN, BU, CS, ...).
- CLDR 48 supplemental currencyData: the current tender (no end date, not marked non-tender) with the latest start.
- CLDR 48 territory display names for en, es and pt.
- Google libphonenumber PhoneNumberMetadata.xml: the territory's countryCode attribute, the calling code.

The output is deterministic: rows sorted by code, LF line ends, no timestamps.
"""

import json
import pathlib
import re
import urllib.request
import xml.etree.ElementTree as ET

CLDR = "https://raw.githubusercontent.com/unicode-org/cldr-json/48.0.0/cldr-json"
LIBPHONENUMBER = "https://raw.githubusercontent.com/google/libphonenumber/v9.0.42/resources/PhoneNumberMetadata.xml"
OUTPUT = (
    pathlib.Path(__file__).resolve().parents[2]
    / "src/Modules/Geo/Frappe.Geo.Infrastructure/Seed/countries.tsv"
)
HEADER = [
    "country_code",
    "alpha3",
    "numeric_code",
    "default_currency_code",
    "calling_code",
    "name_en",
    "name_es",
    "name_pt",
]
EXCLUDED = {"EU", "EZ", "QO", "UN", "XK", "ZZ"}
USER_ASSIGNED = re.compile(r"^(AA|Q[M-Z]|X[A-Z]|ZZ)$")


def fetch(url: str) -> bytes:
    with urllib.request.urlopen(url, timeout=60) as response:
        return response.read()


def supplemental(name: str) -> dict:
    return json.loads(fetch(f"{CLDR}/cldr-core/supplemental/{name}.json"))["supplemental"]


def names(locale: str) -> dict[str, str]:
    data = json.loads(fetch(f"{CLDR}/cldr-localenames-full/main/{locale}/territories.json"))
    return data["main"][locale]["localeDisplayNames"]["territories"]


def tender(history: list[dict]) -> str:
    current = [
        (dates.get("_from", ""), code)
        for entry in history
        for code, dates in entry.items()
        if "_to" not in dates and dates.get("_tender") != "false"
    ]
    return max(current)[1] if current else ""


def main() -> None:
    mappings = supplemental("codeMappings")["codeMappings"]
    regions = supplemental("currencyData")["currencyData"]["region"]
    en, es, pt = names("en"), names("es"), names("pt")
    calling = {
        territory.get("id"): territory.get("countryCode", "")
        for territory in ET.fromstring(fetch(LIBPHONENUMBER)).iter("territory")
    }
    lines = ["\t".join(HEADER)]
    for code in sorted(mappings):
        codes = mappings[code]
        if (
            len(code) != 2
            or not code.isalpha()
            or "_alpha3" not in codes
            or "_numeric" not in codes
            or code in EXCLUDED
            or USER_ASSIGNED.match(code)
            or code not in en
        ):
            continue
        lines.append(
            "\t".join(
                [
                    code,
                    codes["_alpha3"],
                    codes["_numeric"],
                    tender(regions.get(code, [])),
                    calling.get(code, ""),
                    en[code],
                    es.get(code, ""),
                    pt.get(code, ""),
                ]
            )
        )
    OUTPUT.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
