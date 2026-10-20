# PCL 社区下载恢复版（第三方修改）

> **⚠️ 防伪提醒**：近期已出现冒充"PCL 恢复下载版"传播**带毒程序**的案例。
> 本项目的可执行文件**仅通过本页 [Releases](https://github.com/bamboos233/PCL/releases) 发布**，
> 请勿从群文件、网盘或任何二次转载渠道下载所谓"同款"。下载后请核对压缩包内的 SHA256 校验值。

这是 [**Plain Craft Launcher（PCL）**](https://github.com/Meloong-Git/PCL)（作者：**龙腾猫跃**）的
**第三方社区修改版**，由 [@bamboos233](https://github.com/bamboos233) 维护，
基于官方 **2.99.0.0** 开源代码。**本仓库不是官方仓库，与官方无关。**

## 为什么存在这个版本

官方 2.99.0.0 [因法律法规原因暂时关闭了 MC 下载功能](https://github.com/Meloong-Git/PCL/releases/tag/2.99.0.0)。
本修改版仅将这一个开关恢复，不改动其他任何功能逻辑。

## 安全性（欢迎自行审计）

| 项目 | 说明 |
|---|---|
| 源码差异 | 相对官方仅改动 **3 个文件**，全部 diff：[与官方 main 的对比](https://github.com/Meloong-Git/PCL/compare/main...bamboos233:PCL:main) |
| 构建方式 | exe 由 GitHub Actions 从本仓库源码**自动构建**，[构建日志公开可查](https://github.com/bamboos233/PCL/actions)，未在任何本机环境手工编译 |
| 构建配置 | `.github/workflows` 与官方**完全一致**（一字未改） |
| 完整性校验 | 每个版本随 Release 公布 SHA256（见压缩包内 使用说明.txt） |
| 网络行为 | 未修改任何网络模块；无遥测、无后门、无额外上报 |

## 修改内容（对应上面 3 个文件）

1. `Modules/ModSecret.vb`：`RestrictMcDownload` 开关恢复为 `False`；内置 CurseForge API Key（资源下载开箱即用）；新增基于 GitHub Releases 的检查更新
2. `FormMain.xaml.vb`：移除首启更新日志中官方"删除下载功能"条目；启动说明弹窗改为社区版说明
3. `PageLoginMs.xaml.vb`：正版登录入口改为提示引导（见下）

## 与官方版的差异

- **已恢复**：游戏本体 / Forge / Fabric / NeoForge 安装、整合包导入与一键安装、修改 MC 版本
- **暂不支持**：微软正版登录（请用官方版 PCL 登录启动，两个启动器可共用同一游戏文件夹）、静默自动更新（关于页可检查更新）、主题切换、百宝箱
- 官方恢复下载功能后，本仓库将停止维护并引导用户回官方版

## 下载

👉 [最新版本 Release](https://github.com/bamboos233/PCL/releases/latest)（首次运行遇 SmartScreen 提示：更多信息 → 仍要运行）

## 许可与致谢

- 本仓库遵循原项目《PCL 分发有限许可》，完整源码公开（即本仓库）
- 原作者：**龙腾猫跃**，赞助入口见启动器【关于】页面或[官方仓库](https://github.com/Meloong-Git/PCL)
- 感谢 PCL 与社区的开源贡献
