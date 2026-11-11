#!/usr/bin/env bash
# Downloads what the compile check needs into Tools/CompileCheck/.cache (git-ignored):
#  - Unity 2020.3.21 engine reference assemblies (BepInEx NuGet feed, UnityEngine.Modules)
#  - package sources matching Packages/manifest.json: uGUI 1.0.0, TextMesh Pro 3.0.6, Input System 1.3.0
# Nothing here is redistributed with the repository.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
cache="$here/.cache"
mkdir -p "$cache"

tarball() { # url name marker
    local dest="$cache/$2"
    if [ -e "$dest/$3" ]; then echo "$2: cached"; return; fi
    echo "Downloading $1"
    mkdir -p "$dest"
    curl -fsSL "$1" | tar -xz -C "$dest" --strip-components=1
}

if [ ! -e "$cache/unityengine/lib/net35/UnityEngine.CoreModule.dll" ]; then
    echo "Downloading UnityEngine.Modules 2020.3.21"
    curl -fsSL -o "$cache/unityengine.zip" "https://nuget.bepinex.dev/v3/package/unityengine.modules/2020.3.21/unityengine.modules.2020.3.21.nupkg"
    mkdir -p "$cache/unityengine"
    unzip -q -o "$cache/unityengine.zip" -d "$cache/unityengine"
    rm "$cache/unityengine.zip"
else
    echo "unityengine: cached"
fi

tarball "https://codeload.github.com/needle-mirror/com.unity.ugui/tar.gz/refs/tags/1.0.0/Unity-2020.3.18f1" ugui Runtime
tarball "https://download.packages.unity.com/com.unity.textmeshpro/-/com.unity.textmeshpro-3.0.6.tgz" textmeshpro Scripts
tarball "https://download.packages.unity.com/com.unity.inputsystem/-/com.unity.inputsystem-1.3.0.tgz" inputsystem InputSystem

echo "Done. Now run: dotnet build Tools/CompileCheck/Game/Assembly-CSharp.csproj"
