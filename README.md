# V12 SDK for Contract

A development toolkit for building and packaging [Contract](https://github.com/Fy-nite/Contract) games into `.v12pak` archives.

## Installation

### From source
```bash
git clone https://github.com/Fy-nite/V12-Sdk.git
cd V12.Sdk
dotnet publish -c Release -o vcsdk --self-contained false
# Add to your PATH or install as a global tool:
dotnet tool install --global --add-source ./vcsdk V12.Sdk
```

### Requirements
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- `ccl` (Contract CLI) installed and on PATH

### Colored output

Output is colored automatically when writing to a terminal. Colors are disabled
when output is redirected, when `NO_COLOR` is set, or when `TERM=dumb`. Use
`--color` to force them (for example when piping) or `--no-color` to disable them.

---

## Commands

### `new` — Scaffold a project

```bash
vcsdk new <contract|net|renderer> --name <name> [options]
```

| Kind | What it creates |
|---|---|
| `contract` | Contract (`.ct`) game project |
| `net` | .NET game pak (C# components + WorldML) |
| `renderer` | Renderer library targeting `V12.Rendering.IRenderer` |

`--name` is required. The old `vcsdk new <name>` and `vcsdk new-dotnet <name>`
forms were replaced by this.

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--name <name>` | `-n` | Project name (required) | — |
| `--output <dir>` | `-o` | Output directory | `./<name>/` |
| `--type <exe\|lib>` | — | Contract project type | `exe` |
| `--backend <type>` | `-b` | Renderer backend: `none`, `monogame`, `godot` | `none` |
| `--id <id>` | — | `renderer.json` id | lowercased name |

**Examples:**

```bash
vcsdk new contract --name MyGame
vcsdk new net --name MyGame
vcsdk new renderer --name MyRenderer --backend monogame
```

#### Purr install model

`.NET` projects created by `new net` and `new renderer` reference the engine from
`~/.purr`, not from a monorepo checkout:

```
~/.purr/
  packages/        Finite.V12.* and Contract.* nupkgs
  sdk/V12.props    V12Version + package feed, written by `vcsdk sync`
  renderers/<id>/  installed renderer distributions + renderer.json
  templates/       user template overrides
  bin/             tools
```

The generated `Directory.Build.props` imports `~/.purr/sdk/V12.props` and fails
with `V12SDK003` when the feed is missing. Run `vcsdk sync` once to populate it.

`new net` uses:

```xml
<PackageReference Include="Finite.V12.Basic" Version="$(V12Version)" />
```

`Finite.V12.Basic` brings `Finite.V12`, Audio, Rendering and the Contract
runtime. Add `Finite.V12.Paper`, `.Editor`, `.Pak` or `.Rendering` only when the
code uses them.

`new renderer` uses `Finite.V12.Rendering` plus the backend SDK
(`MonoGame.Framework.DesktopGL` or `Godot.NET.Sdk`). Renderer libraries must not
reference `V12.Components.*`.

#### Working on the engine itself

Inside the monorepo, the root `Directory.Build.props` defines
`$(V12EngineProject)`, `$(V12BasicProject)` and the rest of the path table for
engine and sample builds. Generated projects do not use those properties; they
consume the Purr packages. The full key list and pak/manifest reference live in
`Docs/GAMEPAK_PROJECTS.md`.


---

### `build` — Compile contracts and pack a `.v12pak`

```bash
vcsdk build [path] [options]
```

Builds a Contract game project: finds `contract.ctproj`, runs `ccl build`, then packs everything into a `.v12pak`.

When the directory contains a `.csproj` (and no `contract.ctproj`),
`vcsdk build` delegates to `vcsdk pack` so .NET projects are published instead
of shipping C# sources.

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `[path]` | — | Path to game project root | `.` (current dir) |
| `--output <path>` | `-o` | Output `.v12pak` path | `bin/<name>.v12pak` in project dir |
| `--manifest <path>` | `-m` | Use this manifest instead of auto-detecting (validated against the project dir) | *(none)* |
| `--compiler <exe>` | `-x` | Contract compiler executable | `ccl` |
| `--template <dir>` | `-t` | Expand a template before building | *(none)* |
| `--renderer <id>` | `-r` | Installed renderer host to copy beside the pak | `monogame` |
| `--no-renderer` | — | Do not copy a renderer host | *(false)* |

After packing, `vcsdk build` copies the installed single-file renderer host next
to the pak and renames it to match (`MyGame.v12pak` → `MyGame.exe`), so the host
finds the pak automatically on launch.

**Examples:**

```bash
# Build from current directory
vcsdk build

# Build with custom output location
vcsdk build ../MyGame -o ../games/MyGame.v12pak

# Use a custom compiler path
vcsdk build -x /opt/ccl/bin/contract

# Create + build from a template in one step
vcsdk build --template ./SpinWorldTemplate MyGame

# Pack a C#/Godot game with an explicit manifest
vcsdk build ./MyGodotGame -m ./MyGodotGame/build.manifest.json
```

---

### `pack` — Publish a .NET project and pack a precompiled gamepak

```bash
vcsdk pack [path] [options]
```

Runs `dotnet publish` for a `.csproj`, stages the project assembly into
`paks/`, dependency DLLs into `bin/`, and packs a `.v12pak`. The renderer loads
the assembly and registers every concrete `IComponent` it contains, so no C# is
compiled at load time. An `IV12Gamepack` entry point is optional; component
classes are enough.

When a `manifest.json` is present, it is used and augmented with the built
assembly (and `scripts/*.cs` in live mode). Otherwise a manifest is generated
from the staged layout.

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `[path]` | — | Project file or project directory | `.` (current dir) |
| `--output <path>` | `-o` | Output `.v12pak` path | `bin/<name>.v12pak` in project dir |
| `--manifest <path>` | `-m` | Use this manifest instead of auto-detecting | *(none)* |
| `--configuration <c>` | — | dotnet build configuration | `Release` |
| `--renderer <id>` | `-r` | Installed renderer host to copy beside the pak | `monogame` |
| `--no-renderer` | — | Do not copy a renderer host | *(false)* |
| `--copy-target <dir>` | `-c` | Copy the built pak into this directory | *(none)* |
| `--live-scripts` | — | Pack `scripts/*.cs`; compile at load time | *(false)* |
| `--no-restore` | — | Skip `dotnet restore` during publish | *(false)* |
| `--strict` | — | Fail when the project's V12 version differs from the renderer's | *(false)* |

The project assembly and first-party `ProjectReference` assemblies go into
`paks/`; third-party NuGet dependencies go into `bin/`. Engine assemblies
(`V12*.dll`) are never packed, so the renderer's copy stays authoritative.

**Examples:**

```bash
# Publish and pack from the current directory
vcsdk pack

# Pack to a custom output and enforce version parity
vcsdk pack ./MyGame -o ./dist/MyGame.v12pak --strict

# Keep the old hot-reload path (compile C# at load time)
vcsdk pack ./MyGame --live-scripts
```

---

### `serve` — Watch mode for rapid development

```bash
vcsdk serve [path] [options]
```

Watches `.ct` and `.xml` files, automatically recompiling and repacking when changes are detected. For `.csproj` projects it watches `.cs`/`.csproj`/content and reruns `vcsdk pack` instead.

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `[path]` | — | Path to game project root | `.` (current dir) |
| `--output <path>` | `-o` | Output `.v12pak` path | Same as `build` |
| `--compiler <exe>` | `-x` | Contract compiler executable | `ccl` |
| `--interval <ms>` | `-i` | Watch poll interval | `500` |
| `--copy-target <dir>` | `-c` | Copy built pak to this directory | *(none)* |
| `--template <dir>` | `-t` | Expand a template on start | *(none)* |
| `--renderer <id>` | `-r` | Installed renderer host to copy beside the pak | `monogame` |
| `--no-renderer` | — | Do not copy a renderer host | *(false)* |

**Examples:**

```bash
# Watch current project, poll every second
vcsdk serve -i 1000

# Watch and copy built pak directly into launcher's games folder
vcsdk serve -c ../../Scramble-Launcher/games

# Start with template expansion, then watch
vcsdk serve --template ./templates/platformer MyPlatformer
```

Press **Ctrl+C** to stop.

---

### `sync` — Build and pack the V12 modules

```bash
vcsdk sync [options]
```

Clones or pulls the V12-Monorepo, runs `dotnet pack` for every V12 module into
the Purr feed (`~/.purr/packages`), then writes `~/.purr/sdk/V12.props` with the
engine version and feed. The feed includes the engine modules (`Finite.V12`,
`Finite.V12.Rendering`, `Finite.V12.Audio`, `Finite.V12.Audio.OwnAudio`,
`Finite.V12.Basic`, `Finite.V12.Paper`, `Finite.V12.Editor`, `Finite.V12.Pak`)
and the Contract runtime chain (`Contract.Runtime`, `Contract.Compiler`,
`ObjectRT.*`, `ObjektRT.*`) that `Finite.V12` depends on. Any additional
root-level `Finite.V12.*` module is picked up automatically.

Scaffolded projects import `~/.purr/sdk/V12.props`; that file is the single
source of truth for `V12Version` and the package feed.

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--monorepo <url\|path>` | — | Monorepo URL or local checkout | nearest checkout, else `https://github.com/Fy-nite/V12.git` |
| `--ref <branch>` | — | Git ref to sync | `main` |
| `--version <ver>` | `-v` | Package version | `V12Version` from `Directory.Build.props` |
| `--output <path>` | `-o` | Packages output directory | `$PURR_HOME/packages` |
| `--dev` | `-d` | Add a unique `-dev.<timestamp>` suffix | *(false)* |
| `--force` | `-f` | Continue when a module fails to pack | *(false)* |
| `--no-build` | — | Pass `--no-build` to `dotnet pack` | *(false)* |
| `--verbose` | — | Verbose pack output | *(false)* |

**Examples:**

```bash
# Pack the current monorepo into the local Purr feed
vcsdk sync

# Pack a specific local checkout with a dev version
vcsdk sync --monorepo ../V12-Monorepo --dev
```

---

### `install` — Install Contract packages (.coi)

```bash
vcsdk install <package> [options]
```

Installs a package from the Purr registry or a local file. Delegates to `ccl install`.

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `<package>` | — | Package name (from registry) or path to `.coi` file | — |
| `--compiler <exe>` | `-x` | Contract compiler executable | `ccl` |

**Examples:**

```bash
# Install from Purr registry
vcsdk install ObjektRT

# Install a local .coi package
vcsdk install ./my-custom-bindings.coi

# Install with a custom compiler
vcsdk install -x /custom/path/to/contract my-package
```

---

## Templates

Templates let you scaffold game projects with predefined structure: scripts, world layouts, assets, and pre-configured settings.

### Template Format

A template is any directory containing a `contract.ctproj`:

```
my-game-template/
├── contract.ctproj          ← metadata + placeholder values
│   {
│     "Name": "{{ProjectName}}",
│     "Type": "exe",
│     "Main": "scripts/Main.ct",
│     "Output": "bin"
│   }
├── scripts/
│   └── Main.ct              ← uses {{VariableName}} placeholders
├── worlds/
│   └── DefaultWorld/
│       ├── world.xml
│       └── templates/box.xml
└── assets/textures/logo.png
```

### Placeholder Variables

Files inside templates support these placeholders (replaced during expansion):

| Placeholder | Replaces With | Source |
|------------|---------------|--------|
| `{{ProjectName}}` | The game name passed to `new`/`build` | CLI argument |
| `{{ProjectType}}` | `"exe"` or `"lib"` | From template's `contract.ctproj` or CLI `--type` |
| `{{Namespace}}` | Namespace for generated code | From template's `contract.ctproj` or inferred from name |
| `{{Version}}` | Semver version string | From template's `contract.ctproj` or `"0.1.0"` |

### Discovery Locations

When using named templates (`--template spinworld`), vcsdk searches:

1. **Local**: `<project-root>/.purr/templates/<name>/`
2. **Global**: `~/.purr/templates/<name>/`

### Full Workflow

```bash
# 1. Create project from template
ccl new MyPlatformer --template ./platformer-template

# 2. Edit the generated code (placeholders already replaced)
edit MyPlatformer/scripts/Main.ct

# 3. Build and pack
vcsdk build MyPlatformer -o ../games/MyPlatformer.v12pak

# 4. Or watch-mode for dev iteration
vcsdk serve MyPlatformer -c ../Scramble-Launcher/games
```

---

## Project Structure

```
V12.Sdk/
├── Program.cs                  ← CLI entry point
├── PwshUtil.cs                 ← Process runner utility
├── Commands/
│   ├── BuildCommand.cs         ← vcsdk build (Contract)
│   ├── PackCommand.cs          ← vcsdk pack (.NET gamepak)
│   ├── ServeCommand.cs         ← vcsdk serve
│   ├── InstallCommand.cs       ← vcsdk install
│   ├── NewCommand.cs           ← vcsdk new <kind> --name
│   ├── NetProjectScaffolder.cs ← new net
│   ├── RendererProjectScaffolder.cs ← new renderer
│   └── TemplateScaffolder.cs   ← template copy + placeholder replacement
├── templates/
│   ├── mygame/                 ← Contract named template
│   ├── net/                    ← .NET game pak
│   └── renderer/
│       ├── none/
│       ├── monogame/
│       └── godot/
├── V12.Sdk.csproj
└── bin/                        ← (not committed)
```

---

## Contributing

Contributions welcome! Please ensure:

1. Code builds cleanly: `dotnet build`
2. Changes follow existing style
3. New commands include help text

## License

MIT
