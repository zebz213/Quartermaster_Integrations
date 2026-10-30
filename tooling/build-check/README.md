# Compile check

`BuildCheck.csproj` compiles the five real source files from `../Quartermaster_Integrations`
against `Stubs.cs` — hand-written stand-ins for UnityEngine, TMPro, UnityEngine.UI, HarmonyLib,
BepInEx and the Valheim types the mod touches.

## Why

The shipping project (`../QuarterMaster_Integrations.csproj`) references the game and mod DLLs
from a Windows Valheim install (`assembly_valheim.dll`, `UnityEngine*.dll`, `BepInEx.dll`,
`0Harmony.dll`, `Quartermaster.dll`, `MyLittleUI.dll`). On a machine without those, `dotnet build`
of the real project can only report "missing reference" errors, which hides whether the mod's own
C# is valid.

This harness gives the compiler enough type information to check *our* code: syntax, names,
overload resolution, `out`/`ref` usage, definite assignment, unreachable code, unused members.

## What it does NOT check

- The accuracy of the stubs. If a stub declares a member that the real API does not have, this
  harness would accept code that fails against the real assemblies. External signatures were
  cross-checked against decompiled mod sources instead (regenerate any of them with
  `./tooling/decompile.sh <ModName>`; output goes to `/tmp/qi-decomp`).
- Anything that requires the real assemblies at runtime: Harmony patch binding, IL, Unity
  behaviour.

## Run

```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$DOTNET_ROOT:$PATH
dotnet build /home/hermes/projects/valheim/quartermaster_integrations/tooling/build-check/BuildCheck.csproj
```

Strict pass (warnings as errors, maximum warning level):

```bash
dotnet build /home/hermes/projects/valheim/quartermaster_integrations/tooling/build-check/BuildCheck.csproj \
  -p:TreatWarningsAsErrors=true -p:WarningLevel=9999 -p:NoWarn=
```

## Adding a reference

When the harness reports a missing member, add the smallest declaration that satisfies the mod to
`Stubs.cs` — keep the signatures honest, and prefer copying the shape from the decompiled source
over guessing.

## Doing a real build

Reference DLLs live in `/home/hermes/projects/valheim/quartermaster_integrations/refs/game` (`valheim_Data/Managed` = your uploaded Managed
folder, `BepInEx/core` = BepInExPack 5.4.2351, `mods/` = public mod assemblies for the audit).
`VALHEIM_DIR` and `BEPINEX_DIR` point the shipping project at them:

```bash
VALHEIM_DIR=/home/hermes/projects/valheim/quartermaster_integrations/refs/game \
BEPINEX_DIR=/home/hermes/projects/valheim/quartermaster_integrations/refs/game/BepInEx \
dotnet build /home/hermes/projects/valheim/quartermaster_integrations/QuarterMaster_Integrations.csproj -c Release
```

## Fetching exact mod versions (Hexium)

`mods/` holds the exact builds the modpack runs, pulled from `valheim.hexium.gg` (its API is
Thunderstore-shaped). Useful when a version is not on Thunderstore:

```bash
# resolve the real download URL, then fetch it
curl -s "https://valheim.hexium.gg/api/experimental/package/Vassteel/Quartermaster/" \
  | python3 -c "import json,sys;print(json.load(sys.stdin)['latest']['download_url'])"
```

Check the version actually inside a DLL afterwards — filenames lie:

```bash
python3 -c "
import re,sys
b=open('Quartermaster.dll','rb').read()
print(sorted({m.decode() for m in re.findall(rb'\b\d+\.\d+\.\d+\b', b)}))"
```

## Inspect

`Inspect/` is a small tool that loads the real assemblies through a `MetadataLoadContext` (nothing is
executed) and checks every string-addressed binding the mod uses at runtime, then prints the built
mod's assembly version:

```bash
dotnet run --project /home/hermes/projects/valheim/quartermaster_integrations/tooling/build-check/Inspect
```

It exits non-zero if any binding is missing. Re-run it after changing a reflection target, or after
updating any integrated mod.
