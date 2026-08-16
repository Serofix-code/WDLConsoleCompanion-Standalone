from __future__ import annotations

import sys

from common import ROOT, load_records

records = load_records()
ids = {record["id"] for record in records}
errors: list[str] = []
for record in records:
    for related in record.get("related", []):
        if related not in ids:
            errors.append(f"{record['id']}: unknown related record {related}")
    for evidence in record.get("evidence", []):
        reference = evidence.get("reference", "")
        if reference.startswith(("src/", "docs/", "research/", "database/", "sdk/", "examples/")) and not (ROOT / reference).exists():
            errors.append(f"{record['id']}: missing evidence reference {reference}")
if errors:
    print("\n".join(errors))
    sys.exit(1)
print(f"All references resolve across {len(records)} records.")
