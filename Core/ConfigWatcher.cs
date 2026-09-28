using System;
using System.ComponentModel;
using System.IO;

namespace launcher.Core
{
    // 配置文件热更新监听：FileSystemWatcher + 防抖定时器（从 Form1 抽取，单一职责）
    // 监听 launcher.json 变化，触发 Reloaded 事件供 UI 层订阅
    //
    // 【分层修正】防抖定时器原先是 System.Windows.Forms.Timer —— 它把 Core 层焊死在
    // UI 消息循环上（没有消息泵就永远不触发），是不可单测、也不能脱离 WinForms 运行的
    // 分层倒置。现改用 System.Timers.Timer：回调跑在线程池线程，再通过 ISynchronizeInvoke
    // 显式 marshal 回 UI 线程，语义与原来完全一致，但依赖方向倒过来了 —— Core 只依赖
    // ISynchronizeInvoke 这个抽象，不再依赖任何一个具体的 UI 计时器。
    internal sealed class ConfigWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly System.Timers.Timer _debounceTimer;
        private readonly ISynchronizeInvoke _owner;
        private volatile bool _skipNext;

        public event Action Reloaded;

        public ConfigWatcher(ISynchronizeInvoke owner, string baseDir)
        {
            _owner = owner;
            _watcher = new FileSystemWatcher
            {
                Path = baseDir,
                Filter = "launcher.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _watcher.Changed += OnChanged;
            _watcher.Created += OnChanged;
            // 监听 Renamed：部分编辑器（如 VS Code）用「写临时文件 → rename 替换」做原子写，
            // 只触发 Renamed 不触发 Changed，不监听会漏掉热更新。
            _watcher.Renamed += OnChanged;

            // AutoReset = false：一次只响一次，由 OnChanged 重新 Start 实现防抖，
            // 与原 WinForms Timer 的 Stop/Start 写法等价。
            _debounceTimer = new System.Timers.Timer(250) { AutoReset = false };
            _debounceTimer.Elapsed += DebounceTimer_Elapsed;
        }

        // 跳过下一次变更通知（自写盘时调用，避免触发文件监视器的二次重载）
        public bool SkipNext
        {
            get { return _skipNext; }
            set { _skipNext = value; }
        }

        public void Start()
        {
            _watcher.EnableRaisingEvents = true;
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (_skipNext) return;
            try
            {
                // FileSystemWatcher 的回调在线程池线程，System.Timers.Timer 的 Start/Stop 线程安全
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
            catch (ObjectDisposedException) { /* 窗体关闭竞态，忽略 */ }
        }

        private void DebounceTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            try { _debounceTimer.Stop(); } catch (ObjectDisposedException) { return; }
            if (_skipNext) return;

            try
            {
                // 回调在线程池线程，必须回到 UI 线程改控件。
                // 句柄尚未创建（启动瞬间）时 Invoke 会抛，直接放弃这次重载 —— 与旧行为一致，
                // 只是旧代码是靠"WinForms Timer 只在消息循环里跑"来天然规避的。
                if (_owner != null && _owner.InvokeRequired)
                    _owner.Invoke((Action)RaiseReloaded, null);
                else
                    RaiseReloaded();
            }
            catch (Exception ex)
            {
                Logger.Log("配置热更新marshal失败: " + ex.Message);
            }
        }

        private void RaiseReloaded()
        {
            try { Reloaded?.Invoke(); }
            catch (Exception ex) { Logger.Log("配置热更新失败: " + ex); }
        }

        public void Dispose()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnChanged;
                _watcher.Created -= OnChanged;
                _watcher.Renamed -= OnChanged;
                _watcher.Dispose();
            }
            if (_debounceTimer != null)
            {
                _debounceTimer.Elapsed -= DebounceTimer_Elapsed;
                _debounceTimer.Dispose();
            }
        }
    }
}
