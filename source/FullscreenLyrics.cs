using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TextBlock=Lucent.SmoothText;

namespace Lucent
{
    // Lyrics only, over the whole screen: the song's colors drifting behind and the lines big in the middle, lit word by word. The song's name, a few
    // buttons and the way out show while the mouse moves and fade after a moment, with the pointer; Esc or F leaves. It covers the screen of the window
    // it was opened from, and while another window is used it no longer stays above it.
    internal sealed class FullscreenLyrics:Window
    {
        static FullscreenLyrics shown;
        internal static bool IsOpen {get{return shown!=null;}}
        internal static void Toggle(Window from)
        {
            if(shown!=null){shown.Close();return;}
            shown=new FullscreenLyrics(from);
            shown.Show();
        }
        internal readonly PlayerCore Core;
        readonly FlowingBackdrop backdrop=FlowingBackdrop.ForPlayers();
        internal readonly LyricsView Lyrics=new LyricsView();
        readonly Grid chrome=new Grid();
        readonly DispatcherTimer idle=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(2600)};
        string song;int token;bool chromeShown=true;Point lastMouse;

        FullscreenLyrics(Window from)
        {
            Title="Lucent · Lyrics";
            Icon=from.Icon;
            Resources=from.Resources;
            WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=true;
            Foreground=Brushes.White;FontFamily=Design.Font;UseLayoutRounding=true;
            Background=new SolidColorBrush(Color.FromRgb(13,13,16));
            TextOptions.SetTextFormattingMode(this,TextFormattingMode.Ideal);
            // On the screen of the window it was opened from (then maximized there, which covers the whole screen, taskbar too).
            var screen=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(from).Handle).Bounds;
            double scale=VisualTreeHelper.GetDpi(from).DpiScaleX;
            WindowStartupLocation=WindowStartupLocation.Manual;
            Left=screen.Left/scale;Top=screen.Top/scale;Width=screen.Width/scale;Height=screen.Height/scale;
            SourceInitialized+=(s,e)=>WindowState=WindowState.Maximized;

            Core=new PlayerCore(this,false) {artworkPixels=800,showProgress=ShowProgress,trackChanged=LoadLyrics,artworkChanged=image=>backdrop.SetCover(image)};
            var root=new Grid();
            root.Children.Add(backdrop);
            Lyrics.HorizontalAlignment=HorizontalAlignment.Center;
            Lyrics.Seek=seconds=>{var ignored=Core.Seek(seconds);};
            root.Children.Add(Lyrics);

            // The name of the song at the top left, the way out at the top right, and the three buttons at the bottom.
            Core.title.FontSize=18;Core.title.FontWeight=FontWeights.Bold;Core.title.FontFamily=Design.DisplayFont;
            Core.artist.FontSize=14;Core.artist.FontWeight=FontWeights.Medium;Core.artist.Foreground=new SolidColorBrush(Color.FromArgb(190,255,255,255));
            var names=new StackPanel {HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(36,28,0,0),MaxWidth=560};
            names.Children.Add(Core.title);names.Children.Add(Core.artist);
            chrome.Children.Add(names);
            var leave=new Button {Content=Design.Icon("ExitFullscreen",20),ToolTip="Leave full screen (Esc)",HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,26,30,0)};
            PlayerCore.Frame(leave,40);leave.Height=40;
            PlayerCore.Motion(leave,1.14,.86);
            System.Windows.Automation.AutomationProperties.SetName(leave,"Leave full screen");
            leave.Click+=(s,e)=>Close();
            chrome.Children.Add(leave);
            Core.ConfigureTransport(44,22,52,28);
            Core.previous.Height=Core.next.Height=Core.shuffle.Height=Core.repeat.Height=44;
            Core.previous.Margin=Core.next.Margin=new Thickness(14,0,14,0);
            var transport=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,34)};
            transport.Children.Add(Core.previous);transport.Children.Add(Core.play);transport.Children.Add(Core.next);
            chrome.Children.Add(transport);
            root.Children.Add(chrome);
            Content=root;

            SizeChanged+=(s,e)=>Fit();
            Loaded+=async(s,e)=>
            {
                Fit();
                FlowingBackdrop.SetAlone(backdrop);
                backdrop.SetActive(!BigPlayerView.PerformanceMode());
                Lyrics.ShowMessage("Looking for lyrics…");
                await Core.Start();
                LoadLyrics();
                Activate();
            };
            // While another window is used, this one lets it come in front.
            Activated+=(s,e)=>Topmost=true;
            Deactivated+=(s,e)=>Topmost=false;
            PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Escape||e.Key==Key.F){e.Handled=true;Close();}};
            idle.Tick+=(s,e)=>{idle.Stop();ShowChrome(false);};
            MouseMove+=(s,e)=>
            {
                // A pointer that has not moved (Windows sends a move when a window opens or the screen changes) does not wake the buttons.
                var now=e.GetPosition(this);
                if(Math.Abs(now.X-lastMouse.X)<2&&Math.Abs(now.Y-lastMouse.Y)<2)return;
                lastMouse=now;
                ShowChrome(true);
                idle.Stop();
                if(!chrome.IsMouseOver)idle.Start();
            };
            idle.Start();
            Closed+=(s,e)=>{idle.Stop();Core.Stop();backdrop.SetActive(false);FlowingBackdrop.SetAlone(null);if(ReferenceEquals(shown,this))shown=null;};
        }
        // The lines are sized for the screen and kept to a comfortable reading width in the middle.
        void Fit()
        {
            if(ActualWidth<=0||ActualHeight<=0)return;
            Lyrics.Width=Math.Min(ActualWidth-160,Math.Max(600,ActualWidth*.68));
            Lyrics.Margin=new Thickness(0,96,0,110);
            Lyrics.SetStyle(Math.Max(34,Math.Min(64,ActualHeight/16)),true);
        }
        void ShowChrome(bool on)
        {
            if(on==chromeShown)return;
            chromeShown=on;
            Design.Animate(chrome,OpacityProperty,chrome.Opacity,on?1:0,on?160:600);
            chrome.IsHitTestVisible=on;
            Cursor=on?null:Cursors.None;
        }
        void ShowProgress()
        {
            var playing=Core.Track;
            if(playing==null?song!=null:(playing.Source+"|"+playing.Title+"|"+playing.Artist)!=song)LoadLyrics();
            Lyrics.Update(Core.progressValue);
        }
        // Looks the song up once (the players share what they found).
        void LoadLyrics()
        {
            var track=Core.Track;
            if(track==null||string.IsNullOrEmpty(track.Title))
            {
                if(song==null&&Lyrics.Note!=null&&Lyrics.Note!="Looking for lyrics…")return;
                song=null;token++;
                Lyrics.ShowMessage("Nothing playing");
                return;
            }
            string key=track.Source+"|"+track.Title+"|"+track.Artist;
            if(key==song)return;
            song=key;
            int mine=++token;
            Lyrics.ShowMessage("Looking for lyrics…");
            string title=track.Title,artist=track.Artist;double length=track.Duration;
            Task.Run(()=>LyricsService.Find(title,artist,length)).ContinueWith(task=>
            {
                var result=task.Status==TaskStatus.RanToCompletion?task.Result:null;
                Dispatcher.BeginInvoke(new Action(()=>
                {
                    if(mine!=token)return;
                    Lyrics.SetLyrics(result);
                    Lyrics.Update(Core.progressValue);
                }));
            });
        }
    }
}
