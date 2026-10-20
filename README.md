# PCL 下载修复疫苗

在**本机**把官方 **PCL 2.99.0.0 正式版**被移除的 Minecraft 下载入口恢复出来的单文件工具。

> 这是第三方独立工具，**与 PCL 官方（龙腾猫跃）无任何关系**。本仓库只包含我们自己的源码，
> **不包含也不分发任何 PCL 二进制文件、修复版 PCL 或 PCL 派生差分数据**。

## 它做什么

官方 PCL 2.99.0.0 通过编译期常量折叠删除了 Minecraft 下载入口（源码里的
`RestrictMcDownload` 被折叠，EXE 中不存在可简单翻转的开关）。本项目：

1. 用 dnlib 对官方 2.99.0.0 EXE 做 6 处 IL 级等价恢复，生成修复版 PCL；
2. 用二进制差分量化的方式生成压缩补丁 `Patch.dat`（约 425 KB）；
3. 用一个小的 WinForms 程序把这个补丁**内嵌**进去，得到单文件
   `PCL下载修复疫苗.exe` —— 用户把 PCL 拖进去即完成修复。

修复内容：

1. 下载页 Loader 恢复（Minecraft 版本列表加载）；
2. 「修改 Minecraft 版本」卡片恢复显示；
3. 手动整合包导入入口恢复；
4. 整合包安装类型分发恢复；
5. 资源页整合包安装按钮恢复；
6. 版本显示改为「PCL 疫苗本地修复版 2.99.0.0」，并替换原「不再提供下载」提示。

**不修改**：购买验证、微软登录、赞助解锁、服务器 URL、下载文件校验。

## 使用方式（用户侧）

1. 完全退出 PCL；
2. 把 `Plain Craft Launcher 2.exe` 拖进疫苗窗口（或点「选择 PCL 启动器…」/ 拖到程序图标上）；
3. 显示绿色「已完成，下载入口已恢复」即完成，原文件旁会生成
   `Plain Craft Launcher 2.exe.疫苗备份.bak`。

只支持官方 2.99.0.0 正式版（原始 SHA256
`6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386`）。
其他版本、被修改过的文件、非 EXE、文件夹、多文件、只读/被占用文件都会被**明确拒绝**且不修改文件。
重复运行已修复的文件会提示「已修复，无需重复处理」。
全程离线，不联网、不写注册表。

## 仓库结构

```
src/          疫苗本体与构建脚本（待构建时必须自备 Patch.dat）
  VaccineCore.cs      差分解析、SHA256 校验、备份、原子替换
  VaccineApp.cs       WinForms 拖入界面
  build-vaccine.ps1   一键构建（系统自带 csc.exe，无需 VS）
tools/        开发期工具
  BuildPatch.cs       对官方 PCL 生成修复版（dnlib）
  MakeDelta.cs        原始 EXE -> 修复版 EXE 的压缩差分生成器
  VerifyRuntime.cs    对修复版做 CLR JIT 检查
tests/        自动化验证（真实 OLE 拖拽、错误输入、备份、占用等）
docs/         构建步骤与验证记录
```

## 构建

本仓库有意**不包含** `Patch.dat`（它由 PCL 二进制派生而来，分发涉及 PCL 的许可与版权）。
完整复现步骤见 **[docs/BUILD.md](docs/BUILD.md)**，概括：

1. 从 PCL 官方 release 获取 2.99.0.0 与 2.14.0.0（其 `最新正式版.zip` 实为 2.13.1.1），
   以及 dnlib 4.5.0（NuGet）；
2. 用 `tools/BuildPatch.cs` 生成修复版 PCL 并做 JIT 检查；
3. 用 `tools/MakeDelta.cs` 生成 `Patch.dat` 放入 `src/`；
4. 运行 `src/build-vaccine.ps1` 得到单文件疫苗 EXE；
5. 用 `tests/test-vaccine.ps1` 做自动化验证（需传入官方原始 EXE）。

构建与测试环境：Windows 10/11 + 自带 .NET Framework 4.x（`csc.exe`），无需 Visual Studio。

## 验证

自动化测试（14 项）与真实端到端下载验证记录见 **[docs/VERIFICATION.md](docs/VERIFICATION.md)**。
摘要：14/14 通过；真实 OLE 拖拽修复成功；修复版 PCL 实际下载并安装 Minecraft 26.3 成功
（5,735 个文件、约 547 MB）；错误输入/只读/占用/损坏文件均被拒绝且原文件不变。

## 版权与许可（重要）

- **本仓库的源代码**（src/tools/tests/docs）采用 MIT License，见 [LICENSE](LICENSE)。
- **PCL 本身的版权归龙腾猫跃所有**，PCL 使用自定义的《PCL 分发有限许可》与
  《PCL 存储库合理使用指南》。本仓库不包含 PCL 二进制或其派生数据；
  你若按 docs/BUILD.md 在本地生成 `Patch.dat` / 修复版 PCL，这些产物**不受本仓库 MIT
  许可覆盖**，使用与分发需自行遵守 PCL 的许可条款（署名、命名、公开源码等要求），
  或取得原作者授权。
- 修复版 PCL 是第三方修改产物，请勿使用 PCL 官方名称/标识暗示官方来源，也请勿传播修改版。
- PCL 移除下载功能是开发者的合规决定；使用本项目恢复该功能属于**你的个人选择**，
  请自行确认所在地区的法律法规要求，作者不对使用后果负责。

## 免责声明

本项目仅供本机学习与个人使用。按 MIT 协议，软件按「原样」提供，不附带任何担保；
因使用或无法使用本软件造成的任何损失由使用者自行承担。
