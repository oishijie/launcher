using System;
using System.Drawing;
using System.Runtime.Serialization;

namespace launcher.Models
{
    // 单个槽位的数据模型：替代原先散落在多个 Dictionary<PictureBox,...> 中的状态
    [DataContract]
    public class SlotData
    {
        [DataMember] public int Index;          // 槽位序号（稳定身份，替代 pictureBox 控件名）
        [DataMember] public string FilePath = string.Empty; // 文件 / 文件夹 / http(s) 链接；空串表示空槽
        [DataMember] public bool IsFolder;      // 是否为文件夹
        [DataMember] public string IconPath = string.Empty; // 用户自定义图标路径（绝对或相对程序目录）；空=自动
        public Image CachedImage;               // 渲染后的位图（分页缓存）；不参与序列化
        public Image RawIcon;                    // 纯图标位图（不含文字标签，主题无关）；用于主题切换时快速合成

        public bool IsEmpty => string.IsNullOrEmpty(FilePath);

        public SlotData() { }
        public SlotData(int index) { Index = index; FilePath = string.Empty; IsFolder = false; }

        public void DisposeImage()
        {
            if (CachedImage != null) { CachedImage.Dispose(); CachedImage = null; }
            if (RawIcon != null) { RawIcon.Dispose(); RawIcon = null; }
        }
    }
}
