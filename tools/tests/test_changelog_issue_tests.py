"""tools/check-changelog-issue-tests.py: a release whose newest changelog entry cites a GitHub issue
must have a committed test for that plugin that names the same issue.

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
    def run_gate(self, files, plugin="Demo", untracked=None, edits=None):
        """Build a git repo: `files` are committed, `untracked` are written but never added,
        `edits` overwrite a committed file after the commit (an uncommitted change)."""
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)

            def write(rel, text):
                p = root / rel
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_text(text, encoding="utf-8")

            def git(*a):
                subprocess.run(["git", "-C", str(root), "-c", "user.name=t", "-c", "user.email=t@t",
                                "-c", "core.autocrlf=false", *a],
                               check=True, capture_output=True, text=True)

            git("init", "-q")
            for rel, text in files.items():
                write(rel, text)
            git("add", "-A")
            git("commit", "-q", "-m", "fixture")
            for rel, text in (untracked or {}).items():
                write(rel, text)
            for rel, text in (edits or {}).items():
                write(rel, text)
            r = subprocess.run(
                [sys.executable, str(TOOL), "--changelog", str(root / "src" / plugin / "CHANGELOG.md"),
                 "--plugin", plugin, "--repo-root", str(root)],
                capture_output=True, text=True)
            return r.returncode, r.stdout + r.stderr

    def entry(self, body, header="## v1.0.0.2 (2026-11-16) [testing]"):
        return changelog(header + "\n" + body)

    def cites(self, line, section="### Fixed"):
        """Gate result for a newest entry with `line` under `section` and no test at all."""
        return self.run_gate({"src/Demo/CHANGELOG.md": self.entry(section + "\n" + line)})

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

    # --- wrong block 1: a test folder named for something else but built from the plugin's source ---
    def test_a_test_folder_that_compiles_the_plugin_source_counts(self):
        for sep in ("..\\..\\src\\Demo\\X.cs", "../../src/Demo/X.cs"):
            with self.subTest(sep=sep):
                code, out = self.run_gate({
                    "src/Demo/CHANGELOG.md": changelog(NEWEST_CITES_12),
                    "tests/Other.Golden/Other.Golden.csproj": f'<Project><ItemGroup><Compile Include="{sep}" /></ItemGroup></Project>',
                    "tests/Other.Golden/Cases.cs": "// issue #12: golden frame.\n",
                })
                self.assertEqual(code, 0, out)

    def test_a_csproj_that_builds_another_plugin_does_not_count(self):
        code, out = self.run_gate({
            "src/Demo/CHANGELOG.md": changelog(NEWEST_CITES_12),
            "tests/Other.Golden/Other.Golden.csproj": '<Compile Include="..\\..\\src\\DemoOther\\X.cs" />',
            "tests/Other.Golden/Cases.cs": "// issue #12\n",
        })
        self.assertEqual(code, 1, out)

    # --- wrong block 2: only the Fixed section claims a fix ---
    def test_an_issue_only_under_notes_is_not_a_fix_claim(self):
        code, out = self.cites("- Issue #12 is still open.", section="### Notes")
        self.assertEqual(code, 0, out)

    def test_an_entry_without_subsections_is_scanned_whole(self):
        code, out = self.run_gate({"src/Demo/CHANGELOG.md": self.entry("- The overlay no longer covers the close button (issue #12).")})
        self.assertEqual(code, 1, out)

    def test_fixed_heading_variants_and_crlf_are_read(self):
        for body in ("###Fixed\n- Gone (issue #12).", "### Fixes\n- Gone (#12).", "### Fixed\r\n- Gone (issue #12).\r\n"):
            with self.subTest(body=body):
                code, out = self.run_gate({"src/Demo/CHANGELOG.md": self.entry(body)})
                self.assertEqual(code, 1, out)

    def test_the_fixed_section_ends_at_the_next_subsection(self):
        code, out = self.run_gate({"src/Demo/CHANGELOG.md": self.entry(
            "### Fixed\n- Something else.\n### Notes\n- Issue #12 is still open.")})
        self.assertEqual(code, 0, out)

    # --- wrong pass 2: plural and closing forms, parenthesised lists ---
    def test_closing_and_plural_forms_are_fix_claims(self):
        for line in ("- Issues #12 and #13 are gone.", "- Fixes #12.", "- fixed #12", "- Closes #12", "- Resolved #12.",
                     "- The overlay no longer overlaps (#12).", "- Overlay fix (#13, #12).",
                     "- Gone: issue #13, #12 & #14.", "- Overlay (#13 , #12 )."):
            with self.subTest(line=line):
                code, out = self.cites(line)
                self.assertEqual(code, 1, out)
                self.assertIn("#12", out)

    def test_a_number_not_joined_to_a_claim_word_is_not_a_claim(self):
        for line in ("- prefix #12 handling.", "- Matches upstream #12.", "- See #12 for context.", "- Prefix #3 and #12."):
            with self.subTest(line=line):
                code, out = self.cites(line)
                self.assertEqual(code, 0, out)

    def test_a_longer_number_in_the_changelog_is_not_the_shorter_one(self):
        code, out = self.cites("- issue #120 is gone.")
        self.assertEqual(code, 1, out)
        self.assertIn("issue #120", out)
        self.assertNotIn("issue #12,", out)
        self.assertNotIn("issue #12 ", out)

    def test_each_listed_number_needs_its_own_test(self):
        files = {"src/Demo/CHANGELOG.md": self.entry("### Fixed\n- Issues #12 and #13."),
                 "tests/Demo.Harness/Cases.cs": "// issue #12\n"}
        code, out = self.run_gate(files)
        self.assertEqual(code, 1, out)
        self.assertIn("issue #13", out)
        self.assertNotIn("issue #12,", out)
        files["tests/Demo.Harness/More.cs"] = "// issue #13\n"
        code, out = self.run_gate(files)
        self.assertEqual(code, 0, out)

    def test_a_longer_number_does_not_satisfy_a_shorter_listed_one(self):
        code, out = self.run_gate({
            "src/Demo/CHANGELOG.md": changelog(NEWEST_CITES_12),
            "tests/Demo.Harness/Cases.cs": "// issue #120\n",
        })
        self.assertEqual(code, 1, out)

    def test_a_plural_test_reference_counts(self):
        code, out = self.run_gate({
            "src/Demo/CHANGELOG.md": changelog(NEWEST_CITES_12),
            "tests/Demo.Harness/Cases.cs": "// Issues #12 and #13\n",
        })
        self.assertEqual(code, 0, out)

    # --- wrong pass 1: only committed test content counts ---
    def test_an_untracked_test_file_does_not_count(self):
        code, out = self.run_gate({"src/Demo/CHANGELOG.md": changelog(NEWEST_CITES_12), "tests/Demo.Harness/Other.cs": "// nothing\n"},
                                  untracked={"tests/Demo.Harness/Cases.cs": "// issue #12\n"})
        self.assertEqual(code, 1, out)

    def test_an_uncommitted_edit_naming_the_issue_does_not_count(self):
        code, out = self.run_gate({"src/Demo/CHANGELOG.md": changelog(NEWEST_CITES_12), "tests/Demo.Harness/Cases.cs": "// nothing\n"},
                                  edits={"tests/Demo.Harness/Cases.cs": "// issue #12\n"})
        self.assertEqual(code, 1, out)

    def test_a_repo_without_git_history_refuses_with_the_git_error(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "src" / "Demo").mkdir(parents=True)
            (root / "src" / "Demo" / "CHANGELOG.md").write_text(changelog(NEWEST_CITES_12), encoding="utf-8")
            r = subprocess.run([sys.executable, str(TOOL), "--changelog", str(root / "src" / "Demo" / "CHANGELOG.md"),
                                "--plugin", "Demo", "--repo-root", str(root)], capture_output=True, text=True)
            self.assertEqual(r.returncode, 1, r.stdout + r.stderr)
            self.assertIn("git", (r.stdout + r.stderr).lower())


    def test_the_lmc_release_that_fixes_issue_12_passes(self):
        r = subprocess.run(
            [sys.executable, str(TOOL), "--changelog", str(ROOT / "src" / "LazyMarketCompanion" / "CHANGELOG.md"),
             "--plugin", "LazyMarketCompanion", "--repo-root", str(ROOT)],
            capture_output=True, text=True)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)


if __name__ == "__main__":
    unittest.main()
