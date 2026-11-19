#!/usr/bin/env python3
"""Markdown ban for new files.

Usage: python3 tools/md_ban.py [--repo DIR] [--base REF] [--event FILE]

Lists changes between the merge base of REF (default origin/main) and HEAD,
plus the working tree and untracked, not ignored, files, with git rename and
copy detection. A path counts as new markdown when it ends in .md or .mdx,
does not exist at the base, and is:

  * added (status A, or untracked);
  * copied (status C), whatever the source;
  * renamed (status R) from a path that is not .md or .mdx.

A rename from one markdown path to another is allowed. Every markdown path that
exists at the base is grandfathered, so edits to it pass.

If --event points at a GitHub pull_request event payload whose pull request
carries the label docs-approved, the check passes. Local runs have no label,
so any new markdown fails. The script prints new paths and exits 1.
"""

import argparse
import json
import subprocess
import sys
from pathlib import Path

APPROVAL_LABEL = "docs-approved"
MARKDOWN_SUFFIXES = (".md", ".mdx")


def is_markdown(path):
    return path.lower().endswith(MARKDOWN_SUFFIXES)


def git(repo, *args):
    return subprocess.run(["git", *args], cwd=repo, check=True, capture_output=True).stdout.decode()


def base_commit(repo, base):
    try:
        return git(repo, "merge-base", base, "HEAD").strip()
    except subprocess.CalledProcessError:
        return git(repo, "rev-parse", "--verify", base + "^{commit}").strip()


def parse_name_status(out):
    fields = [f for f in out.split("\0") if f]
    changes = []
    i = 0
    while i < len(fields):
        status = fields[i][0]
        if status in "RC":
            changes.append((status, fields[i + 1], fields[i + 2]))
            i += 3
        else:
            changes.append((status, None, fields[i + 1]))
            i += 2
    return changes


def new_markdown(changes, base_paths):
    found = []
    for status, source, path in changes:
        if not is_markdown(path) or path in base_paths:
            continue
        if status == "A" or status == "C" or (status == "R" and not is_markdown(source)):
            found.append(path)
    return sorted(set(found))


def repo_changes(repo, base):
    commit = base_commit(repo, base)
    diff = git(repo, "diff", "-z", "--name-status", "-M", "-C", "--find-copies-harder", commit)
    untracked = git(repo, "ls-files", "-z", "--others", "--exclude-standard")
    changes = parse_name_status(diff)
    changes += [("A", None, p) for p in untracked.split("\0") if p]
    base_paths = set(p for p in git(repo, "ls-tree", "-r", "-z", "--name-only", commit).split("\0") if p)
    return changes, base_paths


def approved(event):
    if not event:
        return False
    payload = json.loads(Path(event).read_text(encoding="utf-8"))
    labels = (payload.get("pull_request") or {}).get("labels") or []
    return any(label.get("name") == APPROVAL_LABEL for label in labels)


def main(argv=None):
    parser = argparse.ArgumentParser(description="Fail on new markdown files.")
    parser.add_argument("--repo", default=str(Path(__file__).resolve().parent.parent))
    parser.add_argument("--base", default="origin/main")
    parser.add_argument("--event")
    args = parser.parse_args(argv)
    paths = new_markdown(*repo_changes(args.repo, args.base))
    if not paths:
        return 0
    if approved(args.event):
        print(f"{len(paths)} new markdown file(s), allowed by label {APPROVAL_LABEL}.")
        return 0
    for path in paths:
        print(f"{path}: new markdown file not allowed")
    print(f"{len(paths)} new markdown file(s). Put the text in the PR or issue, or ask Peter for label {APPROVAL_LABEL}.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
