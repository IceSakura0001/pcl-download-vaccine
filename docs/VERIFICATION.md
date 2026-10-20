# 验证记录

验证日期：2026-10-20
验证环境：Windows 11（10.0.26200）、.NET Framework 4.8、显示 2560×1600 @200% DPI。

所有测试均针对**实际构建出的疫苗 EXE** 运行，不是开发中间脚本。
测试使用官方 PCL 2.99.0.0 的**副本**，原始文件全程未被修改。

## 1. 自动化测试（14/14 通过）

`tests/test-vaccine.ps1` 的运行结果：

```text
[PASS] vaccine-file-identity     疫苗 EXE 身份与哈希
[PASS] already-patched           重复运行 → 已修复，无需重复处理；无额外备份
[PASS] drop-success              真实 OLE 拖拽 → 修复成功、备份正确、无残留临时文件
[PASS] unsupported-exe           不支持的 EXE → 拒绝且文件未改
[PASS] not-exe                   非 EXE 文本 → 拒绝
[PASS] fake-pe                   文本改名的假 EXE → 拒绝
[PASS] folder                    拖入文件夹 → 拒绝
[PASS] multiple-files            一次拖两个文件 → 拒绝，两个文件均未被修改
[PASS] readonly-file             只读文件 → 提示只读，未改、未生成备份
[PASS] locked-file               文件被其他进程占用 → 提示占用，未改
[PASS] corrupted-file            翻转一个字节 → 输入哈希不符，拒绝，文件未改
[PASS] backup-collision          已有同名备份 → 不覆盖旧备份，新备份哈希正确
[PASS] debug-log-off             默认不写调试日志
[PASS] original-untouched        原始文件 SHA256 未变化
TOTAL: 14  FAILED: 0
```

关键断言：

- 修复后文件 SHA256 必须等于
  `6B97F32C521A2C97FA1CD571BDC463895C4ED4AD4E701E0BAD0863846F6A7658`；
- 备份文件 SHA256 必须等于原始
  `6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386`；
- 所有失败路径都必须保持原文件字节不变，且不残留 `.疫苗临时-*.tmp`。

## 2. 真实拖拽验证（非模拟消息）

自动化测试用一个独立的拖放源进程（`tests/DragSource.cs`）发起 Windows **OLE 拖放**，
与资源管理器里拖文件走完全相同的一条系统路径。结果：

- 拖拽返回 `effect=Copy`；
- 疫苗状态变为「已完成，下载入口已恢复」；
- 被拖入的副本被替换为修复版（SHA256 校验通过），并在同目录生成备份；
- 界面拖入框在拖拽悬停时高亮。

（说明：开发期间也尝试过直接发送 `WM_DROPFILES` 消息做测试，但 HDROP 内存句柄
不能跨进程伪造，因此最终采用真实 OLE 拖放源来验证。）

## 3. 端到端下载验证

把疫苗生成的修复版 PCL 放到**独立测试目录**（独立的 `.minecraft`，不触碰用户原有数据），
实际启动并操作：

1. 主界面正常加载版本信息（请求 `launchermeta.mojang.com` 的版本清单成功）；
2. 进入「下载」页，正式版/预览版/远古版/愚人节版列表完整；
   Forge、Fabric、NeoForge、OptiFine、LiteLoader 列表全部加载 `Finished`；
3. 选中 **26.3** 并点击「开始下载」，真实下载并安装完成。

PCL 日志摘录（时间与路径已做简化）：

```text
[ModNet] 发起网络请求（GET，https://launchermeta.mojang.com/mc/game/version_manifest.json）
[ModDownload] Mojang 官方源加载耗时：662ms，可优先使用官方源
[ModLoader] 加载器 LoaderTask`2 DlClientList Mojang 状态改变：Finished
...
[ModLoader] 加载器 LoaderCombo`1 下载原版 26.3 状态改变：Finished
[ModLoader] 加载器 LoaderTask`2 安装游戏 状态改变：Finished
[ModMain] 弹出提示：26.3 安装成功！
```

安装结果：`assets` 与 `libraries` 共 **5,735 个文件、约 547 MB**。
测试结束后已删除该独立目录，用户原有的 `.minecraft` 未被触碰。

> 端到端测试使用的是与发布版**同一份源码**构建的疫苗程序；
> 两者生成的修复版 PCL 的 SHA256 均为 `6B97F32C…`（逐字节相同），
> 因此结论对发布版同样成立。

## 4. 哈希对照

| 对象 | 大小 | SHA256 |
|---|---|---|
| 官方 PCL 2.99.0.0 原始 EXE | 5,980,160 | `6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386` |
| 修复版 PCL | 6,014,976 | `6B97F32C521A2C97FA1CD571BDC463895C4ED4AD4E701E0BAD0863846F6A7658` |
| 疫苗 EXE（发布构建） | 475,136 | `305BA19AE937AA3DC8DEC18CDB74D7D3E2B67107BCCC86DA8606C8B5F1F7C5A7` |
| Patch.dat | 425,348 | 见差分头内嵌的源/输出哈希 |

## 5. 未验证的部分（如实说明）

- 整合包页面的**实际安装**流程未做端到端验证。补丁已包含手动导入入口、
  整合包类型分发和资源页安装按钮的修复，但本轮只实测了原版游戏下载。
- 修复版 PCL 的皮肤、联机等其他功能未逐一回归。
- 疫苗与修复版 PCL 均为未签名程序，部分安全软件可能误报。
