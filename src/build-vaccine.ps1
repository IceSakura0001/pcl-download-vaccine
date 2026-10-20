<#
  构建 PCL下载修复疫苗.exe（单文件 WinForms）

  前置条件：
    1. 本目录（src）中存在 Patch.dat —— 由 tools/BuildPatch.cs + tools/MakeDelta.cs
       配合官方 PCL 2.99.0.0 / 2.14.0.0 生成，步骤见 docs/BUILD.md。
       本仓库出于版权与许可考虑不包含该文件。
    2. 系统装有 .NET Framework 4.x（Windows 自带），使用其 csc.exe 编译，
       不需要 Visual Studio、不需要联网。

  用法示例：
    powershell -ExecutionPolicy Bypass -File build-vaccine.ps1
    powershell -ExecutionPolicy Bypass -File build-vaccine.ps1 -OutFile "D:\out\PCL下载修复疫苗.exe"
#>
param(
    [string]$TaskRoot = $PSScriptRoot,
    [string]$OutFile = ''
)
$ErrorActionPreference = 'Stop'

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fr = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path $csc)) { throw "找不到 .NET Framework 4.x 编译器：$csc" }

$patch = Join-Path $TaskRoot 'Patch.dat'
if (!(Test-Path $patch)) {
    throw "缺少 Patch.dat：$patch`n请先按 docs/BUILD.md 生成差分补丁（本仓库不包含该文件，避免分发 PCL 派生数据）。"
}

if (-not $OutFile) {
    $OutFile = Join-Path (Split-Path -Parent $TaskRoot) 'out\PCL下载修复疫苗.exe'
}
$outDir = Split-Path -Parent $OutFile
if ($outDir -and !(Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

# 1. 生成图标（16~256 多尺寸 ICO）
& $csc /nologo /codepage:65001 /target:exe ('/out:' + $TaskRoot + '\MakeIcon.exe') ('/r:' + $fr + '\System.dll') ('/r:' + $fr + '\System.Drawing.dll') ($TaskRoot + '\MakeIcon.cs')
if ($LASTEXITCODE -ne 0) { throw 'MakeIcon 编译失败' }
& ($TaskRoot + '\MakeIcon.exe') ($TaskRoot + '\vaccine.ico')
if ($LASTEXITCODE -ne 0) { throw '图标生成失败' }

# 2. 编译单文件 GUI EXE，并内嵌 Patch.dat
& $csc /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu `
    ('/out:' + $OutFile) `
    ('/win32icon:' + $TaskRoot + '\vaccine.ico') `
    ('/win32manifest:' + $TaskRoot + '\app.manifest') `
    ('/resource:' + $patch + ',Patch.dat') `
    ('/r:' + $fr + '\System.dll') `
    ('/r:' + $fr + '\System.Core.dll') `
    ('/r:' + $fr + '\System.Drawing.dll') `
    ('/r:' + $fr + '\System.Windows.Forms.dll') `
    ($TaskRoot + '\VaccineCore.cs') ($TaskRoot + '\VaccineApp.cs') ($TaskRoot + '\AssemblyInfo.cs')
if ($LASTEXITCODE -ne 0) { throw '疫苗 GUI 编译失败' }

# 3. 输出摘要
$fi = Get-Item $OutFile
$hash = (Get-FileHash $OutFile -Algorithm SHA256).Hash
Write-Host ('OUTPUT: ' + $fi.FullName)
Write-Host ('SIZE:   ' + $fi.Length)
Write-Host ('SHA256: ' + $hash)
Write-Host ''
Write-Host '下一步：可用 tests/test-vaccine.ps1 做自动化验证（需要官方 PCL 2.99.0.0 原始文件）。'
