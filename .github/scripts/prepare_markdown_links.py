import argparse
from pathlib import Path


class RepositoryLinks:
    def __init__(self, root):
        self.root = Path(root).resolve()

    def prepare(self):
        docs = self.root / "docs"
        if not docs.is_dir():
            raise ValueError(f"Missing documentation directory: {docs}")
        if docs.is_symlink():
            raise ValueError(f"Refusing to rewrite a documentation symlink: {docs}")

        changed = 0
        prefixes = (
            "https://github.com/modelcontextprotocol/csharp-sdk/tree/main/",
            "https://github.com/modelcontextprotocol/csharp-sdk/blob/main/",
        )
        for path in docs.rglob("*.md"):
            if path.is_symlink():
                raise ValueError(f"Refusing to rewrite a documentation symlink: {path}")
            text = path.read_text(encoding="utf-8")
            updated = text
            for prefix in prefixes:
                updated = updated.replace(prefix, "/")
            if updated != text:
                path.write_text(updated, encoding="utf-8")
                changed += 1
        return changed


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Resolve this repository's main-branch links against the proposed checkout for mlc."
    )
    parser.add_argument("root")
    arguments = parser.parse_args()
    count = RepositoryLinks(arguments.root).prepare()
    print(f"Prepared repository-local links in {count} Markdown files; mlc still validates each target.")
