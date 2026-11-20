param(
    [string]$OutputDirectory = "artifacts",
    # 只构建框架依赖的精简版，跳过自带运行时的完整版。本地调试时省一半时间与磁盘。
    [switch]$LiteOnly,
    # 构建渠道。Release 走 Release 配置，产出 zip 与校验和；
    # Development 走 Debug 配置，只产出一个可直接运行的文件夹到 artifacts\dev，不打 zip。
    [ValidateSet("Release", "Development")]
    [string]$Channel = "Release"
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

    # 版本号允许带预发布后缀（如 1.3.5-dev）：后缀只用于命名与界面显示，
    # FileVersion 里没有它，所以比较时只取数字前缀。
    $expected = [Version](($version -split '-', 2)[0])
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

# 用 .NET 的 ZipFile 而不是 Compress-Archive：Windows PowerShell 5.1 的
# Compress-Archive 会把子目录写成反斜杠（licenses\Apache-2.0.txt），不符合 ZIP
# 规范，部分解压工具会把整个路径当成文件名。ZipFile 写标准正斜杠。
function New-PackageZip {
    param(
        [Parameter(Mandatory)][string]$SourceDirectory,
        [Parameter(Mandatory)][string]$DestinationPath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $DestinationPath) {
        Remove-Item -LiteralPath $DestinationPath -Force
    }

    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $SourceDirectory,
        $DestinationPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)
}

# 压缩后再查一次包内容，防止 ZIP 条目路径与预期不一致。
function Assert-ZipEntries {
    param([Parameter(Mandatory)][string]$ZipPath, [Parameter(Mandatory)][string[]]$Entries)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $names = $archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') }
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
# 必须显式按 UTF-8 读：csproj 是无 BOM 的 UTF-8，Windows PowerShell 5.1 默认按
# 系统 ANSI 代码页解码，中文注释会被拆成乱码，严重时连属性里的引号都被吃掉。
[xml]$projectXml = Get-Content -LiteralPath $projectPath -Encoding UTF8
$version = ($projectXml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "无法从 $projectPath 读取 <Version>；发布脚本需要它来命名产物。"
}

# 渠道决定构建配置：BuildInfo 的 Build Type 就取自配置名（Debug 之外的配置一律按
# Development 处理），所以这里不能再写死在 publish 调用里。
$configuration = if ($Channel -eq "Release") { "Release" } else { "Debug" }

# 正式包不允许带 -dev：它是渠道后缀，由构建配置派生，写进版本号会让 Release 版
# 在界面上显示成 1.3.5-dev，与「Release 只显示数字版本号」直接冲突。
if ($Channel -eq "Release" -and $version -match '(?i)-dev(\.|$)') {
    throw "csproj 的 <Version> 是 $version：带 -dev 的版本只能出 Development 渠道（-Channel Development），不能当作正式包发布。"
}

# Development：只产出一个可直接运行的文件夹，不复制发行文档、不打 zip、不写校验和。
# 这里刻意不启用 PublishSingleFile：调试要的是最新的 exe 和 pdb，单文件会把符号藏起来、
# 拖慢启动，对本地调试只有坏处。
if ($Channel -eq "Development") {
    $devOutput = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "dev"))
    if (-not $devOutput.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理 artifacts 目录以外的路径：$devOutput"
    }

    # dev 版正在运行时 exe 被映射，Remove-Item 会失败在一半；先探一次占用，
    # 给出能直接照做的提示，比让 dotnet publish 抛 MSB3021 好排查。
    $devExe = Join-Path $devOutput "Keyana.exe"
    if (Test-Path -LiteralPath $devExe) {
        try {
            $probe = [System.IO.File]::Open($devExe, 'Open', 'ReadWrite', 'None')
            $probe.Dispose()
        }
        catch {
            throw "开发版正在运行（$devExe 被占用）。请先从托盘退出它，再重新构建。"
        }
    }

    if (Test-Path -LiteralPath $devOutput) {
        # Data 跨构建保留：开发时反复重编，设置、主题和攒下来的统计不该每次都清零。
        # 想要干净环境时手动删掉 artifacts\dev\Data 即可。
        Get-ChildItem -LiteralPath $devOutput -Force |
            Where-Object { $_.Name -ne "Data" } |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
    }

    New-Item -ItemType Directory -Path $devOutput -Force | Out-Null
    Invoke-DotNet @(
        "publish", $projectPath, "-c", $configuration, "-r", "win-x64", "--self-contained", "false",
        "-o", $devOutput
    )

    Write-Host "开发版构建完成：$devOutput"
    return
}

$fullOutput = Join-Path $artifactsRoot "v$version-full"
$liteOutput = Join-Path $artifactsRoot "v$version-lite"

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
$outputsToClean = if ($LiteOnly) { @($liteOutput) } else { @($fullOutput, $liteOutput) }
foreach ($target in $outputsToClean) {
    $resolvedTarget = [System.IO.Path]::GetFullPath($target)
    if (-not $resolvedTarget.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理 artifacts 目录以外的路径：$resolvedTarget"
    }

    if (Test-Path -LiteralPath $resolvedTarget) {
        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
    }
}

if (-not $LiteOnly) {
    Invoke-DotNet @(
        "publish", $projectPath, "-c", $configuration, "-r", "win-x64", "--self-contained", "true",
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
}

Invoke-DotNet @(
    "publish", $projectPath, "-c", $configuration, "-r", "win-x64", "--self-contained", "false",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-o", $liteOutput
)

New-Item -ItemType Directory -Path (Join-Path $liteOutput "Data") -Force | Out-Null
Copy-PackageDocuments -Destination $liteOutput
Assert-PublishOutput -Path $liteOutput -Label "精简版"

$builtZips = @()
$requiredEntries = @(
    "Keyana.exe", "README.md", "LICENSE", "THIRD_PARTY_NOTICES.md", "licenses/Apache-2.0.txt")

if (-not $LiteOnly) {
    $fullZip = Join-Path $artifactsRoot "Keyana-V$version-full-win-x64.zip"
    New-PackageZip -SourceDirectory $fullOutput -DestinationPath $fullZip
    Assert-ZipEntries -ZipPath $fullZip -Entries $requiredEntries
    $builtZips += $fullZip
}

$liteZip = Join-Path $artifactsRoot "Keyana-V$version-lite-win-x64.zip"
New-PackageZip -SourceDirectory $liteOutput -DestinationPath $liteZip
Assert-ZipEntries -ZipPath $liteZip -Entries $requiredEntries
$builtZips += $liteZip

$checksums = $builtZips | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($_))"
}
[System.IO.File]::WriteAllLines((Join-Path $artifactsRoot "SHA256SUMS.txt"), $checksums)

if ($LiteOnly) {
    Write-Host "精简版发布完成：$artifactsRoot"
} else {
    Write-Host "发布完成：$artifactsRoot"
}

