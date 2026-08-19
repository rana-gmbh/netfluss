#!/usr/bin/env python3
"""Generate .NET .resx files from the macOS Localizable.strings catalogues.

The macOS .strings files stay the single source of truth for both platforms: 400 keys
across en/de/zh-Hans/zh-Hant already exist and are reviewed, and forking them would
guarantee the two apps drift apart. This script is run at build time (and by CI, which
fails if the checked-in .resx files are stale).

Three things are translated on the way across:

  * Format specifiers. Cocoa writes %@ / %d / %.0f; .NET writes {0} / {1}. Replacement is
    positional and in source order, which is safe here because none of the NetFluss
    strings use explicit %1$@ argument indexes.

  * Platform vocabulary. A string such as "System Default follows the language selected in
    macOS." is wrong on Windows. Rather than silently shipping it, every string matching a
    platform term is written to a review report and, where an override exists, rewritten.

  * Case collisions. .NET resource names fold case; macOS .strings keys do not. Keys that
    differ only in capitalization are stored under a "~N" suffixed name and reassembled at
    lookup time by Localization.L — see COLLISION_SUFFIX below.

Usage:
    python3 strings2resx.py [--check]

--check verifies the generated files are up to date without writing (for CI).
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys
from xml.sax.saxutils import escape

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE_DIR = REPO_ROOT / "Packaging" / "Resources"
OUTPUT_DIR = REPO_ROOT / "windows" / "src" / "NetFluss.Core" / "Resources"
REPORT_PATH = OUTPUT_DIR / "platform-review.md"

# lproj folder -> .resx culture suffix. The empty suffix is the neutral fallback.
LANGUAGES = {
    "en": "",
    "de": ".de",
    "zh-Hans": ".zh-Hans",
    "zh-Hant": ".zh-Hant",
}

ENTRY_RE = re.compile(r'^\s*"((?:[^"\\]|\\.)*)"\s*=\s*"((?:[^"\\]|\\.)*)"\s*;\s*$')
SPECIFIER_RE = re.compile(r"%(?:\d+\$)?(?:[-+ #0]*\d*(?:\.\d+)?)?(?:@|ll[du]|l[du]|[dfsu@])")

# .NET resource names fold case. ResourceWriter hashes them case-insensitively so that
# ResourceSet can offer ignoreCase lookups, and resgen responds to two names differing
# only in capitalization by *dropping the later one with a warning* (MSB3568) rather than
# failing — a green build that is silently missing a string in every language.
#
# macOS .strings keys are case-sensitive, and NetFluss legitimately uses three such pairs:
# a title-case heading and a sentence-case control label sitting next to each other
# ("Custom Date Range" the popover title vs "Custom date range" the button). Both are live
# on the Mac and their German and Chinese values must stay reachable, so the collision is
# resolved here rather than by deleting a key upstream.
#
# Within a group of keys that fold together, the first in English source order keeps its
# exact name and every later one gets "~2", "~3", … appended. Localization.L in
# NetFluss.Core reverses this by probing key, key~2 … key~N when an exact lookup misses,
# so C# call sites keep passing the macOS key verbatim. Both halves of that contract are
# pinned by LocalizationCaseCollisionTests — change one and the tests fail.
COLLISION_SUFFIX = "~"
COLLISION_LIMIT = 9

# Terms that cannot survive the crossing unchanged. Anything matched here lands in the
# review report; entries with a replacement are rewritten automatically.
PLATFORM_TERMS = ("macOS", "Mac ", "the Mac", "menu bar", "Menu bar", "Menu Bar",
                  "Keychain", "Finder", "Dock", "System Settings", "AirDrop")

PLATFORM_OVERRIDES = {
    "macOS": "Windows",
    "System Settings": "Settings",
    "Keychain": "Credential Manager",
}

RESX_HEADER = """<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />
              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="resheader">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
"""


def unescape(text: str) -> str:
    return text.replace('\\"', '"').replace("\\n", "\n").replace("\\t", "\t").replace("\\\\", "\\")


def convert_specifiers(text: str) -> str:
    """Cocoa positional specifiers -> composite format items, in source order."""
    index = 0

    def replace(_match: re.Match[str]) -> str:
        nonlocal index
        item = "{%d}" % index
        index += 1
        return item

    # Braces are literal in .NET composite formatting and must be doubled first,
    # otherwise a string containing "{" would throw at runtime.
    text = text.replace("{", "{{").replace("}", "}}")
    return SPECIFIER_RE.sub(replace, text)


def parse_strings(path: pathlib.Path) -> dict[str, str]:
    entries: dict[str, str] = {}
    in_block_comment = False

    for line_number, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        line = raw.strip()

        if in_block_comment:
            if "*/" in line:
                in_block_comment = False
            continue
        if line.startswith("/*"):
            if "*/" not in line:
                in_block_comment = True
            continue
        if not line or line.startswith("//"):
            continue

        match = ENTRY_RE.match(line)
        if not match:
            raise SystemExit(f"{path}:{line_number}: cannot parse: {raw!r}")

        key = unescape(match.group(1))
        value = unescape(match.group(2))
        if key in entries:
            raise SystemExit(f"{path}:{line_number}: duplicate key {key!r}")
        entries[key] = value

    return entries


def resolve_case_collisions(keys: list[str]) -> dict[str, str]:
    """Map each converted key to the .resx resource name it is stored under.

    Identity for everything except keys that fold together with an earlier one; see the
    COLLISION_SUFFIX comment for why those cannot both keep their own name.
    """
    groups: dict[str, list[str]] = {}
    for key in keys:
        groups.setdefault(key.casefold(), []).append(key)

    names: dict[str, str] = {}
    taken = {key.casefold() for key in keys}

    for members in groups.values():
        # First in English source order wins the bare name, so the common case keeps a
        # resource name that is readable and diffable against the .strings catalogue.
        names[members[0]] = members[0]

        for ordinal, key in enumerate(members[1:], start=2):
            if ordinal > COLLISION_LIMIT:
                raise SystemExit(
                    f"error: {len(members)} keys fold to {members[0]!r}, more than the "
                    f"{COLLISION_LIMIT} that Localization.L probes for. Raise "
                    f"COLLISION_LIMIT here and CollisionLimit in Localization.cs together."
                )

            name = f"{key}{COLLISION_SUFFIX}{ordinal}"
            if name.casefold() in taken:
                raise SystemExit(
                    f"error: disambiguating {key!r} produces {name!r}, which collides with "
                    f"a real key. Rename the offending key in the .strings catalogues."
                )

            taken.add(name.casefold())
            names[key] = name

    return names


def apply_platform_overrides(value: str) -> tuple[str, bool]:
    replaced = value
    for term, substitute in PLATFORM_OVERRIDES.items():
        replaced = replaced.replace(term, substitute)
    return replaced, replaced != value


def escape_attribute(text: str) -> str:
    """XML attribute escaping.

    saxutils.escape only handles & < >. Two NetFluss keys quote a UI label — e.g.
    'macOS may show a "Background Items Added" approval prompt' — and an unescaped
    double quote inside a double-quoted attribute produces a .resx that will not parse.
    """
    return escape(text, {'"': "&quot;", "'": "&apos;", "\n": "&#10;", "\t": "&#9;"})


def render_resx(entries: dict[str, str]) -> str:
    parts = [RESX_HEADER]
    for key, value in entries.items():
        # xml:space="preserve" keeps leading/trailing spaces, which several strings rely on.
        parts.append(f'  <data name="{escape_attribute(key)}" xml:space="preserve">\n')
        parts.append(f"    <value>{escape(value)}</value>\n")
        parts.append("  </data>\n")
    parts.append("</root>\n")
    return "".join(parts)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="fail if generated files are stale")
    args = parser.parse_args()

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    sources = {
        language: parse_strings(SOURCE_DIR / f"{language}.lproj" / "Localizable.strings")
        for language in LANGUAGES
    }
    english = sources["en"]

    # One name map shared by every language, derived from the English catalogue. Computing
    # it per language would let a German file that happens to list a colliding pair in the
    # opposite order pick the opposite winner, and the German lookup would then miss.
    ordered: list[str] = []
    seen: set[str] = set()
    for entries in sources.values():
        for key in entries:
            converted_key = convert_specifiers(key)
            if converted_key not in seen:
                seen.add(converted_key)
                ordered.append(converted_key)

    names = resolve_case_collisions(ordered)
    collisions = {key: name for key, name in names.items() if key != name}

    review: list[str] = []
    generated: dict[pathlib.Path, str] = {}

    for language, suffix in LANGUAGES.items():
        entries = sources[language]

        missing = sorted(set(english) - set(entries))
        extra = sorted(set(entries) - set(english))
        if missing:
            print(f"warning: {language} is missing {len(missing)} key(s): {missing[:3]}...", file=sys.stderr)
        if extra:
            print(f"warning: {language} has {len(extra)} key(s) not in English: {extra[:3]}...", file=sys.stderr)

        converted: dict[str, str] = {}
        for key, value in entries.items():
            value, was_overridden = apply_platform_overrides(value)
            converted[names[convert_specifiers(key)]] = convert_specifiers(value)

            if any(term in value for term in PLATFORM_TERMS) or was_overridden:
                status = "auto-rewritten" if was_overridden else "needs review"
                review.append(f"| `{language}` | `{key.replace(chr(124), chr(92) + chr(124))}` | {status} | {value.replace(chr(124), chr(92) + chr(124))} |")

        # Defence in depth: whatever the map said, never hand resgen a file it would have
        # to silently drop entries from.
        folded: dict[str, str] = {}
        for name in converted:
            clash = folded.setdefault(name.casefold(), name)
            if clash != name:
                raise SystemExit(
                    f"error: {language} would emit {name!r} and {clash!r}, which .NET "
                    f"treats as the same resource name."
                )

        generated[OUTPUT_DIR / f"Strings{suffix}.resx"] = render_resx(converted)

    report = [
        "# Platform vocabulary review",
        "",
        "Generated by `windows/tools/strings2resx.py`. Every string below mentions a",
        "platform-specific concept that may not be correct on Windows. Rows marked",
        "*auto-rewritten* were changed by `PLATFORM_OVERRIDES`; rows marked *needs review*",
        "were left alone and want a human decision.",
        "",
        "| Language | Key | Status | Value |",
        "| --- | --- | --- | --- |",
        *review,
        "",
    ]
    generated[REPORT_PATH] = "\n".join(report)

    stale = []
    for path, content in generated.items():
        existing = path.read_text(encoding="utf-8") if path.exists() else None
        if existing == content:
            continue
        stale.append(path)
        if not args.check:
            path.write_text(content, encoding="utf-8")

    if args.check and stale:
        stale_names = ", ".join(str(p.relative_to(REPO_ROOT)) for p in stale)
        print(f"error: generated resources are stale: {stale_names}", file=sys.stderr)
        print("run: python3 windows/tools/strings2resx.py", file=sys.stderr)
        return 1

    print(f"{'checked' if args.check else 'wrote'} {len(generated) - 1} .resx file(s) "
          f"from {len(english)} English keys; {len(review)} platform review row(s); "
          f"{len(collisions)} case collision(s) disambiguated")
    for key, name in sorted(collisions.items()):
        print(f"  case collision: {key!r} stored as {name!r}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
