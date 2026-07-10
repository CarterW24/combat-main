#!/usr/bin/env python3
# Mirror the OSFR client files from an existing asset host into ~/osfr-manifest
# so this server can serve client downloads itself.
#
# Usage: python3 mirror_client.py
# Re-run any time; already-downloaded files (matching size) are skipped.

import os
import sys
import urllib.request
import xml.etree.ElementTree as ET

SOURCE = "https://fabledrealms.dev"
DEST = os.path.expanduser("~/osfr-manifest")

HEADERS = {"User-Agent": "OSFR-Mirror/1.0"}


def fetch(url):
    req = urllib.request.Request(url, headers=HEADERS)
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def walk(folder, path, files):
    name = folder.get("name")
    if name:
        path = os.path.join(path, name)
    for f in folder.findall("File"):
        files.append((path, f.get("name"), int(f.get("size"))))
    for sub in folder.findall("Folder"):
        walk(sub, path, files)


def main():
    os.makedirs(DEST, exist_ok=True)

    print(f"Fetching manifest from {SOURCE} ...")
    manifest_bytes = fetch(f"{SOURCE}/clientmanifest.xml")

    root = ET.fromstring(manifest_bytes)

    files = []
    for folder in root.findall("Folder"):
        walk(folder, "", files)

    total = len(files)
    total_bytes = sum(size for _, _, size in files)
    print(f"Manifest lists {total} files ({total_bytes / 1024 / 1024:.0f} MB)")

    done = 0
    failed = []
    for rel_path, filename, size in files:
        done += 1
        local_dir = os.path.join(DEST, "client", rel_path)
        local_file = os.path.join(local_dir, filename)

        if os.path.exists(local_file) and os.path.getsize(local_file) == size:
            continue

        url = "/".join(
            [SOURCE, "client"]
            + [p for p in rel_path.replace(os.sep, "/").split("/") if p]
            + [urllib.request.quote(filename)]
        )

        os.makedirs(local_dir, exist_ok=True)
        try:
            data = fetch(url)
            with open(local_file, "wb") as out:
                out.write(data)
            print(f"[{done}/{total}] {rel_path}/{filename} ({size / 1024:.0f} KB)")
        except Exception as e:
            failed.append((url, str(e)))
            print(f"[{done}/{total}] FAILED {url}: {e}", file=sys.stderr)

    # Only install the manifest after the files are in place, so players
    # never see a manifest whose files aren't downloadable yet.
    if not failed:
        with open(os.path.join(DEST, "clientmanifest.xml"), "wb") as out:
            out.write(manifest_bytes)
        print("Done. clientmanifest.xml installed - client mirror is live.")
    else:
        print(f"\n{len(failed)} files failed; manifest NOT installed. Re-run to retry.")
        sys.exit(1)


if __name__ == "__main__":
    main()
