param(
    [string]$OutputDirectory = "artifacts"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$projectPath = Join-Path $repositoryRoot "src\Keyana.App\Keyana.App.csproj"
$readmePath = Join-Path $repositoryRoot "docs\USER_GUIDE.md"

# 版本号只在 csproj 的 <Version> 里维护，这里读回来给产物命名，
# 避免出现「exe 是 1.2.0、压缩包名还是 1.1.1」这种不一致。
[xml]$projectXml = Get-Content -LiteralPath $projectPath
$version = ($projectXml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "无法从 $projectPath 读取 <Version>；发布脚本需要它来命名产物。"
}

$fullOutput = Join-Path $artifactsRoot "v$version-full"
$liteOutput = Join-Path $artifactsRoot "v$version-lite"

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
foreach ($target in @($fullOutput, $liteOutput)) {
    $resolvedTarget = [System.IO.Path]::GetFullPath($target)
    if (-not $resolvedTarget.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理 artifacts 目录以外的路径：$resolvedTarget"
    }

    if (Test-Path -LiteralPath $resolvedTarget) {
        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
    }
}

dotnet publish $projectPath -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $fullOutput

New-Item -ItemType Directory -Path (Join-Path $fullOutput "Data") -Force | Out-Null
Copy-Item -LiteralPath $readmePath -Destination (Join-Path $fullOutput "README.md")

dotnet publish $projectPath -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $liteOutput

New-Item -ItemType Directory -Path (Join-Path $liteOutput "Data") -Force | Out-Null
Copy-Item -LiteralPath $readmePath -Destination (Join-Path $liteOutput "README.md")

$fullZip = Join-Path $artifactsRoot "Keyana-V$version-full-win-x64.zip"
$liteZip = Join-Path $artifactsRoot "Keyana-V$version-lite-win-x64.zip"
Compress-Archive -Path (Join-Path $fullOutput "*") -DestinationPath $fullZip -CompressionLevel Optimal -Force
Compress-Archive -Path (Join-Path $liteOutput "*") -DestinationPath $liteZip -CompressionLevel Optimal -Force

$checksums = @($fullZip, $liteZip) | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($_))"
}
[System.IO.File]::WriteAllLines((Join-Path $artifactsRoot "SHA256SUMS.txt"), $checksums)

Write-Host "发布完成：$artifactsRoot"

