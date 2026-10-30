from pathlib import Path
import tempfile
import unittest

from prepare_markdown_links import RepositoryLinks


class RepositoryLinksTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        (self.root / "docs").mkdir()

    def test_main_links_become_checkout_paths_without_ignoring_missing_targets(self):
        path = self.root / "docs" / "example.md"
        path.write_text(
            "[sample](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/SkillsServer)\n"
            "[source](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/src/missing.cs)\n",
            encoding="utf-8",
        )

        self.assertEqual(RepositoryLinks(self.root).prepare(), 1)
        self.assertEqual(
            path.read_text(encoding="utf-8"),
            "[sample](/samples/SkillsServer)\n[source](/src/missing.cs)\n",
        )

    def test_other_repositories_and_pinned_revisions_remain_web_links(self):
        path = self.root / "docs" / "example.md"
        original = (
            "[other](https://github.com/other/project/tree/main/sample)\n"
            "[pinned](https://github.com/modelcontextprotocol/csharp-sdk/tree/1234567/sample)\n"
            "[relative](other.md)\n"
        )
        path.write_text(original, encoding="utf-8")

        self.assertEqual(RepositoryLinks(self.root).prepare(), 0)
        self.assertEqual(path.read_text(encoding="utf-8"), original)

    def test_missing_documentation_is_not_a_success(self):
        with self.assertRaises(ValueError):
            RepositoryLinks(self.root / "absent").prepare()

    def test_alert_markers_are_escaped_without_skipping_links_in_the_body(self):
        path = self.root / "docs" / "example.md"
        original = "".join(
            f"> [!{kind}]\n> [link](missing.md) and [missing reference].\n\n"
            for kind in ("NOTE", "TIP", "IMPORTANT", "WARNING", "CAUTION")
        )
        path.write_text(original, encoding="utf-8")

        self.assertEqual(RepositoryLinks(self.root).prepare(), 1)
        self.assertEqual(path.read_text(encoding="utf-8"), original.replace("> [!", "> \\[!"))
        self.assertEqual(RepositoryLinks(self.root).prepare(), 0)

    def test_nested_alerts_and_whitespace_preserve_line_numbers(self):
        path = self.root / "docs" / "example.md"
        path.write_text("  > > [!NOTE] \t\n> [link](missing.md)\n", encoding="utf-8")

        self.assertEqual(RepositoryLinks(self.root).prepare(), 1)
        self.assertEqual(
            path.read_text(encoding="utf-8"),
            "  > > \\[!NOTE] \t\n> [link](missing.md)\n",
        )

    def test_only_standalone_known_alert_markers_are_escaped(self):
        path = self.root / "docs" / "example.md"
        original = (
            "[!NOTE]\n"
            "> [!UNKNOWN]\n"
            "> [!NOTE](missing.md)\n"
            "> [!NOTE] and [link](missing.md)\n"
            "> [missing reference]\n"
        )
        path.write_text(original, encoding="utf-8")

        self.assertEqual(RepositoryLinks(self.root).prepare(), 0)
        self.assertEqual(path.read_text(encoding="utf-8"), original)


if __name__ == "__main__":
    unittest.main()
