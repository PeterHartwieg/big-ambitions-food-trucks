from pathlib import Path
import contextlib
import io
import json
import subprocess
import sys
import tempfile
import unittest

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
import md_ban


def git(root, *args):
    subprocess.run(['git', *args], cwd=root, check=True, capture_output=True)


class MarkdownBanTests(unittest.TestCase):
    def test_branch_adds_no_markdown(self):
        self.assertEqual(md_ban.new_markdown(*md_ban.repo_changes(TOOLS.parent, 'origin/main')), [])

    def setUp(self):
        self.folder = tempfile.TemporaryDirectory(prefix='ft-md-ban-')
        self.root = Path(self.folder.name)
        git(self.root, 'init', '-q', '-b', 'main')
        git(self.root, 'config', 'user.email', 't@example.com')
        git(self.root, 'config', 'user.name', 't')
        self.write('README.md', 'old readme\nwith several lines\nof text\n')
        self.write('notes.txt', 'plain notes\nwith several lines\nof text\n')
        self.commit()
        git(self.root, 'branch', 'base')

    def tearDown(self):
        self.folder.cleanup()

    def write(self, name, text):
        (self.root / name).parent.mkdir(parents=True, exist_ok=True)
        (self.root / name).write_text(text)

    def commit(self):
        git(self.root, 'add', '-A')
        git(self.root, 'commit', '-q', '-m', 'x')

    def found(self):
        return md_ban.new_markdown(*md_ban.repo_changes(self.root, 'base'))

    def run_main(self, event=None, base='base'):
        args = ['--repo', str(self.root), '--base', base]
        if event is not None:
            events = tempfile.TemporaryDirectory(prefix='ft-md-ban-event-')
            self.addCleanup(events.cleanup)
            path = Path(events.name) / 'event.json'
            path.write_text(json.dumps(event))
            args += ['--event', str(path)]
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = md_ban.main(args)
        self.output = out.getvalue()
        return code

    def test_no_markdown_passes(self):
        self.write('a.py', 'x = 1\n')
        self.write('README.md', 'edited readme\nwith several lines\nof text\n')
        self.commit()
        self.assertEqual(self.found(), [])
        self.assertEqual(self.run_main(), 0)

    def test_added_markdown_fails(self):
        self.write('docs/plan.md', 'plan\n')
        self.write('page.MDX', 'page\n')
        self.commit()
        self.assertEqual(self.found(), ['docs/plan.md', 'page.MDX'])
        self.assertEqual(self.run_main(), 1)
        self.assertIn('docs/plan.md: new markdown file not allowed', self.output)

    def test_untracked_markdown_fails(self):
        self.write('status.md', 'status\n')
        self.assertEqual(self.found(), ['status.md'])

    def test_copy_fails_and_markdown_rename_passes(self):
        git(self.root, 'mv', 'README.md', 'GUIDE.md')
        self.commit()
        self.assertEqual(self.found(), [])
        git(self.root, 'branch', '-f', 'base')
        self.write('copy.md', (self.root / 'GUIDE.md').read_text())
        self.commit()
        self.assertEqual(self.found(), ['copy.md'])

    def test_rename_from_non_markdown_fails(self):
        git(self.root, 'mv', 'notes.txt', 'notes.md')
        self.commit()
        self.assertEqual(self.found(), ['notes.md'])

    def test_label_allows_new_markdown(self):
        self.write('plan.md', 'plan\n')
        self.commit()
        self.assertEqual(self.run_main({'pull_request': {'labels': [{'name': 'docs-approved'}]}}), 0)
        self.assertEqual(self.run_main({'pull_request': {'labels': [{'name': 'other'}]}}), 1)
        self.assertEqual(self.run_main({}), 1)

    def test_markdown_deleted_on_main_passes_against_merge_parent(self):
        git(self.root, 'checkout', '-q', '-b', 'feature')
        self.write('b.txt', 'b\n')
        self.commit()
        git(self.root, 'checkout', '-q', 'main')
        git(self.root, 'rm', '-q', 'README.md')
        self.commit()
        git(self.root, 'merge', '-q', '--no-ff', '-m', 'merge', 'feature')
        self.assertEqual(self.run_main(base='HEAD^1'), 0)


if __name__ == '__main__':
    unittest.main()
