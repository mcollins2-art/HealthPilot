#!/usr/bin/env python3
import argparse
import hashlib
import json
import pathlib
import urllib.request


def download(url: str, output_dir: pathlib.Path) -> dict:
    output_dir.mkdir(parents=True, exist_ok=True)
    filename = pathlib.Path(urllib.request.urlparse(url).path).name or "download.dat"
    destination = output_dir / filename
    hasher = hashlib.sha256()

    with urllib.request.urlopen(url) as response, destination.open("wb") as target:
        while True:
            chunk = response.read(1024 * 1024)
            if not chunk:
                break
            target.write(chunk)
            hasher.update(chunk)

    return {
        "url": url,
        "filePath": str(destination.resolve()),
        "sha256": hasher.hexdigest(),
        "bytes": destination.stat().st_size
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="Download healthcare transparency data files.")
    parser.add_argument("--url", action="append", required=True, help="Data source URL. Repeat for multiple files.")
    parser.add_argument("--out-dir", default="data/raw", help="Output directory for downloaded files.")
    parser.add_argument("--manifest", default="data/raw/manifest.json", help="Manifest file path.")
    args = parser.parse_args()

    output_dir = pathlib.Path(args.out_dir)
    results = [download(url, output_dir) for url in args.url]

    manifest_path = pathlib.Path(args.manifest)
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(json.dumps({"downloads": results}, indent=2), encoding="utf-8")
    print(json.dumps({"downloads": results}, indent=2))


if __name__ == "__main__":
    main()
