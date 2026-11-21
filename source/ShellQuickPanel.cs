using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using TextBlock=Lucent.SmoothText;

namespace Lucent
{
    // The quick panel: one shortcut brings a small glass panel over the game with the things you change while playing (vibrance, the crosshair,
    // Night Vision, the song), without alt-tabbing. It closes with the same shortcut, Esc, or a click back into the game, and the game gets the focus back.
    // It is an ordinary window on top (nothing is put into the game), so games in exclusive full screen draw over it; borderless and windowed games show it.
    internal sealed partial class Shell
    {
        QuickPanel quickPanel;
        void ToggleQuickPanel()
        {
            if(TestMode)return;
            if(quickPanel!=null&&quickPanel.IsOpen){quickPanel.CloseBack();return;}
            if(quickPanel==null)quickPanel=new QuickPanel(this);
            quickPanel.Open();
        }
        // The panel is built while the app is idle after starting, so the first press of the shortcut doesn't wait for it to be made.
        void PrepareQuickPanel()
        {
            if(TestMode)return;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(()=>
            {
                if(quickPanel!=null)return;
                try{quickPanel=new QuickPanel(this);quickPanel.Prepare();}
                catch(Exception){quickPanel=null;}
            }));
        }
        // What the panel reads and changes; all of it goes through the same paths as the app's own controls.
        internal double PanelVibrance{get{return current.Saturation;}}
        internal void PanelSetVibrance(double value)
        {
            Slider slider;
            if(sliders.TryGetValue("Vibrance",out slider))slider.Value=value;
            else{current.Saturation=value;Changed();}
        }
        internal bool PanelCrosshairOn{get{return crosshair!=null;}}
        internal void PanelToggleCrosshair(){ToggleCrosshair();}
        internal bool PanelDarkOn{get{return current.DarkEnabled;}}
        internal string PanelToggleDark()
        {
            if(!preferences.RiskAccepted)return "Review the game-rules notice in Vision first.";
            current.DarkEnabled=!current.DarkEnabled;
            if(darkBoostToggle!=null)darkBoostToggle.IsChecked=current.DarkEnabled;
            Changed();
            return null;
        }
        internal MediaTrack PanelTrack{get{return preferences.MediaControls?ambientTrack:null;}}
        internal void PanelMedia(byte key){SendMedia(key);}
        internal string PanelGame{get{return playingNow;}}
        internal string PanelKeyText{get{return preferences.PanelKey==0?"the quick panel shortcut":BindText(preferences.PanelKey);}}
    }

    internal sealed class QuickPanel:Window
    {
        readonly Shell shell;
        IntPtr returnTo;
        readonly TranslateTransform slide=new TranslateTransform();
        readonly Slider vibrance=new Slider {Minimum=100,Maximum=300,SmallChange=5,LargeChange=25,IsMoveToPointEnabled=true,Height=34};
        readonly TextBlock vibranceValue=new TextBlock {FontSize=15,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
        readonly CheckBox crosshairToggle,darkToggle;
        readonly TextBlock gameLine=new TextBlock {FontSize=12,Foreground=new SolidColorBrush(Color.FromRgb(188,188,188)),TextTrimming=TextTrimming.CharacterEllipsis};
        readonly TextBlock songTitle=new TextBlock {FontSize=14,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
        readonly TextBlock songArtist=new TextBlock {FontSize=12,Foreground=new SolidColorBrush(Color.FromRgb(188,188,188)),TextTrimming=TextTrimming.CharacterEllipsis};
        readonly Image cover=new Image {Stretch=Stretch.UniformToFill};
        readonly TextBlock note=new TextBlock {FontSize=12,Foreground=new SolidColorBrush(Color.FromRgb(188,188,188)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
        readonly FrameworkElement musicBox;
        bool syncing,closingBack,acting;
        public QuickPanel(Shell owner)
        {
            shell=owner;
            WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;
            Topmost=true;ShowInTaskbar=false;SizeToContent=SizeToContent.WidthAndHeight;FontFamily=Design.Font;Foreground=Brushes.White;Title="Lucent quick panel";
            // The app's own styles (toggles, buttons, sliders), so the panel looks like the app.
            Resources.MergedDictionaries.Add(owner.Resources);
            var stack=new StackPanel {Width=340};
            // Header: the mark, the name, what is running, and a close button.
            var header=new DockPanel {Margin=new Thickness(0,0,0,14)};
            var close=new Button {Content=Design.Icon("Close",14),Width=30,Height=30,MinHeight=0,Padding=new Thickness(0),Background=Brushes.Transparent,Cursor=Cursors.Hand,ToolTip="Close"};
            close.Click+=(s,e)=>CloseBack();
            DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
            var mark=Art.Logo(22);mark.Margin=new Thickness(0,0,10,0);mark.VerticalAlignment=VerticalAlignment.Center;
            DockPanel.SetDock(mark,Dock.Left);header.Children.Add(mark);
            var titles=new StackPanel {VerticalAlignment=VerticalAlignment.Center};
            titles.Children.Add(new TextBlock {Text="Quick panel",FontSize=16,FontWeight=FontWeights.Bold});
            titles.Children.Add(gameLine);
            header.Children.Add(titles);
            stack.Children.Add(header);
            // Vibrance.
            var vibranceRow=new DockPanel();
            DockPanel.SetDock(vibranceValue,Dock.Right);vibranceRow.Children.Add(vibranceValue);
            vibranceRow.Children.Add(new TextBlock {Text="Vibrance",FontSize=14,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
            stack.Children.Add(vibranceRow);
            vibrance.ValueChanged+=(s,e)=>
            {
                vibranceValue.Text=Math.Round(vibrance.Value)+"%";
                if(!syncing)shell.PanelSetVibrance(Math.Round(vibrance.Value));
            };
            stack.Children.Add(vibrance);
            // The crosshair and Night Vision, as the app's own switches.
            crosshairToggle=new CheckBox {Content="Crosshair",Margin=new Thickness(0,6,0,6),Style=(Style)owner.Resources["ToggleStyle"]};
            crosshairToggle.Click+=(s,e)=>Act(()=>{shell.PanelToggleCrosshair();Sync();});
            darkToggle=new CheckBox {Content="Night Vision",Margin=new Thickness(0,6,0,6),Style=(Style)owner.Resources["ToggleStyle"]};
            darkToggle.Click+=(s,e)=>Act(()=>{string problem=shell.PanelToggleDark();if(problem!=null)note.Text=problem;Sync();});
            stack.Children.Add(crosshairToggle);stack.Children.Add(darkToggle);
            // The song: cover, title and artist, and the three buttons.
            var music=new StackPanel {Margin=new Thickness(0,12,0,0)};
            var songRow=new DockPanel();
            var coverBox=new Grid {Width=46,Height=46,Margin=new Thickness(0,0,12,0),Clip=new RectangleGeometry(new Rect(0,0,46,46),10,10)};
            coverBox.Children.Add(new Border {Background=new SolidColorBrush(Color.FromRgb(40,40,44))});
            var noteIcon=Design.Icon("Music",20);noteIcon.HorizontalAlignment=HorizontalAlignment.Center;noteIcon.VerticalAlignment=VerticalAlignment.Center;noteIcon.Opacity=.5;
            coverBox.Children.Add(noteIcon);coverBox.Children.Add(cover);
            DockPanel.SetDock(coverBox,Dock.Left);songRow.Children.Add(coverBox);
            var words=new StackPanel {VerticalAlignment=VerticalAlignment.Center};words.Children.Add(songTitle);words.Children.Add(songArtist);
            songRow.Children.Add(words);
            music.Children.Add(songRow);
            var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,10,0,0)};
            Func<string,byte,Button> media=(icon,key)=>
            {
                var b=new Button {Content=Design.Icon(icon,18),Width=54,Height=38,MinHeight=0,Padding=new Thickness(0),Margin=new Thickness(4,0,4,0),Cursor=Cursors.Hand};
                b.Click+=async(s,e)=>{Act(()=>shell.PanelMedia(key));await System.Threading.Tasks.Task.Delay(700);Sync();};
                return b;
            };
            buttons.Children.Add(media("Previous",0xB1));buttons.Children.Add(media("Play",0xB3));buttons.Children.Add(media("Next",0xB0));
            music.Children.Add(buttons);
            musicBox=music;
            stack.Children.Add(music);
            stack.Children.Add(note);
            // The glass card.
            var card=new Grid();
            card.Children.Add(new Border {Background=new SolidColorBrush(Color.FromArgb(232,16,16,20)),CornerRadius=new CornerRadius(18)});
            card.Children.Add(Design.GlassEdge(18));
            card.Children.Add(new Border {Padding=new Thickness(20,16,20,18),Child=stack});
            Content=new Border {Padding=new Thickness(20),Child=new Border {Child=card,RenderTransform=slide,Effect=new System.Windows.Media.Effects.DropShadowEffect {BlurRadius=26,ShadowDepth=4,Opacity=.5,Color=Colors.Black}}};
            KeyDown+=(s,e)=>{if(e.Key==Key.Escape){e.Handled=true;CloseBack();}};
            // A click anywhere else (back into the game) puts it away.
            // (Not while one of its own buttons is doing something: turning the crosshair on opens its window, which takes the focus for a moment.)
            Deactivated+=(s,e)=>{if(IsOpen&&!closingBack&&!acting)PutAway();};
        }
        // Open means shown and not on its way out.
        public bool IsOpen{get{return IsVisible&&!hiding;}}
        bool hiding;int hideRound;
        // Puts the panel away: it fades out, and is hidden only once a fully clear frame of it has been drawn.
        // A see-through window that is shown again first puts up the last picture it drew, so hiding it while it was still fully visible
        // made the old panel flash up for a moment on the next open before the fade-in started (it looked like it opened twice).
        void PutAway()
        {
            if(!IsVisible||hiding)return;
            hiding=true;
            int round=++hideRound;
            var fade=new DoubleAnimation(0,TimeSpan.FromMilliseconds(90));
            fade.Completed+=(s,e)=>
            {
                if(!hiding||round!=hideRound)return;
                int frames=0;
                EventHandler tick=null;
                tick=(s2,e2)=>
                {
                    if(++frames<4&&hiding&&round==hideRound)return;
                    CompositionTarget.Rendering-=tick;
                    if(hiding&&round==hideRound){hiding=false;Hide();}
                };
                CompositionTarget.Rendering+=tick;
            };
            BeginAnimation(OpacityProperty,fade);
        }
        // Runs one of the panel's actions; whatever window it opens may take the focus for a moment, so the panel stays and takes it back.
        async void Act(Action action)
        {
            acting=true;
            try{action();}
            catch(Exception){}
            await System.Threading.Tasks.Task.Delay(250);
            acting=false;
            if(IsOpen){Activate();Topmost=false;Topmost=true;}
        }
        // Builds the window once, off screen and invisible, so the first time it is opened in a game it comes up at once.
        public void Prepare()
        {
            Opacity=0;Left=-32000;Top=-32000;ShowActivated=false;
            Show();UpdateLayout();Hide();
            ShowActivated=true;
        }
        // Shows the panel at the right side of the main display and takes the keyboard and mouse; the window that had them is remembered.
        // Its size is known before it shows, so it appears in its place in one go (it used to show, then jump there, which looked like it opened twice).
        public void Open()
        {
            returnTo=GetForegroundWindow();
            Sync();
            note.Text="Press "+shell.PanelKeyText+" or Esc to close.";
            var area=SystemParameters.WorkArea;
            var content=(FrameworkElement)Content;
            content.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            var size=content.DesiredSize;
            Left=area.Right-size.Width-8;Top=area.Top+Math.Max(0,(area.Height-size.Height)/2);
            // Pressed again while it was still fading out: it simply comes back from where it is.
            if(hiding){hiding=false;hideRound++;}
            else if(!IsVisible)
            {
                BeginAnimation(OpacityProperty,null);Opacity=0;
                slide.BeginAnimation(TranslateTransform.XProperty,null);slide.X=18;
                Show();
            }
            Activate();
            BeginAnimation(OpacityProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(130)));
            slide.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(180)){EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}});
        }
        // Puts the panel away and gives the game back the keyboard and mouse at once (the fade-out happens over the game).
        public void CloseBack()
        {
            if(!IsOpen)return;
            closingBack=true;
            PutAway();
            if(returnTo!=IntPtr.Zero)SetForegroundWindow(returnTo);
            closingBack=false;
        }
        void Sync()
        {
            syncing=true;
            try
            {
                vibrance.Value=shell.PanelVibrance;vibranceValue.Text=Math.Round(shell.PanelVibrance)+"%";
                crosshairToggle.IsChecked=shell.PanelCrosshairOn;
                darkToggle.IsChecked=shell.PanelDarkOn;                string game=shell.PanelGame;
                gameLine.Text=string.IsNullOrEmpty(game)?"No game running":"Playing "+game;
                var track=shell.PanelTrack;
                bool any=track!=null&&!string.IsNullOrEmpty(track.Title);
                musicBox.Visibility=any?Visibility.Visible:Visibility.Collapsed;
                if(any)
                {
                    songTitle.Text=track.Title;songArtist.Text=string.IsNullOrEmpty(track.Artist)?track.Source:track.Artist;
                    cover.Source=null;
                    if(track.Artwork!=null)
                        try{var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.DecodePixelWidth=92;b.StreamSource=new System.IO.MemoryStream(track.Artwork);b.EndInit();b.Freeze();cover.Source=b;}
                        catch(Exception){cover.Source=null;}
                }
            }
            finally{syncing=false;}
        }
        [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
    }
}
