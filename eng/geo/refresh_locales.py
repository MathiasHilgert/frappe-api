# /// script
# requires-python = ">=3.12"
# dependencies = []
# ///
"""Regenerates the geo locales seed from pinned sources; run offline by a developer, never by tests.

Sources (Unicode CLDR 48, cldr-json 48.0.0):
- cldr-core coverageLevels.json: every locale whose effective coverage is modern (the root locale und excluded).
- cldr-localenames-full languages, scripts, territories, variants and localeDisplayNames: each locale's display name
  in en, es and pt, and its endonym (its name in itself), composed once here with the CLDR locale display pattern.
- cldr-misc-full layout.json: the text direction, from the character order.

The output is deterministic: rows sorted by tag, LF line ends, no timestamps.
"""

import concurrent.futures
import functools
import json
import pathlib
import urllib.error
import urllib.request

CLDR = "https://raw.githubusercontent.com/unicode-org/cldr-json/48.0.0/cldr-json"
OUTPUT = (
    pathlib.Path(__file__).resolve().parents[2]
    / "src/Modules/Geo/Frappe.Geo.Infrastructure/Seed/locales.tsv"
)
HEADER = ["locale_tag", "language_code", "text_direction", "endonym", "name_en", "name_es", "name_pt"]
NAME_LOCALES = ["en", "es", "pt"]


def fetch(path: str) -> dict:
    with urllib.request.urlopen(f"{CLDR}/{path}", timeout=60) as response:
        return json.loads(response.read())


@functools.cache
def display_names(locale: str) -> dict:
    def part(name: str) -> dict:
        try:
            data = fetch(f"cldr-localenames-full/main/{locale}/{name}.json")
        except urllib.error.HTTPError as error:
            # A locale without names of one kind (often variants) publishes no file for it.
            if error.code != 404:
                raise
            return {name: {}}
        return data["main"][locale]["localeDisplayNames"]

    pattern = part("localeDisplayNames")["localeDisplayPattern"]
    return {
        "languages": part("languages")["languages"],
        "scripts": part("scripts")["scripts"],
        "territories": part("territories")["territories"],
        "variants": part("variants")["variants"],
        "pattern": pattern["localePattern"],
        "separator": pattern["localeSeparator"],
    }


def subtag_name(names: dict, subtag: str) -> str:
    if len(subtag) == 4 and subtag.isalpha():
        kind, key = "scripts", subtag
    elif len(subtag) == 2 or (len(subtag) == 3 and subtag.isdigit()):
        kind, key = "territories", subtag
    else:
        kind, key = "variants", subtag.upper()
    # A locale missing a subtag's name falls back to English, then to the subtag itself, as CLDR display does.
    return names[kind].get(key) or display_names("en")[kind].get(key) or subtag


# The longest leading run of subtags with a language name of its own, then the other subtags in parentheses.
def display_name(tag: str, locale: str) -> str:
    names = display_names(locale)
    subtags = tag.split("-")
    for length in range(len(subtags), 0, -1):
        base = names["languages"].get("-".join(subtags[:length]))
        if base is not None:
            break
    else:
        raise KeyError(f"{locale} has no language name for {tag}")
    rest = [subtag_name(names, subtag) for subtag in subtags[length:]]
    if not rest:
        return base
    details = functools.reduce(lambda left, right: names["separator"].format(left, right), rest)
    return names["pattern"].format(base, details)


def direction(tag: str) -> str:
    layout = fetch(f"cldr-misc-full/main/{tag}/layout.json")["main"][tag]["layout"]
    return "rtl" if layout["orientation"]["characterOrder"] == "right-to-left" else "ltr"


def row(tag: str) -> str:
    fields = [tag, tag.split("-")[0], direction(tag), display_name(tag, tag)]
    fields += [display_name(tag, locale) for locale in NAME_LOCALES]
    return "\t".join(fields)


def main() -> None:
    coverage = fetch("cldr-core/coverageLevels.json")["effectiveCoverageLevels"]
    tags = sorted(tag for tag, level in coverage.items() if level == "modern" and tag != "und")
    for locale in NAME_LOCALES:
        display_names(locale)
    with concurrent.futures.ThreadPoolExecutor(max_workers=16) as pool:
        rows = list(pool.map(row, tags))
    OUTPUT.write_text("\n".join(["\t".join(HEADER), *rows]) + "\n", encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
