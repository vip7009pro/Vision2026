using OpenCvSharp;

namespace VisionInspectionApp.UI.Services;

public sealed class SharedImageContext
{
    private Mat? _image;

    public event EventHandler? ImageChanged;

    public void SetImage(Mat? image)
    {
        lock (this)
        {
            _image?.Dispose();
            _image = image is null ? null : image.Clone();
        }

        ImageChanged?.Invoke(this, EventArgs.Empty);
    }

    public Mat? GetSnapshot()
    {
        lock (this)
        {
            return _image is null ? null : _image.Clone();
        }
    }
}
