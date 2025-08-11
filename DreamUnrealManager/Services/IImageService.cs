using System;

namespace DreamUnrealManager.Services
{
    public interface IImageService
    {
        Uri PathToUriOrPlaceholder(string? path);
    }
}