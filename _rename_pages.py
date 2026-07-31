#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
One-shot refactor: rename legacy Window1..Window14 classes to semantic names.
Scope is fixed to the exact source files; obj/bin/_build/_smoketest are NOT touched.
Word-boundary regex prevents Window1 from matching inside Window10 etc.
BOM is preserved byte-for-byte (decode/encode utf-8).
"""
import os, re

ROOT = r"F:\new\WINHELP"

# old class token -> new semantic name (namespace WINHELP kept)
MAPPING = {
    "Window1":  "SiteFinderPage",
    "Window2":  "WinHelperPage",
    "Window3":  "AppearancePage",
    "Window4":  "SettingsPage",
    "Window5":  "BugReportPage",
    "Window6":  "PcHelpPage",
    "Window7":  "SystemStatusPage",
    "Window8":  "AgentAssistantPage",
    "Window9":  "OfficialSitesPage",
    "Window10": "SystemCleanerPage",
    "Window11": "StartupPage",
    "Window12": "NetworkDiagnosticsPage",
    "Window13": "TroubleshootWizardPage",
    "Window14": "BeginnerGuidePage",
}

# longest tokens first so e.g. Window10 is matched before Window1
PATTERNS = sorted(
    ((k, re.compile(r"\b" + re.escape(k) + r"\b"), v) for k, v in MAPPING.items()),
    key=lambda x: len(x[0]), reverse=True,
)

# files whose CONTENT must be token-replaced
CONTENT_FILES = [
    "App.xaml.cs",
    "MainWindow.xaml.cs",
    "UpdateManager.cs",
    "SetupPage.xaml",
]
for old in MAPPING:
    CONTENT_FILES.append(old + ".xaml")
    CONTENT_FILES.append(old + ".xaml.cs")

total = 0
print("=== content replacement ===")
for f in CONTENT_FILES:
    path = os.path.join(ROOT, f)
    if not os.path.exists(path):
        print("MISSING (skip):", f)
        continue
    with open(path, "rb") as fh:
        data = fh.read()
    text = data.decode("utf-8")
    cnt = 0
    for _, pat, new in PATTERNS:
        text, n = pat.subn(new, text)
        cnt += n
    if cnt:
        with open(path, "wb") as fh:
            fh.write(text.encode("utf-8"))
        print(f"EDITED  {f:24s} {cnt:3d} replacement(s)")
        total += cnt
    else:
        print(f"no-op   {f}")
print(f"total content replacements: {total}")

print("\n=== file rename ===")
for old, new in MAPPING.items():
    for ext in (".xaml", ".xaml.cs"):
        op = os.path.join(ROOT, old + ext)
        np = os.path.join(ROOT, new + ext)
        if os.path.exists(op):
            os.rename(op, np)
            print(f"RENAMED {old+ext} -> {new+ext}")
        else:
            print(f"RENAME MISSING {old+ext}")
print("\ndone.")
