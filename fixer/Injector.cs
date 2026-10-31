using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace PclDownloadFix
{
    /// <summary>
    /// v2.2：文件级补丁器。
    ///
    /// 背景：官方 2.99.0.1 的闭源部分会对比「内存中的方法字节」与「磁盘文件字节」，
    /// 不一致则抛出 BadImageFormatException 干扰（实测证实：内存补丁必现、文件补丁正常）。
    /// 因此 v2.2 直接修改磁盘上的 PCL 文件（自动备份为 .bak），保持磁盘与内存一致。
    ///
    /// 三处补丁点（基于官方 2.99.0.1 exe 的 IL 实测形态）：
    ///   1. PageDownloadInstall.LoaderInit：存根构建段 → ldsfld DlClientListLoader + nop
    ///   2. PageDownloadLeft..ctor：隐藏「安装」入口的指令块 → nop 化
    ///   3. PageInstanceModDisabled.BtnDownload_Loaded：ldc.i4.2(Collapsed) → ldc.i4.0(Visible)
    ///
    /// PCL 官方自动更新会还原文件，届时重新运行本程序即可。
    /// </summary>
    internal static class Injector
    {
        private const string PclExeType = "PCL.PageDownloadInstall";
        private const string LoaderInitMethod = "LoaderInit";
        private const string LoaderFieldType = "PCL.ModDownload";
        private const string LoaderFieldName = "DlClientListLoader";
        private const string StubName = "关闭下载";
        private const string DownloadLeftType = "PCL.PageDownloadLeft";
        private const string ModDisabledType = "PCL.PageInstanceModDisabled";
        private const string LoadedMethod = "BtnDownload_Loaded";

        private static string PclVersion = "";

        private sealed class PatchPoint
        {
            internal string Name;      // 补丁点名称（用于汇总显示）
            internal int FileOffset;   // 补丁目标在 PE 文件中的绝对偏移
            internal byte[] Bytes;     // 要写入的字节（原位替换，长度不可超出原区域）
        }

        /// <summary>
        /// 对磁盘上的 PCL 主程序应用补丁。返回结果摘要。
        /// </summary>
        internal static string PatchFile(string pclPath)
        {
            var bytes = File.ReadAllBytes(pclPath);
            var patches = BuildPatches(bytes);

            if (patches.Count == 0)
                return "PCL " + PclVersion + "（未发现需要修复的拦截点，文件未改动）";

            // 备份（仅首次；已有备份则不覆盖最早的原始备份）
            var bak = pclPath + ".bak";
            if (!File.Exists(bak)) File.Copy(pclPath, bak, false);

            var applied = new List<string>();
            foreach (var p in patches)
            {
                Buffer.BlockCopy(p.Bytes, 0, bytes, p.FileOffset, p.Bytes.Length);
                applied.Add(p.Name);
            }
            File.WriteAllBytes(pclPath, bytes);

            return "PCL " + PclVersion + "（已恢复：" + string.Join("、", applied.ToArray()) + "；原文件已备份为 .bak）";
        }

        // ---------------- 补丁计算 ----------------

        /// <summary>
        /// 分析 PCL 主程序，生成全部可应用的补丁点。
        /// 每个补丁点独立定位，不存在则自动跳过（兼容 2.99.0.0 / 2.99.0.1 / 已修复状态）。
        /// </summary>
        private static List<PatchPoint> BuildPatches(byte[] file)
        {
            var result = new List<PatchPoint>();
            ModuleDefMD mod;
            using (var ms = new MemoryStream(file))
                mod = ModuleDefMD.Load(ms);
            PclVersion = mod.Assembly.Version.ToString();

            BuildLoaderInitPatch(mod, file, result);
            BuildCtorPatch(mod, file, result);
            BuildLoadedPatch(mod, file, result);
            return result;
        }

        private static bool MatchLoaderTask(Instruction ins)
        {
            var m = ins.Operand as IMethod;
            return m != null && m.DeclaringType != null && m.DeclaringType.Name.String.StartsWith("LoaderTask");
        }

        private static bool IsBranch(Instruction ins)
        {
            var c = ins.OpCode.Code;
            return c == Code.Br || c == Code.Br_S || c == Code.Brtrue || c == Code.Brtrue_S
                || c == Code.Brfalse || c == Code.Brfalse_S;
        }

        /// <summary>
        /// 补丁 1：把 LoaderInit 中的「关闭下载」存根构建段替换为 ldsfld DlClientListLoader。
        /// 官方 2.99.0.1 实测形态：无条件构建存根（ldstr「关闭下载」+ 闭包缓存 + newobj LoaderTask），
        /// 替换段 → ldsfld(5B) + nop 填充；其后 finish 构造与 PageLoaderInit 调用原样保留。
        /// </summary>
        private static void BuildLoaderInitPatch(ModuleDefMD mod, byte[] file, List<PatchPoint> result)
        {
            try
            {
                var type = mod.Find(PclExeType, false);
                var method = type == null ? null : type.Methods.FirstOrDefault(m => m.Name == LoaderInitMethod);
                if (method == null || method.Body == null) return;

                var ins = method.Body.Instructions;
                int start = -1, end = -1;
                for (int i = 0; i < ins.Count; i++)
                {
                    if (start < 0 && ins[i].OpCode.Code == Code.Ldstr && (ins[i].Operand as string) == StubName) start = i;
                    if (start >= 0 && ins[i].OpCode.Code == Code.Newobj && MatchLoaderTask(ins[i])) { end = i; break; }
                }

                var fieldType = mod.Find(LoaderFieldType, false);
                var field = fieldType == null ? null : fieldType.Fields.FirstOrDefault(f => f.Name == LoaderFieldName);
                if (field == null) return;

                if (start < 0 || end < start) return; // 没有存根：这一版本来就带下载功能

                int rva = (int)(uint)method.RVA;
                int fileOff = RvaToOffset(file, rva);
                if (fileOff < 0) return;

                int firstByte = file[fileOff];
                bool fat = (firstByte & 3) == 3;
                int headerSize = fat ? (((BitConverter.ToUInt16(file, fileOff) >> 12) & 0xF) * 4) : 1;
                int codeSize = fat ? BitConverter.ToInt32(file, fileOff + 4) : (firstByte >> 2);

                int ilStart = fileOff + headerSize;
                var oldIl = new byte[codeSize];
                Buffer.BlockCopy(file, ilStart, oldIl, 0, codeSize);

                // 替换范围：ldstr「关闭下载」起始 → newobj LoaderTask 结束
                int repStart = (int)ins[start].Offset;
                int repEnd = (int)ins[end].Offset + 5; // newobj 共 5 字节
                int repLen = repEnd - repStart;
                if (repLen < 5 || repEnd > codeSize) return;

                // 自检 1：替换段之后的保留区里，不得有分支跳回替换段内部
                for (int i = end + 1; i < ins.Count; i++)
                {
                    if (!IsBranch(ins[i])) continue;
                    var t = ins[i].Operand as Instruction;
                    if (t != null && (int)t.Offset >= repStart && (int)t.Offset < repEnd) return;
                }
                // 自检 2：替换段之前不得有分支（官方为顺序参数准备；若存在条件分支说明形态不同，放弃）
                for (int i = 0; i < start; i++)
                {
                    if (IsBranch(ins[i])) return;
                }

                // 构造替换字节：ldsfld(5B) + nop 填充到原段长度（后续指令偏移不变）
                var rep = new List<byte>(repLen);
                rep.Add(0x7E); // ldsfld
                rep.AddRange(BitConverter.GetBytes(field.MDToken.Raw));
                while (rep.Count < repLen) rep.Add(0x00);

                result.Add(new PatchPoint
                {
                    Name = "下载页版本列表",
                    FileOffset = ilStart + repStart,
                    Bytes = rep.ToArray()
                });
            }
            catch { /* 该补丁点失败不影响其他补丁点 */ }
        }

        /// <summary>
        /// 补丁 2：PageDownloadLeft 构造函数（2.99.0.1 起无条件隐藏「安装」入口）。
        /// 做法：InitializeComponent 调用之后到 ret 之前的全部指令 nop 化（偏移保持）。
        /// 自检：NOP 区必须引用 get_ItemInstall（0.1 特征），防误伤。
        /// </summary>
        private static void BuildCtorPatch(ModuleDefMD mod, byte[] file, List<PatchPoint> result)
        {
            try
            {
                var type = mod.Find(DownloadLeftType, false);
                if (type == null) return;
                var ctor = type.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Body != null);
                if (ctor == null) return;

                var ins = ctor.Body.Instructions;
                var lastRet = ins.LastOrDefault(x => x.OpCode.Code == Code.Ret);
                if (lastRet == null) return;

                int initIdx = -1;
                for (int i = 0; i < ins.Count; i++)
                {
                    var m = ins[i].Operand as IMethod;
                    if (ins[i].OpCode.Code == Code.Call && m != null && m.Name.String == "InitializeComponent") { initIdx = i; break; }
                }
                if (initIdx < 0) return;

                int nopStart = (int)ins[initIdx].Offset + 5; // call 占 5 字节
                int nopEnd = (int)lastRet.Offset;            // NOP 到 ret 之前
                if (nopEnd <= nopStart) return;              // 无隐藏逻辑（如 2.99.0.0），跳过

                // 自检：NOP 区必须引用 get_ItemInstall（确认是 2.99.0.1 的隐藏逻辑，防误伤）
                bool hasItem = false;
                foreach (var x in ins)
                {
                    if (x.Offset < nopStart || x.Offset >= nopEnd) continue;
                    var m = x.Operand as IMethod;
                    if (m != null && m.Name.String == "get_ItemInstall") { hasItem = true; break; }
                }
                if (!hasItem) return;

                // 自检：NOP 区外的分支不得落入 NOP 区（官方 ctor 无分支，防御性检查）
                foreach (var x in ins)
                {
                    if (!IsBranch(x)) continue;
                    var t = x.Operand as Instruction;
                    if (t == null) continue;
                    int to = (int)t.Offset;
                    if (to >= nopStart && to < nopEnd && ((int)x.Offset < nopStart || (int)x.Offset >= nopEnd)) return;
                }

                result.Add(new PatchPoint
                {
                    Name = "下载页安装入口",
                    FileOffset = IlOffsetOf(ctor, file, nopStart),
                    Bytes = new byte[nopEnd - nopStart] // 全 0x00 = nop
                });
            }
            catch { }
        }

        /// <summary>
        /// 补丁 3：PageInstanceModDisabled.BtnDownload_Loaded（2.99.0.1 起强制 Collapsed）。
        /// 官方实测形态：[0] ldc.i4.2（Collapsed）→ 比较后设置可见性。
        /// 做法：ldc.i4.2 → ldc.i4.0（Visible），「下载」按钮恢复显示。
        /// 自检：方法首指令为 ldc.i4.2 + stloc.0 且方法内引用 get_BtnDownload；
        /// 2.99.0.0 的该方法开头是设置求值表达式，不匹配则自动跳过。
        /// </summary>
        private static void BuildLoadedPatch(ModuleDefMD mod, byte[] file, List<PatchPoint> result)
        {
            try
            {
                var type = mod.Find(ModDisabledType, false);
                if (type == null) return;
                var method = type.Methods.FirstOrDefault(m => m.Name == LoadedMethod && m.Body != null);
                if (method == null) return;

                var ins = method.Body.Instructions;
                if (ins.Count < 2) return;
                if (ins[0].OpCode.Code != Code.Ldc_I4_2 || ins[1].OpCode.Code != Code.Stloc_0) return;
                if (!ins.Any(x => { var m = x.Operand as IMethod; return m != null && m.Name.String == "get_BtnDownload"; })) return;

                int fileOff = RvaToOffset(file, (int)(uint)method.RVA);
                if (fileOff < 0) return;
                int firstByte = file[fileOff];
                int headerSize = ((firstByte & 3) == 3) ? (((BitConverter.ToUInt16(file, fileOff) >> 12) & 0xF) * 4) : 1;
                int ilStart = fileOff + headerSize;

                result.Add(new PatchPoint
                {
                    Name = "缺 Mod 页下载按钮",
                    FileOffset = ilStart, // ldc.i4.2 位于 IL_0，即方法体 IL 起点
                    Bytes = new byte[] { 0x16 } // ldc.i4.2 -> ldc.i4.0
                });
            }
            catch { }
        }

        // ---------------- 工具 ----------------

        /// <summary>计算某指令偏移（相对方法体 IL 起点）对应的文件绝对偏移。</summary>
        private static int IlOffsetOf(MethodDef method, byte[] file, int ilOffset)
        {
            int rva = (int)(uint)method.RVA;
            int fileOff = RvaToOffset(file, rva);
            if (fileOff < 0) return -1;
            int firstByte = file[fileOff];
            int headerSize = ((firstByte & 3) == 3) ? (((BitConverter.ToUInt16(file, fileOff) >> 12) & 0xF) * 4) : 1;
            return fileOff + headerSize + ilOffset;
        }

        internal static int RvaToOffset(byte[] pe, int rva)
        {
            int peOff = BitConverter.ToInt32(pe, 0x3C);
            if (peOff <= 0 || peOff + 0x18 > pe.Length) return -1;
            int numSections = BitConverter.ToUInt16(pe, peOff + 6);
            int optSize = BitConverter.ToUInt16(pe, peOff + 20);
            int optOff = peOff + 24;
            int secOff = optOff + optSize;
            for (int i = 0; i < numSections; i++)
            {
                int s = secOff + i * 40;
                if (s + 40 > pe.Length) return -1;
                int va = BitConverter.ToInt32(pe, s + 12);
                int rawSize = BitConverter.ToInt32(pe, s + 16);
                int rawPtr = BitConverter.ToInt32(pe, s + 20);
                if (rva >= va && rva < va + Math.Max(rawSize, 1)) return rawPtr + (rva - va);
            }
            return -1;
        }
    }
}
