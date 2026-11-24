using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

namespace PortwatchDesktop;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        if (e.Args.Contains("--self-test"))
        {
            try { TrackerService.SelfTest(); Shutdown(0); }
            catch { Shutdown(1); }
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        if (e.Args.Contains("--smoke-gui") || e.Args.Contains("--preview"))
        {
            window.PreviewSample();
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(1320, 820));
            content.Arrange(new Rect(0, 0, 1320, 820));
            content.UpdateLayout();
            int preview = Array.IndexOf(e.Args, "--preview");
            if (preview >= 0 && preview + 1 < e.Args.Length)
            {
                var bitmap = new RenderTargetBitmap(1320, 820, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(e.Args[preview + 1]); encoder.Save(file);
            }
            Shutdown(0);
            return;
        }
        window.Show();
    }
}
