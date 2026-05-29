#!/usr/bin/env bash
set -euo pipefail

CMD_NAME="${CMD_NAME:-ytmd}"
RID="osx-arm64"
CONFIGURATION="Release"
PUBLISH_DIR="publish/${RID}"
INSTALL_DIR="${HOME}/.local/bin"
INSTALL_PATH="${INSTALL_DIR}/${CMD_NAME}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

fail() {
  printf 'Error: %s\n' "$1" >&2
  exit 1
}

info() {
  printf '%s\n' "$1"
}

if ! command -v dotnet >/dev/null 2>&1; then
  fail "dotnet is not installed or not on PATH."
fi

PROJECT_FILE="$(find "${REPO_ROOT}" -maxdepth 1 -name '*.csproj' -print | head -n 1)"
if [ -z "${PROJECT_FILE}" ]; then
  fail "No .csproj file was found in ${REPO_ROOT}."
fi

PROJECT_BASENAME="$(basename "${PROJECT_FILE}" .csproj)"
ASSEMBLY_NAME="$(sed -n 's:.*<AssemblyName>\(.*\)</AssemblyName>.*:\1:p' "${PROJECT_FILE}" | head -n 1)"
if [ -z "${ASSEMBLY_NAME}" ]; then
  ASSEMBLY_NAME="${PROJECT_BASENAME}"
fi

ABS_PUBLISH_DIR="${REPO_ROOT}/${PUBLISH_DIR}"
PUBLISHED_EXECUTABLE="${ABS_PUBLISH_DIR}/${ASSEMBLY_NAME}"

info "Publishing ${PROJECT_BASENAME} for ${RID}..."
mkdir -p "${ABS_PUBLISH_DIR}"

dotnet publish "${PROJECT_FILE}" \
  -c "${CONFIGURATION}" \
  -r "${RID}" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o "${ABS_PUBLISH_DIR}"

if [ ! -f "${PUBLISHED_EXECUTABLE}" ]; then
  fail "Publish completed, but the expected executable was not found at ${PUBLISHED_EXECUTABLE}."
fi

mkdir -p "${INSTALL_DIR}"
cp "${PUBLISHED_EXECUTABLE}" "${INSTALL_PATH}"
chmod +x "${INSTALL_PATH}"

info "Installed ${CMD_NAME} to ${INSTALL_PATH}"

case ":${PATH}:" in
  *":${INSTALL_DIR}:"*)
    info "${INSTALL_DIR} is already on PATH."
    ;;
  *)
    info "Add ${INSTALL_DIR} to PATH to run ${CMD_NAME} directly:"
    info '  echo '\''export PATH="$HOME/.local/bin:$PATH"'\'' >> ~/.zshrc'
    info '  source ~/.zshrc'
    ;;
esac

info "Published files are in ${ABS_PUBLISH_DIR}"
