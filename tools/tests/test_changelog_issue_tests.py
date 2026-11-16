"""tools/check-changelog-issue-tests.py: a release whose newest changelog entry cites a GitHub issue
must have a test under tests/<Plugin>* that names the same issue.

Run: python3 -m unittest tools/tests/test_changelog_issue_tests.py
"""
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TOOL = ROOT / "tools" / "check-changelog-issue-tests.py"

BOM = "﻿"


def changelog(*entries):
    return BOM + "\n\n".join(entries) + "\n"


NEWEST_CITES_12 = "## v1.0.0.2 (2026-11-16) [testing]\n### Fixed\n- The buttons no longer cover the close button (issue #12)."
OLDER_CITES_4 = "## v1.0.0.1 (2026-11-01)\n### Fixed\n- Auto-target kept its target (issue #4)."
NEWEST_CITES_NONE = "## v1.0.0.3 (2026-11-17) [testing]\n### Changed\n- Wording."


class GateTests(unittest.TestCase):
    def run_gate(self, files, plugin="Demo"):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for rel, text in files.items():
                p = root / rel
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_text(text, encoding="utf-8")
            r = subprocess.run(
                [sys.executable, str(TOOL), "--changelog", str(root / "src" / plugin / "CHANGELOG.md"),
                 "--plugin", plugin, "--repo-root", str(root)],
                capture_output=True, text=True)
            return r.returncode, r.stdout + r.stderr

    def test_cited_issue_without_a_test_refuses(self):
        code, out = self.run_gate({"src/Demo/CHANGELOG.md": changelog(NEWEST_CITES_12, OLDER_CITES_4)})
        self.assertEqual(code, 1, out)
        self.assertIn("issue #12", out)

    def test_cited_issue_with_a_test_naming_it_passes(self):
        code, out = self.run_gate({
            "src/Demo/CHANGELOG.md": changelog(NEWEST_CITES_12, OLDER_CITES_4),
            "tests/Demo.Harness/OverlayCases.cs": "// Offline cases (issue #12): the overlay never covers the close button.\n",
        })
        self.assertEqual(code, 0, out)

    def test_any_test_folder_of_the_plugin_counts(self):
        code, out = self.run_gate({
            "src/Demo/CHANGELOG.md": changelog(NEWEST_CITES_12),
            "tests/Demo.UiHarness/Program.cs": "// Issue #12: frame check.\n",
        })
        self.assertEqual(code, 0, out)

    def test_only_the_newest_entry_is_checked(self):
        code, out = self.run_gate({"src/Demo/CHANGELOG.md": changelog(NEWEST_CITES_NONE, NEWEST_CITES_12, OLDER_CITES_4)})
        self.assertEqual(code, 0, out)

    def test_a_shorter_issue_number_does_not_satisfy_a_longer_one(self):
        code, out = self.run_gate({
            "src/Demo/CHANGELOG.md": changelog(NEWEST_CITES_12),
            "tests/Demo.Harness/Cases.cs": "// issue #1 and issue #123\n",
        })
        self.assertEqual(code, 1, out)

    def test_another_plugins_tests_do_not_count(self):
        code, out = self.run_gate({
            "src/Demo/CHANGELOG.md": changelog(NEWEST_CITES_12),
            "tests/DemoOther.Harness/Cases.cs": "// issue #12\n",
            "tests/Other.Harness/Cases.cs": "// issue #12\n",
        })
        self.assertEqual(code, 1, out)

    def test_build_output_does_not_count(self):
        code, out = self.run_gate({
            "src/Demo/CHANGELOG.md": changelog(NEWEST_CITES_12),
            "tests/Demo.Harness/bin/Release/Cases.cs": "// issue #12\n",
            "tests/Demo.Harness/obj/Release/Cases.cs": "// issue #12\n",
        })
        self.assertEqual(code, 1, out)

    def test_the_lmc_release_that_fixes_issue_12_passes(self):
        r = subprocess.run(
            [sys.executable, str(TOOL), "--changelog", str(ROOT / "src" / "LazyMarketCompanion" / "CHANGELOG.md"),
             "--plugin", "LazyMarketCompanion", "--repo-root", str(ROOT)],
            capture_output=True, text=True)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)


if __name__ == "__main__":
    unittest.main()
