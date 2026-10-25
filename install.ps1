param(
    [string]$version = "0.1.0"
)

# Build the nupkg. Prefer the monorepo sibling layout, fall back to the
# standalone vendored libs/ checkout.
$v12BasicRoot = Join-Path (Get-Location) "libs\V12.Basic"
if (-not (Test-Path (Join-Path $v12BasicRoot "V12.Basic.csproj"))) {
    $v12BasicRoot = Join-Path (Get-Location) "..\V12.Basic"
}
dotnet pack -p:V12BasicRoot=$v12BasicRoot -c Release V12.Sdk.csproj -o ./nupkg

# Remove previous global install (use package ID, not command name)
dotnet tool uninstall --global V12.Sdk 2>$null

# Disable source mapping temporarily for --add-source to work
$env:NUGET_ENABLE_SOURCE_MAPPING = "false"
dotnet tool install --global --add-source ./nupkg --version $version V12.Sdk

Write-Host "`nInstalled 'vcsdk' globally. Run 'vcsdk --help' to get started." -ForegroundColor Green