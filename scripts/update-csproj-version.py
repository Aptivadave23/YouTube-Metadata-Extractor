#!/usr/bin/env python3
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def find_first(parent, tag_name):
    for child in parent:
        if child.tag.rsplit("}", 1)[-1] == tag_name:
            return child
    return None


def ensure_child(parent, tag_name, namespace):
    existing = find_first(parent, tag_name)
    if existing is not None:
        return existing

    if namespace:
        child = ET.SubElement(parent, f"{{{namespace}}}{tag_name}")
    else:
        child = ET.SubElement(parent, tag_name)

    return child


def main() -> int:
    if len(sys.argv) != 3:
        print("Usage: update-csproj-version.py <csproj-path> <version>", file=sys.stderr)
        return 1

    csproj_path = Path(sys.argv[1])
    version = sys.argv[2].strip()

    if not csproj_path.exists():
        print(f"Project file not found: {csproj_path}", file=sys.stderr)
        return 1

    base_version = version.split("-", 1)[0]
    parts = base_version.split(".")
    if len(parts) != 3 or not all(part.isdigit() for part in parts):
        print(f"Expected semantic version like X.Y.Z or X.Y.Z-suffix, got: {version}", file=sys.stderr)
        return 1

    assembly_version = f"{parts[0]}.{parts[1]}.{parts[2]}.0"
    is_prerelease = "-" in version

    tree = ET.parse(csproj_path)
    root = tree.getroot()

    namespace = ""
    if root.tag.startswith("{") and "}" in root.tag:
        namespace = root.tag[1:].split("}", 1)[0]

    property_group = None
    for child in root:
        if child.tag.rsplit("}", 1)[-1] != "PropertyGroup":
            continue

        has_target_framework = find_first(child, "TargetFramework") is not None
        if has_target_framework:
            property_group = child
            break

    if property_group is None:
        if namespace:
            property_group = ET.SubElement(root, f"{{{namespace}}}PropertyGroup")
        else:
            property_group = ET.SubElement(root, "PropertyGroup")

    if not is_prerelease:
        ensure_child(property_group, "Version", namespace).text = base_version

    ensure_child(property_group, "AssemblyVersion", namespace).text = assembly_version
    ensure_child(property_group, "FileVersion", namespace).text = assembly_version
    ensure_child(property_group, "InformationalVersion", namespace).text = version

    tree.write(csproj_path, encoding="utf-8", xml_declaration=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
