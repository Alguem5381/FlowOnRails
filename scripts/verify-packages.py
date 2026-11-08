"""Build and execute isolated consumers of the packed artifacts, without project references."""

import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def inspect_packages(directory):
    versions = {}

    for path in sorted(directory.glob("*.nupkg")):
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            manifests = [name for name in names if name.endswith(".nuspec")]
            require(len(manifests) == 1, f"Expected one manifest in {path.name}.")
            metadata = ET.fromstring(archive.read(manifests[0])).find("{*}metadata")
            package = metadata.findtext("{*}id")
            version = metadata.findtext("{*}version")
            require(package in {"FlowOnRails", "FlowOnRails.AspNetCore"}, f"Unexpected package: {package}.")
            require(package not in versions, f"Multiple versions of {package}; use a clean output directory.")
            require(path.name == f"{package}.{version}.nupkg", f"Unexpected package file name: {path.name}.")
            versions[package] = version

            required = {f"lib/net10.0/{package}.dll", f"lib/net10.0/{package}.xml", "README.md"}
            require(required.issubset(names), f"Missing runtime, XML documentation or README in {path.name}.")
            require(metadata.findtext("{*}license") == "MIT", f"Missing MIT license in {path.name}.")
            dependencies = {item.attrib["id"] for item in metadata.findall(".//{*}dependency")}
            dlls = {name for name in names if name.endswith(".dll")}
            expected_dlls = {f"lib/net10.0/{package}.dll"}

            if package == "FlowOnRails.AspNetCore":
                expected_dlls.add("analyzers/dotnet/cs/FlowOnRails.AspNetCore.Generators.dll")
                require(dependencies == {"FlowOnRails"}, "The satellite must depend only on the base package.")
                frameworks = {item.attrib["name"] for item in metadata.findall(".//{*}frameworkReference")}
                require("Microsoft.AspNetCore.App" in frameworks, "The ASP.NET Core framework reference is missing.")
            else:
                require(not dependencies, "The base package must not depend on the satellite or Roslyn.")

            require(dlls == expected_dlls, f"Unexpected assemblies in {path.name}: {dlls}.")
            print(f"Validated contents of {path.name}.", flush=True)

    require(set(versions) == {"FlowOnRails", "FlowOnRails.AspNetCore"}, "Pack both projects before verification.")

    return versions


def write_sources(path, directory, published_base):
    configuration = ET.Element("configuration")
    sources = ET.SubElement(configuration, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", key="local", value=str(directory))

    if published_base:
        ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
        mapping = ET.SubElement(configuration, "packageSourceMapping")
        ET.SubElement(mapping, "clear")
        local = ET.SubElement(mapping, "packageSource", key="local")
        ET.SubElement(local, "package", pattern="FlowOnRails.AspNetCore")
        public = ET.SubElement(mapping, "packageSource", key="nuget.org")
        ET.SubElement(public, "package", pattern="FlowOnRails")

    ET.ElementTree(configuration).write(path, encoding="utf-8", xml_declaration=True)


def verify_consumer(root, scratch, fixture, version_property, version, config):
    consumer = scratch / fixture
    shutil.copytree(root / "tests" / "packaging" / fixture, consumer,
                    ignore=shutil.ignore_patterns("bin", "obj"))
    project = consumer / f"{fixture}.csproj"
    properties = [f"-p:{version_property}={version}"]

    subprocess.run(["dotnet", "restore", str(project), "--configfile", str(config),
                    "--packages", str(scratch / "packages"), "--no-http-cache", *properties],
                   cwd=scratch, check=True, timeout=180)
    build = ["dotnet", "build", str(project), "-c", "Release", "--no-restore", "-m:1",
             "/nodeReuse:false", "-p:UseSharedCompilation=false", *properties]
    subprocess.run(build, cwd=scratch, check=True, timeout=180)

    binary = consumer / "bin" / "Release" / "net10.0" / f"{fixture}.dll"
    deps = json.loads(binary.with_suffix(".deps.json").read_text())
    require(not any(name.startswith("Microsoft.CodeAnalysis") for name in deps["libraries"]),
            "Roslyn must not be a runtime dependency of the consumer.")
    subprocess.run(["dotnet", str(binary)], cwd=scratch, check=True, timeout=60)

    if fixture == "AspNetCoreConsumer":
        invalid = subprocess.run([*build, "-p:DefineConstants=INVALID_STATUS"], cwd=scratch,
                                 capture_output=True, text=True, timeout=180)
        output = invalid.stdout + invalid.stderr
        require(invalid.returncode != 0 and "FORHTTP004" in output,
                f"The packaged generator did not diagnose an invalid status.\n{output}")
        print("Packaged generator: FORHTTP004 diagnostic passed.", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path, help="Directory containing both .nupkg files")
    parser.add_argument("--published-base", action="store_true",
                        help="Restore the satellite's base dependency exclusively from nuget.org")
    args = parser.parse_args()
    directory = args.directory.resolve(strict=True)
    versions = inspect_packages(directory)
    root = Path(__file__).resolve().parents[1]

    with tempfile.TemporaryDirectory(prefix="flowonrails-packages-") as temporary:
        scratch = Path(temporary)
        config = scratch / "NuGet.Config"
        write_sources(config, directory, args.published_base)

        if not args.published_base:
            verify_consumer(root, scratch, "CoreConsumer", "FlowOnRailsVersion",
                            versions["FlowOnRails"], config)

        verify_consumer(root, scratch, "AspNetCoreConsumer", "FlowOnRailsAspNetCoreVersion",
                        versions["FlowOnRails.AspNetCore"], config)

    mode = "published base dependency" if args.published_base else "local packages"
    print(f"Package verification passed ({mode}).")


if __name__ == "__main__":
    main()
