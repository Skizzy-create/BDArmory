"""Build the BDArmory Overdrive release zip.

    python Overdrive/make_release.py --version 1.13.1.0-overdrive.1 \
        --dll BDArmory/bin/Release/BDArmory.dll [--upstream BDArmory.1.13.1.0_....zip] [--out dist]

The zip is the BDArmory Plus 1.13.1.0 release zip (downloaded and checked against its
SHA-256 unless --upstream gives a local copy) with these files replaced:

    GameData/BDArmory/Plugins/BDArmory.dll           the Overdrive build (--dll)
    GameData/BDArmory/Localization/UI/en-us.cfg      from BDArmory/Distribution
    GameData/BDArmory/BDArmory.version               from BDArmory/Distribution (points at this fork)
    GameData/BDArmory/License.txt                    from BDArmory/Distribution (notes the fork)

Everything else is byte-identical to the BDArmory Plus release. Entries are sorted and carry a
fixed timestamp, so the same inputs give the same zip. The DLL is refused if it contains a
debug directory or a local path (build with -p:DebugType=None -p:DebugSymbols=false).
Python 3.9+, standard library only.
"""
import argparse
import hashlib
import io
import os
import sys
import urllib.request
import zipfile

UPSTREAM_URLS = [
    "https://github.com/BrettRyland/BDArmory/releases/download/v1.13.1.0/"
    "BDArmory.1.13.1.0_2026-08-15T09.11.53%2B00.00.zip",
    "https://spacedock.info/mod/2487/BDArmory%20Plus/download/1.13.1.0",
]
UPSTREAM_SHA256 = "83B59CC552A6F70CA9607343BEE9E794C55A1D3817915992FD5F8C545F0CCD6C"
UPSTREAM_SIZE = 42969535
ZIP_TIME = (2020, 1, 1, 0, 0, 0)
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIST = os.path.join(ROOT, "BDArmory", "Distribution", "GameData", "BDArmory")
FROM_DIST = ["Localization/UI/en-us.cfg", "BDArmory.version", "License.txt"]
# byte patterns that must not appear in the DLL (debug directory, PDB names, local paths)
FORBIDDEN = [b"RSDS", b".pdb", b"C:\\Users", b"C:/Users", b"D:\\", b"D:/", b"/home/", b"/Users/"]


def sha256(data):
    return hashlib.sha256(data).hexdigest().upper()


def upstream_bytes(path):
    if path:
        data = open(path, "rb").read()
    else:
        data, errors = None, []
        for url in UPSTREAM_URLS:
            try:
                req = urllib.request.Request(url, headers={"User-Agent": "BDArmory-Overdrive"})
                with urllib.request.urlopen(req, timeout=300) as resp:
                    data = resp.read()
                break
            except OSError as exc:
                errors.append(f"{url}: {exc}")
        if data is None:
            sys.exit("could not download the BDArmory Plus release:\n  " + "\n  ".join(errors))
    if len(data) != UPSTREAM_SIZE or sha256(data) != UPSTREAM_SHA256:
        sys.exit(f"upstream zip mismatch: {len(data)} bytes, sha256 {sha256(data)}")
    return data


def check_dll(data):
    low = data.lower()
    hits = [p.decode() for p in FORBIDDEN
            if p.lower() in low or p.decode().lower().encode("utf-16-le") in low]
    if hits:
        sys.exit(f"BDArmory.dll contains {hits}; build with -p:DebugType=None -p:DebugSymbols=false")


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--version", required=True, help="e.g. 1.13.1.0-overdrive.1")
    ap.add_argument("--dll", required=True)
    ap.add_argument("--upstream", help="local copy of the BDArmory Plus 1.13.1.0 release zip")
    ap.add_argument("--out", default=os.path.join(ROOT, "dist"))
    args = ap.parse_args()

    dll = open(args.dll, "rb").read()
    check_dll(dll)
    replace = {"GameData/BDArmory/Plugins/BDArmory.dll": dll}
    for rel in FROM_DIST:
        replace["GameData/BDArmory/" + rel] = open(os.path.join(DIST, *rel.split("/")), "rb").read()

    src = zipfile.ZipFile(io.BytesIO(upstream_bytes(args.upstream)))
    entries = {}
    for info in src.infolist():
        name = info.filename.replace("\\", "/")
        if name.endswith("/"):
            continue
        if not name.startswith("GameData/BDArmory/"):
            sys.exit(f"unexpected entry in the upstream zip: {name}")
        entries[name] = src.read(info)
    missing = [n for n in replace if n not in entries]
    if missing:
        sys.exit(f"upstream zip lacks {missing}")
    entries.update(replace)

    os.makedirs(args.out, exist_ok=True)
    out = os.path.join(args.out, f"BDArmoryOverdrive-{args.version}.zip")
    with zipfile.ZipFile(out + ".part", "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for name in sorted(entries):
            info = zipfile.ZipInfo(name, date_time=ZIP_TIME)
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            zf.writestr(info, entries[name], compresslevel=9)
    os.replace(out + ".part", out)
    data = open(out, "rb").read()
    print(f"{out}\n  {len(entries)} files, {len(data):,} bytes\n  sha1   {hashlib.sha1(data).hexdigest().upper()}"
          f"\n  sha256 {sha256(data)}\n  BDArmory.dll sha256 {sha256(dll)}")


if __name__ == "__main__":
    main()
