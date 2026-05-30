#!/usr/bin/env python3
"""PostToolUse formatter for the BlackIce.Server repo.

Runs `dotnet format` on a single edited C# file so commits stay consistently styled
for a community-facing codebase. Two design notes specific to this repo:

  * It scopes to the NEAREST .csproj above the edited file, not the 14-project
    solution. Whole-solution `dotnet format` loads every project's MSBuild workspace
    (tens of seconds); per-project keeps it quick.
  * It is meant to be wired as an ASYNC hook, so it never blocks Claude's turn — it
    just tidies the file after the fact.

Always exits 0: formatting is a quality nicety, never a gate.
"""
import json
import os
import subprocess
import sys


def nearest_csproj(start_file: str) -> str | None:
    """Walk up from the file's directory to find the closest .csproj."""
    d = os.path.dirname(os.path.abspath(start_file))
    while True:
        projs = [f for f in os.listdir(d) if f.endswith(".csproj")] if os.path.isdir(d) else []
        if projs:
            return os.path.join(d, projs[0])
        parent = os.path.dirname(d)
        if parent == d:
            return None
        d = parent


def main() -> int:
    try:
        data = json.load(sys.stdin)
    except Exception:
        return 0

    ti = data.get("tool_input", {}) or {}
    resp = data.get("tool_response", {}) or {}
    path = resp.get("filePath") or ti.get("file_path") or ""

    if not path.endswith(".cs") or not os.path.isfile(path):
        return 0
    # Never touch decompiled/local-only trees.
    if "decompiled" in path.replace("\\", "/").lower():
        return 0

    proj = nearest_csproj(path)
    if not proj:
        return 0

    try:
        subprocess.run(
            ["dotnet", "format", proj, "--include", os.path.abspath(path),
             "--no-restore", "--verbosity", "quiet"],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=120,
        )
    except Exception:
        pass  # quality step — swallow everything
    return 0


if __name__ == "__main__":
    sys.exit(main())
