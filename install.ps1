param(
    [string]$version = "0.1.0"
)

# Build the nupkg
dotnet pack -p:V12BasicRoot=$(pwd)\libs\V12.Basic -c Release V12.Sdk.csproj -o ./nupkg

# Remove previous global install (use package ID, not command name)
dotnet tool uninstall --global V12.Sdk 2>$null

# Disable source mapping temporarily for --add-source to work
$env:NUGET_ENABLE_SOURCE_MAPPING = "false"
dotnet tool install --global --add-source ./nupkg --version $version V12.Sdk

Write-Host "`nInstalled 'vcsdk' globally. Run 'vcsdk --help' to get started." -ForegroundColor Green