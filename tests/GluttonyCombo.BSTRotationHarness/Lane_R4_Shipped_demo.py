#!/usr/bin/env python3
"""Fails-before demonstration for the r4-shipped regression lane (task tasks-20261015-crucible-r4-regression-shipped-fixes-01).

Run on DAJOEYROG from the lalalazy checkout root (or any worktree that holds the lane files):

    python tests\\GluttonyCombo.BSTRotationHarness\\Lane_R4_Shipped_demo.py <abaddon|dispel|dash|hourglass|production|dispel-parent> [--out DIR] [--keep]

It makes a scratch worktree at the build BEFORE the fix, copies in the lane's own files (Support + that fix's file; the files are byte-identical to the
integration head's, no stub is added to the source under test), makes `Program` partial and adds the one call to `Main`, builds the BST rotation
harness (H1) and runs it. The output file starts with `#` header lines and ends with the harness's own last line (`FAILED (k of m)` when the fix's
cases fail, as they must).  `production` overlays all four fix files on the tag GluttonyCombo-v1.0.4.301.
"""
import os, re, shutil, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
HARNESS = os.path.join("tests", "GluttonyCombo.BSTRotationHarness")
FIXES = {
    # name: (build before the fix, fix methods, note)
    "abaddon": ("ce67410e4", ["Abaddon"], "1.0.4.304, the Abaddon alone with a Morpho alive is held at any HP"),
    # 692fd8227's parent (33e61844b) is the commit that added the fix's own failing-first cases, so four older cases fail there too; its parent b1c3849d (1.0.4.269 testing) has the
    # same dispel code and a green harness, and is the build the demonstration runs on. "dispel-parent" runs the lane on 692fd8227~1 as well, for the record.
    "dispel": ("b1c3849d", ["Dispel"], "the build before the dispel fix (1.0.4.269 testing): Regen 989 a live dispel row, no proven-first aim"),
    "dispel-parent": ("692fd8227~1", ["Dispel"], "692fd8227's parent: the same code plus the fix's own failing-first cases"),
    "dash": ("21295dd09", ["Dash"], "1.0.4.301 development: DashLandingDecline has no mechanic hold"),
    "hourglass": ("587d75b65", ["Hourglass"], "the build before a0c56e308: the Final Hourglass is attacked whatever the needle does"),
    "production": ("GluttonyCombo-v1.0.4.301", ["Abaddon", "Dispel", "Dash", "Hourglass"], "production tag 1.0.4.301"),
}


def run(cmd, cwd, **kw):
    return subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, shell=isinstance(cmd, str), **kw)


def main():
    args = sys.argv[1:]
    if not args or args[0] not in FIXES:
        sys.exit(__doc__)
    name = args[0]
    out_dir = args[args.index("--out") + 1] if "--out" in args else os.path.join(ROOT, "..", "r4-shipped-demo-out")
    keep = "--keep" in args
    ref, methods, note = FIXES[name]
    os.makedirs(out_dir, exist_ok=True)
    wt = os.path.abspath(os.path.join(ROOT, "..", f"wt-r4-shipped-demo-{name}"))
    if os.path.isdir(wt):
        run(["git", "worktree", "remove", "--force", wt], ROOT)
    r = run(["git", "worktree", "add", "--detach", wt, ref], ROOT)
    if r.returncode:
        sys.exit(r.stdout + r.stderr)
    sha = run(["git", "log", "-1", "--format=%h %s", "HEAD"], wt).stdout.strip()

    h = os.path.join(wt, HARNESS)
    files = ["Lane_R4_Shipped_Support.cs"] + [f"Lane_R4_Shipped_{m}.cs" for m in methods]
    for f in files:
        shutil.copy(os.path.join(HERE, f), os.path.join(h, f))
    calls = "".join(f"        R4Shipped{m}();\n" for m in methods)
    with open(os.path.join(h, "Lane_R4_Shipped.cs"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write("namespace GluttonyCombo.BSTRotationHarness;\n\ninternal static partial class Program\n{\n"
                 "    private static void R4ShippedLane20261015()\n    {\n" + calls + "    }\n}\n")
    prog = os.path.join(h, "Program.cs")
    text = open(prog, encoding="utf-8", newline="").read()
    if "internal static class Program" in text:
        text = text.replace("internal static class Program", "internal static partial class Program", 1)
    if "R4ShippedLane20261015();" not in text:
        anchor = "        SimulateAllLevels(verbose);"
        assert text.count(anchor) == 1, "Main anchor not found once"
        text = text.replace(anchor, "        R4ShippedLane20261015();\n" + anchor)
    open(prog, "w", encoding="utf-8", newline="").write(text)

    b = run(f'dotnet build {HARNESS} -c Release', wt)
    out_file = os.path.join(out_dir, "fails-before-%s.txt" % name if name != "production" else "production-301.txt")
    header = [f"# r4-shipped fails-before demonstration: {name} ({note})",
              f"# build under test: {ref} = {sha}",
              f"# overlay (copied unchanged from the integration head, no source stub): {', '.join(files)} + generated Lane_R4_Shipped.cs calling R4Shipped{'(), R4Shipped'.join(methods)}()",
              f"# command: dotnet build {HARNESS} -c Release && dotnet {HARNESS}\\bin\\Release\\net10.0\\GluttonyCombo.BSTRotationHarness.dll"]
    if b.returncode:
        body = b.stdout + b.stderr
        open(out_file, "w", encoding="utf-8").write("\n".join(header) + "\n# BUILD FAILED\n" + body)
        sys.exit("build failed, see " + out_file)
    exe = os.path.join(wt, HARNESS, "bin", "Release", "net10.0", "GluttonyCombo.BSTRotationHarness.dll")
    r = run(["dotnet", exe], wt)
    open(out_file, "w", encoding="utf-8").write("\n".join(header) + "\n" + r.stdout + r.stderr)
    print(out_file, "exit", r.returncode)
    print("\n".join(r.stdout.strip().splitlines()[-1:]))
    if not keep:
        run(["git", "worktree", "remove", "--force", wt], ROOT)


main()
