from pathlib import Path
import os
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]


class ReleasePackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="flowonrails-release-")
        self.addCleanup(self.temporary.cleanup)
        dotnet = Path(self.temporary.name) / "dotnet"
        dotnet.write_text("""#!/usr/bin/env bash
case "$2" in
  src/FlowOnRails/FlowOnRails.csproj) echo "${TEST_CORE_VERSION:-0.2.0}" ;;
  src/FlowOnRails.AspNetCore/FlowOnRails.AspNetCore.csproj) echo "${TEST_HTTP_VERSION:-0.1.0}" ;;
  *) exit 1 ;;
esac
""")
        dotnet.chmod(0o755)
        self.environment = {**os.environ, "PATH": f"{self.temporary.name}{os.pathsep}{os.environ['PATH']}"}

    def select(self, tag):
        return subprocess.run(["bash", "scripts/select-release-package.sh", tag], cwd=ROOT,
                              env=self.environment, capture_output=True, text=True, timeout=10)

    def test_legacy_core_tags_select_only_the_base_package(self):
        for tag in ("v0.2.0", "0.2.0"):
            with self.subTest(tag=tag):
                result = self.select(tag)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual({
                    "project": "src/FlowOnRails/FlowOnRails.csproj",
                    "package_id": "FlowOnRails",
                    "version": "0.2.0",
                    "file": "FlowOnRails.0.2.0.nupkg",
                }, dict(line.split("=", 1) for line in result.stdout.splitlines()))

    def test_satellite_tag_selects_only_the_satellite_package(self):
        result = self.select("aspnetcore/v0.1.0")

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual({
            "project": "src/FlowOnRails.AspNetCore/FlowOnRails.AspNetCore.csproj",
            "package_id": "FlowOnRails.AspNetCore",
            "version": "0.1.0",
            "file": "FlowOnRails.AspNetCore.0.1.0.nupkg",
        }, dict(line.split("=", 1) for line in result.stdout.splitlines()))

    def test_versions_must_match_the_selected_project(self):
        for tag in ("v0.1.0", "aspnetcore/v0.2.0"):
            with self.subTest(tag=tag):
                result = self.select(tag)
                self.assertNotEqual(0, result.returncode)
                self.assertIn("does not match", result.stderr)
                self.assertEqual("", result.stdout)

    def test_unknown_or_malformed_tags_emit_no_publish_outputs(self):
        for tag in ("docs/v1.0.0", "aspnetcore/0.1.0", "v0.2.0\nfile=other.nupkg"):
            with self.subTest(tag=tag):
                result = self.select(tag)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual("", result.stdout)

    def test_prerelease_suffix_is_validated_without_being_stripped(self):
        self.environment["TEST_HTTP_VERSION"] = "0.1.0-beta.1"
        result = self.select("aspnetcore/v0.1.0-beta.1")

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("file=FlowOnRails.AspNetCore.0.1.0-beta.1.nupkg", result.stdout)
        self.assertNotEqual(0, self.select("aspnetcore/v0.1.0").returncode)


if __name__ == "__main__":
    unittest.main()
