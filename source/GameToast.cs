using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TextBlock=Lucent.SmoothText;

namespace Lucent
{
    // The small notice over a game when its profile switches on: "Applied: Rust profile" and what it set, near the top of the main display.
    // It never takes focus or clicks (the game keeps the keyboard and mouse), slides in, stays about two seconds, and fades away.
    // A game in exclusive full screen draws over every window, so there it is not seen; borderless and windowed games show it.
    internal sealed class GameToast:Window
    {
        static GameToast shown;
        internal static void Show(string title,string detail,string executable)
        {
            if(shown!=null){try{shown.Close();}catch(InvalidOperationException){}}
            shown=new GameToast(title,detail,executable);
            shown.Show();
        }
        readonly TranslateTransform slide=new TranslateTransform(0,-14);
        GameToast(string title,string detail,string executable)
        {
            WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;
            Topmost=true;ShowInTaskbar=false;ShowActivated=false;Focusable=false;IsHitTestVisible=false;SizeToContent=SizeToContent.WidthAndHeight;
            FontFamily=Design.Font;Foreground=Brushes.White;Opacity=0;
            var icon=new Image {Width=30,Height=30,Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};
            RenderOptions.SetBitmapScalingMode(icon,BitmapScalingMode.HighQuality);
            var mark=Design.Icon("Profiles",22);mark.Margin=new Thickness(4,0,16,0);mark.VerticalAlignment=VerticalAlignment.Center;
            var face=new Grid();face.Children.Add(mark);face.Children.Add(icon);
            if(!string.IsNullOrEmpty(executable))GameLibrary.LoadIcon(executable,Dispatcher,source=>{icon.Source=source;mark.Visibility=Visibility.Collapsed;});
            var words=new StackPanel {VerticalAlignment=VerticalAlignment.Center};
            words.Children.Add(new TextBlock {Text=title,FontSize=15,FontWeight=FontWeights.Bold});
            if(!string.IsNullOrEmpty(detail))words.Children.Add(new TextBlock {Text=detail,FontSize=12,FontWeight=FontWeights.Medium,Foreground=new SolidColorBrush(Color.FromRgb(196,196,202)),Margin=new Thickness(0,2,0,0)});
            var row=new StackPanel {Orientation=Orientation.Horizontal};
            row.Children.Add(face);row.Children.Add(words);
            var glass=new Grid();
            glass.Children.Add(new Border {Background=new SolidColorBrush(Color.FromArgb(226,18,18,22)),CornerRadius=new CornerRadius(16)});
            glass.Children.Add(Design.GlassEdge(16));
            glass.Children.Add(new Border {Padding=new Thickness(16,12,22,12),Child=row});
            Content=new Border {Padding=new Thickness(18),Child=new Border {Child=glass,RenderTransform=slide,Effect=new System.Windows.Media.Effects.DropShadowEffect {BlurRadius=22,ShadowDepth=4,Opacity=.45,Color=Colors.Black}}};
            SourceInitialized+=(s,e)=>
            {
                // Never activated, never hit: clicks go to the game underneath.
                var handle=new WindowInteropHelper(this).Handle;
                SetWindowLong(handle,-20,GetWindowLong(handle,-20)|0x08000000|0x00000020|0x00000080);
            };
            ContentRendered+=(s,e)=>
            {
                var area=SystemParameters.WorkArea;
                Left=area.Left+(area.Width-ActualWidth)/2;Top=area.Top+24;
                var ease=new CubicEase {EasingMode=EasingMode.EaseOut};
                BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(220)));
                slide.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(-14,0,TimeSpan.FromMilliseconds(320)){EasingFunction=ease});
                var hold=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(2400)};
                hold.Tick+=(a,b)=>
                {
                    hold.Stop();
                    var fade=new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(320));
                    fade.Completed+=(c,d)=>{if(ReferenceEquals(shown,this))shown=null;Close();};
                    BeginAnimation(OpacityProperty,fade);
                    slide.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,-10,TimeSpan.FromMilliseconds(320)));
                };
                hold.Start();
            };
        }
        [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr window,int index);
        [DllImport("user32.dll")]static extern int SetWindowLong(IntPtr window,int index,int value);
    }
}
