#!/usr/bin/env python3
"""Package a completed Release build and refresh its local catalog entry."""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--timestamp", required=True, help="Release timestamp in UTC, e.g. 2026-10-11T17:00:00Z")
args = parser.parse_args()
project = root / "Jellyfin.Plugin.AudioTrackSelector"
properties = ET.parse(project / "Jellyfin.Plugin.AudioTrackSelector.csproj").getroot().find("PropertyGroup")
version = properties.findtext("Version")
framework = properties.findtext("TargetFramework")
assembly = properties.findtext("AssemblyName")
binary = project / "bin" / "Release" / framework / f"{assembly}.dll"
if not binary.is_file():
    parser.error("Build the plugin with dotnet build -c Release before packaging")

package_name = f"jellyfin-plugin-audioTrackselector_{version}.zip"
destination = root / "dist" / package_name
destination.parent.mkdir(exist_ok=True)
# Stable archive metadata makes identical binaries produce identical checksums.
with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED) as archive:
    entry = zipfile.ZipInfo(binary.name, (1980, 1, 1, 0, 0, 0))
    entry.compress_type = zipfile.ZIP_DEFLATED
    entry.external_attr = 0o644 << 16
    archive.writestr(entry, binary.read_bytes())
(destination.parent / "README.md").write_text((root / "README.md").read_text())
checksum = hashlib.md5(destination.read_bytes()).hexdigest()  # Jellyfin catalog format
manifest_path = root / "manifest.json"
manifest = json.loads(manifest_path.read_text())
versions = manifest[0]["versions"]
release = {
    "version": version,
    "changelog": "Jellyfin 12.2 / .NET 10 build. Select audio before playback planning; preserve explicit track choices; honor codec/container conditions and request limits; prefer configured language; remove unsafe fallback and late runtime switching.",
    "targetAbi": "12.2.0.0",
    "sourceUrl": f"https://github.com/JoshuaBalcitis/jellyfin-audio-track-selector-plugin/releases/download/v{version}/{package_name}",
    "checksum": checksum,
    "timestamp": args.timestamp,
}
manifest[0]["versions"] = [release] + [entry for entry in versions if entry["version"] != version]
manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n")
print(f"Package: {destination}\nMD5: {checksum}")
