#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  printf 'Usage: %s <version>\n' "$(basename "$0")" >&2
  exit 1
fi

VERSION="$1"
CONFIGURATION="${CONFIGURATION:-Release}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_FILE="$(find "${REPO_ROOT}" -maxdepth 1 -name '*.csproj' -print | head -n 1)"
PUBLISH_ROOT="${REPO_ROOT}/publish"
RELEASE_ASSET_DIR="${PUBLISH_ROOT}/release-assets"

fail() {
  printf 'Error: %s\n' "$1" >&2
  exit 1
}

if [ -z "${PROJECT_FILE}" ]; then
  fail "No .csproj file found in ${REPO_ROOT}."
fi

if ! command -v dotnet >/dev/null 2>&1; then
  fail "dotnet is not installed or not on PATH."
fi

if ! command -v python3 >/dev/null 2>&1; then
  fail "python3 is required to update csproj version metadata."
fi

TARGET_FRAMEWORK="$(python3 - "${PROJECT_FILE}" <<'PY'
import sys
import xml.etree.ElementTree as ET

tree = ET.parse(sys.argv[1])
root = tree.getroot()
for child in root:
    if child.tag.rsplit('}', 1)[-1] != 'PropertyGroup':
        continue
    for node in child:
        if node.tag.rsplit('}', 1)[-1] == 'TargetFramework':
            print((node.text or '').strip())
            raise SystemExit(0)
raise SystemExit(1)
PY
)"

[ -n "${TARGET_FRAMEWORK}" ] || fail "Could not determine TargetFramework from ${PROJECT_FILE}."

python3 "${SCRIPT_DIR}/update-csproj-version.py" "${PROJECT_FILE}" "${VERSION}"

mkdir -p "${RELEASE_ASSET_DIR}"
rm -f "${RELEASE_ASSET_DIR}/ytmd-osx-arm64.tar.gz" \
  "${RELEASE_ASSET_DIR}/ytmd-osx-x64.tar.gz" \
  "${RELEASE_ASSET_DIR}/ytmd-linux-x64.tar.gz" \
  "${RELEASE_ASSET_DIR}/ytmd-win-x64.zip"

publish_rid() {
  local rid="$1"
  local package_name="$2"
  local package_type="$3"
  local publish_dir="${PUBLISH_ROOT}/${rid}"
  local executable_name="ytmd"
  local executable_path="${publish_dir}/${executable_name}"

  if [ "${rid}" = "win-x64" ]; then
    executable_name="${executable_name}.exe"
    executable_path="${publish_dir}/${executable_name}"
  fi

  rm -rf "${publish_dir}"
  mkdir -p "${publish_dir}"

  dotnet publish "${PROJECT_FILE}" \
    -c "${CONFIGURATION}" \
    -r "${rid}" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -o "${publish_dir}"

  [ -f "${executable_path}" ] || fail "Expected published executable missing: ${executable_path}"

  case "${package_type}" in
    targz)
      chmod +x "${executable_path}"
      tar -C "${publish_dir}" -czf "${RELEASE_ASSET_DIR}/${package_name}" .
      ;;
    zip)
      python3 - "${publish_dir}" "${RELEASE_ASSET_DIR}/${package_name}" <<'PY'
import os
import sys
import zipfile

source_dir = sys.argv[1]
zip_path = sys.argv[2]

with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
    for root, _, files in os.walk(source_dir):
        for file_name in files:
            full_path = os.path.join(root, file_name)
            rel_path = os.path.relpath(full_path, source_dir)
            zf.write(full_path, rel_path)
PY
      ;;
    *)
      fail "Unknown package type: ${package_type}"
      ;;
  esac
}

publish_rid "osx-arm64" "ytmd-osx-arm64.tar.gz" "targz"
publish_rid "osx-x64" "ytmd-osx-x64.tar.gz" "targz"
publish_rid "linux-x64" "ytmd-linux-x64.tar.gz" "targz"
publish_rid "win-x64" "ytmd-win-x64.zip" "zip"

printf 'Prepared release assets for version %s in %s\n' "${VERSION}" "${RELEASE_ASSET_DIR}"
