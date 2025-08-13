using System;

namespace DreamUnrealManager.Contracts.Services
{
    public interface IImageService
    {
        Uri PathToUriOrPlaceholder(string? path);
    }
}