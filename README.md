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

---

## Commands

### `build` — Compile contracts and pack a `.v12pak`

```bash
vcsdk build [path] [options]
```

Builds a game project: finds `contract.ctproj`, runs `ccl build`, then packs everything into a `.v12pak`.

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `[path]` | — | Path to game project root | `.` (current dir) |
| `--output <path>` | `-o` | Output `.v12pak` path | `bin/<name>.v12pak` in project dir |
| `--compiler <exe>` | `-x` | Contract compiler executable | `ccl` |
| `--template <dir>` | `-t` | Expand a template before building | *(none)* |

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
```

---

### `serve` — Watch mode for rapid development

```bash
vcsdk serve [path] [options]
```

Watches `.ct` and `.xml` files, automatically recompiling and repacking when changes are detected.

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `[path]` | — | Path to game project root | `.` (current dir) |
| `--output <path>` | `-o` | Output `.v12pak` path | Same as `build` |
| `--compiler <exe>` | `-x` | Contract compiler executable | `ccl` |
| `--interval <ms>` | `-i` | Watch poll interval | `500` |
| `--copy-target <dir>` | `-c` | Copy built pak to this directory | *(none)* |
| `--template <dir>` | `-t` | Expand a template on start | *(none)* |

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
├── Program.cs              ← CLI entry point
├── PwshUtil.cs             ← Process runner utility
├── Commands/
│   ├── BuildCommand.cs     ← vcsdk build
│   ├── ServeCommand.cs     ← vcsdk serve
│   ├── InstallCommand.cs   ← vcsdk install
│   └── NewCommand.cs       ← (reserved, delegates to ccl new)
├── V12.Sdk.csproj
└── bin/                    ← (not committed)
```

---

## Contributing

Contributions welcome! Please ensure:

1. Code builds cleanly: `dotnet build`
2. Changes follow existing style
3. New commands include help text

## License

MIT
