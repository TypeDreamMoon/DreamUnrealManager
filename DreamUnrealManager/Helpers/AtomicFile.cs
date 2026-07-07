using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DreamUnrealManager.Helpers
{
    /// <summary>
    /// 原子写文件：先写入同目录的唯一临时文件，再用 File.Replace/File.Move 原子替换目标文件。
    /// 避免写入过程中崩溃或掉电把目标文件截断为半截 JSON，从而丢失数据；
    /// 每次调用使用唯一临时名，避免并发写入同一文件时互相破坏。
    /// </summary>
    public static class AtomicFile
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        public static void WriteAllText(string path, string content, string? backupPath = null)
        {
            var temp = PrepareTemp(path);
            try
            {
                File.WriteAllText(temp, content, Utf8NoBom);
                Commit(temp, path, backupPath);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }

        public static async Task WriteAllTextAsync(string path, string content, string? backupPath = null)
        {
            var temp = PrepareTemp(path);
            try
            {
                await File.WriteAllTextAsync(temp, content, Utf8NoBom).ConfigureAwait(false);
                Commit(temp, path, backupPath);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }

        private static string PrepareTemp(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 唯一临时名（同目录，保证同卷），避免并发写入共用同一临时文件。
            return path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        }

        private static void Commit(string temp, string path, string? backupPath)
        {
            if (File.Exists(path))
            {
                try
                {
                    // NTFS 上原子替换；backupPath 非空时旧文件会被原子地移动为备份。
                    File.Replace(temp, path, backupPath, ignoreMetadataErrors: true);
                }
                catch (Exception)
                {
                    // 某些文件系统/占用场景下 File.Replace 会失败，回退为覆盖移动（非原子，但仍优于残留临时文件）。
                    File.Move(temp, path, overwrite: true);
                }
            }
            else
            {
                // 目标尚不存在；overwrite 兜住“检查后被并发创建”的竞态。
                File.Move(temp, path, overwrite: true);
            }
        }

        private static void TryDelete(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // 忽略清理失败
            }
        }
    }
}
