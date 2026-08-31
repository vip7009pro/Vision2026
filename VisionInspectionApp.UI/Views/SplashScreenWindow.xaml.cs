using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace VisionInspectionApp.UI.Views;

/// <summary>
/// Màn hình Splash Screen tải ứng dụng ban đầu với hiệu ứng mượt mà và thông báo tiến trình.
/// </summary>
public partial class SplashScreenWindow : Window
{
    public SplashScreenWindow()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            if (TryFindResource("FadeInStoryboard") is Storyboard fadeIn)
            {
                fadeIn.Begin(this);
            }
        };
    }

    public void SetProgress(double percentage, string statusMessage)
    {
        Dispatcher.Invoke(() =>
        {
            PrgLoading.Value = Math.Clamp(percentage, 0, 100);
            TxtPercentage.Text = $"{(int)percentage}%";
            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                TxtStatus.Text = statusMessage;
            }
        });
    }

    public async Task FadeOutAndCloseAsync()
    {
        var tcs = new TaskCompletionSource<bool>();

        Dispatcher.Invoke(() =>
        {
            if (TryFindResource("FadeOutStoryboard") is Storyboard fadeOut)
            {
                fadeOut.Completed += (s, e) =>
                {
                    Close();
                    tcs.TrySetResult(true);
                };
                fadeOut.Begin(this);
            }
            else
            {
                Close();
                tcs.TrySetResult(true);
            }
        });

        await tcs.Task;
    }
}
