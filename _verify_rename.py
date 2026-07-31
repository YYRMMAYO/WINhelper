#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import os, re

ROOT = r"F:\new\WINHELP"
EXCLUDE = {"obj", "bin", ".obj2", "_build", "_smoketest", "dist", "_old_artifacts", ".git"}
PAT = re.compile(r"\bWindow(1[0-4]|[1-9])\b")

hits = 0
for dirpath, dirnames, filenames in os.walk(ROOT):
    # prune excluded dirs in-place
    dirnames[:] = [d for d in dirnames if d not in EXCLUDE]
    for fn in filenames:
        if fn.lower().endswith((".xaml", ".cs")):
            p = os.path.join(dirpath, fn)
            try:
                with open(p, "rb") as fh:
                    text = fh.read().decode("utf-8", "ignore")
            except Exception:
                continue
            for m in PAT.finditer(text):
                rel = os.path.relpath(p, ROOT)
                print(f"{rel}:{0}  stale token: {m.group(0)}")
                hits += 1
print("STALE_REFERENCES_FOUND" if hits else "NO_STALE_REFERENCES")
