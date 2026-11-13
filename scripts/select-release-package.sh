#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: bash scripts/select-release-package.sh <release-tag>" >&2
  exit 1
fi

case "$1" in
  analysis/v*)
    project="src/FlowOnRails.Analysis/FlowOnRails.Analysis.csproj"
    package_id="FlowOnRails.Analysis"
    tag_version="${1#analysis/v}"
    ;;
  aspnetcore/v*)
    project="src/FlowOnRails.AspNetCore/FlowOnRails.AspNetCore.csproj"
    package_id="FlowOnRails.AspNetCore"
    tag_version="${1#aspnetcore/v}"
    ;;
  v[0-9]*|[0-9]*)
    project="src/FlowOnRails/FlowOnRails.csproj"
    package_id="FlowOnRails"
    tag_version="${1#v}"
    ;;
  *)
    echo "Unsupported release tag '$1'. Use v<version>, aspnetcore/v<version> or analysis/v<version>." >&2
    exit 1
    ;;
esac

package_version="$(dotnet msbuild "$project" -getProperty:PackageVersion)"

if [[ "$tag_version" != "$package_version" ]]; then
  echo "Release tag '$1' does not match $package_id version '$package_version'." >&2
  exit 1
fi

echo "project=$project"
echo "package_id=$package_id"
echo "version=$package_version"
echo "file=$package_id.$package_version.nupkg"
