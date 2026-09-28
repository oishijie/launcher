using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace launcher.Core
{
    /// <summary>一条全盘搜索结果。</summary>
    public sealed class EverythingHit
    {
        public string Path;
        public bool IsFolder;
    }

    /// <summary>
    /// Everything 全盘搜索（借鉴亦安 everything_search.rs）。
    ///
    /// 三条降级链，与亦安一致：
    ///   ① SDK（Everything64.dll 的 IPC）—— 最快，无进程启动开销；
    ///   ② 拉起程序目录里捆绑的 Everything（便携、后台托盘），再走 ①；
    ///   ③ es.exe 命令行兜底。
    ///
    /// 全程只在后台线程调用：Everything_QueryW(TRUE) 是阻塞的，而且首次拉起
    /// Everything 还要等它建好索引（最长约 12 秒），绝不能压在 UI 线程上。
    /// </summary>
    internal static class EverythingSearch
    {
        private const string Dll = "Everything64.dll";

        // SDK 错误码（Everything.h）
        private const uint EVERYTHING_ERROR_IPC = 2;

        // 等 Everything 起来 + 建好索引的上限（亦安用 60 × 200ms）
        private const int StartupPollCount = 60;
        private const int StartupPollMs = 200;

        // SDK 的搜索状态是进程级全局的，多个查询并发会互相踩，统一加锁串行化
        private static readonly object Gate = new object();
        private static bool _startAttempted;

        // SDK 一旦确定"这个 DLL 在本进程里永远加载不了"（缺文件 / 位数不符 / 缺导出），
        // 就打上死标记：之后所有查询直接走 es.exe，不再尝试 SDK 那条路。
        //
        // 这不只是快慢问题 —— P/Invoke 抛 BadImageFormatException 要走一遍 CLR 的
        // 程序集加载失败流程，比一次正常调用贵几个数量级，而搜索是"每敲一个字调一次"。
        // 假如此时用户正在输入框里打字，每次按键都要白付这笔开销。
        private static bool _sdkDead;
        private static string _sdkDeadReason;

        /// <summary>SDK 这条路是否还值得尝试（一旦判定不可加载就永久短路）。</summary>
        private static bool SdkUsable { get { return !_sdkDead; } }

        private static void MarkSdkDead(string reason)
        {
            _sdkDead = true;
            _sdkDeadReason = reason;
        }

        // ===== Everything SDK（x64）=====

        [DllImport(Dll, CharSet = CharSet.Unicode, EntryPoint = "Everything_SetSearchW")]
        private static extern void Everything_SetSearch(string lpString);

        [DllImport(Dll, EntryPoint = "Everything_QueryW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Everything_Query([MarshalAs(UnmanagedType.Bool)] bool bWait);

        [DllImport(Dll)]
        private static extern void Everything_SetMax(uint dwMax);

        [DllImport(Dll)]
        private static extern uint Everything_GetNumResults();

        [DllImport(Dll, CharSet = CharSet.Unicode, EntryPoint = "Everything_GetResultFullPathNameW")]
        private static extern void Everything_GetResultFullPathName(uint nIndex, StringBuilder lpString, uint nMaxCount);

        [DllImport(Dll)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Everything_IsDBLoaded();

        [DllImport(Dll)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Everything_IsFolderResult(uint dwIndex);

        [DllImport(Dll)]
        private static extern uint Everything_GetLastError();

        [DllImport(Dll)]
        private static extern void Everything_Reset();

        /// <summary>
        /// 全盘搜索。返回 false 表示三条路都走不通（Everything 没装、也没捆绑、SDK 用不了）。
        /// 必须在后台线程调用。
        /// </summary>
        public static bool Search(string query, int max, out List<EverythingHit> hits, out string error)
        {
            hits = null;
            error = null;
            query = (query ?? string.Empty).Trim();
            if (query.Length == 0) { hits = new List<EverythingHit>(); return true; }
            if (max <= 0) max = 40;

            // ① SDK 直查
            string sdkErr;
            if (TrySearchViaSdk(query, max, out hits, out sdkErr)) return true;

            // ② 拉起捆绑的 Everything，再走 SDK
            string startErr;
            if (TryStartBundledEverything(out startErr))
            {
                if (TrySearchViaSdk(query, max, out hits, out error)) return true;
            }
            else if (!string.IsNullOrEmpty(startErr))
            {
                error = startErr;
            }

            // ③ es.exe 兜底
            string esErr;
            if (TrySearchViaEs(query, max, out hits, out esErr)) return true;

            if (string.IsNullOrEmpty(error)) error = esErr;
            if (string.IsNullOrEmpty(error)) error = sdkErr;
            if (string.IsNullOrEmpty(error)) error = "Everything 不可用";
            return false;
        }

        /// <summary>
        /// 自检（设置面板「检测」按钮）：与真实搜索走**同一条**降级链，把结果说成一句人话。
        /// 返回文本不含前缀，由调用方自行拼接。
        /// 必须在后台线程调用 —— 拉起 Everything 时要等索引，最长约 12 秒。
        /// </summary>
        public static string Probe()
        {
            // 与真实搜索**同一条**降级链，逐级判定：只要有一级能用，就该报"可用"。
            //
            // 这里踩过一次坑：旧实现只要 SDK 那一级不通就直接返回「不可用」，
            // 而实际上 es.exe 那一级完全正常 —— 用户看到"检测说不匹配"，转头搜索却好使，
            // 自检反倒把人带偏了。自检的全部价值就在于"说的是不是实话"，宁可啰嗦也不能骗人。
            List<EverythingHit> hits;
            string sdkErr = null, startErr = null, esErr = null;

            bool ok = TrySearchViaSdk("*", 1, out hits, out sdkErr);
            if (!ok && TryStartBundledEverything(out startErr))
                ok = TrySearchViaSdk("*", 1, out hits, out sdkErr);

            bool viaEs = false;
            if (!ok)
            {
                viaEs = TrySearchViaEs("*", 1, out hits, out esErr);
                ok = viaEs;
            }

            if (!ok)
                return "不可用 —— " + (esErr ?? startErr ?? sdkErr ?? "未知原因");

            // 链路通了，再问一次总条数：让用户看见"索引里到底有没有东西"。
            // 这是最常见的坑 —— Everything 在跑但索引是空的，搜索永远没结果，
            // 而日志里只有一句"结果 0 条"，看不出是没装好还是没建完索引。
            int n = TryGetIndexCount();
            string mode = viaEs ? "命令行模式" : "SDK 模式";
            if (n > 0) return "就绪（" + mode + "）· 已索引 " + n.ToString("N0") + " 项";
            if (n == 0) return "已连接，但索引为空 —— 首次使用请等 Everything 建完索引";
            return "已连接（" + mode + "）· 查询链路正常";
        }

        /// <summary>问 es.exe 要索引总条数（-get-result-count）；失败返回 -1。</summary>
        private static int TryGetIndexCount()
        {
            try
            {
                var es = FindBundledFile("es.exe");
                if (es == null) return -1;

                var psi = new ProcessStartInfo
                {
                    FileName = es,
                    Arguments = "-get-result-count \"*\"",
                    WorkingDirectory = Path.GetDirectoryName(es),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.Default
                };
                using (var p = Process.Start(psi))
                {
                    string line = p.StandardOutput.ReadLine();
                    if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } }
                    int n;
                    if (!string.IsNullOrEmpty(line) && int.TryParse(line.Trim(), out n)) return n;
                }
            }
            catch { }
            return -1;
        }

        private static bool TrySearchViaSdk(string query, int max, out List<EverythingHit> hits, out string error)
        {
            hits = null;
            error = null;

            // 已判定加载不了 → 直接短路，不再让每次按键都白付一次 P/Invoke 失败的代价
            if (_sdkDead) { error = _sdkDeadReason; return false; }

            lock (Gate)
            {
                try
                {
                    Everything_Reset();
                    Everything_SetSearch(query);
                    Everything_SetMax((uint)max);

                    if (!Everything_Query(true))
                    {
                        uint code = Everything_GetLastError();
                        error = code == EVERYTHING_ERROR_IPC
                            ? "Everything 未运行（IPC 连接失败）"
                            : "Everything 查询失败，错误码 " + code;
                        return false;
                    }

                    uint n = Everything_GetNumResults();
                    var list = new List<EverythingHit>((int)Math.Min(n, (uint)max));
                    var sb = new StringBuilder(1024);
                    for (uint i = 0; i < n && list.Count < max; i++)
                    {
                        sb.Length = 0;
                        Everything_GetResultFullPathName(i, sb, (uint)sb.Capacity);
                        string p = sb.ToString();
                        if (string.IsNullOrEmpty(p)) continue;

                        bool isFolder;
                        try { isFolder = Everything_IsFolderResult(i); }
                        catch { isFolder = false; }

                        list.Add(new EverythingHit { Path = p, IsFolder = isFolder });
                    }
                    hits = list;
                    return true;
                }
                catch (DllNotFoundException)
                {
                    MarkSdkDead("缺少 Everything64.dll");
                    error = _sdkDeadReason;
                    return false;
                }
                catch (EntryPointNotFoundException)
                {
                    MarkSdkDead("Everything64.dll 版本过旧（缺少 SDK 导出）");
                    error = _sdkDeadReason;
                    return false;
                }
                catch (BadImageFormatException)
                {
                    // 32 位进程加载 64 位原生库的典型症状。这不是"Everything 不可用"，
                    // 只是"SDK 这条路走不通"—— es.exe 命令行不依赖进程位数，照常工作。
                    // 措辞要短：它会拼在设置面板说明区末行显示，那一行放不下就会折行、
                    // 而说明区高度是按行数给的（超出的部分直接裁掉）。
                    MarkSdkDead("Everything64.dll 是 64 位库，程序是 32 位 → 请改用 64 位编译");
                    error = _sdkDeadReason;
                    return false;
                }
                catch (Exception ex)
                {
                    error = "Everything 查询异常：" + ex.Message;
                    return false;
                }
            }
        }

        /// <summary>
        /// 启动程序目录里捆绑的 Everything（便携模式），并等它把 IPC 准备好。
        /// 只尝试一次，失败后不再反复拉起。
        /// </summary>
        private static bool TryStartBundledEverything(out string error)
        {
            error = null;
            lock (Gate)
            {
                if (_startAttempted) return false;
                _startAttempted = true;
            }

            try
            {
                var exe = FindBundledFile("Everything.exe");
                if (exe == null)
                {
                    error = "程序目录未找到 Everything.exe";
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "-startup",
                    WorkingDirectory = Path.GetDirectoryName(exe),   // 保证读到同目录的 Everything.ini（便携）
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);

                // 等索引就绪。SDK 可用时问 Everything_IsDBLoaded（零开销的 IPC 查询）；
                // SDK 不可用（如位数不符）时改问 es.exe —— 反正后面也只能靠它。
                // 两条路都在等同一件事：服务端把 Everything.db 建完。
                bool viaSdk = SdkUsable;
                for (int i = 0; i < StartupPollCount; i++)
                {
                    System.Threading.Thread.Sleep(viaSdk ? StartupPollMs : StartupPollMs * 2);

                    if (viaSdk)
                    {
                        bool dbLoaded = false;
                        lock (Gate)
                        {
                            try { dbLoaded = Everything_IsDBLoaded(); }
                            catch (Exception ex)
                            {
                                // SDK 在本进程里根本加载不了：标记永久短路，接下来几轮改用 es.exe 探活，
                                // 而不是在这里直接判失败 —— Everything 其实已经起来了，只是没法用 SDK 问它。
                                if (ex is BadImageFormatException)
                                    MarkSdkDead("Everything64.dll 是 64 位库，当前进程是 32 位");
                                viaSdk = false;
                                continue;
                            }
                        }
                        if (dbLoaded) return true;
                    }
                    else if (TryGetIndexCount() >= 0)
                    {
                        return true;   // es.exe 已经能拿到条数 = IPC 通了
                    }
                }
                error = "已启动 Everything，但索引尚未就绪";
                return false;
            }
            catch (Exception ex)
            {
                error = "启动 Everything 失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>es.exe 命令行兜底（stdout 每行一个完整路径）。</summary>
        private static bool TrySearchViaEs(string query, int max, out List<EverythingHit> hits, out string error)
        {
            hits = null;
            error = null;
            try
            {
                var es = FindBundledFile("es.exe");
                if (es == null) { error = "程序目录未找到 es.exe"; return false; }

                var psi = new ProcessStartInfo
                {
                    FileName = es,
                    Arguments = "-n " + max + " -hide-empty-search-results \"" + query.Replace("\"", "") + "\"",
                    WorkingDirectory = Path.GetDirectoryName(es),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.Default   // es.exe 输出跟随系统 ANSI 代码页
                };

                var list = new List<EverythingHit>();
                int exit = 0;
                using (var p = Process.Start(psi))
                {
                    string line;
                    while ((line = p.StandardOutput.ReadLine()) != null && list.Count < max)
                    {
                        line = line.Trim();
                        if (line.Length == 0) continue;
                        bool isFolder = false;
                        try { isFolder = Directory.Exists(line); } catch { }
                        list.Add(new EverythingHit { Path = line, IsFolder = isFolder });
                    }
                    if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } }
                    try { if (p.HasExited) exit = p.ExitCode; } catch { }
                }

                // es.exe 连不上 Everything 时**不往 stdout 写任何东西**，只给一个非 0 退出码。
                // 不检查退出码，就会把"根本没连上"当成"搜索成功、只是没结果" —— 自检骗人的根子。
                if (exit != 0 && list.Count == 0)
                {
                    error = "es.exe 退出码 " + exit + "（Everything 未运行或 IPC 未就绪）";
                    return false;
                }

                hits = list;
                return true;
            }
            catch (Exception ex)
            {
                error = "es.exe 调用失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>在 exe 目录与 exe 目录\everything\ 两处找捆绑文件。</summary>
        private static string FindBundledFile(string fileName)
        {
            try
            {
                string root = AppDomain.CurrentDomain.BaseDirectory;
                string a = Path.Combine(root, fileName);
                if (File.Exists(a)) return a;
                string b = Path.Combine(Path.Combine(root, "everything"), fileName);
                if (File.Exists(b)) return b;
            }
            catch { }
            return null;
        }
    }
}
