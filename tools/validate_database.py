from __future__ import annotations

import re
import sys
from datetime import date

from common import ROOT, load_json, load_records

KINDS = {"function", "event", "lua_api", "command", "ui", "type", "enum", "hash", "system", "signature", "offset", "call_chain"}
CONFIDENCE = {"confirmed", "strongly_inferred", "inferred", "unknown"}
STATUS = {"usable", "experimental", "under_development", "unresolved", "deprecated"}
ID = re.compile(r"^[a-z0-9]+(?:[._-][a-z0-9]+)+$")
SHA256 = re.compile(r"^[A-Fa-f0-9]{64}$")
REQUIRED = {"id", "kind", "name", "summary", "confidence", "builds", "evidence", "status"}


def validate() -> list[str]:
    errors: list[str] = []
    try:
        records = load_records()
    except (OSError, ValueError) as error:
        return [str(error)]

    for record in records:
        source = record.get("_source", "unknown")
        label = f"{source}:{record.get('id', '<missing id>')}"
        missing = REQUIRED - record.keys()
        if missing:
            errors.append(f"{label}: missing {', '.join(sorted(missing))}")
        if not ID.fullmatch(str(record.get("id", ""))):
            errors.append(f"{label}: invalid stable id")
        if record.get("kind") not in KINDS:
            errors.append(f"{label}: invalid kind")
        if record.get("confidence") not in CONFIDENCE:
            errors.append(f"{label}: invalid confidence")
        if record.get("status") not in STATUS:
            errors.append(f"{label}: invalid status")
        for field in ("name", "summary"):
            if not isinstance(record.get(field), str) or not record[field].strip():
                errors.append(f"{label}: {field} must be non-empty")
        builds = record.get("builds")
        if not isinstance(builds, list) or not builds:
            errors.append(f"{label}: builds must be a non-empty array")
        else:
            for index, build in enumerate(builds):
                if not isinstance(build, dict):
                    errors.append(f"{label}: build {index} is not an object")
                    continue
                for field in ("platform", "distribution", "module", "observed"):
                    if not isinstance(build.get(field), str) or not build[field]:
                        errors.append(f"{label}: build {index} missing {field}")
                try:
                    date.fromisoformat(build.get("observed", ""))
                except ValueError:
                    errors.append(f"{label}: build {index} has invalid observed date")
                digest = build.get("moduleSha256")
                if digest is not None and not SHA256.fullmatch(str(digest)):
                    errors.append(f"{label}: build {index} has invalid SHA-256")
        evidence = record.get("evidence")
        if not isinstance(evidence, list) or not evidence:
            errors.append(f"{label}: evidence must be a non-empty array")
        elif any(not isinstance(item, dict) or not item.get("reference") or not item.get("notes") for item in evidence):
            errors.append(f"{label}: each evidence item needs reference and notes")

    manifest = load_json(ROOT / "database" / "catalog.json")
    for entry in manifest.get("catalogs", []):
        path = ROOT / entry.get("path", "")
        if not path.is_file():
            errors.append(f"catalog {entry.get('id')}: missing path {path.relative_to(ROOT)}")
            continue
        try:
            load_json(path)
        except (OSError, ValueError) as error:
            errors.append(f"catalog {entry.get('id')}: invalid JSON: {error}")
    return errors


if __name__ == "__main__":
    problems = validate()
    if problems:
        print("Database validation failed:")
        print("\n".join(f"- {problem}" for problem in problems))
        sys.exit(1)
    print(f"Database validation passed: {len(load_records())} research records.")
