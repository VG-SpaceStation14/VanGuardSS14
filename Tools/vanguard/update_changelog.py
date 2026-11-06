#!/usr/bin/env python3
import json
import os
import re
import subprocess
import sys
from datetime import datetime
from pathlib import Path

from ruamel.yaml import YAML
from ruamel.yaml.scalarstring import SingleQuotedScalarString

REPOSITORY = os.environ.get("GITHUB_REPOSITORY", "VG-SpaceStation14/VanGuardSS14")
ROOT = Path(__file__).resolve().parents[2]
CHANGELOG_FILE = ROOT / "Resources/Changelog/ChangelogVanGuard.yml"
PER_PAGE = 100
TYPE_MAP = {
    "add": "Add",
    "tweak": "Tweak",
    "fix": "Fix",
    "remove": "Remove",
}
PLACEHOLDER_MESSAGES = {
    "добавлена новая возможность",
    "исправлена ошибка",
    "изменена механика",
    "удален устаревший функционал",
    "удалено устаревшее функционал",
    "добавлено веселье",
    "удалено веселье",
    "изменено веселье",
    "исправлено веселье",
}
MARKER_RE = re.compile(r"^\s*(?::cl:|🆑)\s*(.*?)\s*$", re.IGNORECASE)
CHANGE_RE = re.compile(
    r"^\s*[-*+]\s*(add|tweak|fix|remove)\s*:\s*(.*?)\s*$",
    re.IGNORECASE,
)


def normalize_text(value):
    value = value.lower().replace("ё", "е")
    value = re.sub(r"\s+", " ", value)
    value = re.sub(r"[.!?,:;]+$", "", value)
    return value.strip()


def is_placeholder(message):
    return normalize_text(message) in PLACEHOLDER_MESSAGES


def fetch_all_merged_prs():
    command = [
        "gh",
        "api",
        "--paginate",
        "--slurp",
        f"repos/{REPOSITORY}/pulls?state=closed&per_page={PER_PAGE}",
    ]
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or "GitHub API request failed")
    pages = json.loads(result.stdout)
    prs = []
    for page in pages:
        if isinstance(page, list):
            prs.extend(page)
    merged = [pr for pr in prs if pr.get("merged_at")]
    merged.sort(key=lambda pr: (pr["merged_at"], pr.get("number", 0)))
    return merged


def remove_html_comments(text):
    return re.sub(r"<!--.*?(?:-->|$)", "", text, flags=re.DOTALL)


def remove_code_blocks(text):
    result = []
    inside_block = False
    marker = None
    for line in text.splitlines():
        stripped = line.lstrip()
        if not inside_block:
            match = re.match(r"(```+|~~~+)", stripped)
            if match:
                inside_block = True
                marker = match.group(1)[0]
                continue
            result.append(line)
        elif re.match(rf"^[ \t]*{re.escape(marker)}{{3,}}[ \t]*$", line):
            inside_block = False
            marker = None
    return "\n".join(result)


def clean_pr_body(body):
    return remove_code_blocks(remove_html_comments(body or ""))


def parse_changelog_body(body):
    lines = clean_pr_body(body).splitlines()
    marker_index = None
    marker_author = ""
    for index, line in enumerate(lines):
        match = MARKER_RE.match(line)
        if match:
            marker_index = index
            marker_author = match.group(1).strip()
            break
    if marker_index is None:
        return None
    author = marker_author
    changes = []
    for line in lines[marker_index + 1:]:
        stripped = line.strip()
        if not stripped:
            continue
        match = CHANGE_RE.match(line)
        if match:
            change_type = TYPE_MAP[match.group(1).lower()]
            message = match.group(2).strip()
            if not message:
                continue
            message = re.split(
                r"\s+This is an auto-generated comment:\s*release notes by coderabbit\.ai",
                message,
                flags=re.IGNORECASE,
            )[0].strip()
            if not message or is_placeholder(message):
                continue
            changes.append({"message": message, "type": change_type})
            continue
        if not author and not changes:
            if not stripped.startswith(("-", "*", "+")):
                author = stripped
                continue
        if changes and line[:1].isspace() and not stripped.startswith(("-", "*", "+")):
            changes[-1]["message"] += " " + stripped
            continue
        if changes:
            break
    if not changes:
        return None
    return {"author": author, "changes": changes}


def format_pr_date(value):
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    return parsed.astimezone().isoformat()


def convert_pr(pr, entry_id):
    parsed = parse_changelog_body(pr.get("body") or "")
    if parsed is None:
        return None
    github_author = pr.get("user", {}).get("login") or "Unknown"
    return {
        "author": parsed["author"] or github_author,
        "changes": parsed["changes"],
        "id": entry_id,
        "time": SingleQuotedScalarString(format_pr_date(pr["merged_at"])),
    }


def generate_entries(prs):
    entries = []
    for pr in prs:
        entry = convert_pr(pr, len(entries) + 1)
        if entry is not None:
            entries.append(entry)
    return entries


def validate_entries(entries):
    if not entries:
        raise RuntimeError("No changelog entries were generated")
    for expected, entry in enumerate(entries, start=1):
        if entry["id"] != expected:
            raise RuntimeError(f"Non-sequential changelog id: {entry['id']}")
        if not entry["author"]:
            raise RuntimeError(f"Missing author for changelog id {entry['id']}")
        if not entry["changes"]:
            raise RuntimeError(f"Empty changes for changelog id {entry['id']}")
        for change in entry["changes"]:
            if change["type"] not in {"Add", "Tweak", "Fix", "Remove"}:
                raise RuntimeError(f"Invalid change type: {change['type']}")
            if not change["message"]:
                raise RuntimeError(f"Empty message for changelog id {entry['id']}")


def write_yaml(entries):
    yaml = YAML()
    yaml.allow_unicode = True
    yaml.default_flow_style = False
    yaml.preserve_quotes = True
    yaml.width = 120
    yaml.indent(mapping=2, sequence=4, offset=2)
    temp_path = CHANGELOG_FILE.with_suffix(CHANGELOG_FILE.suffix + ".tmp")
    with temp_path.open("w", encoding="utf-8") as file:
        yaml.dump({"Entries": entries}, file)
    os.replace(temp_path, CHANGELOG_FILE)


def main():
    if not CHANGELOG_FILE.parent.is_dir():
        raise RuntimeError(f"Changelog directory not found: {CHANGELOG_FILE.parent}")
    prs = fetch_all_merged_prs()
    entries = generate_entries(prs)
    validate_entries(entries)
    write_yaml(entries)
    print(f"Rebuilt {CHANGELOG_FILE} with {len(entries)} entries")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(error, file=sys.stderr)
        sys.exit(1)
