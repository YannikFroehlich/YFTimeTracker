using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace YFTimeTracker.Windows.GameIcons;

internal interface IExecutableIconExtractor
{
    Task<bool> ExtractAsync(string executablePath, string destinationPath, CancellationToken cancellationToken);
}

internal sealed class ShellExecutableIconExtractor : IExecutableIconExtractor
{
    private const uint RequestedIconSize = 256;

    public async Task<bool> ExtractAsync(
        string executablePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executable = await StorageFile.GetFileFromPathAsync(executablePath);
        cancellationToken.ThrowIfCancellationRequested();

        using var thumbnail = await executable.GetThumbnailAsync(
            ThumbnailMode.SingleItem,
            RequestedIconSize,
            ThumbnailOptions.UseCurrentScale);
        if (thumbnail is null || thumbnail.Size == 0)
        {
            return false;
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("The icon destination has no parent directory.");
        var folder = await StorageFolder.GetFolderFromPathAsync(destinationDirectory);
        var destination = await folder.CreateFileAsync(
            Path.GetFileName(destinationPath),
            CreationCollisionOption.ReplaceExisting);
        using var output = await destination.OpenAsync(FileAccessMode.ReadWrite);
        var decoder = await BitmapDecoder.CreateAsync(thumbnail);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return output.Size > 0;
    }
}
