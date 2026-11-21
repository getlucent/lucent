using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TextBlock=Lucent.SmoothText;

namespace Lucent
{
    // The command palette: Ctrl+K in Lucent opens a box where typing does things at once, "vibrance 150", "crosshair off", "valorant" (a profile),
    // "boost" (a page). Enter runs the top one, the arrows pick another, Esc or a click outside closes it.
    internal sealed partial class Shell
    {
        Button cleanRamButton;
        Grid paletteLayer;
        Border paletteCard;
        TextBox paletteBox;
        StackPanel paletteRows;
        List<PaletteMatch> paletteShown=new List<PaletteMatch>();
        int paletteIndex;
        internal bool PaletteOpen {get{return paletteLayer!=null&&paletteLayer.Visibility==Visibility.Visible;}}

        void HookPalette()
        {
            PreviewKeyDown+=(s,e)=>
            {
                if(e.Key==Key.K&&Keyboard.Modifiers==ModifierKeys.Control){e.Handled=true;TogglePalette();}
            };
        }
        // Everything the palette can do, made fresh each time it opens (profiles come and go).
        internal List<PaletteCommand> PaletteCommands()
        {
            var list=new List<PaletteCommand>();
            Action<string,string,string,string[],Action<string>> plain=(title,hint,icon,words,run)=>list.Add(new PaletteCommand {Title=title,Hint=hint,Icon=icon,Words=words,Run=run});
            Action<string,string,string,string[],double,double,string,Action<double>> number=(title,hint,icon,words,min,max,unit,run)=>
                list.Add(new PaletteCommand {Title=title,Hint=hint,Icon=icon,Words=words,Argument=PaletteArgument.Number,Min=min,Max=max,Unit=unit,Run=a=>run(double.Parse(a,System.Globalization.CultureInfo.InvariantCulture))});
            Action<string,string,string,string[],Func<bool>,Action> toggle=(title,hint,icon,words,now,flip)=>
                list.Add(new PaletteCommand {Title=title,Hint=hint,Icon=icon,Words=words,Argument=PaletteArgument.OnOff,Run=a=>{if(CommandPalette.Wanted(a,now())!=now())flip();}});
            Action<string,double> slide=(name,value)=>{Slider slider;if(sliders.TryGetValue(name,out slider))slider.Value=value;};

            number("Vibrance","Set vibrance, 100–300%","Vibrance",new[]{"saturation","color","colour","digital"},Look.MinimumVibrance,Look.MaximumVibrance,"%",v=>slide("Vibrance",v));
            number("Red filter","Warm the screen, 0–100%","Sun",new[]{"night","eye","care","warm","blue","light"},0,100,"%",v=>slide("Red Filter",v));
            number("Screen dim","Dim the screen, 0–85%","Dim",new[]{"darker","brightness"},0,85,"%",v=>slide("Screen Dim",v));
            plain("Neutral colors","Vibrance 100%, no red filter, no dim","Color",new[]{"reset","normal","default","colors","colours"},a=>{slide("Vibrance",100);slide("Red Filter",0);slide("Screen Dim",0);});
            toggle("Crosshair","Switch the crosshair on or off","Crosshair",new[]{"aim","dot","reticle"},()=>crosshair!=null,ToggleCrosshair);
            toggle("Night Vision","See into dark corners","Visual tools",new[]{"shadows","dark","gamma","boost"},()=>current.DarkEnabled,ToggleDarkBoost);
            number("Night Vision strength","How strong Night Vision is, 0–100","Visual tools",new[]{"shadows","gamma"},0,100,"",v=>{if(!current.DarkEnabled&&preferences.RiskAccepted)ToggleDarkBoost();slide("Strength",v);});
            if(SunsetIsSetting)toggle("Vice Sunset theme","The Vice logo and icon, and the sunset while nothing plays","Color",new[]{"gta","vice","theme","sunset","colors"},()=>SunsetWanted,()=>SetSunsetWanted(!SunsetWanted));
            toggle("Zoom","Zoom in on the middle of the screen","Search",new[]{"magnify","scope","sniper"},()=>zoomed,ToggleZoom);
            plain("Switch resolution","Swap between your two resolutions","Swap",new[]{"display","stretched","res"},a=>{TogglePair();UpdateHome();});
            plain("Desktop settings","Go back to your own settings (no profile)","Profiles",new[]{"profile","default"},a=>LoadLook(preferences.Desktop,"Desktop"));
            foreach(var profile in preferences.Profiles.ToList())
            {
                var chosen=profile;
                plain(chosen.Name+" profile","Use the "+chosen.Name+" profile","Profiles",new[]{"profile","game"},a=>LoadLook(chosen.Look,chosen.Name));
            }
            plain("Play / pause","Play or pause the music","Play",new[]{"music","song","resume","stop"},a=>SendMedia(0xB3));
            plain("Next song","Skip to the next song","Next",new[]{"music","skip","track"},a=>SendMedia(0xB0));
            plain("Previous song","Go back a song","Previous",new[]{"music","back","track"},a=>SendMedia(0xB1));
            plain("Player","Open the player","BigPlayer",new[]{"music","big","song"},a=>SetBigPlayerOpen(true));
            plain("Mini player","Open the mini player","OpenPlayer",new[]{"music","small","window"},a=>MediaPopup());
            plain("Lyrics in full screen","Lyrics only, over the whole screen","Fullscreen",new[]{"lyrics","words","karaoke","music"},a=>FullscreenLyrics.Toggle(this));
            plain("Clean RAM","Free memory now","Game Boost",new[]{"memory","boost","ram","free"},a=>{ShowPage("Game Boost");if(cleanRamButton!=null&&cleanRamButton.IsEnabled)cleanRamButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));});
            plain("Quick panel","The small panel for use over a game","Settings",new[]{"overlay","panel"},a=>ToggleQuickPanel());
            foreach(string key in new[]{"Home","Video","Resolution","Crosshair","Profiles","Visual tools","Game Boost"})
            {
                string page=key,shown=TabTitle(key);
                plain(shown,"Go to "+shown,key=="Video"?"Color":key=="Resolution"?"Monitor":key,new[]{"page","tab","go",key.ToLowerInvariant()},a=>ShowPage(page));
            }
            foreach(string name in new[]{"General","Appearance","Shortcuts","Mini Player","Discord RPC","Stats","Backgrounds","About"})
            {
                string category=name;
                plain("Settings: "+name,"Open "+name+" in Settings","Settings",new[]{"settings","options","preferences"},a=>{if(!settingsOpen)SetSettingsOpen(true);SelectSettingsCategory(category);});
            }
            return list;
        }

        internal void TogglePalette()
        {
            if(PaletteOpen){ClosePalette();return;}
            if(paletteLayer==null)BuildPalette();
            paletteBox.Text="";
            ShowPaletteMatches();
            paletteLayer.Visibility=Visibility.Visible;
            paletteLayer.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(Design.Instant?0:120)));
            var lift=(TranslateTransform)paletteCard.RenderTransform;
            lift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(-10,0,TimeSpan.FromMilliseconds(Design.Instant?0:180)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,new Action(()=>{paletteBox.Focus();Keyboard.Focus(paletteBox);}));
        }
        void ClosePalette()
        {
            if(!PaletteOpen)return;
            paletteLayer.BeginAnimation(OpacityProperty,null);
            paletteLayer.Visibility=Visibility.Collapsed;
            Focus();
        }
        void BuildPalette()
        {
            paletteLayer=new Grid {Background=new SolidColorBrush(Color.FromArgb(110,0,0,0)),Visibility=Visibility.Collapsed};
            paletteLayer.MouseLeftButtonDown+=(s,e)=>{if(ReferenceEquals(e.OriginalSource,paletteLayer))ClosePalette();};
            Panel.SetZIndex(paletteLayer,60);
            var stack=new StackPanel();
            // The box, with a magnifier and a hint while it is empty.
            var field=new Grid {Margin=new Thickness(16,12,16,10)};
            var glass=Design.Icon("Search",18);glass.HorizontalAlignment=HorizontalAlignment.Left;glass.VerticalAlignment=VerticalAlignment.Center;glass.Opacity=.7;
            paletteBox=new TextBox {FontFamily=Design.Font,FontSize=18,Foreground=Brushes.White,CaretBrush=Brushes.White,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(30,6,6,6),VerticalContentAlignment=VerticalAlignment.Center,Style=null};
            System.Windows.Automation.AutomationProperties.SetName(paletteBox,"Command");
            var hint=new TextBlock {Text="Type a command — vibrance 150, crosshair off, boost…",FontSize=16,Foreground=new SolidColorBrush(Color.FromArgb(120,255,255,255)),Margin=new Thickness(34,0,0,0),VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
            paletteBox.TextChanged+=(s,e)=>{hint.Visibility=paletteBox.Text.Length==0?Visibility.Visible:Visibility.Collapsed;ShowPaletteMatches();};
            paletteBox.PreviewKeyDown+=PaletteKey;
            field.Children.Add(paletteBox);field.Children.Add(glass);field.Children.Add(hint);
            stack.Children.Add(field);
            stack.Children.Add(new Border {Height=1,Background=new SolidColorBrush(Color.FromArgb(28,255,255,255))});
            paletteRows=new StackPanel {Margin=new Thickness(8,6,8,8)};
            stack.Children.Add(paletteRows);
            var card=new Grid();
            card.Children.Add(new Border {Background=new SolidColorBrush(Color.FromArgb(244,18,18,22)),CornerRadius=new CornerRadius(16)});
            card.Children.Add(Design.GlassEdge(16));
            card.Children.Add(stack);
            paletteCard=new Border {Width=580,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,90,0,0),Child=card,RenderTransform=new TranslateTransform(),
                Effect=new System.Windows.Media.Effects.DropShadowEffect {BlurRadius=40,ShadowDepth=10,Opacity=.55,Color=Colors.Black}};
            paletteLayer.Children.Add(paletteCard);
            root.Children.Add(paletteLayer);
        }
        void PaletteKey(object sender,KeyEventArgs e)
        {
            if(e.Key==Key.Escape){e.Handled=true;ClosePalette();return;}
            if(e.Key==Key.Down){e.Handled=true;MovePalette(1);return;}
            if(e.Key==Key.Up){e.Handled=true;MovePalette(-1);return;}
            if(e.Key==Key.Enter){e.Handled=true;RunPalette(paletteIndex);}
        }
        void MovePalette(int by)
        {
            if(paletteShown.Count==0)return;
            paletteIndex=(paletteIndex+by+paletteShown.Count)%paletteShown.Count;
            PaintPaletteRows();
        }
        const int PaletteRows=8;
        void ShowPaletteMatches()
        {
            paletteShown=CommandPalette.Match(PaletteCommands(),paletteBox.Text).Take(PaletteRows).ToList();
            paletteIndex=0;
            paletteRows.Children.Clear();
            if(paletteShown.Count==0)
            {
                paletteRows.Children.Add(new TextBlock {Text="Nothing matches. Try “vibrance 150”, “crosshair” or “boost”.",FontSize=13,Foreground=Muted,Margin=new Thickness(12,10,12,10)});
                return;
            }
            for(int i=0;i<paletteShown.Count;i++)
            {
                var match=paletteShown[i];int index=i;
                var row=new Border {CornerRadius=new CornerRadius(10),Padding=new Thickness(12,8,12,8),Background=Brushes.Transparent,Cursor=Cursors.Hand};
                var line=new DockPanel();
                var icon=Design.Icon(match.Command.Icon??"Search",18);icon.Margin=new Thickness(0,0,14,0);icon.VerticalAlignment=VerticalAlignment.Center;icon.Opacity=.85;
                DockPanel.SetDock(icon,Dock.Left);line.Children.Add(icon);
                var words=new StackPanel {VerticalAlignment=VerticalAlignment.Center};
                words.Children.Add(new TextBlock {Text=match.Title,FontSize=15,FontWeight=FontWeights.SemiBold});
                words.Children.Add(new TextBlock {Text=match.Hint,FontSize=12,Foreground=Muted,Margin=new Thickness(0,2,0,0)});
                line.Children.Add(words);
                row.Child=line;
                row.MouseEnter+=(s,e)=>{paletteIndex=index;PaintPaletteRows();};
                row.MouseLeftButtonUp+=(s,e)=>{e.Handled=true;RunPalette(index);};
                paletteRows.Children.Add(row);
            }
            PaintPaletteRows();
        }
        void PaintPaletteRows()
        {
            for(int i=0;i<paletteRows.Children.Count;i++)
            {
                var row=paletteRows.Children[i] as Border;if(row==null)continue;
                row.Background=i==paletteIndex?(Brush)new SolidColorBrush(Color.FromArgb(34,255,255,255)):Brushes.Transparent;
            }
        }
        // Runs the chosen command. One that still needs its number puts its name in the box to type it after.
        internal void RunPalette(int index)
        {
            if(index<0||index>=paletteShown.Count)return;
            var match=paletteShown[index];
            if(!match.Ready){paletteBox.Text=match.Command.Title+" ";paletteBox.CaretIndex=paletteBox.Text.Length;return;}
            ClosePalette();
            try{match.Command.Run(match.Argument);SetStatus(match.Title);}
            catch(Exception error){Message(error.Message);}
        }
    }
}
