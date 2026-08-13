from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Iterable

ROOT = Path(__file__).resolve().parents[1]
RECORD_DIR = ROOT / "database" / "records"


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8-sig") as stream:
        return json.load(stream)


def load_records() -> list[dict[str, Any]]:
    records: list[dict[str, Any]] = []
    for path in sorted(RECORD_DIR.glob("*.json")):
        value = load_json(path)
        if not isinstance(value, list):
            raise ValueError(f"{path.relative_to(ROOT)} must contain a JSON array")
        for record in value:
            if not isinstance(record, dict):
                raise ValueError(f"{path.relative_to(ROOT)} contains a non-object record")
            record = dict(record)
            record["_source"] = path.relative_to(ROOT).as_posix()
            records.append(record)
    return records


def strings(value: Any) -> Iterable[str]:
    if isinstance(value, str):
        yield value
    elif isinstance(value, dict):
        for key, child in value.items():
            if key != "_source":
                yield from strings(child)
    elif isinstance(value, list):
        for child in value:
            yield from strings(child)
