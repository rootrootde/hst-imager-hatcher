#!/usr/bin/env bash
set -euo pipefail

if [[ $# != 3 ]]; then
    echo "usage: $0 RUNTIME 1.7.649-hatcher.N output-directory" >&2
    exit 1
fi
target=$1
version=$2
arch=${target##*-}
executable=Hst.Imager.ConsoleApp
binary=hst.imager
case "$target" in
    osx-arm64|osx-x64) platform=macos ;;
    linux-arm64|linux-x64) platform=linux ;;
    win-arm64|win-x64) platform=windows; executable+=.exe; binary+=.exe ;;
    *) echo "unsupported target: $target" >&2; exit 1 ;;
esac
if [[ ! "$version" =~ ^1\.7\.649-hatcher\.[1-9][0-9]*$ ]]; then
    echo "expected fork version 1.7.649-hatcher.N (N > 0)" >&2
    exit 1
fi

src=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$3"
output=$(cd "$3" && pwd)
archive="hst-imager_v${version}_console_${platform}_${arch}.zip"
if [[ -e "$output/$archive" ]]; then
    echo "archive already exists: $output/$archive" >&2
    exit 1
fi
staging=$(mktemp -d)
trap 'rm -rf "$staging"' EXIT

dotnet publish "$src/Hst.Imager.ConsoleApp/Hst.Imager.ConsoleApp.csproj" \
    --configuration Release --runtime "$target" --self-contained true \
    --output "$staging" \
    --source https://api.nuget.org/v3/index.json --source "$src/packages" \
    -p:PublishSingleFile=true -p:PublishReadyToRun=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:Version="$version" -p:PackageVersion="$version" \
    -p:AssemblyVersion=1.7.649.0 -p:FileVersion=1.7.649.0

mv "$staging/$executable" "$staging/$binary"
find "$staging" -name '*.pdb' -delete
cp "$src/../license.txt" "$staging/upstream-license.txt"
printf 'version: %s\nsource: %s\nruntime: %s\n' \
    "$version" "$(git -C "$src" rev-parse HEAD)" "$target" > "$staging/build-info.txt"
if [[ -n "$(git -C "$src" status --porcelain)" ]]; then
    printf 'working tree: modified\n' >> "$staging/build-info.txt"
fi
python3 - "$staging" "$output/$archive" <<'PY'
import hashlib
import pathlib
import sys
import zipfile

staging, path = map(pathlib.Path, sys.argv[1:])
with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
    for entry in sorted(staging.rglob("*")):
        archive.write(entry, entry.relative_to(staging))
for algorithm in ("sha256", "md5"):
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, algorithm).hexdigest()
    path.with_suffix(path.suffix + "." + algorithm).write_text(f"{digest}  {path.name}\n")
PY
