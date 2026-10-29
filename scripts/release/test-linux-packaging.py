#!/usr/bin/env python3
"""Filesystem/archiving guard checks for the Linux release assembler."""
import importlib.util
import pathlib
import tarfile
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("linux_packaging", pathlib.Path(__file__).with_name("package-linux.py"))
packaging = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packaging)


class LinuxPackagingTests(unittest.TestCase):
    def test_archive_roundtrip_preserves_executable_and_empty_content_directory(self):
        with tempfile.TemporaryDirectory(prefix="galnet-tar-test-") as temporary:
            parent = pathlib.Path(temporary)
            root = parent / "game with spaces"
            (root / "pak/I18n").mkdir(parents=True)
            executable = root / "Game"
            executable.write_text("#!/bin/sh\nexit 0\n")
            executable.chmod(0o755)
            archive = parent / "game.tar.gz"
            packaging.archive(root, archive)
            with tarfile.open(archive) as tar:
                self.assertEqual(tar.getmember("game with spaces/Game").mode & 0o111, 0o111)
                self.assertEqual(tar.getmember("game with spaces/Game").uid, 0)
                tar.extractall(parent / "unpacked", filter="data")
            self.assertTrue((parent / "unpacked/game with spaces/pak/I18n").is_dir())

    def test_input_links_are_rejected(self):
        with tempfile.TemporaryDirectory(prefix="galnet-link-test-") as temporary:
            parent = pathlib.Path(temporary)
            (parent / "source").mkdir()
            (parent / "source/escape").symlink_to(parent, target_is_directory=True)
            with self.assertRaisesRegex(RuntimeError, "input link"):
                packaging.copy_tree(parent / "source", parent / "destination")

    def test_archive_links_are_rejected(self):
        with tempfile.TemporaryDirectory(prefix="galnet-link-test-") as temporary:
            parent = pathlib.Path(temporary)
            (parent / "source").mkdir()
            (parent / "source/escape").symlink_to(parent, target_is_directory=True)
            with self.assertRaisesRegex(RuntimeError, "output link"):
                packaging.archive(parent / "source", parent / "archive.tar.gz")

    def test_publish_top_level_links_are_rejected(self):
        with tempfile.TemporaryDirectory(prefix="galnet-link-test-") as temporary:
            parent = pathlib.Path(temporary)
            (parent / "source").mkdir()
            (parent / "payload").write_bytes(b"native")
            (parent / "source/shared.so").symlink_to(parent / "payload")
            with self.assertRaisesRegex(RuntimeError, "input link"):
                packaging.merge_publish(parent / "source", parent / "destination", "Editor")

    def test_conflicting_editor_cli_dependency_is_not_overwritten(self):
        with tempfile.TemporaryDirectory(prefix="galnet-merge-test-") as temporary:
            parent = pathlib.Path(temporary)
            (parent / "source").mkdir()
            (parent / "package/lib").mkdir(parents=True)
            (parent / "source/shared.dll").write_bytes(b"different")
            (parent / "package/lib/shared.dll").write_bytes(b"original")
            with self.assertRaisesRegex(RuntimeError, "dependency differs"):
                packaging.merge_publish(parent / "source", parent / "package", "Editor")
            self.assertEqual((parent / "package/lib/shared.dll").read_bytes(), b"original")

    def test_non_elf_native_payload_is_rejected(self):
        with tempfile.TemporaryDirectory(prefix="galnet-arch-test-") as temporary:
            root = pathlib.Path(temporary)
            (root / "lib/native").mkdir(parents=True)
            (root / "lib/native/wrong.so").write_bytes(b"MZ Windows")
            with self.assertRaisesRegex(RuntimeError, "Wrong native architecture"):
                packaging.verify_native(root)

    def test_non_elf_published_native_library_is_rejected(self):
        with tempfile.TemporaryDirectory(prefix="galnet-arch-test-") as temporary:
            root = pathlib.Path(temporary)
            (root / "lib").mkdir()
            (root / "lib/libgalnet_miniaudio.so").write_bytes(b"MZ Windows")
            with self.assertRaisesRegex(RuntimeError, "Wrong native architecture"):
                packaging.verify_native(root)

    def test_release_output_cannot_be_nested_inside_publish_input(self):
        with tempfile.TemporaryDirectory(prefix="galnet-path-test-") as temporary:
            root = pathlib.Path(temporary)
            with self.assertRaisesRegex(ValueError, "outside every input"):
                packaging.package(root, root / "media", root / "nested", None)


if __name__ == "__main__":
    unittest.main()
