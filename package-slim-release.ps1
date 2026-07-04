param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path $PSScriptRoot
$project = Join-Path $root "LineResistanceHost\LineResistanceHost_WinUI.csproj"
$appName = "LineResistanceHost_WinUI"
$releaseRoot = Join-Path $root "artifacts\release-$Version"
$openCmdName = "OpenConsole.cmd"

$rids = @(
    @{ Rid = "win-x64"; Platform = "x64" },
    @{ Rid = "win-arm64"; Platform = "ARM64" }
)

$keepDirs = @("Assets", "Microsoft.UI.Xaml", "zh-CN", "zh-TW", "en-us")
$removePatterns = @(
    "*.pdb",
    "Microsoft.Web.WebView2*.dll",
    "WebView2Loader.dll",
    "Microsoft.ML.OnnxRuntime.dll",
    "System.Numerics.Tensors.dll",
    "Microsoft.Windows.AI*.dll",
    "Microsoft.Windows.AI*.winmd",
    "Microsoft.Windows.Widgets*.dll",
    "Microsoft.Windows.Widgets*.winmd",
    "Microsoft.Windows.AppNotifications*.dll",
    "Microsoft.Windows.AppNotifications*.winmd",
    "Microsoft.Windows.PushNotifications*.dll",
    "Microsoft.Windows.PushNotifications*.winmd",
    "Microsoft.Windows.BadgeNotifications*.dll",
    "Microsoft.Windows.BadgeNotifications*.winmd",
    "Microsoft.Security.Authentication.OAuth*.dll",
    "Microsoft.Security.Authentication.OAuth*.winmd",
    "Microsoft.Windows.Storage.Pickers*.dll",
    "Microsoft.Windows.Storage.Pickers*.winmd",
    "Microsoft.Windows.Media.Capture*.dll",
    "Microsoft.Windows.Media.Capture*.winmd",
    "Microsoft.Windows.ApplicationModel.Background*.dll",
    "Microsoft.Windows.ApplicationModel.Background*.winmd",
    "Microsoft.Windows.System.Power*.dll",
    "Microsoft.Windows.System.Power*.winmd",
    "Microsoft.Graphics.Imaging*.dll",
    "Microsoft.Graphics.Imaging*.winmd"
)

Get-Process $appName -ErrorAction SilentlyContinue | Stop-Process -Force
if (Test-Path $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

foreach ($ridInfo in $rids) {
    $rid = $ridInfo.Rid
    $platform = $ridInfo.Platform

    Write-Host "=== Building $rid slim framework-dependent ==="
    dotnet build $project -c Release -r $rid `
        -p:Platform=$platform `
        -p:SelfContained=false `
        -p:PublishSelfContained=false `
        -p:WindowsAppSDKSelfContained=false `
        -p:PublishReadyToRun=false `
        -p:PublishTrimmed=false

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for $rid slim"
    }

    $output = Join-Path $root "LineResistanceHost\bin\$platform\Release\net10.0-windows10.0.26100.0\$rid"
    if (-not (Test-Path (Join-Path $output "$appName.exe"))) {
        throw "Cannot find build output exe for $rid slim"
    }

    $stage = Join-Path $releaseRoot "$rid-slim"
    if (Test-Path $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }

    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item -Path (Join-Path $output "*") -Destination $stage -Recurse -Force

    Get-ChildItem $stage -Directory |
        Where-Object { $keepDirs -notcontains $_.Name } |
        Remove-Item -Recurse -Force

    $logDirectory = Join-Path $stage "logs"
    if (Test-Path $logDirectory) {
        Remove-Item -LiteralPath $logDirectory -Recurse -Force
    }

    foreach ($pattern in $removePatterns) {
        Get-ChildItem $stage -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue |
            Remove-Item -Force
    }

    [System.IO.File]::WriteAllText(
        (Join-Path $stage $openCmdName),
        "@echo off`r`ncd /d %~dp0`r`ncmd`r`n",
        [System.Text.Encoding]::ASCII)

    [System.IO.File]::WriteAllText(
        (Join-Path $stage "Runtime-required.txt"),
        "LineResistanceHost_WinUI slim package dependencies:`r`n- .NET Desktop Runtime 10.0 matching this package architecture.`r`n- Microsoft Windows App SDK Runtime 2.2 matching this package architecture.`r`n`r`nUse the non-slim package if you want most runtime files bundled locally.`r`n",
        [System.Text.Encoding]::UTF8)

    $zipPath = Join-Path $releaseRoot "$appName-$Version-$rid-slim.zip"
    if (Test-Path $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zipPath -CompressionLevel Optimal

    $stageSize = (Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum
    $zipSize = (Get-Item $zipPath).Length
    [PSCustomObject]@{
        Rid = $rid
        Files = (Get-ChildItem $stage -Recurse -File).Count
        StageMB = [math]::Round($stageSize / 1MB, 2)
        ZipMB = [math]::Round($zipSize / 1MB, 2)
        Zip = $zipPath
    } | Format-List
}
