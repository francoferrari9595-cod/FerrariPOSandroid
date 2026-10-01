# FerrariPOS V73.1.57 - compilación PowerShell
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK no está instalado."
}
Remove-Item ".\EXE" -Recurse -Force -ErrorAction SilentlyContinue
New-Item ".\EXE" -ItemType Directory | Out-Null
dotnet restore .\FerrariPOS.csproj
dotnet publish .\FerrariPOS.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\EXE
Write-Host "LISTO: $PSScriptRoot\EXE\FerrarisPOS.exe"
