using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PortwatchDesktop;

internal sealed class DeveloperWindow : Window
{
    private readonly TextBox output;
    public DeveloperWindow(Window owner,IEnumerable<string> history,Action fetch,Action test30,Action testStable,Func<string> report,Action clear)
    {
        if(owner.IsLoaded)Owner=owner;Title="Portwatch Developer Mode";Width=820;Height=530;MinWidth=620;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;Style=(Style)FindResource(typeof(Window));Background=(Brush)FindResource("Page");Foreground=(Brush)FindResource("Text");
        var layout=new DockPanel{Margin=new Thickness(20)};
        var header=new StackPanel{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);
        header.Children.Add(new TextBlock{Text="Developer mode",FontSize=25,FontWeight=FontWeights.Bold});
        header.Children.Add(new TextBlock{Text="Session diagnostics · test alerts do not change tracker state",Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(0,4,0,12)});
        var controls=new WrapPanel();header.Children.Add(controls);
        void Add(string name,Action action){var b=new Button{Content=name,Margin=new Thickness(0,0,7,6)};b.Click+=(_,_)=>action();controls.Children.Add(b);}
        Add("Test 30 FPS",test30);Add("Test 100%",testStable);Add("Fetch now",fetch);Add("Copy diagnostics",()=>Clipboard.SetText(report()+"\n\nSession log:\n"+output!.Text));Add("Clear log",()=>{clear();output!.Clear();});
        output=new TextBox{Text=string.Join(Environment.NewLine,history),IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=(Brush)FindResource("Card"),Foreground=(Brush)FindResource("Text"),BorderThickness=new Thickness(0),Padding=new Thickness(14),FontFamily=new FontFamily("Cascadia Mono, Consolas"),FontSize=13};layout.Children.Add(new Border{Style=(Style)FindResource("CardStyle"),Padding=new Thickness(5),Child=output});Content=layout;
    }
    public void Append(string line){output.AppendText(line+Environment.NewLine);if(output.LineCount>1000){var text=output.Text;output.Text=text.Length>100000?text[^80000..]:text;}output.ScrollToEnd();}
}
