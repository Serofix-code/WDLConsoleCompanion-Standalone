from __future__ import annotations

import argparse

from common import ROOT, load_json, load_records, strings

parser = argparse.ArgumentParser(description="Search WDL community SDK records and catalogs")
parser.add_argument("query", nargs="+", help="words to find")
args = parser.parse_args()
terms = [term.casefold() for term in args.query]
matches: list[tuple[str, str, str, str]] = []

for record in load_records():
    haystack = " ".join(strings(record)).casefold()
    if all(term in haystack for term in terms):
        matches.append((record["id"], record["confidence"], record["name"], record["summary"]))

manifest = load_json(ROOT / "database" / "catalog.json")
for catalog in manifest["catalogs"]:
    value = load_json(ROOT / catalog["path"])
    haystack = " ".join(strings(value)).casefold()
    if all(term in haystack for term in terms):
        matches.append((f"catalog:{catalog['id']}", catalog["confidence"], catalog["path"], "Runtime catalog contains matching text"))

for identifier, confidence, name, summary in matches:
    print(f"[{confidence.upper()}] {identifier}\n  {name}\n  {summary}")
if not matches:
    print("No matches.")
