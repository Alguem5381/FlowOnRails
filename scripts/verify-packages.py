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
            require(package in {"FlowOnRails", "FlowOnRails.AspNetCore", "FlowOnRails.Analysis"}, f"Unexpected package: {package}.")
            require(package not in versions, f"Multiple versions of {package}; use a clean output directory.")
            require(path.name == f"{package}.{version}.nupkg", f"Unexpected package file name: {path.name}.")
            versions[package] = version

            framework = "netstandard2.0" if package == "FlowOnRails.Analysis" else "net10.0"
            required = {"README.md", f"lib/{framework}/{package}.dll", f"lib/{framework}/{package}.xml"}
            require(required.issubset(names), f"Missing runtime, XML documentation or README in {path.name}.")
            require(metadata.findtext("{*}license") == "MIT", f"Missing MIT license in {path.name}.")
            dependencies = {item.attrib["id"] for item in metadata.findall(".//{*}dependency")}
            dlls = {name for name in names if name.endswith(".dll")}
            expected_dlls = {f"lib/{framework}/{package}.dll"}

            if package == "FlowOnRails":
                expected_dlls |= {f"analyzers/dotnet/cs/{assembly}.dll" for assembly in (
                    "FlowOnRails.Analysis", "FlowOnRails.Analyzers", "FlowOnRails.CodeFixes")}
                require(not dependencies, "The base package must not declare compiler or runtime package dependencies.")
                require("buildTransitive/FlowOnRails.props" in names, "Missing analyzer configuration props.")
            elif package == "FlowOnRails.Analysis":
                require(dependencies == {"Microsoft.CodeAnalysis.CSharp"}, "The tooling API must declare its Roslyn dependency.")
            elif package == "FlowOnRails.AspNetCore":
                expected_dlls.add("analyzers/dotnet/cs/FlowOnRails.AspNetCore.Generators.dll")
                require(dependencies == {"FlowOnRails"}, "The satellite must depend only on the base package.")
                frameworks = {item.attrib["name"] for item in metadata.findall(".//{*}frameworkReference")}
                require("Microsoft.AspNetCore.App" in frameworks, "The ASP.NET Core framework reference is missing.")

            require(dlls == expected_dlls, f"Unexpected assemblies in {path.name}: {dlls}.")
            print(f"Validated contents of {path.name}.", flush=True)

    require(set(versions) == {"FlowOnRails", "FlowOnRails.AspNetCore", "FlowOnRails.Analysis"}, "Pack all three projects before verification.")

    return versions


def write_sources(path, directory, published_base, dependency_feed=None):
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

    if dependency_feed:
        ET.SubElement(sources, "add", key="dependencies", value=dependency_feed)
        mapping = ET.SubElement(configuration, "packageSourceMapping")
        local = ET.SubElement(mapping, "packageSource", key="local")
        ET.SubElement(local, "package", pattern="FlowOnRails*")
        dependencies = ET.SubElement(mapping, "packageSource", key="dependencies")
        ET.SubElement(dependencies, "package", pattern="*")

    ET.ElementTree(configuration).write(path, encoding="utf-8", xml_declaration=True)


def verify_consumer(root, scratch, fixture, version_property, version, config, base_version,
                    framework, runtime_async=False):
    consumer = scratch / fixture
    shutil.copytree(root / "tests" / "packaging" / fixture, consumer,
                    ignore=shutil.ignore_patterns("bin", "obj"))
    if fixture == "AnalysisConsumer" and framework == "net11.0":
        shutil.copyfile(root / "tests/packaging/AnalyzerConsumer/CSharp15.cs", consumer / "Input.cs.txt")
    project = consumer / f"{fixture}.csproj"
    properties = [f"-p:{version_property}={version}", f"-p:PackageConsumerFramework={framework}"]
    if runtime_async:
        properties.append("-p:Features=runtime-async=on")
    if version_property != "FlowOnRailsVersion":
        properties.append(f"-p:FlowOnRailsVersion={base_version}")

    subprocess.run(["dotnet", "restore", str(project), "--configfile", str(config),
                    "--packages", str(scratch / "packages"), "--no-http-cache", *properties],
                   cwd=scratch, check=True, timeout=180)
    build = ["dotnet", "build", str(project), "-c", "Release", "--no-restore", "-m:1", "-warnaserror",
             "/nodeReuse:false", "-p:UseSharedCompilation=false", *properties]
    if fixture == "AnalyzerConsumer":
        build.append("-p:FlowOnRailsEnableFlowAnalysis=true")

    subprocess.run(build, cwd=scratch, check=True, timeout=180)

    binary = consumer / "bin" / "Release" / framework / f"{fixture}.dll"
    deps = json.loads(binary.with_suffix(".deps.json").read_text())
    if fixture != "AnalysisConsumer":
        require(not any(name.startswith(("Microsoft.CodeAnalysis", "FlowOnRails.Analysis")) for name in deps["libraries"]),
                "Roslyn and the analysis API must not be runtime dependencies of an application consumer.")
    subprocess.run(["dotnet", str(binary)], cwd=scratch, check=True, timeout=60)

    if fixture == "AnalyzerConsumer":
        report_path = consumer / "obj" / "flow.sarif"
        report = json.loads(report_path.read_text(encoding="utf-8-sig"))
        results = [result for run in report["runs"] for result in run.get("results", [])]
        require(not any(result["ruleId"] in {"AD0001", "CS8032", "CS8784", "CS8785", "CS9057"}
                        for result in results), "An analyzer or generator failed to load or execute.")
        require({"FORFLOW001", "FORFLOW002", "FORFLOW003"}.issubset({result["ruleId"] for result in results}),
                "The packaged analyzer did not emit declarations, call summaries and the switch action diagnostic.")
        summaries = [result for result in results if result["ruleId"] == "FORFLOW001"
                     and result["properties"]["customProperties"].get("MethodName") == "Read"]
        require(any(result["properties"]["customProperties"].get("ErrorTypes") == "Producer.Missing"
                    and result["properties"]["customProperties"].get("HasUnknownErrors") == "False"
                    for result in summaries), "The consumer did not read the producer's precise summary.")
        if framework == "net11.0":
            expected_errors = {
                "ReadClosed": {"Denied", "Missing"},
                "ReadLabeledBreak": {"Missing"},
                "ReadLabeledContinue": {"Denied", "Missing"},
                "ReadIndexer": {"Missing"},
                "ReadCollectionEffects": {"Missing"},
                "ReadTask": {"Missing"},
                "ReadValueTask": {"Denied"},
                "ReadTaskBind": {"Missing"},
                "ReadValueTaskMapError": {"Denied"},
                "ReadUnion": {"Missing"},
                "ReadUnionSuccess": set(),
            }
            for method, expected in expected_errors.items():
                summaries = [result["properties"]["customProperties"] for result in results
                             if result["ruleId"] == "FORFLOW001"
                             and result["properties"]["customProperties"].get("MethodName") == method]
                require(len(summaries) == 1, f"Missing or duplicate C# 15 summary for {method}.")
                actual = {name.strip() for name in summaries[0].get("ErrorTypes", "").split(",") if name.strip()}
                require(actual == expected and summaries[0].get("HasUnknownErrors") == "False",
                        f"Unexpected exported C# 15 summary for {method}: {summaries[0]}.")
        without_opt_in = build[:-1]
        for label, options in (("default", []), ("explicit false", ["-p:FlowOnRailsEnableFlowAnalysis=false"])):
            subprocess.run([*without_opt_in, "-t:Rebuild", *options], cwd=scratch, check=True, timeout=180)
            disabled = json.loads(report_path.read_text(encoding="utf-8-sig"))
            require(not any(result["ruleId"].startswith("FORFLOW") for run in disabled["runs"]
                            for result in run.get("results", [])), f"Unexpected analysis diagnostics ({label}).")
            subprocess.run(["dotnet", str(binary), "--disabled"], cwd=scratch, check=True, timeout=60)
        print("Packaged analysis: explicit opt-in, default off and explicit false passed.", flush=True)

    if fixture == "AspNetCoreConsumer":
        invalid = subprocess.run([*build, "-p:DefineConstants=INVALID_STATUS"], cwd=scratch,
                                 capture_output=True, text=True, timeout=180)
        output = invalid.stdout + invalid.stderr
        require(invalid.returncode != 0 and "FORHTTP004" in output,
                f"The packaged generator did not diagnose an invalid status.\n{output}")
        print("Packaged generator: FORHTTP004 diagnostic passed.", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path, help="Directory containing the three .nupkg files")
    parser.add_argument("--published-base", action="store_true",
                        help="Restore the base dependency of consumers exclusively from nuget.org")
    parser.add_argument("--tooling-dependency-feed", default="https://api.nuget.org/v3/index.json",
                        help="NuGet source for Roslyn dependencies of the tooling API consumer")
    parser.add_argument("--framework", action="append", choices=("net10.0", "net11.0"),
                        help="Consumer framework to verify; repeat for both (default: net10.0)")
    parser.add_argument("--runtime-async", action="store_true",
                        help="Compile net11.0 consumers with runtime-async=on")
    args = parser.parse_args()
    directory = args.directory.resolve(strict=True)
    versions = inspect_packages(directory)
    root = Path(__file__).resolve().parents[1]
    frameworks = list(dict.fromkeys(args.framework or ["net10.0"]))
    require(not args.runtime_async or frameworks == ["net11.0"],
            "Runtime async verification requires only --framework net11.0.")

    with tempfile.TemporaryDirectory(prefix="flowonrails-packages-") as temporary:
        for framework in frameworks:
            scratch = Path(temporary) / framework
            scratch.mkdir()
            sdk_policy = root / "global.json" if framework == "net10.0" else root / "tests/compatibility/global.json"
            shutil.copyfile(sdk_policy, scratch / "global.json")
            print(f"Verifying {framework}, runtime async: {args.runtime_async}.", flush=True)
            subprocess.run(["dotnet", "--version"], cwd=scratch, check=True, timeout=30)
            config = scratch / "NuGet.Config"
            write_sources(config, directory, args.published_base)

            if not args.published_base:
                verify_consumer(root, scratch, "CoreConsumer", "FlowOnRailsVersion",
                                versions["FlowOnRails"], config, versions["FlowOnRails"], framework, args.runtime_async)

            verify_consumer(root, scratch, "AspNetCoreConsumer", "FlowOnRailsAspNetCoreVersion",
                            versions["FlowOnRails.AspNetCore"], config, versions["FlowOnRails"], framework, args.runtime_async)

            verify_consumer(root, scratch, "AnalyzerConsumer", "FlowOnRailsVersion",
                            versions["FlowOnRails"], config, versions["FlowOnRails"], framework, args.runtime_async)

            if not args.published_base:
                tooling_config = scratch / "Tooling.NuGet.Config"
                write_sources(tooling_config, directory, False, args.tooling_dependency_feed)
                verify_consumer(root, scratch, "AnalysisConsumer", "FlowOnRailsAnalysisVersion",
                                versions["FlowOnRails.Analysis"], tooling_config, versions["FlowOnRails"], framework, args.runtime_async)

    mode = "published base dependency" if args.published_base else "local packages"
    print(f"Package verification passed ({mode}).")


if __name__ == "__main__":
    main()
