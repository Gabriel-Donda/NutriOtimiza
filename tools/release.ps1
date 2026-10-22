# Publica uma nova versão do NutriOtimiza no GitHub Releases (tag vX.Y.Z + instaladores e .sha256).
# Uso: pwsh tools\release.ps1 [-Notes "texto"] [-SkipBuild] [-DryRun]
#   -Notes      notas da release (padrão: commits desde a última tag v*)
#   -SkipBuild  não roda tools\build.ps1 (usa o que já está em artifacts\)
#   -DryRun     simula: não cria tag, não faz push, não cria release; falhas de pré-checagem viram avisos
param(
    [string]$Notes,
    [switch]$SkipBuild,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Versão não encontrada em Directory.Build.props.' }
$tag = "v$version"

Set-Location $root

# Em -DryRun, falhas de pré-checagem viram aviso para permitir validar o resto do fluxo
function Fail([string]$message) {
    if ($DryRun) { Write-Warning "(DryRun) $message" } else { throw $message }
}

$gh = @(
    (Get-Command gh -ErrorAction SilentlyContinue).Source,
    "$env:ProgramFiles\GitHub CLI\gh.exe",
    "${env:ProgramFiles(x86)}\GitHub CLI\gh.exe",
    "$env:LOCALAPPDATA\Programs\GitHub CLI\gh.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

Write-Host "NutriOtimiza $version  ->  $tag" -ForegroundColor Cyan
if ($DryRun) { Write-Host '(modo DryRun: nada será alterado no remoto)' -ForegroundColor Yellow }

# ---------- 1. Pré-checagens ----------
Write-Host '== Pré-checagens' -ForegroundColor Cyan

$hasRemote = $false
if ($DryRun) {
    Write-Warning '(DryRun) Checagens de gh (instalação/login) e do remoto são puladas.'
    $remoteUrl = (git remote get-url origin 2>$null)
    $hasRemote = ($LASTEXITCODE -eq 0 -and $remoteUrl)
} else {
    if (-not $gh) { throw 'GitHub CLI não encontrado. Instale com: winget install GitHub.cli' }
    & $gh auth status 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI não autenticado. Rode: gh auth login' }
    $remoteUrl = (git remote get-url origin 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $remoteUrl) {
        throw 'Repositório remoto "origin" não configurado. Rode: git remote add origin https://github.com/dono/repo.git'
    }
    $hasRemote = $true
}

if (git status --porcelain) {
    Fail 'Há alterações não commitadas. Faça commit (ou stash) antes de publicar.'
}

if ($hasRemote) {
    git fetch origin --tags --quiet
    if ($LASTEXITCODE -ne 0) { Fail 'Falha no git fetch origin.' }
    $upstream = (git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $upstream) {
        Fail 'A branch atual não tem upstream. Envie primeiro com: git push -u origin HEAD'
    } else {
        $ahead = [int](git rev-list --count '@{u}..HEAD')
        $behind = [int](git rev-list --count 'HEAD..@{u}')
        if ($ahead -gt 0) { Fail "A branch local tem $ahead commit(s) não enviado(s). Rode: git push" }
        if ($behind -gt 0) { Fail "A branch local está $behind commit(s) atrás de $upstream. Rode: git pull" }
    }
} else {
    Write-Warning '(DryRun) Sem remoto: pulando fetch e comparação com upstream.'
}

if (git tag --list $tag) { Fail "A tag $tag já existe localmente. Aumente <Version> em Directory.Build.props." }
if ($hasRemote) {
    if (git ls-remote --tags origin "refs/tags/$tag") { Fail "A tag $tag já existe no remoto. Aumente <Version> em Directory.Build.props." }
}
if (-not $DryRun) {
    & $gh release view $tag 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "Já existe uma release $tag no GitHub. Aumente <Version> em Directory.Build.props." }
}

# ---------- 2. Build ----------
if (-not $SkipBuild) {
    Write-Host '== Build' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Build falhou.' }
} else {
    Write-Host '== Build (pulado: -SkipBuild)' -ForegroundColor Cyan
}

# ---------- 3. Artefatos ----------
Write-Host '== Conferindo artefatos' -ForegroundColor Cyan
$files = foreach ($a in 'x64', 'x86') {
    "NutriOtimiza-Setup-$version-$a.exe"
    "NutriOtimiza-Setup-$version-$a.exe.sha256"
    "NutriOtimiza-Portable-$version-$a.zip"
    "NutriOtimiza-Portable-$version-$a.zip.sha256"
}
$missing = $files | Where-Object { -not (Test-Path (Join-Path $artifacts $_)) }
if ($missing) { throw "Arquivos ausentes em artifacts\: $($missing -join ', '). Rode tools\build.ps1." }

foreach ($f in $files | Where-Object { $_ -like '*.sha256' }) {
    $shaPath = Join-Path $artifacts $f
    $target = Join-Path $artifacts ($f -replace '\.sha256$', '')
    $expected = ((Get-Content -Raw $shaPath).Trim() -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -Algorithm SHA256 $target).Hash.ToLowerInvariant()
    if ($expected -ne $actual) { throw "SHA-256 não confere para $($f -replace '\.sha256$', ''): esperado $expected, obtido $actual." }
    Write-Host "  ok  $f" -ForegroundColor DarkGray
}
$assetPaths = $files | ForEach-Object { Join-Path $artifacts $_ }

# ---------- 4. Notas ----------
Write-Host '== Notas da release' -ForegroundColor Cyan
if ([string]::IsNullOrWhiteSpace($Notes)) {
    $lastTag = (git describe --tags --abbrev=0 --match 'v*' 2>$null)
    if ($LASTEXITCODE -ne 0) { $lastTag = $null }
    $range = if ($lastTag) { "$lastTag..HEAD" } else { 'HEAD' }
    $log = (git log --pretty='- %s' $range) -join "`n"
    if (-not $log) { $log = '- Melhorias e correções.' }
    $Notes = "## NutriOtimiza $version`n`n$log"
    Write-Host "  geradas a partir de: $(if ($lastTag) { $lastTag } else { 'todo o histórico' })" -ForegroundColor DarkGray
}
$notesFile = Join-Path ([System.IO.Path]::GetTempPath()) "nutriotimiza-notes-$version.md"
[System.IO.File]::WriteAllText($notesFile, $Notes, (New-Object System.Text.UTF8Encoding($false)))
Write-Host $Notes

# ---------- 5. Tag e release ----------
Write-Host '== Publicando' -ForegroundColor Cyan
$ghCmd = if ($gh) { $gh } else { 'gh' }
if ($DryRun) {
    Write-Host 'Comandos que seriam executados:' -ForegroundColor Yellow
    Write-Host "  git tag -a $tag -m `"NutriOtimiza $version`""
    Write-Host "  git push origin $tag"
    Write-Host "  gh release create $tag"
    $assetPaths | ForEach-Object { Write-Host "      $_" }
    Write-Host "      --title `"NutriOtimiza $version`" --notes-file `"$notesFile`" --verify-tag"
    Write-Host "`nDryRun concluído. Nada foi alterado." -ForegroundColor Green
    return
}

git tag -a $tag -m "NutriOtimiza $version"
if ($LASTEXITCODE -ne 0) { throw "Falha ao criar a tag $tag." }
git push origin $tag
if ($LASTEXITCODE -ne 0) {
    git tag -d $tag | Out-Null
    throw "Falha ao enviar a tag $tag. A tag local foi removida."
}
& $ghCmd release create $tag @assetPaths --title "NutriOtimiza $version" --notes-file $notesFile --verify-tag
if ($LASTEXITCODE -ne 0) { throw "Falha ao criar a release $tag. A tag já foi enviada; remova-a (git push origin :refs/tags/$tag) antes de tentar de novo." }
Remove-Item $notesFile -Force -ErrorAction SilentlyContinue

# ---------- 6. URL ----------
$url = & $ghCmd release view $tag --json url -q .url
Write-Host "`nRelease publicada: $url" -ForegroundColor Green
