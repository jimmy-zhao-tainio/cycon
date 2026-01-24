using System;

namespace Cycon.Host.Thumbnails;

public interface IThumbnailService
{
    bool TryGetCached(string path, int sizePx, out ThumbnailImage image);
    void RequestThumbnail(int ownerId, int generation, string path, int sizePx);
    void SetOwnerGeneration(int ownerId, int generation);
    bool ConsumeHasUpdates();
    bool TryDequeueRelease(out int imageId);
    ThumbnailImage GetFallbackIcon(bool isDirectory, int sizePx);
}

public readonly record struct ThumbnailImage(int ImageId, byte[] RgbaPixels, int Width, int Height, bool UseNearest);

