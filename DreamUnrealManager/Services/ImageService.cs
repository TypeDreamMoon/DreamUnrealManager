using System;
using System.IO;
using DreamUnrealManager.Contracts.Services;

namespace DreamUnrealManager.Services
{
    public class ImageService : IImageService
    {
        private readonly Uri _placeholderUri;

        public ImageService()
        {
            // 注意这里用相对路径时 WinUI3 需要 ms-appx 或 ms-appdata 前缀
            _placeholderUri = new Uri("ms-appx:///Assets/MdiUnreal.png");
        }

        public Uri PathToUriOrPlaceholder(string? path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    return new Uri(path, UriKind.Absolute);
                }
            }
            catch { /* ignore */ }

            return _placeholderUri;
        }
    }
}