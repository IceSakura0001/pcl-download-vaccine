# 自动化验证脚本（开发/发布前自检用）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File test-vaccine.ps1 `
#       -Vaccine "..\out\PCL下载修复疫苗.exe" `
#       -Original "C:\path\to\Plain Craft Launcher 2.exe"
#
# 说明：
#   - Original 必须是官方 PCL 2.99.0.0 正式版原始文件（SHA256 见 README）。
#   - 会在本目录生成 .test-artifacts/（测试副本、截图、结果 JSON），可随时删除。
#   - 会启动疫苗窗口和测试用拖放源窗口，测试期间请勿操作鼠标键盘。

param(
    [string]$Vaccine = '',
    [string]$Original = '',
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [int]$WinY = 80
)
$ErrorActionPreference = 'Stop'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fr = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'

if (-not $Vaccine) { $Vaccine = Join-Path $RepoRoot 'out\PCL下载修复疫苗.exe' }
if (-not $Original -or !(Test-Path $Original)) {
    throw "请用 -Original 指定官方 PCL 2.99.0.0 原始 EXE 路径。"
}
if (!(Test-Path $Vaccine)) { throw "找不到疫苗 EXE：$Vaccine（先运行 src\build-vaccine.ps1）" }

$uiTool = Join-Path $PSScriptRoot 'UiTool.exe'
$dragSource = Join-Path $PSScriptRoot 'DragSource.exe'
$work = Join-Path $PSScriptRoot '.test-artifacts'
$origHash = (Get-FileHash $Original -Algorithm SHA256).Hash
$outHash = '6B97F32C521A2C97FA1CD571BDC463895C4ED4AD4E701E0BAD0863846F6A7658'
$tempLog = Join-Path $env:TEMP 'pcl-vaccine-debug.log'

# ---------- 编译测试辅助工具 ----------
if (!(Test-Path $uiTool)) {
    & $csc /nologo /codepage:65001 /target:exe ('/out:' + $uiTool) ('/r:' + $fr + '\System.dll') ('/r:' + $fr + '\System.Core.dll') ('/r:' + $fr + '\System.Drawing.dll') ('/r:' + $fr + '\System.Windows.Forms.dll') ('/r:' + $fr + '\WPF\WindowsBase.dll') ('/r:' + $fr + '\WPF\UIAutomationClient.dll') ('/r:' + $fr + '\WPF\UIAutomationTypes.dll') (Join-Path $PSScriptRoot 'UiTool.cs')
    if ($LASTEXITCODE -ne 0) { throw 'UiTool 编译失败' }
}
if (!(Test-Path $dragSource)) {
    & $csc /nologo /codepage:65001 /target:winexe ('/out:' + $dragSource) ('/r:' + $fr + '\System.dll') ('/r:' + $fr + '\System.Drawing.dll') ('/r:' + $fr + '\System.Windows.Forms.dll') (Join-Path $PSScriptRoot 'DragSource.cs')
    if ($LASTEXITCODE -ne 0) { throw 'DragSource 编译失败' }
}

if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null

$screenLine = (& $uiTool screen | Select-String '^screen=(\d+)x(\d+)').Matches
$screenW = if ($screenLine.Count -gt 0) { [int]$screenLine[0].Groups[1].Value } else { 1920 }
$winX = [Math]::Max(0, $screenW - 1230)

$script:results = New-Object System.Collections.ArrayList
$script:failures = 0
$script:seq = 0

function Kill-Gui {
    Get-Process -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            if ($_.Path -eq $Vaccine -or $_.ProcessName -eq 'DragSource') { $_.Kill() }
        } catch { }
    }
    Start-Sleep -Milliseconds 350
}

function Start-Vaccine([string[]]$Arguments) {
    if ($Arguments -and $Arguments.Count -gt 0) {
        $quoted = $Arguments | ForEach-Object { '"' + $_ + '"' }
        return Start-Process -FilePath $Vaccine -ArgumentList ($quoted -join ' ') -PassThru
    }
    return Start-Process -FilePath $Vaccine -PassThru
}

function Get-UiText([int]$ProcId) {
    $script:seq++
    $dump = Join-Path $work ('dump-' + $ProcId + '-' + $script:seq + '.txt')
    & $uiTool dump $ProcId $dump 600 | Out-Null
    if (Test-Path $dump) { return (Get-Content $dump -Raw -Encoding UTF8) }
    return ''
}

function Wait-UiText([int]$ProcId, [string]$Pattern, [int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $text = ''
    while ((Get-Date) -lt $deadline) {
        $text = Get-UiText $ProcId
        if ($text -match [regex]::Escape($Pattern)) { return $text }
        Start-Sleep -Milliseconds 600
    }
    return $text
}

function Fresh-Copy([string]$Name) {
    $dir = Join-Path $work $Name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $dst = Join-Path $dir 'Plain Craft Launcher 2.exe'
    Copy-Item $Original $dst -Force
    return $dst
}

function Get-LeftoverTemp([string]$Dir) {
    return @(Get-ChildItem $Dir -Force -Filter '.疫苗临时-*.tmp' -ErrorAction SilentlyContinue)
}

function Add-Case([string]$Name, [bool]$Pass, [string]$Detail, [string]$Shot) {
    if (-not $Pass) { $script:failures++ }
    [void]$script:results.Add([pscustomobject]@{ Case = $Name; Pass = $Pass; Detail = $Detail; Shot = $Shot })
    $tag = if ($Pass) { 'PASS' } else { 'FAIL' }
    Write-Host ('[' + $tag + '] ' + $Name + ' :: ' + $Detail)
}

function Complete-Case {
    param([string]$Name, $Proc, [string]$Expect, [scriptblock]$Verify, [int]$TimeoutSec = 25)
    $text = Wait-UiText $Proc.Id $Expect $TimeoutSec
    $shot = Join-Path $work ($Name + '.png')
    try { & $uiTool shot $Proc.Id $shot | Out-Null } catch { }
    $pass = $text -match [regex]::Escape($Expect)
    $detail = ''
    if ($Verify) {
        $verifyResult = & $Verify
        if ($verifyResult) { $pass = $false; $detail = [string]$verifyResult }
    }
    if (-not $detail) { $detail = "status contains '$Expect'" }
    Kill-Gui
    Add-Case $Name $pass $detail $shot
}

function Get-DropCenter([int]$VaccinePid) {
    $dump = Join-Path $work ('cdump-' + $VaccinePid + '.txt')
    & $uiTool dump $VaccinePid $dump 400 | Out-Null
    foreach ($line in (Get-Content $dump -Encoding UTF8)) {
        if ($line -match 'ControlType\.Pane.*rect=(-?\d+),(-?\d+),(\d+),(\d+)') {
            $c = [pscustomobject]@{ x = [int]$matches[1]; y = [int]$matches[2]; w = [int]$matches[3]; h = [int]$matches[4] }
            if ($c.h -ge 250 -and $c.h -le 500 -and $c.w -ge 700) {
                return @([int]($c.x + $c.w / 2), [int]($c.y + $c.h / 2))
            }
        }
    }
    throw '未找到拖放区面板'
}

function Get-DragSourceCenter {
    $geom = Join-Path $env:TEMP 'dragsource.geom'
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $geom) {
            $parts = (Get-Content $geom -Encoding UTF8)[0].Split(',')
            return @([int]$parts[0], [int]$parts[1])
        }
        Start-Sleep -Milliseconds 300
    }
    throw '拖放源窗口未就绪'
}

function Invoke-DragCase {
    param([string]$Name, [string[]]$Files, [string]$Expect, [scriptblock]$Verify, [int]$TimeoutSec = 25)
    Kill-Gui
    Remove-Item (Join-Path $env:TEMP 'dragsource.geom') -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $env:TEMP 'dragsource.log') -ErrorAction SilentlyContinue
    $proc = Start-Vaccine @()
    [void](Wait-UiText $proc.Id '等待拖入' 20)
    & $uiTool movewin $proc.Id $winX $WinY | Out-Null
    & $uiTool top $proc.Id | Out-Null
    $drop = Get-DropCenter $proc.Id
    $quoted = $Files | ForEach-Object { '"' + $_ + '"' }
    $src = Start-Process -FilePath $dragSource -ArgumentList ($quoted -join ' ') -PassThru
    $start = Get-DragSourceCenter
    & $uiTool top $src.Id | Out-Null
    Start-Sleep -Milliseconds 400
    & $uiTool drag $start[0] $start[1] $drop[0] $drop[1] 40 | Out-Null
    Complete-Case $Name $proc $Expect $Verify $TimeoutSec
    Get-Process -Name DragSource -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
    $dragLog = Get-Content (Join-Path $env:TEMP 'dragsource.log') -ErrorAction SilentlyContinue
    if ($dragLog -and $dragLog -notmatch 'effect=Copy') { Add-Case ($Name + '-drag-effect') $false ('unexpected: ' + ($dragLog -join '; ')) $null }
}

Write-Host "Vaccine:  $Vaccine"
Write-Host "Original: $Original ($origHash)"
Write-Host ('Vaccine SHA256: ' + (Get-FileHash $Vaccine -Algorithm SHA256).Hash)
Write-Host ''

Remove-Item $tempLog -ErrorAction SilentlyContinue

# ---------- 0. 疫苗 EXE 身份 ----------
$vaccineHash = (Get-FileHash $Vaccine -Algorithm SHA256).Hash
Add-Case 'vaccine-file-identity' ($vaccineHash -ne $origHash -and (Test-Path $Vaccine)) ('sha256=' + $vaccineHash) $null

# ---------- 1. 重复运行：已修复 ----------
$already = Fresh-Copy 'already-base'
$proc = Start-Vaccine @($already)
[void](Wait-UiText $proc.Id '已完成' 25)
Kill-Gui
$proc = Start-Vaccine @($already)
Complete-Case 'already-patched' $proc '已修复，无需重复处理' {
    if ((Get-FileHash $already -Algorithm SHA256).Hash -ne $outHash) { '文件哈希被意外改变' }
    elseif (@(Get-ChildItem (Split-Path $already) -Filter '*.bak').Count -ne 1) { '产生了多余备份' }
    else { '' }
}

# ---------- 2. 真实 OLE 拖拽修复 ----------
$dropTarget = Fresh-Copy 'drop-success'
Invoke-DragCase 'drop-success' @($dropTarget) '已完成，下载入口已恢复' {
    if ((Get-FileHash $dropTarget -Algorithm SHA256).Hash -ne $outHash) { '修复后哈希不符' }
    elseif ((Get-FileHash ($dropTarget + '.疫苗备份.bak') -Algorithm SHA256).Hash -ne $origHash) { '备份哈希不符' }
    elseif ((Get-LeftoverTemp (Split-Path $dropTarget)).Count -ne 0) { '残留临时文件' }
    else { '' }
}

# ---------- 3. 不支持的 EXE ----------
$proc = Start-Vaccine @((Join-Path $PSScriptRoot 'UiTool.exe'))
Complete-Case 'unsupported-exe' $proc '未完成' {
    $t = Get-UiText $proc.Id
    if ($t -notmatch '不支持此文件') { '缺少不支持文件提示' } else { '' }
}

# ---------- 4. 非 EXE 文件 ----------
$txt = Join-Path $work 'not-an-exe.txt'
Set-Content -Path $txt -Value 'hello' -Encoding UTF8
$proc = Start-Vaccine @($txt)
Complete-Case 'not-exe' $proc '未完成' {
    $t = Get-UiText $proc.Id
    if ($t -notmatch '这不是 \.exe 文件') { '缺少非 EXE 提示' } else { '' }
}

# ---------- 5. 伪 EXE ----------
$fake = Join-Path $work 'fake.exe'
Set-Content -Path $fake -Value 'not a real executable' -Encoding ASCII
$proc = Start-Vaccine @($fake)
Complete-Case 'fake-pe' $proc '未完成' {
    $t = Get-UiText $proc.Id
    if ($t -notmatch '这不是有效的 Windows 可执行文件') { '缺少无效 PE 提示' } else { '' }
}

# ---------- 6. 文件夹 ----------
$folder = Join-Path $work 'a-folder'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$proc = Start-Vaccine @($folder)
Complete-Case 'folder' $proc '拖入的是文件夹' {
    $t = Get-UiText $proc.Id
    if ($t -notmatch '文件夹') { '缺少文件夹提示' } else { '' }
}

# ---------- 7. 多文件拖拽 ----------
$m1 = Fresh-Copy 'multi-1'
$m2 = Fresh-Copy 'multi-2'
Invoke-DragCase 'multiple-files' @($m1, $m2) '一次只能处理一个文件' {
    if ((Get-FileHash $m1 -Algorithm SHA256).Hash -ne $origHash) { '第一个文件被修改' }
    elseif ((Get-FileHash $m2 -Algorithm SHA256).Hash -ne $origHash) { '第二个文件被修改' }
    else { '' }
}

# ---------- 8. 只读文件 ----------
$ro = Fresh-Copy 'readonly'
(Get-Item $ro).IsReadOnly = $true
$proc = Start-Vaccine @($ro)
Complete-Case 'readonly-file' $proc '未完成' {
    $t = Get-UiText $proc.Id
    $bad = ''
    if ($t -notmatch '只读') { $bad = '缺少只读提示' }
    elseif ((Get-FileHash $ro -Algorithm SHA256).Hash -ne $origHash) { $bad = '只读文件被修改' }
    elseif (Test-Path ($ro + '.疫苗备份.bak')) { $bad = '为只读文件创建了备份' }
    (Get-Item $ro).IsReadOnly = $false
    $bad
}

# ---------- 9. 文件被占用 ----------
$locked = Fresh-Copy 'locked'
$lockJob = Start-Job -ScriptBlock {
    param($path)
    $fs = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    Start-Sleep -Seconds 60
    $fs.Close()
} -ArgumentList $locked
Start-Sleep -Milliseconds 1200
$proc = Start-Vaccine @($locked)
Complete-Case 'locked-file' $proc '未完成' {
    $t = Get-UiText $proc.Id
    $bad = ''
    if ($t -notmatch '占用') { $bad = '缺少占用提示' }
    elseif ((Get-FileHash $locked -Algorithm SHA256).Hash -ne $origHash) { $bad = '被占用文件被修改' }
    $bad
}
Stop-Job $lockJob -ErrorAction SilentlyContinue
Remove-Job $lockJob -Force -ErrorAction SilentlyContinue

# ---------- 10. 损坏文件 ----------
$corrupt = Fresh-Copy 'corrupt'
$bytes = [System.IO.File]::ReadAllBytes($corrupt)
$bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 0xFF
[System.IO.File]::WriteAllBytes($corrupt, $bytes)
$corruptHash = (Get-FileHash $corrupt -Algorithm SHA256).Hash
$proc = Start-Vaccine @($corrupt)
Complete-Case 'corrupted-file' $proc '未完成' {
    if ((Get-FileHash $corrupt -Algorithm SHA256).Hash -ne $corruptHash) { '损坏文件被修改' } else { '' }
}

# ---------- 11. 备份重名 ----------
$collision = Fresh-Copy 'backup-collision'
$dummy = $collision + '.疫苗备份.bak'
Set-Content -Path $dummy -Value 'dummy backup that must not be overwritten' -Encoding UTF8
$proc = Start-Vaccine @($collision)
Complete-Case 'backup-collision' $proc '已完成，下载入口已恢复' {
    $bad = ''
    $newBackups = @(Get-ChildItem (Split-Path $collision) -Filter 'Plain Craft Launcher 2.exe.疫苗备份-*.bak')
    if ((Get-FileHash $collision -Algorithm SHA256).Hash -ne $outHash) { $bad = '目标未修复' }
    elseif ((Get-Content $dummy -Raw).Trim() -ne 'dummy backup that must not be overwritten') { $bad = '旧备份被覆盖' }
    elseif ($newBackups.Count -ne 1) { $bad = '应恰好生成一个带时间戳备份，实际 ' + $newBackups.Count }
    elseif ((Get-FileHash $newBackups[0].FullName -Algorithm SHA256).Hash -ne $origHash) { $bad = '时间戳备份哈希不符' }
    $bad
}

# ---------- 12. 默认不写调试日志 ----------
Add-Case 'debug-log-off' (-not (Test-Path $tempLog)) '未设置 PCLVACCINE_DEBUG 时不写调试日志' $null

# ---------- 13. 原始文件未被改动 ----------
Add-Case 'original-untouched' ((Get-FileHash $Original -Algorithm SHA256).Hash -eq $origHash) '原始文件 SHA256 未变化' $null

Kill-Gui
$summaryPath = Join-Path $work 'results.txt'
$jsonPath = Join-Path $work 'results.json'
$script:results | Format-Table -AutoSize | Out-String | Set-Content $summaryPath -Encoding UTF8
$script:results | ConvertTo-Json | Set-Content $jsonPath -Encoding UTF8
Write-Host ''
Write-Host ('TOTAL: ' + $script:results.Count + '  FAILED: ' + $script:failures)
Write-Host ('results: ' + $summaryPath)
if ($script:failures -gt 0) { exit 1 } else { exit 0 }
