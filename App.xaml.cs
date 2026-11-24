using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

namespace PortwatchDesktop;

public partial class App : Application
{
    private Mutex? instanceMutex;
    private EventWaitHandle? showEvent;
    private RegisteredWaitHandle? showRegistration;
    private bool ownsMutex;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        if (e.Args.Contains("--self-test"))
        {
            try { TrackerService.SelfTest(); Shutdown(0); }
            catch { Shutdown(1); }
            return;
        }
        bool testMode=e.Args.Contains("--smoke-gui")||e.Args.Contains("--preview")||e.Args.Contains("--smoke-tray")||e.Args.Contains("--smoke-settings");
        if(!testMode&&!ClaimInstance()){Shutdown(0);return;}
        var window = new MainWindow();
        MainWindow = window;
        if(e.Args.Contains("--smoke-tray")){Shutdown(window.VerifyTray()?0:1);return;}
        if(e.Args.Contains("--smoke-settings")){Shutdown(window.VerifySettings()?0:1);return;}
        if (e.Args.Contains("--smoke-gui") || e.Args.Contains("--preview"))
        {
            window.PreviewSample();
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(1320, 880));
            content.Arrange(new Rect(0, 0, 1320, 880));
            content.UpdateLayout();
            int preview = Array.IndexOf(e.Args, "--preview");
            if (preview >= 0 && preview + 1 < e.Args.Length)
            {
                var bitmap = new RenderTargetBitmap(1320, 880, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(e.Args[preview + 1]); encoder.Save(file);
            }
            Shutdown(0);
            return;
        }
        if(showEvent is not null)showRegistration=ThreadPool.RegisterWaitForSingleObject(showEvent,(_,_)=>{if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(window.ShowFromTray));},null,Timeout.Infinite,false);
        window.Show();
        if(e.Args.Contains("--start-hidden"))window.Dispatcher.BeginInvoke(new Action(window.StartHidden),System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private bool ClaimInstance()
    {
        try
        {
            instanceMutex=new Mutex(true,@"Local\PortwatchDesktop.Instance",out ownsMutex);
            if(!ownsMutex)
            {
                if(EventWaitHandle.TryOpenExisting(@"Local\PortwatchDesktop.Show",out var existing)){using(existing)existing.Set();}
                return false;
            }
            showEvent=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\PortwatchDesktop.Show");
        }
        catch(UnauthorizedAccessException){showEvent?.Dispose();showEvent=null;}
        catch(IOException){showEvent?.Dispose();showEvent=null;}
        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        showRegistration?.Unregister(null);showEvent?.Dispose();
        if(ownsMutex)instanceMutex?.ReleaseMutex();instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
