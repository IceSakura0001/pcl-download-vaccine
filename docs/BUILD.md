# 构建与复现步骤

本仓库**不包含 PCL 二进制、修复版 PCL 和 `Patch.dat`**（版权与许可原因，见 README）。
需要在本地用 PCL 官方发布文件重新生成，然后构建疫苗 EXE。

## 0. 前置条件

- Windows 10 / 11
- .NET Framework 4.x（Windows 自带，路径通常为
  `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`）
- PowerShell 5.1
- 能访问 GitHub 与 NuGet（仅构建时需要，生成的疫苗 EXE 自身全程离线）

## 1. 准备一个工作目录

```text
work/
  official/Plain Craft Launcher 2.exe     # 官方 PCL 2.99.0.0 正式版
  previous/Plain Craft Launcher 2.exe     # 旧 Release 里的“最新正式版”（实际为 2.13.1.1）
  dnlib.dll                               # dnlib 4.5.0 (net45)
  repo/                                   # 本仓库
```

## 2. 获取官方 PCL 文件

从 PCL 官方 GitHub 仓库对应 tag 下载源码压缩包，解压后使用其中的
`最新正式版.zip` 里的 EXE：

- 2.99.0.0：<https://codeload.github.com/Meloong-Git/PCL/zip/refs/tags/2.99.0.0>
- 2.14.0.0：<https://codeload.github.com/Meloong-Git/PCL/zip/refs/tags/2.14.0.0>

> 注意：2.14.0.0 源码压缩包中的 `最新正式版.zip` 实际装的是 **2.13.1.1**，
> 不是 2.14.0.0。构建只从这个旧二进制取回手动整合包入口与整合包分发尾段；
> 不要对外称“使用了 2.14.0.0 编译的二进制”。

校验 2.99.0.0 原始 EXE（必须完全一致，否则不要继续）：

```text
长度:   5980160
SHA256: 6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386
```

## 3. 获取 dnlib 4.5.0

从 NuGet 下载 `dnlib` 4.5.0（<https://www.nuget.org/packages/dnlib/4.5.0>），
解压 `.nupkg`（本质是 zip），取 `lib/net45/dnlib.dll`。

## 4. 生成修复版 PCL（`PCL-patched.exe`）

```powershell
$repo = '<本仓库路径>'
$work = '<work 路径>'
$csc  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

# 编译补丁生成器
& $csc /nologo /codepage:65001 /out:"$work\BuildPatch.exe" /r:"$work\dnlib.dll" "$repo\tools\BuildPatch.cs"

# 生成修复版（不改动官方原文件）
& "$work\BuildPatch.exe" "$work\official\Plain Craft Launcher 2.exe" "$work\previous\Plain Craft Launcher 2.exe" "$work\PCL-patched.exe"
```

修复内容（BuildPatch.cs 中共 6 处）：

1. `PCL.PageDownloadInstall.LoaderInit()`：恢复 `PCL.ModDownload.DlClientListLoader` 加载；
2. `PCL.PageDownloadModify.LoadInstanceFinished(...)`：恢复「修改 Minecraft 版本」卡片显示；
3. `PCL.ModModpack.ModpackInstall()`：恢复旧版手动整合包导入入口；
4. `PCL.ModModpack.ModpackInstall(string,string,string)`：恢复整合包类型分发尾段（保留新版解析与 finally 清理）；
5. `PCL.PageDownloadResourceDetail.InitializeStackResourceVersions(...)`：为整合包资源恢复安装按钮；
6. 版本显示改为「PCL 疫苗本地修复版 2.99.0.0」，替换内置更新提示。

## 5. JIT 检查（可选但建议）

```powershell
& $csc /nologo /codepage:65001 /out:"$work\VerifyRuntime.exe" "$repo\tools\VerifyRuntime.cs"
& "$work\VerifyRuntime.exe" "$work\PCL-patched.exe"
```

六个关键方法应全部输出 `JIT PASS`（只加载程序集并 JIT 指定方法，不启动主程序）。

## 6. 生成差分补丁 `Patch.dat`

```powershell
& $csc /nologo /codepage:65001 /out:"$work\MakeDelta.exe" "$repo\tools\MakeDelta.cs"
& "$work\MakeDelta.exe" "$work\official\Plain Craft Launcher 2.exe" "$work\PCL-patched.exe" "$repo\src\Patch.dat"
```

本次发布使用的差异数据（供参考）：

```text
Patch.dat 大小     425,348 字节
操作流             419 个 COPY + 417 个 DATA
输出 EXE SHA256    6B97F32C521A2C97FA1CD571BDC463895C4ED4AD4E701E0BAD0863846F6A7658
```

> dnlib 重新写 PE 时可能写入不同时间戳，导致重新生成的 `PCL-patched.exe`
> 哈希与上表不同。此时差分补丁会一并变化，最终以**补丁头里记录的输出哈希**为准。

差分格式（外层 GZip，整数为 .NET BinaryWriter 小端）：

```text
头:  8 字节 ASCII "PVAC0001"
     32 字节 原始 EXE SHA256
     32 字节 输出 EXE SHA256
     Int32  输出总长度
操作: byte 0x00 COPY -> Int32 sourceOffset + Int32 length
      byte 0x01 DATA -> Int32 length + length 字节
      byte 0xFF 结束
```

`VaccineCore.cs` 在应用时会校验：输入 SHA256、每个操作边界、输出长度、输出 SHA256。

## 7. 构建疫苗 EXE

把 `Patch.dat` 放到 `src/` 下（`.gitignore` 已忽略它），然后：

```powershell
powershell -ExecutionPolicy Bypass -File "$repo\src\build-vaccine.ps1" -OutFile "$repo\out\PCL下载修复疫苗.exe"
```

脚本会：生成多尺寸图标 → 用 csc 编译 WinForms → 把 `Patch.dat` 以资源
`Patch.dat` 内嵌 → 输出单文件 EXE（约 475 KB）。

本仓库发布时使用的构建结果（供对照）：

```text
大小     475,136 字节
SHA256   305BA19AE937AA3DC8DEC18CDB74D7D3E2B67107BCCC86DA8606C8B5F1F7C5A7
```

## 8. 自检

```powershell
powershell -ExecutionPolicy Bypass -File "$repo\tests\test-vaccine.ps1" `
    -Vaccine "$repo\out\PCL下载修复疫苗.exe" `
    -Original "$work\official\Plain Craft Launcher 2.exe"
```

见 `docs/VERIFICATION.md`。测试会在 `tests/.test-artifacts/` 生成副本与截图；
测试会短暂启动窗口并模拟真实 OLE 拖拽，请勿在测试期间操作鼠标键盘。

## 9. 常见问题

- **缺少 Patch.dat**：构建脚本会直接报错。按第 6 步在本地生成；仓库不提供该文件。
- **原始 EXE 哈希不符**：确认使用的是官方 2.99.0.0 正式版（不要用改过的文件）。
- **BuildPatch 报 Unsupported input hash**：同上，输入文件不是目标版本。
- **编译报 CS1567**：通常是输出目录不存在，先创建 `out/`。
- **csc 路径不同**：32 位系统用 `Framework` 而不是 `Framework64`。
