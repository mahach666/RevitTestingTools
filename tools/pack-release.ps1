<#
.SYNOPSIS
    Собирает файлы релиза в папку artifacts: архивы надстройки по годам, MSI надстройки, CLI, инспектор, пакеты NuGet
    и SHA256SUMS.txt. MSI (installer\RevitTestHost.Installer.wixproj) собирается, только когда собраны все годы 2020-2027.

.DESCRIPTION
    Версия берётся из Directory.Build.props. Архив надстройки собирается на каждый год из -RevitVersions
    против Revit API этого года (по умолчанию 2020-2027). Файлы в Output перезаписываются, ничего не удаляется.

    Состав архива надстройки: его содержимое распаковывается в %ProgramData%\Autodesk\Revit\Addins\<версия>\
    (для 2027 в %AppData%\Autodesk\Revit\Addins\2027\: из %ProgramData% Revit 2027 манифесты не загружает)
    (RevitTestHost.addin в корне, три сборки в подпапке TestHost).
#>
param(
    [string[]]$RevitVersions = @(2020..2027),
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# powershell -File не превращает 2020,2025 в массив: принимаем и массив, и строку с запятыми
$years = @($RevitVersions | ForEach-Object { "$_" -split '[,; ]+' } | Where-Object { $_ })
$bad = @($years | Where-Object { $_ -notmatch '^\d{4}$' -or ([int]$_ -lt 2020) -or ([int]$_ -gt 2027) })
if ($bad.Count -gt 0) { throw "Неподдерживаемые годы: $($bad -join ', '). Допустимы 2020-2027." }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$props = [xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)
$versionNode = $props.SelectSingleNode('/Project/PropertyGroup/Version')
$version = if ($versionNode) { $versionNode.InnerText } else { $null }
if (-not $version) { throw 'В Directory.Build.props не найдено свойство Version.' }

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$Output = (Resolve-Path $Output).Path
$stage = Join-Path $Output 'stage'
New-Item -ItemType Directory -Force -Path $stage | Out-Null

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') завершился с кодом $LASTEXITCODE" }
}

function New-Zip([string]$SourceDir, [string]$ZipPath) {
    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath }
    Compress-Archive -Path (Join-Path $SourceDir '*') -DestinationPath $ZipPath
}

# 1. Надстройка: по архиву на каждый год, сборка против Revit API этого года
foreach ($year in $years) {
    $addinBuild = Join-Path $stage "addin-build-$year"
    Invoke-DotNet build (Join-Path $root 'src\RevitTestHost.Addin\RevitTestHost.Addin.csproj') -c Release "-p:RevitVersion=$year" -o $addinBuild
    $addinStage = Join-Path $stage "addin-$year"
    New-Item -ItemType Directory -Force -Path (Join-Path $addinStage 'TestHost') | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'src\RevitTestHost.Addin\RevitTestHost.addin') -Destination $addinStage
    foreach ($name in 'RevitTestHost.Addin.dll', 'RevitTestHost.Core.dll', 'RevitTestHost.Sdk.dll') {
        Copy-Item -LiteralPath (Join-Path $addinBuild $name) -Destination (Join-Path $addinStage 'TestHost')
    }
    New-Zip $addinStage (Join-Path $Output "RevitTestHost-Addin-$version-Revit$year.zip")
}

# 1a. MSI надстройки (для текущего пользователя, все годы в одном файле): только когда собраны все годы 2020-2027
$missing = @(2020..2027 | Where-Object { "$_" -notin $years })
if ($missing.Count -eq 0) {
    $msiBuild = Join-Path $stage 'installer'
    Invoke-DotNet build (Join-Path $root 'installer\RevitTestHost.Installer.wixproj') -c Release "-p:AddinStageRoot=$stage" -o $msiBuild
    Copy-Item -LiteralPath (Join-Path $msiBuild "RevitTestHost-Addin-$version.msi") -Destination $Output -Force
}
else {
    Write-Warning "MSI надстройки не собран: нет годов $($missing -join ', ') (MSI включает все годы 2020-2027)."
}

# 2. CLI (нужен .NET 8 Runtime)
$cliStage = Join-Path $stage 'cli'
Invoke-DotNet publish (Join-Path $root 'src\RevitTestHost.Cli\RevitTestHost.Cli.csproj') -c Release -o $cliStage
New-Zip $cliStage (Join-Path $Output "RevitTestHost-Cli-$version.zip")

# 3. Инспектор
$inspectorStage = Join-Path $stage 'inspector'
Invoke-DotNet publish (Join-Path $root 'tools\RevitUITesting.Inspector\RevitUITesting.Inspector.csproj') -c Release -o $inspectorStage
New-Zip $inspectorStage (Join-Path $Output "RevitUITesting-Inspector-$version.zip")

# 4. Пакеты NuGet
Invoke-DotNet pack (Join-Path $root 'src\RevitTestHost.Sdk\RevitTestHost.Sdk.csproj') -c Release -o $Output
Invoke-DotNet pack (Join-Path $root 'src\RevitUITesting\RevitUITesting.csproj') -c Release -o $Output

# 5. Контрольные суммы
$files = @(Get-ChildItem -LiteralPath $Output -File | Where-Object { $_.Extension -in '.zip', '.msi', '.nupkg', '.snupkg' } | Sort-Object Name)
$sums = $files | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
Set-Content -LiteralPath (Join-Path $Output 'SHA256SUMS.txt') -Value $sums -Encoding Ascii
$sums
