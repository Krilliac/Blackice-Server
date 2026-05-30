#!/usr/bin/env python3
"""PreToolUse leak-guard for the BlackIce.Server repo.

This repo is intended to be open-sourced under GPLv3 and must contain ONLY original
code + protocol docs. The one mistake that's genuinely hard to walk back is committing
copyrighted game material (the game's DLLs, decompiled source, asset dumps) or a
credential-bearing artifact (oplog.jsonl carries Steam tokens; the dev DB; the generated
server config) into public history.

`.gitignore` already excludes these, but it does nothing against `git add -f` (which
defeats .gitignore) or against a Write/Edit that creates such a file inside the tree.
This hook closes those gaps. It is deliberately NARROW to avoid false positives:

  * A normal `git add .` / `git add -A` / `git commit -am` is ALLOWED — .gitignore
    already filters the forbidden paths, so blocking these would only annoy.
  * A force-add (`git add -f`/`--force`) is BLOCKED — it bypasses .gitignore, the
    single most likely way copyrighted material slips in.
  * A `git add`/`git commit` that explicitly names a forbidden path is BLOCKED.
  * A Write/Edit whose target is a forbidden path/extension is BLOCKED.

Reads the PreToolUse JSON envelope on stdin. To block, it prints a reason to stderr and
exits 2 (the documented PreToolUse "deny" convention).
"""
import json
import re
import sys

# Substrings (case-insensitive) that mark game-derived or secret-bearing material.
# Kept as plain substrings so they match regardless of path separator or prefix.
FORBIDDEN_SUBSTRINGS = [
    "decompiled/", "decompiled\\",
    "captures/", "captures\\",
    "asset-dumps/", "asset-dumps\\",
    "third-party/", "third-party\\",
    "black ice_data",          # the game's data folder
    "oplog.jsonl",             # decrypted ops — carries Steam auth tokens
    "steam-ticket-spike.log",  # spike log — carries a real SteamID + ticket bytes
    "blackice.server.json",    # generated runtime config (may hold secrets)
]
# Extensions that are almost always game binaries / build output, never original source.
FORBIDDEN_EXTENSIONS = (".dll", ".exe", ".db", ".db-shm", ".db-wal", ".pdb")


def is_forbidden_path(path: str) -> str | None:
    """Return a human reason if `path` points at protected material, else None."""
    p = path.replace("\\", "/").lower()
    for frag in FORBIDDEN_SUBSTRINGS:
        if frag.replace("\\", "/") in p:
            return f"path matches protected pattern '{frag}'"
    if p.endswith(FORBIDDEN_EXTENSIONS):
        return f"'{path}' has a game-binary/secret extension"
    return None


def check_bash(command: str) -> str | None:
    """Inspect a shell command for risky git staging. Returns a reason to block, or None."""
    c = command.lower()
    # Only git staging/commit commands are interesting here.
    if not re.search(r"\bgit\s+(add|commit|stash)\b", c):
        return None

    # Force-add defeats .gitignore — the highest-risk footgun.
    if re.search(r"\bgit\s+add\b", c) and re.search(r"(^|\s)(-f|--force)(\s|$)", c):
        return ("`git add -f/--force` bypasses .gitignore, which is the guard keeping "
                "copyrighted game material out of this public repo. Stage the specific "
                "original-source files by name instead.")

    # An explicit forbidden path named in the command.
    for frag in FORBIDDEN_SUBSTRINGS:
        if frag.replace("\\", "/") in c:
            return f"git command references protected material ('{frag}')."
    # A forbidden extension named directly (e.g. `git add Foo.dll`).
    for ext in FORBIDDEN_EXTENSIONS:
        if re.search(r"\S+" + re.escape(ext) + r"(\s|$|['\"])", c):
            return f"git command references a '{ext}' file — game binaries/secrets must not be committed."
    return None


def main() -> int:
    try:
        data = json.load(sys.stdin)
    except Exception:
        return 0  # never block on a malformed envelope

    tool = data.get("tool_name", "")
    ti = data.get("tool_input", {}) or {}

    reason = None
    if tool == "Bash":
        reason = check_bash(ti.get("command", "") or "")
    elif tool in ("Write", "Edit", "NotebookEdit"):
        reason = is_forbidden_path(ti.get("file_path", "") or "")

    if reason:
        sys.stderr.write(
            "BLOCKED by BlackIce leak-guard: " + reason + "\n"
            "If this is genuinely original material, rename it off the protected pattern; "
            "otherwise keep it local (it's gitignored for a reason - see CLAUDE.md / NOTICE)."
        )
        return 2  # exit 2 => PreToolUse denies the tool call
    return 0


if __name__ == "__main__":
    sys.exit(main())
