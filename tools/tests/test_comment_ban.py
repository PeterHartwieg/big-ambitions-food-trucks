from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
import comment_ban


class CommentBanTests(unittest.TestCase):
    def test_repo_has_no_unlisted_comments(self):
        _, violations = comment_ban.scan(TOOLS.parent)
        self.assertEqual([f'{c.path}:{c.line}' for c in violations], [])

    def test_every_listed_excuse_is_still_a_comment(self):
        found, _ = comment_ban.scan(TOOLS.parent)
        starts = {(c.path, c.line) for c in found}
        stale = sorted(f'{p}:{n}' for p, n in comment_ban.load_excuses(TOOLS.parent) - starts)
        self.assertEqual(stale, [])

    def scan_fixture(self, files, excuses=''):
        with tempfile.TemporaryDirectory(prefix='ft-comment-ban-') as folder:
            root = Path(folder)
            (root / 'docs').mkdir()
            (root / 'docs/comment-excuses.md').write_text(excuses)
            for name, text in files.items():
                (root / name).parent.mkdir(parents=True, exist_ok=True)
                (root / name).write_text(text)
            _, violations = comment_ban.scan(root, list(files))
            return [(c.path, c.line) for c in violations]

    def test_plain_comment_fails(self):
        self.assertEqual(self.scan_fixture({'A.cs': 'class A {\n    // explains\n    int x = 1; // trailing\n}\n'}),
                         [('A.cs', 2), ('A.cs', 3)])
        self.assertEqual(self.scan_fixture({'a.py': 'x = 1\n# explains\n'}), [('a.py', 2)])
        self.assertEqual(self.scan_fixture({'a.sh': 'echo hi  # explains\n'}), [('a.sh', 1)])

    def test_listed_excuse_passes(self):
        files = {'A.cs': 'class A {\n    // workaround: the game drops this\n    // until it is fixed.\n    int x;\n}\n'}
        self.assertEqual(self.scan_fixture(files, '- `A.cs:2` workaround\n'), [])
        self.assertEqual(self.scan_fixture(files, '- `A.cs:3` workaround\n'), [('A.cs', 2)])

    def test_directives_pass(self):
        files = {
            'A.cs': '#nullable enable\n#if DEBUG\n#pragma warning disable CS0414\n#endif\n[assembly: X]\nclass A {}\n',
            'a.py': '#!/usr/bin/env python3\n# -*- coding: utf-8 -*-\nimport os  # noqa: F401\n',
            'a.sh': '#!/bin/bash\n# shellcheck disable=SC2086\necho $x\n',
        }
        self.assertEqual(self.scan_fixture(files), [])

    def test_string_contents_are_not_comments(self):
        files = {
            'A.cs': 'class A {\n    string a = "http://x // y";\n    string b = @"c:\\ /* ""z"" */";\n'
                    '    string c = $"{(n > 0 ? "//" : "#")} // {{literal}}";\n    char d = \'/\';\n}\n',
            'a.py': 'u = "http://x # y"\n',
            'a.sh': 'echo "a # b" \'c # d\' ${#x}\ncat <<EOF\n# heredoc\nEOF\n',
            'a.ps1': '$a = "x # y"\n$b = @"\n# here\n"@\n',
            'a.cjs': 'const r = /\\/\\//g;\nconst t = `${"//"} //`;\n',
        }
        self.assertEqual(self.scan_fixture(files), [])

    def test_cli_exits_nonzero_on_violation(self):
        with tempfile.TemporaryDirectory(prefix='ft-comment-ban-') as folder:
            root = Path(folder)
            subprocess.run(['git', 'init', '-q'], cwd=root, check=True)
            (root / 'A.cs').write_text('// new\nclass A {}\n')
            subprocess.run(['git', 'add', 'A.cs'], cwd=root, check=True)
            result = subprocess.run([sys.executable, str(TOOLS / 'comment_ban.py'), '--repo', str(root)],
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 1)
            self.assertIn('A.cs:1', result.stdout)


if __name__ == '__main__':
    unittest.main()
