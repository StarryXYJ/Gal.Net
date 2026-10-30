"""Exercise release sealing with real files; no builds, network or publication."""
import importlib.util
from pathlib import Path
import shutil
import unittest
import uuid

spec = importlib.util.spec_from_file_location("release", Path(__file__).with_name("cross-platform-release.py"))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class ReleaseMatrixTests(unittest.TestCase):
    def setUp(self):
        self.work = release.REPO / "out" / (".release-matrix-" + uuid.uuid4().hex)
        self.root, self.inputs = self.work / "release", self.work / "inputs"
        self.version, self.commit = "1.2.3-preview.1", "fixture-commit"
        self.root.mkdir(parents=True)
        release.write(self.root / "release-index.json", {"schemaVersion": 1, "version": self.version,
                      "tag": "v" + self.version, "sourceCommit": self.commit,
                      "packageValidation": {"passed": True}, "files": []})
        self.names = release.expected_assets(self.version)
        for rid in ("win-x64", "linux-x64", "android-arm64"):
            for deployment in (("MonoAOT",) if rid == "android-arm64" else ("SelfContained", "FrameworkDependent")):
                bundle = self.inputs / (rid + "-" + deployment)
                assets = bundle / "assets"
                assets.mkdir(parents=True)
                for name in self.names:
                    if rid not in name:
                        continue
                    if deployment == "FrameworkDependent" and "framework-dependent" not in name:
                        continue
                    if deployment == "SelfContained" and "framework-dependent" in name:
                        continue
                    (assets / name).write_text("fixture payload: " + name)
                release.write(bundle / "bundle.json", {"passed": True, "version": self.version,
                              "sourceCommit": self.commit, "target": rid, "deployment": deployment,
                              "files": release.inventory(assets)})

    def tearDown(self):
        self.assertEqual(self.work.parent, release.REPO / "out")
        shutil.rmtree(self.work)

    def seal(self):
        release.seal(self.root, self.inputs, self.version, self.commit, "v" + self.version)

    def test_complete_matrix_is_sealed_with_every_hash(self):
        self.assertEqual(len(self.names), 19)
        self.seal()
        index = release.read(self.root / "release-index.json")
        self.assertEqual(index["schemaVersion"], 2)
        self.assertEqual({path.name for path in (self.root / "assets").iterdir()}, self.names)
        release.verify_files(self.root, index["files"])
        self.assertEqual(len((self.root / "SHA256SUMS").read_text().splitlines()), 19)

    def test_missing_platform_fails_before_sealing(self):
        (self.inputs / "android-arm64-MonoAOT/bundle.json").unlink()
        with self.assertRaisesRegex(ValueError, "Incomplete"):
            self.seal()

    def test_wrong_source_cannot_mix_releases(self):
        path = self.inputs / "linux-x64-SelfContained/bundle.json"
        bundle = release.read(path)
        bundle["sourceCommit"] = "another-commit"
        release.write(path, bundle)
        with self.assertRaisesRegex(ValueError, "source/version"):
            self.seal()

    def test_changed_payload_is_rejected(self):
        path = next((self.inputs / "win-x64-SelfContained/assets").iterdir())
        path.write_text("tampered")
        with self.assertRaisesRegex(ValueError, "changed"):
            self.seal()

    def test_traversal_and_duplicate_entries_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "Invalid"):
            release.verify_files(self.root, [{"path": "../escape", "size": 0, "sha256": "x"}])
        path = self.root / "file"
        path.write_text("content")
        item = {"path": "file", "size": path.stat().st_size, "sha256": release.digest(path)}
        with self.assertRaisesRegex(ValueError, "changed"):
            release.verify_files(self.root, [item, item])


if __name__ == "__main__":
    unittest.main()
