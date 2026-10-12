# ExtensionVsix — optional Visual Studio extension

`ExtensionVsix/Template` is an **optional, isolated deliverable** (recorded decision 1, 2026-10-10, confirmed in Phase 20). It is not part of the WebApi/application build, test, packaging or release gate, and `TemplateCleanArchitecture.App.slnf` does not contain it. The full `TemplateCleanArchitecture.sln` still lists it so Visual Studio can open everything; `dotnet build TemplateCleanArchitecture.sln` is **not** a supported command for either the application or the VSIX (it fails with `VSIX deployment is not supported with 'dotnet build'`).

## What it is

- Legacy (non-SDK-style) C# VSIX project, `net472` (`TargetFrameworkVersion` v4.7.2), project types `{82b43b9b-a64c-4715-b499-d71e9ca2bd60}` (VSIX) + `{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}` (C#).
- Manifest identity `Template.e9f68a18-5d0f-445b-836d-4ebdbca840f0`, version `1.0`, display name `Template`, description `Empty VSIX Project.`
- The package carries no assembly, pkgdef, command or template (`GeneratePkgDefFile`, `IncludeAssemblyInVSIXContainer` and the debug-symbol switches are `false`): the `.vsix` contains only `extension.vsixmanifest`, `manifest.json`, `catalog.json` and `[Content_Types].xml`. Installing it adds an entry to the extension list. It has no runtime feature to activate.
- Packages (central, `Directory.Packages.props`): `Microsoft.VisualStudio.SDK` 17.14.40265 (`ExcludeAssets="runtime"`) and `Microsoft.VSSDK.BuildTools` 17.14.2142. `MessagePack`/`MessagePack.Annotations` 2.5.307 are a transitive pin that only the VSIX resolves; it clears the MessagePack advisories fixed in 2.5.301. VSSDK 18 is out of scope and needs explicit authorization.

## Supported Visual Studio range

| Item | Value |
|---|---|
| Manifest installation target | `Microsoft.VisualStudio.Community` `[17.0, 18.0)`, `amd64` (Community and higher editions), prerequisite `Microsoft.VisualStudio.Component.CoreEditor` `[17.0,18.0)` |
| Supported servicing baseline | Visual Studio 2022 **17.14** (Microsoft services VS 2022 only at its latest release, mainstream support until January 2032). Older 17.x minors are inside the manifest range but out of support and were not tested. |
| Visual Studio 2026 (18.x) | Not a manifest target. Microsoft says VS 2026 accepts VS 2022 (17.x) extensions. Installing into VS 2026 has **not** been validated (the owner declined installation into the open VS 2026 Insiders instance). |
| Build toolchain verified | VS 2022 Community 17.14.16 (MSBuild 17.14.23) and VS 2026 Community Insiders 18.11 (MSBuild 18.11.0), both on 2026-10-12 |

Required components: Visual Studio (or Build Tools) with MSBuild, the Visual Studio extension development workload/component (`Microsoft.VisualStudio.Component.VSSDK`, or `Microsoft.VisualStudio.Component.VSSDKBuildTools` for Build Tools), and the .NET Framework 4.7.2 targeting pack.

## Validation path

MSBuild is located with the stable `vswhere.exe` (`%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe`). No edition-specific path is used.

```powershell
# Build + package inspection (no deployment): newest stable Visual Studio 2022/2026 instance
powershell -ExecutionPolicy Bypass -File .\scripts\vsix-validation.ps1

# Build with Visual Studio 2026 Insiders MSBuild
powershell -ExecutionPolicy Bypass -File .\scripts\vsix-validation.ps1 -Label vs2026 -VisualStudioVersionRange '[18.0,19.0)' -Prerelease

# Experimental-instance checks (opt-in, only the <version>_<instance>Exp hive, never the normal profile)
powershell -ExecutionPolicy Bypass -File .\scripts\vsix-validation.ps1 -Label exp -Install -Smoke
powershell -ExecutionPolicy Bypass -File .\scripts\vsix-validation.ps1 -Label exp-remove -Uninstall
```

The script restores with NuGet audit (all severities, direct and transitive) and rebuilds with `/p:DeployExtension=false`. It then verifies a non-empty `.vsix`, records its SHA-256, entries, packed manifest and catalog, and checks the identity, the 17.x installation targets and prerequisites, `amd64`, and the resolved VSSDK packages (all 17.x, none 18.x). Evidence goes to `artifacts/validation/vsix/<Label>/` (git-ignored). Any failure returns exit 1. The `.vsix` zip is not byte-reproducible (timestamps and the random `extensionDir` in `catalog.json`), so checksums identify an artifact but do not prove reproducibility.

Validation levels are separate:

1. **Build** — restore, compile and package (`summary.txt`, `build.log`).
2. **Package inspection** — contents, manifest and catalog checks.
3. **Installation** — `-Install` into the experimental hive (`VSIXInstaller /quiet /rootSuffix:Exp /instanceIds:<id>`).
4. **Startup smoke** — `-Smoke` runs `devenv /RootSuffix Exp /Log <file> /Command File.Exit`. This is not proof that anything loaded: read `ActivityLog.xml`.
5. **Activation/functional** — not applicable while the extension has no package, command or template.

Debug/F5: the project starts `devenv.exe /rootsuffix Exp`. Inside Visual Studio, F5 deploys into the experimental instance only.

## CI

The repository has no CI provider. `scripts/vsix-validation.ps1` without switches is the CI-suitable entry point. Run it on a Windows agent with Visual Studio 2022 (17.14) or Build Tools and the components above. Keep it as its own job and do not make application jobs depend on it. No CI has consumed it yet.

## Rollback

The previous state is `Microsoft.VisualStudio.SDK` 17.0.32112.339 and `Microsoft.VSSDK.BuildTools` 17.12.2069, with no MessagePack pin, and an unchanged project and manifest. Revert those `Directory.Packages.props` entries as one batch. Rebuild with `scripts/vsix-validation.ps1`, which built that state green on 2026-10-12 except for the MessagePack audit findings. Reinstall only into an experimental hive, then rerun the application solution-filter gate. Previous artifacts are kept (git-ignored) in `artifacts/validation/vsix/previous-debug-artifact/` (bin\Debug artifact of 2026-10-08 with its old `project.assets.json`) and `artifacts/validation/vsix/baseline-before/` (Release build of the old package set). To remove the extension from an experimental hive, use `-Uninstall`.
