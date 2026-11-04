using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GhostReader
{
    /// <summary>
    /// 按配置目录派生身份：单例互斥量与宿主窗口标题都带哈希后缀，
    /// 这样不同 profile（GHOSTREADER_PROFILE 指向不同目录）的实例互不干扰。
    /// 这个文件此前在源码树里缺失，导致工程无法编译，现补回。
    /// </summary>
    internal static class ProfileIdentity
    {
        private static string Key
        {
            get
            {
                string full = Path.GetFullPath(Settings.ConfigDir).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
                return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full))).Substring(0, 16);
            }
        }

        public static string MutexName { get { return "Local\\GhostReader_V3_" + Key; } }
        public static string HostTitle { get { return "GhostReaderHost_" + Key; } }
    }
}
