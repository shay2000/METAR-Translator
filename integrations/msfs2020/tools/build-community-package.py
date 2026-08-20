#!/usr/bin/env python3
"""Build an installable MSFS 2020 Community package ZIP without the Windows SDK.

The supported release path is ``build.bat``, which drives the MSFS 2020 SDK's
``fspackagetool.exe`` on Windows. That tool compiles the in-game panel
registration into a binary ``.spb`` and generates ``manifest.json`` and
``layout.json``.

This script reproduces the package layout on any platform with only Python 3.9+.
It stages ``PackageSources`` into a Community package folder, writes the two
metadata files itself, and stores the panel registration as XML named ``.spb``.
Microsoft's SimBase loader reads both the compiled and XML forms of a SimBase
document, which is why SDK-free community packages are commonly built this way,
but that behavior is not part of a documented contract. Treat the output as
unverified until it has been loaded in the simulator.

Usage:
    python3 tools/build-community-package.py
    python3 tools/build-community-package.py --output-dir builds --keep-folder
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import stat
import sys
import tempfile
import zipfile
from pathlib import Path, PurePosixPath
from typing import Dict, List, NamedTuple
from xml.etree import ElementTree

PACKAGE_NAME = "metar-viewer-toolbar"
PACKAGE_DEFINITION = PurePosixPath("PackageDefinitions/metar-viewer-toolbar.xml")
PANEL_DEFINITION = PurePosixPath("PackageSources/InGamePanels/InGamePanel_MetarViewer.xml")
SOURCE_HTML_ROOT = PurePosixPath("PackageSources/html_ui")
STAGED_SPB = PurePosixPath("InGamePanels/InGamePanel_MetarViewer.spb")
STAGED_PANEL_HTML = PurePosixPath("html_ui/InGamePanels/MetarViewer/MetarViewer.html")
STAGED_ICON = PurePosixPath("html_ui/Textures/Menu/toolbar/ICON_TOOLBAR_METAR_VIEWER.svg")
METADATA_FILES = ("manifest.json", "layout.json")

# Windows FILETIME ticks (100 ns) between 1601-01-01 and the Unix epoch.
FILETIME_EPOCH_OFFSET = 11_644_473_600
FILETIME_TICKS_PER_SECOND = 10_000_000

# layout.json paths must survive a Windows/MSFS virtual file system round trip.
JUNK_NAMES = frozenset({".ds_store", "thumbs.db", "desktop.ini", "__macosx", ".git"})
FORBIDDEN_EXTENSIONS = frozenset({".bat", ".cmd", ".dll", ".exe", ".ps1"})
WINDOWS_INVALID_CHARACTERS = re.compile(r'[<>:"|?*\x00-\x1f]')
VERSION_PATTERN = re.compile(r"^\d+\.\d+\.\d+$")

# Root-relative references such as /JS/coherent.js are provided by the simulator.
HTML_ASSET_PATTERN = re.compile(
    r"""<(?P<tag>script|link|img)\b[^>]*?\b(?P<attribute>src|href)\s*=\s*(?P<quote>["'])(?P<value>.*?)\3""",
    re.IGNORECASE | re.DOTALL,
)

class PackagingError(Exception):
    """A packaging precondition failed."""

class StagedFile(NamedTuple):
    absolute_path: Path
    relative_path: PurePosixPath
    size: int
    filetime: int

def log(message: str) -> None:
    print(message, flush=True)

def read_text(path: Path, label: str) -> str:
    try:
        raw = path.read_bytes()
    except FileNotFoundError as error:
        raise PackagingError(f"Required {label} is missing: {path}") from error

    if not raw:
        raise PackagingError(f"Required {label} is empty: {path}")
    if raw.startswith(b"\xef\xbb\xbf"):
        raise PackagingError(f"Required {label} must be UTF-8 without a byte-order mark: {path}")

    return raw.decode("utf-8")

def element_text(root: ElementTree.Element, tag: str, label: str) -> str:
    matches = root.iter(tag)
    first = next(matches, None)
    if first is None or first.text is None or not first.text.strip():
        raise PackagingError(f"{label} must contain a non-empty <{tag}> element.")
    return first.text.strip()

def to_filetime(seconds: float) -> int:
    """Convert POSIX seconds to a Windows FILETIME, as layout.json requires."""
    filetime = int((seconds + FILETIME_EPOCH_OFFSET) * FILETIME_TICKS_PER_SECOND)
    if filetime < FILETIME_EPOCH_OFFSET * FILETIME_TICKS_PER_SECOND:
        raise PackagingError(f"Timestamp {seconds} predates the Unix epoch and is not a usable FILETIME.")
    return filetime

def assert_portable_relative_path(relative_path: PurePosixPath, label: str) -> None:
    text = str(relative_path)
    if not text or text == ".":
        raise PackagingError(f"{label} must not be empty.")
    if relative_path.is_absolute():
        raise PackagingError(f"{label} must be relative: {text}")
    if "\\" in text:
        raise PackagingError(f"{label} must use forward slashes: {text}")

    for segment in relative_path.parts:
        if segment in {".", ".."}:
            raise PackagingError(f"{label} contains a traversal segment: {text}")
        if WINDOWS_INVALID_CHARACTERS.search(segment):
            raise PackagingError(f"{label} contains a Windows-invalid character: {text}")
        if segment[-1] in " .":
            raise PackagingError(f"{label} has a segment ending in a space or period: {text}")
        if segment.lower() in JUNK_NAMES:
            raise PackagingError(f"{label} contains build junk that must not ship: {text}")

def collect_source_files(source_root: Path) -> List[PurePosixPath]:
    """Return every regular file under ``source_root``, rejecting unshippable entries."""
    if not source_root.is_dir():
        raise PackagingError(f"Source directory does not exist: {source_root}")

    collected: List[PurePosixPath] = []
    case_insensitive_paths: Dict[str, PurePosixPath] = {}

    for absolute_path in sorted(source_root.rglob("*"), key=lambda item: str(item)):
        relative_path = PurePosixPath(absolute_path.relative_to(source_root).as_posix())

        if absolute_path.is_symlink():
            raise PackagingError(f"Symbolic links cannot be packaged: {relative_path}")
        if absolute_path.is_dir():
            continue
        if not absolute_path.is_file():
            raise PackagingError(f"Unsupported filesystem entry in package sources: {relative_path}")

        assert_portable_relative_path(relative_path, "Package source path")

        if relative_path.suffix.lower() in FORBIDDEN_EXTENSIONS:
            raise PackagingError(f"Executable or script files cannot ship in a package: {relative_path}")
        if absolute_path.stat().st_size == 0:
            raise PackagingError(f"Package source file is empty: {relative_path}")

        # MSFS mounts packages on a case-insensitive VFS, so collisions break installs.
        key = str(relative_path).lower()
        previous = case_insensitive_paths.get(key)
        if previous is not None:
            raise PackagingError(f'Package source paths collide case-insensitively: "{previous}" and "{relative_path}".')
        case_insensitive_paths[key] = relative_path

        collected.append(relative_path)

    if not collected:
        raise PackagingError(f"No files were found under {source_root}.")

    return collected

def read_package_metadata(integration_root: Path) -> Dict[str, str]:
    definition_path = integration_root / PACKAGE_DEFINITION
    xml = read_text(definition_path, "package definition")
    try:
        root = ElementTree.fromstring(xml)
    except ElementTree.ParseError as error:
        raise PackagingError(f"Package definition is not valid XML: {error}") from error

    name = root.get("Name")
    if name != PACKAGE_NAME:
        raise PackagingError(f'Package definition Name must be "{PACKAGE_NAME}"; found "{name}".')

    version = root.get("Version", "")
    if not VERSION_PATTERN.match(version):
        raise PackagingError(f'Package definition Version must be MAJOR.MINOR.PATCH; found "{version}".')

    manufacturer_element = next(root.iter("Manufacturer"), None)
    manufacturer = (manufacturer_element.text or "").strip() if manufacturer_element is not None else ""

    return {
        "name": name,
        "version": version,
        "title": element_text(root, "Title", "Package definition"),
        "creator": element_text(root, "Creator", "Package definition"),
        "manufacturer": manufacturer,
        "description": element_text(root, "Description", "Package definition"),
    }

def verify_panel_definition(integration_root: Path) -> None:
    """Check that the panel registration points at assets the package actually ships."""
    panel_path = integration_root / PANEL_DEFINITION
    xml = read_text(panel_path, "in-game panel definition")
    try:
        root = ElementTree.fromstring(xml)
    except ElementTree.ParseError as error:
        raise PackagingError(f"In-game panel definition is not valid XML: {error}") from error

    filename = element_text(root, "Filename", "In-game panel definition")
    if filename != STAGED_SPB.name:
        raise PackagingError(f'In-game panel definition Filename must be "{STAGED_SPB.name}"; found "{filename}".')

    definition = next(root.iter("InGamePanels.InGamePanelDefinition"), None)
    if definition is None:
        raise PackagingError("In-game panel definition is missing <InGamePanels.InGamePanelDefinition>.")

    panel_url = definition.get("url", "")
    if panel_url != str(STAGED_PANEL_HTML):
        raise PackagingError(f'Panel url must be "{STAGED_PANEL_HTML}"; found "{panel_url}".')

    icon = definition.get("icon", "")
    if icon != STAGED_ICON.stem:
        raise PackagingError(f'Panel icon must be "{STAGED_ICON.stem}"; found "{icon}".')

def verify_panel_html_assets(package_root: Path) -> None:
    """Ensure every local asset the panel HTML loads exists inside the package."""
    html_path = package_root / STAGED_PANEL_HTML
    html = read_text(html_path, "panel HTML entry point")
    html_directory = STAGED_PANEL_HTML.parent

    for match in HTML_ASSET_PATTERN.finditer(html):
        reference = match.group("value").strip()
        tag = match.group("tag").lower()

        if not reference or reference.startswith(("#", "data:", "/")):
            # Root-relative assets are served by the simulator's own html_ui tree.
            continue
        if re.match(r"^(?:https?:)?//", reference, re.IGNORECASE):
            raise PackagingError(f"Panel HTML must not load a remote {tag} asset: {reference}")

        unqualified = re.split(r"[?#]", reference, maxsplit=1)[0]
        resolved = PurePosixPath(os.path.normpath(html_directory / unqualified))
        assert_portable_relative_path(resolved, f"Panel HTML local {tag} asset")

        if not str(resolved).startswith("html_ui/"):
            raise PackagingError(f"Panel HTML local asset escapes html_ui: {reference}")
        if not (package_root / resolved).is_file():
            raise PackagingError(f"Panel HTML references a local asset that is not packaged: {resolved}")

def stage_package(integration_root: Path, package_root: Path) -> None:
    """Copy PackageSources into the Community package layout."""
    html_source = integration_root / SOURCE_HTML_ROOT
    for relative_path in collect_source_files(html_source):
        destination = package_root / "html_ui" / relative_path
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(html_source / relative_path, destination)

    # The SDK compiles this XML into a binary .spb. Without the SDK, ship the XML
    # under the .spb name that the panel definition and the simulator expect.
    spb_destination = package_root / STAGED_SPB
    spb_destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(integration_root / PANEL_DEFINITION, spb_destination)

    for relative_path in (STAGED_PANEL_HTML, STAGED_ICON, STAGED_SPB):
        if not (package_root / relative_path).is_file():
            raise PackagingError(f"Staging did not produce a required package file: {relative_path}")

    verify_panel_html_assets(package_root)

def describe_payload(package_root: Path) -> List[StagedFile]:
    """Describe every payload file, ordinally sorted the way layout.json expects."""
    payload: List[StagedFile] = []

    for relative_path in collect_source_files(package_root):
        if str(relative_path) in METADATA_FILES:
            continue
        absolute_path = package_root / relative_path
        info = absolute_path.stat()
        payload.append(
            StagedFile(
                absolute_path=absolute_path,
                relative_path=relative_path,
                size=info.st_size,
                filetime=to_filetime(info.st_mtime),
            )
        )

    payload.sort(key=lambda item: str(item.relative_path).encode("utf-8"))
    return payload

def write_json(path: Path, document: Dict[str, object]) -> int:
    """Write UTF-8 JSON without a BOM and return the byte length."""
    text = json.dumps(document, indent=2, ensure_ascii=False) + "\n"
    encoded = text.encode("utf-8")
    path.write_bytes(encoded)
    return len(encoded)

def write_metadata(package_root: Path, metadata: Dict[str, str], minimum_game_version: str) -> List[StagedFile]:
    payload = describe_payload(package_root)

    layout = {
        "content": [
            {
                "path": str(item.relative_path),
                "size": item.size,
                "date": item.filetime,
            }
            for item in payload
        ]
    }
    layout_size = write_json(package_root / "layout.json", layout)

    def manifest_document(total_package_size: str) -> Dict[str, object]:
        return {
            "dependencies": [],
            "content_type": "MISC",
            "title": metadata["title"],
            "manufacturer": metadata["manufacturer"],
            "creator": metadata["creator"],
            "package_version": metadata["version"],
            "minimum_game_version": minimum_game_version,
            "release_notes": {"neutral": {"LastUpdate": "", "OlderHistory": ""}},
            "total_package_size": total_package_size,
        }

    # total_package_size covers every file in the package, including manifest.json
    # itself. The field is a fixed 20-character string, so writing a placeholder
    # first yields the final manifest size and the real total converges in one pass.
    placeholder = "0".rjust(20, "0")
    manifest_path = package_root / "manifest.json"
    manifest_size = write_json(manifest_path, manifest_document(placeholder))

    total = sum(item.size for item in payload) + layout_size + manifest_size
    final_size = write_json(manifest_path, manifest_document(str(total).rjust(20, "0")))
    if final_size != manifest_size:
        raise PackagingError("manifest.json size changed after writing total_package_size.")

    return payload

def create_zip(package_root: Path, output_path: Path) -> None:
    """Zip the package so extraction yields Community/metar-viewer-toolbar/..."""
    output_path.parent.mkdir(parents=True, exist_ok=True)
    if output_path.exists():
        output_path.unlink()

    files = collect_source_files(package_root)
    ordered = sorted(files, key=lambda item: str(item).encode("utf-8"))

    with zipfile.ZipFile(output_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for relative_path in ordered:
            absolute_path = package_root / relative_path
            entry = zipfile.ZipInfo.from_file(absolute_path, str(PurePosixPath(PACKAGE_NAME) / relative_path))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = (stat.S_IFREG | 0o644) << 16
            entry.create_system = 0  # Report FAT/Windows for predictable extraction.
            archive.writestr(entry, absolute_path.read_bytes())

def verify_zip(output_path: Path, package_root: Path) -> int:
    """Re-read the archive and confirm it matches the staged package."""
    expected = {
        str(PurePosixPath(PACKAGE_NAME) / relative_path): (package_root / relative_path).stat().st_size
        for relative_path in collect_source_files(package_root)
    }

    with zipfile.ZipFile(output_path) as archive:
        damaged = archive.testzip()
        if damaged is not None:
            raise PackagingError(f"The generated ZIP is corrupt at entry: {damaged}")

        actual = {info.filename: info.file_size for info in archive.infolist() if not info.is_dir()}

    if actual != expected:
        missing = sorted(set(expected) - set(actual))
        unexpected = sorted(set(actual) - set(expected))
        details = []
        if missing:
            details.append(f"missing {missing}")
        if unexpected:
            details.append(f"unexpected {unexpected}")
        if not details:
            details.append("file sizes do not match the staged package")
        raise PackagingError(f"The generated ZIP does not match the staged package: {'; '.join(details)}.")

    required = [
        f"{PACKAGE_NAME}/manifest.json",
        f"{PACKAGE_NAME}/layout.json",
        f"{PACKAGE_NAME}/{STAGED_SPB}",
        f"{PACKAGE_NAME}/{STAGED_PANEL_HTML}",
        f"{PACKAGE_NAME}/{STAGED_ICON}",
    ]
    for entry in required:
        if entry not in actual:
            raise PackagingError(f"The generated ZIP is missing a required entry: {entry}")

    return len(actual)

def parse_arguments(argv: List[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Build an installable MSFS 2020 Community package ZIP without the Windows SDK.",
    )
    parser.add_argument(
        "--output-dir",
        default=None,
        help="Directory for the ZIP (default: <repository root>/builds).",
    )
    parser.add_argument(
        "--minimum-game-version",
        default="1.0.0",
        help="manifest.json minimum_game_version (default: 1.0.0).",
    )
    parser.add_argument(
        "--keep-folder",
        action="store_true",
        help="Also write the unzipped package folder beside the ZIP for local testing.",
    )
    return parser.parse_args(argv)

def main(argv: List[str]) -> int:
    arguments = parse_arguments(argv)

    integration_root = Path(__file__).resolve().parent.parent
    repository_root = integration_root.parent.parent
    output_directory = (
        Path(arguments.output_dir).expanduser().resolve()
        if arguments.output_dir
        else repository_root / "builds"
    )
    output_path = output_directory / f"{PACKAGE_NAME}.zip"

    if not VERSION_PATTERN.match(arguments.minimum_game_version):
        raise PackagingError(
            f'--minimum-game-version must be MAJOR.MINOR.PATCH; found "{arguments.minimum_game_version}".'
        )

    log("[1/4] Reading package definitions...")
    metadata = read_package_metadata(integration_root)
    verify_panel_definition(integration_root)
    log(f"      {metadata['title']} {metadata['version']} by {metadata['creator']}")

    with tempfile.TemporaryDirectory(prefix="metar-viewer-package-") as temporary_directory:
        staging_root = Path(temporary_directory)
        package_root = staging_root / PACKAGE_NAME

        log("[2/4] Staging the Community package layout...")
        stage_package(integration_root, package_root)

        log("[3/4] Generating manifest.json and layout.json...")
        write_metadata(package_root, metadata, arguments.minimum_game_version)

        log("[4/4] Creating and verifying the ZIP...")
        create_zip(package_root, output_path)
        entry_count = verify_zip(output_path, package_root)
        compressed_size = output_path.stat().st_size

        if arguments.keep_folder:
            folder_destination = output_directory / PACKAGE_NAME
            if folder_destination.exists():
                shutil.rmtree(folder_destination)
            shutil.copytree(package_root, folder_destination)

    log("")
    log(f"Created {output_path}")
    log(f"  {entry_count} files, {compressed_size} bytes compressed")
    log(f"  ZIP root: {PACKAGE_NAME}/")
    if arguments.keep_folder:
        log(f"  Package folder: {output_directory / PACKAGE_NAME}")
    log("")
    log("Install: extract the ZIP into your MSFS 2020 Community folder so that")
    log(f"  Community/{PACKAGE_NAME}/manifest.json exists, then restart the simulator.")
    log("")
    log("NOTE: this package was built without the MSFS 2020 SDK, so the panel")
    log("registration ships as XML rather than an SDK-compiled .spb. Confirm the")
    log("toolbar icon and panel behavior in the simulator before distributing it.")
    return 0

if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv[1:]))
    except PackagingError as failure:
        print(f"ERROR: {failure}", file=sys.stderr)
        sys.exit(1)
    except KeyboardInterrupt:
        print("Cancelled.", file=sys.stderr)
        sys.exit(130)
