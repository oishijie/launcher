using System;

namespace launcher.Core
{
    // URL 判断工具：纯字符串逻辑，与 UI 无关（从 IconRenderer 下沉，消除 Core→Controls 反向依赖）。
    public static class UrlUtil
    {
        public static bool IsHttpUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();
            return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }
    }
}
