#!/usr/bin/env bash
set -euo pipefail

if [[ $# != 3 ]]; then
    echo "usage: $0 osx-arm64|osx-x64 1.6.616-hatcher.N output-directory" >&2
    exit 1
fi
target=$1
version=$2
case "$target" in
    osx-arm64) arch=arm64 ;;
    osx-x64) arch=x64 ;;
    *) echo "unsupported target: $target" >&2; exit 1 ;;
esac
if [[ ! "$version" =~ ^1\.6\.616-hatcher\.[1-9][0-9]*$ ]]; then
    echo "expected fork version 1.6.616-hatcher.N (N > 0)" >&2
    exit 1
fi

src=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$3"
output=$(cd "$3" && pwd)
archive="hst-imager_v${version}_console_macos_${arch}.zip"
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
    -p:AssemblyVersion=1.6.616.0 -p:FileVersion=1.6.616.0

mv "$staging/Hst.Imager.ConsoleApp" "$staging/hst.imager"
find "$staging" -name '*.pdb' -delete
cp "$src/../license.txt" "$staging/upstream-license.txt"
printf 'version: %s\nsource: %s\nruntime: %s\n' \
    "$version" "$(git -C "$src" rev-parse HEAD)" "$target" > "$staging/build-info.txt"
if [[ -n "$(git -C "$src" status --porcelain)" ]]; then
    printf 'working tree: modified\n' >> "$staging/build-info.txt"
fi
(cd "$staging" && zip -qr "$output/$archive" .)
(cd "$output" && shasum -a 256 "$archive" > "$archive.sha256")
python3 - "$output/$archive" <<'PY'
import hashlib
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
digest = hashlib.md5(path.read_bytes()).hexdigest()
path.with_suffix(path.suffix + ".md5").write_text(f"{digest}  {path.name}\n")
PY
