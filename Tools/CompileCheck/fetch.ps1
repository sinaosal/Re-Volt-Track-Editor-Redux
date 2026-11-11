# Downloads what the compile check needs into Tools/CompileCheck/.cache (git-ignored):
#  - Unity 2020.3.21 engine reference assemblies (BepInEx NuGet feed, UnityEngine.Modules)
#  - package sources matching Packages/manifest.json: uGUI 1.0.0, TextMesh Pro 3.0.6, Input System 1.3.0
# Nothing here is redistributed with the repository.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$cache = Join-Path $PSScriptRoot '.cache'
New-Item -ItemType Directory -Force $cache | Out-Null

function Get-Download([string]$url, [string]$file) {
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing
}

function Get-Tarball([string]$url, [string]$name, [string]$marker) {
    $dest = Join-Path $cache $name
    if (Test-Path (Join-Path $dest $marker)) { Write-Host "$name: cached"; return }
    $file = Join-Path $cache "$name.tgz"
    Get-Download $url $file
    New-Item -ItemType Directory -Force $dest | Out-Null
    tar -xzf $file -C $dest --strip-components=1
    if ($LASTEXITCODE -ne 0) { throw "Extracting $file failed" }
    Remove-Item $file
}

$engine = Join-Path $cache 'unityengine'
if (-not (Test-Path (Join-Path $engine 'lib/net35/UnityEngine.CoreModule.dll'))) {
    $zip = Join-Path $cache 'unityengine.zip'
    Get-Download 'https://nuget.bepinex.dev/v3/package/unityengine.modules/2020.3.21/unityengine.modules.2020.3.21.nupkg' $zip
    Expand-Archive -Force -Path $zip -DestinationPath $engine
    Remove-Item $zip
} else { Write-Host 'unityengine: cached' }

Get-Tarball 'https://codeload.github.com/needle-mirror/com.unity.ugui/tar.gz/refs/tags/1.0.0/Unity-2020.3.18f1' 'ugui' 'Runtime'
Get-Tarball 'https://download.packages.unity.com/com.unity.textmeshpro/-/com.unity.textmeshpro-3.0.6.tgz' 'textmeshpro' 'Scripts'
Get-Tarball 'https://download.packages.unity.com/com.unity.inputsystem/-/com.unity.inputsystem-1.3.0.tgz' 'inputsystem' 'InputSystem'

Write-Host 'Done. Now run: dotnet build Tools/CompileCheck/Game/Assembly-CSharp.csproj'
