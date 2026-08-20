from __future__ import annotations

import collections
import sys

from common import load_records

records = load_records()
ids = collections.defaultdict(list)
names = collections.defaultdict(list)
for record in records:
    ids[record["id"]].append(record["_source"])
    names[(record["kind"], record["name"].casefold())].append(record["id"])

errors = [f"duplicate id {key}: {values}" for key, values in ids.items() if len(values) > 1]
errors += [f"duplicate {key[0]} name {key[1]}: {values}" for key, values in names.items() if len(values) > 1]
if errors:
    print("\n".join(errors))
    sys.exit(1)
print(f"No duplicate symbols in {len(records)} records.")
