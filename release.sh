#!/usr/bin/env bash
# Packages the plugin the same way release.ps1 does: publish, then gather the DLLs,
# the deps file, the translation files and plugin.json into dist/<pluginId>/.
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
dist_root="${1:-$script_dir/dist}"

# Reads a top-level string value out of the flat plugin manifest.
read_manifest_value() {
    local key="$1"
    local value
    value="$(sed -n "s/.*\"$key\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" "$script_dir/plugin.json" | head -n 1)"

    if [[ -z "$value" ]]; then
        echo "plugin.json has no '$key' value." >&2
        exit 1
    fi

    printf '%s' "$value"
}

plugin_id="$(read_manifest_value id)"
entry_assembly="$(read_manifest_value entryAssembly)"
assembly_name="${entry_assembly%.dll}"
version="$(read_manifest_value version)"

project="$script_dir/$assembly_name.csproj"
publish_output="$script_dir/bin/publish"
output_path="$dist_root/$plugin_id"

echo "Publishing $assembly_name v$version..."
dotnet publish "$project" -c Release -o "$publish_output" --nologo -v quiet

rm -rf "$output_path"
mkdir -p "$output_path"

shopt -s nullglob
dlls=("$publish_output"/*.dll)
shopt -u nullglob

if [[ ${#dlls[@]} -eq 0 ]]; then
    echo "The publish output contains no assemblies." >&2
    exit 1
fi

cp -- "${dlls[@]}" "$output_path/"
cp -- "$publish_output/$assembly_name.deps.json" "$output_path/"
cp -- "$script_dir/plugin.json" "$output_path/"

# The translation files are optional; a plugin without them just stays English.
shopt -s nullglob
strings=("$script_dir"/strings.*.json)
shopt -u nullglob
if [[ ${#strings[@]} -gt 0 ]]; then
    cp -- "${strings[@]}" "$output_path/"
fi

echo "Release v$version ready at: $output_path"
