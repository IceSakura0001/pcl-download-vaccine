using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace PclVaccine
{
    enum VaccineErrorKind
    {
        InvalidInput,
        Unsupported,
        AlreadyPatched,
        Locked,
        ReadOnlyFile,
        NoPermission,
        PatchData,
        Internal
    }

    sealed class VaccineException : Exception
    {
        public VaccineErrorKind Kind;
        public VaccineException(VaccineErrorKind kind, string message) : base(message) { Kind = kind; }
        public VaccineException(VaccineErrorKind kind, string message, Exception inner) : base(message, inner) { Kind = kind; }
    }

    sealed class PatchOp
    {
        public bool IsCopy;
        public int SourceOffset;
        public byte[] Data;
        public int Length;
    }

    sealed class DeltaPatch
    {
        public byte[] SourceHash;
        public byte[] OutputHash;
        public int OutputLength;
        public readonly List<PatchOp> Ops = new List<PatchOp>();

        const int MaxLength = 512 * 1024 * 1024;

        public static DeltaPatch Parse(byte[] gzip)
        {
            if (gzip == null || gzip.Length < 16)
                throw new VaccineException(VaccineErrorKind.PatchData, "内置补丁数据为空或已损坏。");

            byte[] raw;
            try
            {
                using (var ms = new MemoryStream(gzip, false))
                using (var gz = new GZipStream(ms, CompressionMode.Decompress))
                {
                    var buffer = new MemoryStream();
                    var chunk = new byte[81920];
                    int n;
                    while ((n = gz.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        buffer.Write(chunk, 0, n);
                        if (buffer.Length > 64 * 1024 * 1024)
                            throw new VaccineException(VaccineErrorKind.PatchData, "内置补丁数据异常膨胀，已拒绝处理。");
                    }
                    raw = buffer.ToArray();
                }
            }
            catch (VaccineException) { throw; }
            catch (Exception ex)
            {
                throw new VaccineException(VaccineErrorKind.PatchData, "内置补丁数据无法解压。", ex);
            }

            using (var ms = new MemoryStream(raw, false))
            using (var br = new BinaryReader(ms))
            {
                var patch = new DeltaPatch();
                var magic = ReadExact(br, 8);
                if (Encoding.ASCII.GetString(magic) != "PVAC0001")
                    throw new VaccineException(VaccineErrorKind.PatchData, "内置补丁数据头无效。");

                patch.SourceHash = ReadExact(br, 32);
                patch.OutputHash = ReadExact(br, 32);
                patch.OutputLength = br.ReadInt32();
                if (patch.OutputLength <= 0 || patch.OutputLength > MaxLength)
                    throw new VaccineException(VaccineErrorKind.PatchData, "内置补丁数据的目标长度无效。");

                bool terminated = false;
                while (ms.Position < ms.Length)
                {
                    byte op = br.ReadByte();
                    if (op == 255) { terminated = true; break; }
                    if (op == 0)
                    {
                        int offset = br.ReadInt32();
                        int length = br.ReadInt32();
                        if (offset < 0 || length < 0 || length > MaxLength)
                            throw new VaccineException(VaccineErrorKind.PatchData, "内置补丁数据包含无效的复制操作。");
                        patch.Ops.Add(new PatchOp { IsCopy = true, SourceOffset = offset, Length = length });
                    }
                    else if (op == 1)
                    {
                        int length = br.ReadInt32();
                        if (length < 0 || length > MaxLength)
                            throw new VaccineException(VaccineErrorKind.PatchData, "内置补丁数据包含无效的数据块。");
                        patch.Ops.Add(new PatchOp { IsCopy = false, Data = ReadExact(br, length), Length = length });
                    }
                    else
                    {
                        throw new VaccineException(VaccineErrorKind.PatchData, "内置补丁数据包含未知操作码。");
                    }
                    if (patch.Ops.Count > 200000)
                        throw new VaccineException(VaccineErrorKind.PatchData, "内置补丁数据操作数量异常。");
                }

                if (!terminated)
                    throw new VaccineException(VaccineErrorKind.PatchData, "内置补丁数据缺少结束标记。");
                if (ms.Position != ms.Length)
                    throw new VaccineException(VaccineErrorKind.PatchData, "内置补丁数据结束标记后存在多余内容。");
                return patch;
            }
        }

        public byte[] Apply(byte[] source)
        {
            if (source == null || source.Length == 0)
                throw new VaccineException(VaccineErrorKind.InvalidInput, "目标文件为空。");

            if (!BytesEqual(Sha256(source), SourceHash))
                throw new VaccineException(VaccineErrorKind.Unsupported,
                    "不支持此文件：它不是经过验证的 PCL 2.99.0.0 官方正式版，或已被其他程序修改。未做任何更改。");

            var output = new byte[OutputLength];
            int pos = 0;
            for (int i = 0; i < Ops.Count; i++)
            {
                var op = Ops[i];
                if (pos + op.Length > OutputLength)
                    throw new VaccineException(VaccineErrorKind.PatchData, "内置补丁数据越界。");
                if (op.IsCopy)
                {
                    if (op.SourceOffset + op.Length > source.Length)
                        throw new VaccineException(VaccineErrorKind.PatchData, "内置补丁数据引用了源文件之外的区域。");
                    Buffer.BlockCopy(source, op.SourceOffset, output, pos, op.Length);
                }
                else
                {
                    Buffer.BlockCopy(op.Data, 0, output, pos, op.Length);
                }
                pos += op.Length;
            }

            if (pos != OutputLength)
                throw new VaccineException(VaccineErrorKind.PatchData, "内置补丁数据重建长度不符。");

            if (!BytesEqual(Sha256(output), OutputHash))
                throw new VaccineException(VaccineErrorKind.PatchData, "补丁重建结果校验失败，已拒绝写入。");

            return output;
        }

        public string OutputHashHex { get { return Hex(OutputHash); } }

        static byte[] ReadExact(BinaryReader br, int count)
        {
            if (count < 0 || count > MaxLength)
                throw new VaccineException(VaccineErrorKind.PatchData, "内置补丁数据长度无效。");
            var data = br.ReadBytes(count);
            if (data.Length != count)
                throw new VaccineException(VaccineErrorKind.PatchData, "内置补丁数据意外结束。");
            return data;
        }

        public static byte[] Sha256(byte[] data)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(data);
        }

        public static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static string Hex(byte[] bytes)
        {
            if (bytes == null) return "";
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("X2"));
            return sb.ToString();
        }
    }

    sealed class PatchOutcome
    {
        public string TargetPath;
        public string BackupPath;
        public bool AlreadyPatched;
        public string OutputSha256;
    }

    static class VaccineCore
    {
        const string ResourceName = "Patch.dat";

        public static DeltaPatch LoadEmbeddedPatch()
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                    throw new VaccineException(VaccineErrorKind.PatchData, "程序内部缺少补丁数据（资源未嵌入）。");
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return DeltaPatch.Parse(ms.ToArray());
                }
            }
        }

        /// <summary>校验并修复一个 PCL 启动器 EXE。失败时抛出 VaccineException，且不会破坏原文件。</summary>
        public static PatchOutcome PatchFile(string path, DeltaPatch patch, Action<string> log)
        {
            if (log == null) log = delegate { };
            if (string.IsNullOrEmpty(path))
                throw new VaccineException(VaccineErrorKind.InvalidInput, "没有收到文件路径。");

            path = Path.GetFullPath(path);
            if (Directory.Exists(path))
                throw new VaccineException(VaccineErrorKind.InvalidInput, "拖入的是文件夹，请拖入 PCL 启动器 .exe 文件。");
            if (!File.Exists(path))
                throw new VaccineException(VaccineErrorKind.InvalidInput, "找不到该文件：" + path);

            string name = Path.GetFileName(path);
            string ext = Path.GetExtension(path);
            if (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
                throw new VaccineException(VaccineErrorKind.InvalidInput, "请拖入 PCL 启动器 .exe 本身，而不是快捷方式。");
            if (!string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase))
                throw new VaccineException(VaccineErrorKind.InvalidInput, "这不是 .exe 文件：" + name + "\n请拖入 PCL 启动器（Plain Craft Launcher 2.exe）。");

            byte[] source;
            try
            {
                source = File.ReadAllBytes(path);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new VaccineException(VaccineErrorKind.NoPermission, "没有权限读取该文件。", ex);
            }
            catch (IOException ex)
            {
                throw new VaccineException(VaccineErrorKind.Locked, "文件正被占用，无法读取（请先完全关闭 PCL）。", ex);
            }

            if (source.Length < 1024 || source[0] != (byte)'M' || source[1] != (byte)'Z')
                throw new VaccineException(VaccineErrorKind.InvalidInput, "这不是有效的 Windows 可执行文件。");

            log("已读取：" + name + "（" + source.Length.ToString("N0") + " 字节）");

            var inputHash = DeltaPatch.Sha256(source);
            var inputHashHex = DeltaPatch.Hex(inputHash);

            if (DeltaPatch.BytesEqual(inputHash, patch.OutputHash))
            {
                log("检测到该文件已是修复版。");
                return new PatchOutcome
                {
                    TargetPath = path,
                    AlreadyPatched = true,
                    OutputSha256 = inputHashHex
                };
            }

            if (!DeltaPatch.BytesEqual(inputHash, patch.SourceHash))
            {
                throw new VaccineException(VaccineErrorKind.Unsupported,
                    "不支持此文件：SHA256 与官方 2.99.0.0 正式版不符。\n" +
                    "可能是其他版本、已被修改或不是原始 PCL 启动器。未做任何更改。\n\n" +
                    "文件 SHA256：" + inputHashHex);
            }

            log("版本校验通过：官方 PCL 2.99.0.0（SHA256 匹配）。");

            EnsureReplaceable(path);

            byte[] output = patch.Apply(source);
            log("已重建修复版内容（" + output.Length.ToString("N0") + " 字节，SHA256 校验通过）。");

            string backup = CreateBackup(path, inputHash);
            log("已备份原文件：" + Path.GetFileName(backup));

            string temp = Path.Combine(Path.GetDirectoryName(path),
                "." + name + ".疫苗临时-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");

            try
            {
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.Write(output, 0, output.Length);
                    fs.Flush(true);
                }

                var tempBytes = File.ReadAllBytes(temp);
                if (!DeltaPatch.BytesEqual(DeltaPatch.Sha256(tempBytes), patch.OutputHash))
                    throw new VaccineException(VaccineErrorKind.Internal, "临时文件写入校验失败，已取消替换。");

                log("临时文件写入并校验完成。");

                try
                {
                    File.Replace(temp, path, null);
                }
                catch (Exception ex)
                {
                    if (ex is UnauthorizedAccessException)
                        throw new VaccineException(VaccineErrorKind.Locked,
                            "无法替换文件（可能仍被占用或没有写权限）。原文件与备份均保持原样。", ex);
                    throw new VaccineException(VaccineErrorKind.Internal,
                        "替换文件失败：" + ex.Message + "\n原文件与备份均保持原样。", ex);
                }

                var finalBytes = File.ReadAllBytes(path);
                if (!DeltaPatch.BytesEqual(DeltaPatch.Sha256(finalBytes), patch.OutputHash))
                {
                    TryRestore(path, backup, inputHash);
                    throw new VaccineException(VaccineErrorKind.Internal,
                        "替换后校验未通过，已尝试用备份恢复原文件。", null);
                }

                log("替换完成，最终文件 SHA256 校验通过。");
                return new PatchOutcome
                {
                    TargetPath = path,
                    BackupPath = backup,
                    AlreadyPatched = false,
                    OutputSha256 = DeltaPatch.Hex(DeltaPatch.Sha256(finalBytes))
                };
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch { }
            }
        }

        static void EnsureReplaceable(string path)
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                throw new VaccineException(VaccineErrorKind.ReadOnlyFile,
                    "该文件是只读的，无法替换。\n请右键属性取消“只读”后重试。未做任何更改。");

            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new VaccineException(VaccineErrorKind.NoPermission,
                    "没有写入权限，无法替换该文件。未做任何更改。", ex);
            }
            catch (IOException)
            {
                throw new VaccineException(VaccineErrorKind.Locked,
                    "文件正被其他程序占用（PCL 可能仍在运行）。\n请完全退出 PCL 后再试。未做任何更改。");
            }
        }

        static string CreateBackup(string path, byte[] expectedHash)
        {
            string baseBackup = path + ".疫苗备份.bak";
            string backup = baseBackup;
            if (File.Exists(backup))
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                backup = path + ".疫苗备份-" + stamp + ".bak";
                int i = 1;
                while (File.Exists(backup))
                {
                    backup = path + ".疫苗备份-" + stamp + "-" + i.ToString() + ".bak";
                    i++;
                    if (i > 999) throw new VaccineException(VaccineErrorKind.Internal, "无法创建备份文件名。");
                }
            }

            try
            {
                File.Copy(path, backup, false);
            }
            catch (Exception ex)
            {
                throw new VaccineException(VaccineErrorKind.NoPermission,
                    "无法创建备份文件，已取消修复。未做任何更改。\n" + ex.Message, ex);
            }

            var backupBytes = File.ReadAllBytes(backup);
            if (!DeltaPatch.BytesEqual(DeltaPatch.Sha256(backupBytes), expectedHash))
            {
                try { File.Delete(backup); } catch { }
                throw new VaccineException(VaccineErrorKind.Internal, "备份文件校验失败，已取消修复。未做任何更改。");
            }
            return backup;
        }

        static void TryRestore(string path, string backup, byte[] expectedHash)
        {
            try
            {
                if (!File.Exists(backup)) return;
                var backupBytes = File.ReadAllBytes(backup);
                if (!DeltaPatch.BytesEqual(DeltaPatch.Sha256(backupBytes), expectedHash)) return;
                File.Copy(backup, path, true);
            }
            catch { }
        }
    }
}
