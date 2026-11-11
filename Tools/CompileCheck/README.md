# Compile check (no Unity required)

Builds every script Unity compiles into `Assembly-CSharp` (everything under `Assets`, except
`Editor` folders) against:

- Unity 2020.3.21 engine reference assemblies (`UnityEngine.Modules` from the BepInEx NuGet feed)
- the package versions in `Packages/manifest.json`: uGUI 1.0.0, TextMesh Pro 3.0.6, Input System 1.3.0

It uses the scripting defines of a Windows standalone player build with C# 8 and the .NET 4.x API
level, the same as the project settings.

```sh
# once: download into Tools/CompileCheck/.cache (git-ignored, nothing is redistributed)
bash Tools/CompileCheck/fetch.sh          # or: Tools\CompileCheck\fetch.ps1 in PowerShell

dotnet build Tools/CompileCheck/Game/Assembly-CSharp.csproj
```

You only need the .NET SDK (6 or newer).

This catches compile errors, missing APIs, and C# features Unity 2020.3 doesn't support. It doesn't
replace running the editor: anything that depends on scenes, serialized references, or rendering
still has to be tested in Unity.

The Unity-independent core (TDF reading and writing, undo history) also has unit tests:

```sh
dotnet run --project Tests/Core.Tests
```
