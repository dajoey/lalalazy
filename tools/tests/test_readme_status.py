"""tools/readme_status.py: the README plugin table's Status column is written from pluginmaster.json.

Run: python3 -m unittest tools/tests/test_readme_status.py
"""
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TOOL = ROOT / "tools" / "readme_status.py"


def load():
    spec = importlib.util.spec_from_file_location("readme_status", TOOL)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


README = """# lalalazy

## Plugins

| Plugin | Description | Status |
|--------|-------------|--------|
| **Lazy Fish Sitter** | Sits you down while you fish. | Testing |
| **Gluttony Combo** | Combos \\| more combos. | Active |
| **LazyCrucible** | Crucible helper. | Testing |
| **Not In Feed** | Hand-kept row. | Active |

## Installation

| Not | The | Table |
|-----|-----|-------|
| **Lazy Fish Sitter** | other table | untouched |
"""

FEED = [
    {"InternalName": "LazyFishSitter", "Name": "Lazy Fish Sitter", "AssemblyVersion": "0.1.5.1",
     "TestingAssemblyVersion": "0.1.5.1", "IsTestingExclusive": False},
    {"InternalName": "GluttonyCombo", "Name": "Gluttony Combo", "AssemblyVersion": "1.0.4.320",
     "TestingAssemblyVersion": "1.0.4.323", "IsTestingExclusive": False},
    {"InternalName": "LazyCrucible", "Name": "LazyCrucible", "AssemblyVersion": "0.0.0.0",
     "TestingAssemblyVersion": "0.1.9.16", "IsTestingExclusive": True},
]


class ReadmeStatusTest(unittest.TestCase):
    def setUp(self):
        self.mod = load()

    def test_status_cell(self):
        cell = self.mod.status_cell
        self.assertEqual("Production 0.1.5.1", cell(FEED[0]))
        self.assertEqual("Production 1.0.4.320 · Testing 1.0.4.323", cell(FEED[1]))
        self.assertEqual("Testing only 0.1.9.16", cell(FEED[2]))
        self.assertEqual("Testing only 8.6.0", cell({"AssemblyVersion": "0.0.0.0",
                                                     "TestingAssemblyVersion": "8.6.0"}))
        self.assertEqual("Production 2.0.0.0", cell({"AssemblyVersion": "2.0.0.0"}))

    def test_rewrites_only_the_plugin_table_status_column(self):
        out = self.mod.rewrite(README, FEED)
        self.assertIn("| **Lazy Fish Sitter** | Sits you down while you fish. | Production 0.1.5.1 |", out)
        self.assertIn("| **Gluttony Combo** | Combos \\| more combos. | Production 1.0.4.320 · Testing 1.0.4.323 |",
                      out)
        self.assertIn("| **LazyCrucible** | Crucible helper. | Testing only 0.1.9.16 |", out)
        self.assertIn("| **Not In Feed** | Hand-kept row. | Active |", out, "a row the feed lacks is kept")
        self.assertIn("| **Lazy Fish Sitter** | other table | untouched |", out, "only the Plugins table")
        self.assertEqual(README.count("\n"), out.count("\n"))
        self.assertEqual(out, self.mod.rewrite(out, FEED), "a second run changes nothing")

    def test_cli_writes_and_check_reports_drift(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            (repo / "README.md").write_text(README, encoding="utf-8")
            (repo / "pluginmaster.json").write_text(json.dumps(FEED), encoding="utf-8")
            run = lambda *a: subprocess.run([sys.executable, str(TOOL), "--repo", str(repo), *a],
                                            capture_output=True, text=True)
            self.assertEqual(1, run("--check").returncode, "stale README fails --check")
            self.assertEqual(README, (repo / "README.md").read_text(encoding="utf-8"), "--check writes nothing")
            done = run()
            self.assertEqual(0, done.returncode, done.stderr)
            self.assertIn("Production 0.1.5.1", (repo / "README.md").read_text(encoding="utf-8"))
            self.assertEqual(0, run("--check").returncode)
            raw = (repo / "README.md").read_bytes()
            self.assertNotIn(b"\r\n", raw)

    def test_malformed_rows_are_left_alone(self):
        text = ("## Plugins\n\n| Plugin | Description | Status |\n|---|---|---|\n"
                "| **Lazy Fish Sitter** | no closing pipe | Testing\n"
                "| **Gluttony Combo** | extra | Active | stray |\n"
                "| **LazyCrucible** | ok | Testing |  \n")
        out = self.mod.rewrite(text, FEED)
        self.assertIn("| **Lazy Fish Sitter** | no closing pipe | Testing\n", out)
        self.assertIn("| **Gluttony Combo** | extra | Active | stray |\n", out)
        self.assertIn("| **LazyCrucible** | ok | Testing only 0.1.9.16 |  \n", out, "trailing spaces kept")

    def test_status_column_found_by_header_name(self):
        text = ("## Plugins\n\n| Plugin | Status | Description |\n|---|---|---|\n"
                "| **Lazy Fish Sitter** | Testing | sits \\| fishes |\n")
        out = self.mod.rewrite(text, FEED)
        self.assertIn("| **Lazy Fish Sitter** | Production 0.1.5.1 | sits \\| fishes |\n", out)
        no_status = "## Plugins\n\n| Plugin | Description |\n|---|---|\n| **Lazy Fish Sitter** | d |\n"
        self.assertEqual(no_status, self.mod.rewrite(no_status, FEED), "no Status column, no change")

    def test_crlf_readme_keeps_its_line_endings(self):
        crlf = README.replace("\n", "\r\n")
        out = self.mod.rewrite(crlf, FEED)
        self.assertIn("| **Lazy Fish Sitter** | Sits you down while you fish. | Production 0.1.5.1 |\r\n", out)
        self.assertEqual(crlf.count("\r\n"), out.count("\r\n"))

    def test_live_readme_has_a_plugins_table(self):
        # a README row may name a plugin before its first release, so rows are not checked
        # against the feed here (that would turn main red and stop README updates)
        readme = (ROOT / "README.md").read_text(encoding="utf-8")
        self.assertTrue(self.mod.plugin_rows(readme))


if __name__ == "__main__":
    unittest.main()
