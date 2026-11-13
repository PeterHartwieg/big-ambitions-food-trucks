#!/usr/bin/env python3
"""Comment ban for first-party code.

Usage: python3 tools/comment_ban.py [--repo DIR]

Scans every git-tracked source file whose type is listed in LANGUAGES and is
not under EXCLUDED_PREFIXES. Each comment found is either:

  * a directive (see is_directive): shebang, Python coding line, tool pragmas
    such as "# noqa", "# type:", "# shellcheck", "// eslint-", "#requires";
    C# and shader preprocessor lines ("#if", "#pragma", ...) are code, not
    comments, so the tokenizer never reports them;
  * allowed, if the line it starts on is listed in docs/comment-excuses.md as
    a "- `path:line` category" bullet. A run of consecutive full-line comments
    counts as one comment that starts on its first line;
  * a violation. The script prints violations and exits 1.
"""

import argparse
import io
import re
import subprocess
import sys
import tokenize
from dataclasses import dataclass
from pathlib import Path

EXCUSES_FILE = "docs/comment-excuses.md"
EXCUSE_LINE = re.compile(r"^- `([^`:]+):(\d+)`\s+\S")

EXCLUDED_PREFIXES = (
    "unity/FoodTrucks/Dependencies/",
    "build/",
)
EXCLUDED_PARTS = ("bin", "obj")

DIRECTIVE_PATTERNS = (
    re.compile(r"^#!"),
    re.compile(r"^#.*coding[:=]"),
    re.compile(r"^#\s*(noqa|type:|pragma|pylint:|shellcheck|fmt:)"),
    re.compile(r"^#requires\b", re.IGNORECASE),
    re.compile(r"^//\s*(eslint-|@ts-|ReSharper)"),
    re.compile(r"^/\*\s*eslint"),
)


@dataclass
class Comment:
    path: str
    line: int
    end_line: int
    text: str
    full_line: bool


def language_for(path, first_line=""):
    name = path.rsplit("/", 1)[-1]
    if name == ".gitignore":
        return "hash"
    suffix = name.rsplit(".", 1)[-1] if "." in name else ""
    if suffix in ("cs", "shader"):
        return "c"
    if suffix in ("cjs", "js"):
        return "js"
    if suffix == "py":
        return "python"
    if suffix == "sh":
        return "shell"
    if suffix == "ps1":
        return "powershell"
    if suffix == "csproj":
        return "xml"
    if not suffix and first_line.startswith("#!"):
        if "python" in first_line:
            return "python"
        if "sh" in first_line:
            return "shell"
    return None


def is_excluded(path):
    if path.startswith(EXCLUDED_PREFIXES):
        return True
    return any(part in EXCLUDED_PARTS for part in path.split("/")[:-1])


def is_directive(comment):
    if comment.line == 1 and comment.text.startswith("#!"):
        return True
    return any(p.match(comment.text) for p in DIRECTIVE_PATTERNS)


def _line_starts(text):
    starts = [0]
    for i, ch in enumerate(text):
        if ch == "\n":
            starts.append(i + 1)
    return starts


def _spans_to_comments(path, text, spans):
    starts = _line_starts(text)
    import bisect

    out = []
    for start, end in spans:
        line = bisect.bisect_right(starts, start)
        end_line = bisect.bisect_right(starts, max(start, end - 1))
        line_start = starts[line - 1]
        full = text[line_start:start].strip() == ""
        out.append(Comment(path, line, end_line, text[start:end], full))
    return out


def c_spans(text, js=False):
    """Comment spans in C#, shader or JS source.

    Handles "..." and '...' with backslash escapes, C# verbatim @"..." with ""
    escapes, C# interpolated $"..." / $@"..." with {code} holes, JS template
    literals with ${code} holes, JS regex literals, and preprocessor lines
    (a '#' as the first non-blank character), which are skipped whole.
    """
    spans = []
    n = len(text)
    i = 0
    stack = []
    last_sig = ""

    def at_line_start(pos):
        j = pos - 1
        while j >= 0 and text[j] in " \t":
            j -= 1
        return j < 0 or text[j] == "\n"

    def skip_string(i, verbatim, interp, quote):
        while i < n:
            ch = text[i]
            if verbatim and ch == quote:
                if i + 1 < n and text[i + 1] == quote:
                    i += 2
                    continue
                return i + 1, False
            if not verbatim and ch == "\\":
                i += 2
                continue
            if ch == quote:
                return i + 1, False
            if interp and ch == "{":
                if i + 1 < n and text[i + 1] == "{" and quote != "`":
                    i += 2
                    continue
                return i + 1, True
            if quote == "`" and ch == "$" and i + 1 < n and text[i + 1] == "{":
                return i + 2, True
            if not verbatim and quote != "`" and ch == "\n":
                return i + 1, False
            i += 1
        return i, False

    def resume(i, frame):
        verbatim, interp, quote = frame
        i, opened = skip_string(i, verbatim, interp, quote)
        if opened:
            stack.append([frame, 0])
        return i

    while i < n:
        ch = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if ch == "#" and not js and at_line_start(i):
            while i < n and text[i] != "\n":
                i += 1
            continue
        if ch == "/" and nxt == "/":
            j = text.find("\n", i)
            j = n if j < 0 else j
            spans.append((i, j))
            i = j
            continue
        if ch == "/" and nxt == "*":
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            spans.append((i, j))
            i = j
            continue
        if stack and ch == "{":
            stack[-1][1] += 1
        elif stack and ch == "}":
            if stack[-1][1] == 0:
                frame, _ = stack.pop()
                i = resume(i + 1, frame)
                last_sig = '"'
                continue
            stack[-1][1] -= 1
        if js and ch == "/" and (last_sig == "" or last_sig in "(,=:[!&|?{};+-*%<>~^"):
            j = i + 1
            in_class = False
            while j < n and text[j] != "\n":
                c = text[j]
                if c == "\\":
                    j += 2
                    continue
                if c == "[":
                    in_class = True
                elif c == "]":
                    in_class = False
                elif c == "/" and not in_class:
                    break
                j += 1
            i = j + 1
            last_sig = "/"
            continue
        if js and ch == "`":
            i = resume(i + 1, (True, True, "`"))
            last_sig = '"'
            continue
        if not js and ch in "$@":
            j = i
            verbatim = interp = False
            while j < n and text[j] in "$@":
                verbatim |= text[j] == "@"
                interp |= text[j] == "$"
                j += 1
            if j < n and text[j] == '"':
                i = resume(j + 1, (verbatim, interp, '"'))
                last_sig = '"'
                continue
        if ch == '"' or ch == "'":
            i = resume(i + 1, (False, False, ch))
            last_sig = '"'
            continue
        if not ch.isspace():
            last_sig = ch if not (ch.isalnum() or ch == "_") else "a"
        i += 1
    return spans


def python_spans(text):
    spans = []
    starts = _line_starts(text)
    tokens = tokenize.generate_tokens(io.StringIO(text).readline)
    for tok in tokens:
        if tok.type == tokenize.COMMENT:
            (sl, sc), (el, ec) = tok.start, tok.end
            spans.append((starts[sl - 1] + sc, starts[el - 1] + ec))
    return spans


HEREDOC = re.compile(r"<<-?\s*(['\"]?)([A-Za-z_][A-Za-z0-9_]*)\1")


def shell_spans(text):
    """Comment spans in POSIX shell: '#' starting a word outside quotes and
    heredoc bodies."""
    spans = []
    n = len(text)
    i = 0
    pending = []
    while i < n:
        ch = text[i]
        if ch == "\n":
            i += 1
            for tag in pending:
                while i < n:
                    j = text.find("\n", i)
                    j = n if j < 0 else j
                    line = text[i:j]
                    i = j + 1
                    if line.strip() == tag:
                        break
            pending = []
            continue
        if ch == "\\":
            i += 2
            continue
        if ch == "'":
            j = text.find("'", i + 1)
            i = n if j < 0 else j + 1
            continue
        if ch == '"':
            j = i + 1
            while j < n and text[j] != '"':
                j += 2 if text[j] == "\\" else 1
            i = j + 1
            continue
        if ch == "<" and text.startswith("<<", i) and not text.startswith("<<<", i):
            m = HEREDOC.match(text, i)
            if m:
                pending.append(m.group(2))
                i = m.end()
                continue
        if ch == "#" and (i == 0 or text[i - 1] in " \t\n;|&()"):
            j = text.find("\n", i)
            j = n if j < 0 else j
            spans.append((i, j))
            i = j
            continue
        i += 1
    return spans


def powershell_spans(text):
    """Comment spans in PowerShell: '#' and '<# #>' outside '...', "..."
    (backtick escapes) and @'...'@ / @"..."@ here-strings."""
    spans = []
    n = len(text)
    i = 0
    while i < n:
        ch = text[i]
        if ch == "<" and text.startswith("<#", i):
            j = text.find("#>", i + 2)
            j = n if j < 0 else j + 2
            spans.append((i, j))
            i = j
            continue
        if ch == "#":
            j = text.find("\n", i)
            j = n if j < 0 else j
            spans.append((i, j))
            i = j
            continue
        if ch == "@" and i + 1 < n and text[i + 1] in "'\"":
            end = "\n" + text[i + 1] + "@"
            j = text.find(end, i + 2)
            i = n if j < 0 else j + len(end)
            continue
        if ch == "`":
            i += 2
            continue
        if ch == "'":
            j = i + 1
            while j < n:
                if text[j] == "'":
                    if j + 1 < n and text[j + 1] == "'":
                        j += 2
                        continue
                    break
                j += 1
            i = j + 1
            continue
        if ch == '"':
            j = i + 1
            while j < n and text[j] != '"':
                j += 2 if text[j] == "`" else 1
            i = j + 1
            continue
        i += 1
    return spans


def hash_spans(text):
    spans = []
    pos = 0
    for line in text.splitlines(keepends=True):
        if line.lstrip().startswith("#"):
            start = pos + len(line) - len(line.lstrip())
            spans.append((start, pos + len(line.rstrip("\r\n"))))
        pos += len(line)
    return spans


def xml_spans(text):
    return [(m.start(), m.end()) for m in re.finditer(r"<!--.*?-->", text, re.S)]


SPANNERS = {
    "c": c_spans,
    "js": lambda t: c_spans(t, js=True),
    "python": python_spans,
    "shell": shell_spans,
    "powershell": powershell_spans,
    "hash": hash_spans,
    "xml": xml_spans,
}


def comments_in(path, text):
    """Comments in one file, with consecutive full-line comments merged into a
    single Comment that starts on the run's first line."""
    first = text.split("\n", 1)[0]
    lang = language_for(path, first)
    if lang is None:
        return []
    raw = _spans_to_comments(path, text, SPANNERS[lang](text))
    merged = []
    for c in raw:
        prev = merged[-1] if merged else None
        if (
            prev
            and prev.full_line
            and c.full_line
            and c.line == prev.end_line + 1
            and not is_directive(prev)
            and not is_directive(c)
        ):
            prev.end_line = c.end_line
            prev.text += "\n" + c.text
            continue
        merged.append(c)
    return merged


def tracked_files(repo):
    out = subprocess.run(
        ["git", "ls-files", "-z"], cwd=repo, check=True, capture_output=True
    ).stdout.decode()
    return [p for p in out.split("\0") if p and not is_excluded(p)]


def load_excuses(repo):
    path = Path(repo) / EXCUSES_FILE
    allowed = set()
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            m = EXCUSE_LINE.match(line)
            if m:
                allowed.add((m.group(1), int(m.group(2))))
    return allowed


def scan(repo, files=None):
    """Return (comments, violations) for the repo or the given relative paths."""
    allowed = load_excuses(repo)
    found = []
    violations = []
    for rel in files if files is not None else tracked_files(repo):
        full = Path(repo) / rel
        if not full.is_file():
            continue
        try:
            text = full.read_text(encoding="utf-8-sig")
        except UnicodeDecodeError:
            continue
        for c in comments_in(rel, text):
            found.append(c)
            if not is_directive(c) and (rel, c.line) not in allowed:
                violations.append(c)
    return found, violations


def main(argv=None):
    parser = argparse.ArgumentParser(description="Fail on unlisted code comments.")
    parser.add_argument("--repo", default=str(Path(__file__).resolve().parent.parent))
    args = parser.parse_args(argv)
    _, violations = scan(args.repo)
    for c in violations:
        print(f"{c.path}:{c.line}: comment not allowed: {c.text.splitlines()[0][:80]}")
    if violations:
        print(
            f"{len(violations)} comment(s). Remove them, or fix the problem or open an issue."
        )
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
