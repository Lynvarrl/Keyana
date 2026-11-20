#Requires -Version 5.1
<#
.SYNOPSIS
    构建并启动 Development 版，用于本地调试。

.DESCRIPTION
    等价于 publish.ps1 -Channel Development：Debug 配置、框架依赖、带 pdb、不打 zip，
    Build Type 为 Development，产物在 artifacts\dev。启动后侧边栏版本号下方会显示
    本次构建的 Build ID，可以直接确认眼前这个进程是不是刚编出来的那个。

    开发版的单实例锁与正式版分开，所以可以一边跑正式版一边调试，不会互相挡住。

.PARAMETER NoLaunch
    只构建，不启动。
#>
param(
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

& (Join-Path $PSScriptRoot "publish.ps1") -Channel Development

if ($NoLaunch) {
    return
}

$exe = Join-Path $repositoryRoot "artifacts\dev\Keyana.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "开发版产物缺失：$exe"
}

Start-Process -FilePath $exe
