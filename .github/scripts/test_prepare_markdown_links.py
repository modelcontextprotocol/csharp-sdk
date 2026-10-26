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


if __name__ == "__main__":
    unittest.main()
