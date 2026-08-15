using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;

namespace launcher.Core
{
    // 配置文件热更新监听：FileSystemWatcher + 防抖定时器（从 Form1 抽取，单一职责）
    // 监听 launcher.json 变化，触发 Reloaded 事件供 UI 层订阅
    internal sealed class ConfigWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Timer _debounceTimer;
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

            _debounceTimer = new Timer { Interval = 250 };
            _debounceTimer.Tick += DebounceTimer_Tick;
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
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void DebounceTimer_Tick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            if (_skipNext) return;
            if (_owner != null && _owner.InvokeRequired)
                _owner.Invoke((MethodInvoker)RaiseReloaded, null);
            else
                RaiseReloaded();
        }

        private void RaiseReloaded()
        {
            try { Reloaded?.Invoke(); }
            catch (Exception ex) { Logger.Log("配置热更新失败: " + ex); }
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            _debounceTimer?.Dispose();
        }
    }
}
