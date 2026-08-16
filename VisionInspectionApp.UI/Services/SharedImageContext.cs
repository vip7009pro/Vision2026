using System;
using OpenCvSharp;

namespace VisionInspectionApp.UI.Services;

public sealed class SharedImageContext
{
    private Mat? _image;

    public event EventHandler? ImageChanged;

    public void SetImage(Mat? image, bool transferOwnership = false)
    {
        lock (this)
        {
            try
            {
                _image?.Dispose();
            }
            catch { }

            try
            {
                if (image is null || image.IsDisposed || image.Empty())
                {
                    _image = null;
                }
                else
                {
                    _image = transferOwnership ? image : image.Clone();
                }
            }
            catch
            {
                _image = null;
            }
        }

        ImageChanged?.Invoke(this, EventArgs.Empty);
    }

    public Mat? GetSnapshot()
    {
        lock (this)
        {
            if (_image is null || _image.IsDisposed || _image.Empty())
                return null;

            try
            {
                return _image.Clone();
            }
            catch
            {
                return null;
            }
        }
    }
}
