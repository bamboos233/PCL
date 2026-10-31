# PCL 下载修复器 v2.2

针对官方 PCL 2.99.0.0 / 2.99.0.1 的下载功能恢复工具（文件级补丁，自动备份 .bak）。

- 用途、原理、注意事项见 Release 附件「PCL下载修复器v2.2-群发版.zip」内的使用说明
- 源码：本目录 `Injector.cs`（核心补丁逻辑）/ `Program.cs`（界面）
- 编译：.NET Framework 4.8 + dnlib（NuGet），`csc /t:winexe /res:dnlib.dll,dnlib.dll Program.cs Injector.cs`
- 三处补丁点基于官方 2.99.0.1 exe 的 IL 实测形态定位，结构特征匹配，小版本更新一般仍适用

仅供学习交流；PCL 作者：龙腾猫跃，请支持原作者。
