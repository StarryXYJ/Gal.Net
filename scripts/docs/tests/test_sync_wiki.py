import importlib.util
import json
from pathlib import Path
import shutil
import unittest
import uuid
from unittest.mock import patch
import subprocess

MODULE = Path(__file__).resolve().parents[1] / 'sync_wiki.py'
spec = importlib.util.spec_from_file_location('sync_wiki', MODULE)
wiki = importlib.util.module_from_spec(spec)
spec.loader.exec_module(wiki)
COMMIT = 'a' * 40


class DocumentationTests(unittest.TestCase):
    def setUp(self):
        test_root = MODULE.parents[2] / 'Temp'
        test_root.mkdir(exist_ok=True)
        self.root = test_root / ('galnet-docs-test-' + uuid.uuid4().hex)
        self.root.mkdir()
        def cleanup():
            self.assertEqual(test_root.resolve(), self.root.resolve().parent)
            shutil.rmtree(self.root)
        self.addCleanup(cleanup)
        self.docs = self.root / 'docs'
        self.docs.mkdir()
        self.output = self.root / 'wiki'
        self.output.mkdir()
        self.write('README.md', '# Home\n\n<!-- navigation:start -->\n<!-- navigation:end -->\n')
        self.write('guide.md', '# Guide\n\n## 中文 与 API\n\n## Repeat\n\n## Repeat\n')
        self.config = {'schemaVersion':1, 'repository':'https://github.com/test/repo', 'root':{
            'source':'README.md','title':'Home','id':'Home','children':[
                {'source':'guide.md','title':'Guide','id':'Guide'}]}}
        self.save_config()

    def write(self, relative, content):
        path = self.docs / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding='utf-8')

    def save_config(self):
        (self.docs / 'navigation.json').write_text(json.dumps(self.config), encoding='utf-8')

    def load(self):
        return wiki.load_navigation(self.root)

    def test_full_render_converts_pages_fragments_references_and_source_links(self):
        (self.root / 'source.cs').write_text('class Example {}', encoding='utf-8')
        self.write('_assets/picture.png', 'image fixture')
        self.write('guide.md', '# Guide\n\n## 中文 与 API\n\n'
                   '[local](#中文-与-api) [home](README.md) [source](../source.cs)\n'
                   '![image](_assets/picture.png) [by reference][home]\n\n[home]: README.md\n'
                   '`[example](missing.md)`\n\n```markdown\n[example](missing.md)\n```\n')
        config, nodes = self.load()
        wiki.update_indexes(self.root, nodes, write=True)
        wiki.validate(self.root, nodes)
        files = wiki.render(self.root, config, nodes, COMMIT)
        body = files['Guide.md'].decode()
        self.assertIn('[home](Home)', body)
        self.assertIn('[home]: Home', body)
        self.assertIn(f'/blob/{COMMIT}/source.cs', body)
        self.assertIn('[local](#', body)
        self.assertIn('`[example](missing.md)`', body)
        self.assertIn('```markdown\n[example](missing.md)\n```', body)
        self.assertEqual(b'image fixture', files['_assets/picture.png'])
        self.assertIn('[Guide](Guide)', files['_Sidebar.md'].decode())

    def test_missing_links_and_duplicate_heading_anchors(self):
        config, nodes = self.load()
        self.write('README.md', '# Home\n\n[repeat](guide.md#repeat-1)\n')
        wiki.validate(self.root, nodes)
        self.write('README.md', '# Home\n\n[bad](guide.md#absent)\n')
        with self.assertRaisesRegex(wiki.DocumentationError, 'Missing anchor'):
            wiki.validate(self.root, nodes)
        self.write('README.md', '# Home\n\n[bad](absent.md)\n')
        with self.assertRaisesRegex(wiki.DocumentationError, 'Missing link'):
            wiki.validate(self.root, nodes)

    def test_navigation_coverage_titles_and_case_insensitive_ids(self):
        self.write('extra.md', '# Extra\n')
        with self.assertRaisesRegex(wiki.DocumentationError, 'unlisted'):
            self.load()
        (self.docs / 'extra.md').unlink()
        self.config['root']['children'][0]['id'] = 'home'
        self.save_config()
        with self.assertRaisesRegex(wiki.DocumentationError, 'Duplicate'):
            self.load()
        self.config['root']['children'][0]['id'] = 'Guide'
        self.config['root']['children'][0]['title'] = 'Wrong'
        self.save_config()
        with self.assertRaisesRegex(wiki.DocumentationError, 'H1/title'):
            self.load()

    def test_index_is_generated_without_changing_manual_prose(self):
        config, nodes = self.load()
        with self.assertRaisesRegex(wiki.DocumentationError, 'Stale'):
            wiki.update_indexes(self.root, nodes)
        wiki.update_indexes(self.root, nodes, write=True)
        self.assertEqual([], wiki.update_indexes(self.root, nodes))
        self.assertTrue(wiki.read(self.docs / 'README.md').startswith('# Home'))

    def test_sync_idempotent_and_only_removes_owned_files(self):
        (self.output / 'Home.md').write_text('Welcome to the Gal.Net wiki!\n', encoding='utf-8')
        (self.output / 'Personal.md').write_text('personal notes', encoding='utf-8')
        files = {'Home.md': b'# Home\n', 'Old.md': b'old'}
        wiki.synchronize(self.output, files, COMMIT, require_git=False)
        first = {p.name:p.read_bytes() for p in self.output.iterdir()}
        wiki.synchronize(self.output, files, COMMIT, require_git=False)
        self.assertEqual(first, {p.name:p.read_bytes() for p in self.output.iterdir()})
        wiki.synchronize(self.output, {'Home.md': b'# Home\n'}, COMMIT, require_git=False)
        self.assertFalse((self.output / 'Old.md').exists())
        self.assertEqual('personal notes', wiki.read(self.output / 'Personal.md'))

    def test_manual_changes_and_unmanaged_collisions_fail_before_writing(self):
        files = {'Home.md':b'original'}
        wiki.synchronize(self.output, files, COMMIT, require_git=False)
        (self.output / 'Home.md').write_text('manual edit', encoding='utf-8')
        with self.assertRaisesRegex(wiki.DocumentationError, 'Manually changed'):
            wiki.synchronize(self.output, {'Home.md':b'replacement','New.md':b'new'}, COMMIT, require_git=False)
        self.assertFalse((self.output / 'New.md').exists())
        self.assertEqual('manual edit', wiki.read(self.output / 'Home.md'))
        (self.output / wiki.MANIFEST).unlink()
        with self.assertRaisesRegex(wiki.DocumentationError, 'unmanaged'):
            wiki.synchronize(self.output, files, COMMIT, require_git=False)

    def test_traversal_and_git_metadata_are_never_managed(self):
        for name in ['../escape.md', '.git/config', '_assets/../escape', '/outside.md']:
            with self.subTest(name=name):
                with self.assertRaises(wiki.DocumentationError):
                    wiki.synchronize(self.output, {name:b'invalid'}, COMMIT, require_git=False)
        manifest = {'schemaVersion':1,'files':{'.git/config':'anything'}}
        (self.output / wiki.MANIFEST).write_text(json.dumps(manifest), encoding='utf-8')
        with self.assertRaisesRegex(wiki.DocumentationError, 'Invalid generated'):
            wiki.synchronize(self.output, {}, COMMIT, require_git=False)

    def test_parent_repository_is_not_a_wiki_checkout(self):
        with self.assertRaisesRegex(wiki.DocumentationError, 'checkout'):
            wiki.synchronize(self.output, {}, COMMIT)

    def test_generated_links_are_checked_after_url_conversion(self):
        files = {'Home.md':b'# Home\n\n[guide](Guide#section)\n', 'Guide.md':b'# Guide\n\n## Section\n'}
        wiki.validate_rendered(files)
        files['Guide.md'] = b'# Guide\n'
        with self.assertRaisesRegex(wiki.DocumentationError, 'fragment'):
            wiki.validate_rendered(files)

    def test_manifest_owned_missing_file_is_protected(self):
        wiki.synchronize(self.output, {'Home.md':b'original'}, COMMIT, require_git=False)
        (self.output / 'Home.md').unlink()
        with self.assertRaisesRegex(wiki.DocumentationError, 'missing generated'):
            wiki.synchronize(self.output, {}, COMMIT, require_git=False)

    def test_uncommitted_source_is_not_labeled_as_a_committed_snapshot(self):
        config, nodes = self.load()
        with patch.object(wiki.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0, b'older source')):
            with self.assertRaisesRegex(wiki.DocumentationError, 'Uncommitted/different source'):
                wiki.verify_source_commit(self.root, nodes, COMMIT)


if __name__ == '__main__':
    unittest.main()
