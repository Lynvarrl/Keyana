param(
    [string]$OutputDirectory = "artifacts"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$projectPath = Join-Path $repositoryRoot "src\Keyana.App\Keyana.App.csproj"
$readmePath = Join-Path $repositoryRoot "docs\USER_GUIDE.md"
$licensePath = Join-Path $repositoryRoot "LICENSE"
$noticesPath = Join-Path $repositoryRoot "THIRD_PARTY_NOTICES.md"
$licensesDirectory = Join-Path $repositoryRoot "licenses"

# Windows PowerShell 5.1 里 $ErrorActionPreference="Stop" 不会把原生程序的非零退出码
# 变成终止错误：dotnet publish 失败后脚本仍会往下走，最后在缺 exe 的情况下创建 Data、
# 复制 README、压缩并打印“发布完成”。所有 dotnet 调用都必须显式检查 $LASTEXITCODE。
function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) 失败（退出码 $LASTEXITCODE）。"
    }
}

# 每个发行包都必须带 README、许可证、第三方声明与依赖许可证全文。
function Copy-PackageDocuments {
    param([Parameter(Mandatory)][string]$Destination)

    Copy-Item -LiteralPath $readmePath -Destination (Join-Path $Destination "README.md")
    Copy-Item -LiteralPath $licensePath -Destination (Join-Path $Destination "LICENSE")
    Copy-Item -LiteralPath $noticesPath -Destination (Join-Path $Destination "THIRD_PARTY_NOTICES.md")

    $targetLicenses = Join-Path $Destination "licenses"
    New-Item -ItemType Directory -Path $targetLicenses -Force | Out-Null
    Copy-Item -Path (Join-Path $licensesDirectory "*") -Destination $targetLicenses -Recurse -Force
}

# 压缩前校验产物：exe 在、版本与 csproj 一致、README/Data/许可证齐全。
function Assert-PublishOutput {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Label)

    $exePath = Join-Path $Path "Keyana.exe"
    if (-not (Test-Path -LiteralPath $exePath)) {
        throw "$Label 产物缺少 Keyana.exe：$Path"
    }

    $fileVersion = (Get-Item -LiteralPath $exePath).VersionInfo.FileVersion
    if ([string]::IsNullOrWhiteSpace($fileVersion)) {
        throw "$Label 产物没有文件版本信息。"
    }

    $expected = [Version]$version
    $actual = [Version]$fileVersion
    if ($actual.Major -ne $expected.Major -or
        $actual.Minor -ne $expected.Minor -or
        ($expected.Build -ge 0 -and $actual.Build -ne $expected.Build)) {
        throw "$Label 产物版本 $fileVersion 与 csproj 的 $version 不一致。"
    }

    foreach ($required in @(
        "README.md",
        "Data",
        "LICENSE",
        "THIRD_PARTY_NOTICES.md",
        "licenses\Apache-2.0.txt",
        "licenses\dotnet-runtime-LICENSE.txt")) {
        if (-not (Test-Path -LiteralPath (Join-Path $Path $required))) {
            throw "$Label 产物缺少 $required：$Path"
        }
    }
}

# 压缩后再查一次包内容，防止 Compress-Archive 的路径展开和预期不一致。
function Assert-ZipEntries {
    param([Parameter(Mandatory)][string]$ZipPath, [Parameter(Mandatory)][string[]]$Entries)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $names = $archive.Entries | ForEach-Object { $_.FullName }
        foreach ($entry in $Entries) {
            if ($names -notcontains $entry) {
                throw "压缩包 $([System.IO.Path]::GetFileName($ZipPath)) 缺少 $entry。"
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

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

Invoke-DotNet @(
    "publish", $projectPath, "-c", "Release", "-r", "win-x64", "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-o", $fullOutput
)

New-Item -ItemType Directory -Path (Join-Path $fullOutput "Data") -Force | Out-Null
Copy-PackageDocuments -Destination $fullOutput
Assert-PublishOutput -Path $fullOutput -Label "完整版"

Invoke-DotNet @(
    "publish", $projectPath, "-c", "Release", "-r", "win-x64", "--self-contained", "false",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-o", $liteOutput
)

New-Item -ItemType Directory -Path (Join-Path $liteOutput "Data") -Force | Out-Null
Copy-PackageDocuments -Destination $liteOutput
Assert-PublishOutput -Path $liteOutput -Label "精简版"

$fullZip = Join-Path $artifactsRoot "Keyana-V$version-full-win-x64.zip"
$liteZip = Join-Path $artifactsRoot "Keyana-V$version-lite-win-x64.zip"
Compress-Archive -Path (Join-Path $fullOutput "*") -DestinationPath $fullZip -CompressionLevel Optimal -Force
Compress-Archive -Path (Join-Path $liteOutput "*") -DestinationPath $liteZip -CompressionLevel Optimal -Force

Assert-ZipEntries -ZipPath $fullZip -Entries @(
    "Keyana.exe", "README.md", "LICENSE", "THIRD_PARTY_NOTICES.md", "licenses/Apache-2.0.txt")
Assert-ZipEntries -ZipPath $liteZip -Entries @(
    "Keyana.exe", "README.md", "LICENSE", "THIRD_PARTY_NOTICES.md", "licenses/Apache-2.0.txt")

$checksums = @($fullZip, $liteZip) | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($_))"
}
[System.IO.File]::WriteAllLines((Join-Path $artifactsRoot "SHA256SUMS.txt"), $checksums)

Write-Host "发布完成：$artifactsRoot"

