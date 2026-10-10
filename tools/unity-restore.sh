#!/usr/bin/env bash
# Restores a Unity project's NuGet packages (MessagePack) before its first open; run it after a fresh clone
# and whenever packages.config changes. The editor can't compile the shared packages, and so can't run
# NuGetForUnity's own restore, until MessagePack is there.
#
#   tools/unity-restore.sh [project]   (relative to the repository, or absolute; default: shapeland/unity)
#
# NuGetForUnity's CLI (4.5.0) writes analyzer DLLs' .meta files in a format Unity 6.6 rejects (PluginImporter
# version 1), so Unity would import the source generator as an ordinary library. This rewrites them in the
# current format, keeping their GUIDs. The CLI is pinned to that version; check whether a newer one fixes it
# (.claude/design/prototype.md) before moving the pin.
set -euo pipefail

repo=$(cd "$(dirname "$0")/.." && pwd)
project="${1:-shapeland/unity}"
case "$project" in /*) ;; *) project="$repo/$project" ;; esac
[ -f "$project/Assets/packages.config" ] || { echo "No Assets/packages.config in $project" >&2; exit 2; }

# dnx ships with the .NET 10 SDK; on some systems it's only reachable through dotnet.
if command -v dnx > /dev/null; then
  DOTNET_ROLL_FORWARD=Major dnx -y NuGetForUnity.Cli@4.5.0 -- restore "$project"
else
  DOTNET_ROLL_FORWARD=Major dotnet dnx -y NuGetForUnity.Cli@4.5.0 -- restore "$project"
fi

fixed=0
while IFS= read -r meta; do
  guid=$(sed -n 's/^guid: *//p' "$meta")
  [ -n "$guid" ] || { echo "No GUID in $meta" >&2; exit 1; }
  cat > "$meta" <<EOF
fileFormatVersion: 2
guid: $guid
labels:
- RoslynAnalyzer
PluginImporter:
  externalObjects: {}
  serializedVersion: 3
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
    Any:
      enabled: 0
      settings: {}
    Editor:
      enabled: 0
      settings:
        DefaultValueInitialized: true
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
  fixed=$((fixed + 1))
done < <(find "$project/Assets/Packages" -path '*/analyzers/*' -name '*.dll.meta')

echo "Restored $project; rewrote $fixed analyzer .meta file(s)."
