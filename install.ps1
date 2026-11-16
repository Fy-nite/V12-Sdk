param(
    [string]$version = ""
)

$ErrorActionPreference = "Stop"

# Work from the SDK root regardless of the caller's current directory.
$sdkRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }

if (-not $version) {
    $csproj = Join-Path $sdkRoot "V12.Sdk.csproj"
    if (Test-Path $csproj) {
        $match = [regex]::Match((Get-Content $csproj -Raw), '<Version>([^<]+)</Version>')
        if ($match.Success) { $version = $match.Groups[1].Value.Trim() }
    }
    if (-not $version) { $version = "0.1.0" }
}

# Build the nupkg. Prefer the monorepo sibling layout, fall back to the
# standalone vendored libs/ checkout. Join-Path segments so this works on
# Linux/macOS as well as Windows (backslashes are not separators there).
$v12BasicRoot = Join-Path (Join-Path $sdkRoot "libs") "V12.Basic"
if (-not (Test-Path (Join-Path $v12BasicRoot "V12.Basic.csproj"))) {
    $v12BasicRoot = Join-Path (Split-Path $sdkRoot -Parent) "V12.Basic"
}

Push-Location $sdkRoot
try {
    dotnet pack -p:V12BasicRoot=$v12BasicRoot -c Release V12.Sdk.csproj -o ./nupkg
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed" }
}
finally {
    Pop-Location
}

# A reinstall at the same version can otherwise reuse the previously extracted
# tool from the NuGet cache, which is how a machine ends up running old vcsdk
# code after pulling the repo.
$toolCache = Join-Path (Join-Path (Join-Path $HOME ".nuget") "packages") (Join-Path "v12.sdk" $version)
if (Test-Path $toolCache) {
    Remove-Item -LiteralPath $toolCache -Recurse -Force
    Write-Host "Cleared cached tool: $toolCache" -ForegroundColor DarkGray
}

# Remove previous global install (use package ID, not command name)
dotnet tool uninstall --global V12.Sdk 2>$null | Out-Null

# Disable source mapping temporarily for --add-source to work
$env:NUGET_ENABLE_SOURCE_MAPPING = "false"
dotnet tool install --global --add-source (Join-Path $sdkRoot "nupkg") --version $version V12.Sdk
if ($LASTEXITCODE -ne 0) { throw "dotnet tool install failed" }

Write-Host "`nInstalled 'vcsdk' globally ($version). Run 'vcsdk --help' to get started." -ForegroundColor Green
