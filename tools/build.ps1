# Gera os pacotes de distribuição do OtimizaPC em artifacts\:
#   OtimizaPC-Setup-{versão}-{x64|x86}.exe (+ .sha256) e OtimizaPC-Portable-{versão}-{x64|x86}.zip (+ .sha256)
# Uso: pwsh tools\build.ps1 [-SkipTests] [-Arch x64,x86]
param(
    [switch]$SkipTests,
    [string[]]$Arch = @('x64', 'x86')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 não encontrado. Instale com: winget install JRSoftware.InnoSetup' }

Write-Host "OtimizaPC $version" -ForegroundColor Cyan
if (Test-Path $artifacts) { Remove-Item -Recurse -Force $artifacts }
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

if (-not $SkipTests) {
    Write-Host '== Testes' -ForegroundColor Cyan
    dotnet test (Join-Path $root 'tests\OtimizaPC.Tests') -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Testes falharam.' }
}

function Write-Checksum([string]$file) {
    $hash = (Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant()
    # Formato do sha256sum, verificado pelo atualizador do app
    "$hash  $(Split-Path -Leaf $file)" | Set-Content -NoNewline -Encoding ascii "$file.sha256"
}

foreach ($a in $Arch) {
    $publish = Join-Path $artifacts "publish\$a"
    Write-Host "== Publicando $a" -ForegroundColor Cyan
    dotnet publish (Join-Path $root 'src\OtimizaPC.App') -c Release -r "win-$a" -o $publish -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar $a." }
    # Símbolos e documentação XML não vão para o usuário
    Get-ChildItem $publish -Recurse -Include *.pdb, *.xml | Remove-Item -Force

    Write-Host "== Instalador $a" -ForegroundColor Cyan
    & $iscc /Q "/DAppVersion=$version" "/DArch=$a" "/DSourceDir=$publish" (Join-Path $root 'installer\OtimizaPC.iss')
    if ($LASTEXITCODE -ne 0) { throw "Falha ao gerar o instalador $a." }
    Write-Checksum (Join-Path $artifacts "OtimizaPC-Setup-$version-$a.exe")

    Write-Host "== Portátil $a" -ForegroundColor Cyan
    $zip = Join-Path $artifacts "OtimizaPC-Portable-$version-$a.zip"
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip
    Write-Checksum $zip
}

Write-Host "`nPacotes gerados:" -ForegroundColor Green
Get-ChildItem $artifacts -File | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } } | Format-Table -AutoSize
