using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TextBlock=Lucent.SmoothText;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Shell;
using Forms=System.Windows.Forms;
using ResolutionSwitcher;
using System.Windows.Media.Imaging;

namespace Lucent
{
    // Game profiles: the Profiles page, saved looks and automatic switching.
    internal sealed partial class Shell
    {
        FrameworkElement ProfilesPage()
        {
            // One line says what a profile is; the details sit where they are used.
            var stack=Page(TabTitle("Profiles"),"A profile saves your colors, Night Vision and zoom, and a game's profile can also set its resolution and crosshair.");
            // Your games first, then your profiles, then switching, and saving your own settings last.
            stack.Children.Add(GameLibraryCard());
            var looks=Section("Your profiles","Choose the profile to use right now, or save what you have set up as a new one.");
            profilesList=new StackPanel();
            looks.Children.Add(profilesList);
            // Saving the current settings as a new profile: one slim row under the list. The box shows a hint until you type a name.
            var saveRow=new DockPanel {Margin=new Thickness(0,4,0,0)};
            var nameBox=new TextBox {MinHeight=34,VerticalContentAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0),MaxLength=24,ToolTip="Name the new profile (optional)"};
            System.Windows.Automation.AutomationProperties.SetName(nameBox,"Profile name");
            var nameHint=new TextBlock {Text="Name a new profile",FontSize=13,Foreground=Muted,IsHitTestVisible=false,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(26,0,0,0)};   // where the typed text starts
            nameBox.TextChanged+=(s,e)=>nameHint.Visibility=nameBox.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
            var nameField=new Grid();nameField.Children.Add(nameBox);nameField.Children.Add(nameHint);
            var saveButton=Action("+  Save current settings",()=>{SaveCurrentAsProfile(nameBox.Text);nameBox.Text="";});
            saveButton.Padding=new Thickness(14,7,14,7);saveButton.MinHeight=34;saveButton.Margin=new Thickness(0);
            DockPanel.SetDock(saveButton,Dock.Right);
            saveRow.Children.Add(saveButton);saveRow.Children.Add(nameField);
            looks.Children.Add(saveRow);
            stack.Children.Add(looks);
            var auto=Section("Switch automatically","While a game with a profile runs, its settings are used, and yours come back when it closes. Lucent never starts, closes or touches a game.");
            auto.Children.Add(AutoToggle("Use a game's profile while it is running"));
            auto.Children.Add(Toggle("Show a notice when a game's profile is applied",preferences.ProfileToast,v=>{preferences.ProfileToast=v;SaveSoon();}));
            stack.Children.Add(auto);
            RefreshProfiles();
            return stack;
        }
        TextBlock Explain(string text)
        {
            var line=Text(text,13);
            line.Foreground=Muted;line.TextWrapping=TextWrapping.Wrap;line.Margin=new Thickness(0,8,0,0);
            return line;
        }
        // The games found on this PC, each a tile with its icon. Picking one gives it its own look, which then appears under Your looks.
        List<LibraryGame> libraryGames=new List<LibraryGame>();
        WrapPanel libraryPanel;
        TextBlock libraryNote;FrameworkElement libraryNoteRow;
        bool libraryScanned,libraryScanning;
        FrameworkElement GameLibraryCard()
        {
            var card=Section("Your games","");
            libraryNote=Text("",13);
            libraryNote.Foreground=Muted;
            // While it looks, and when it finds nothing, a magnifier searches over a controller beside the words.
            libraryNoteRow=EmptyRow(EmptyArt.Searching(54),libraryNote);
            card.Children.Add(libraryNoteRow);
            libraryPanel=new WrapPanel {Margin=new Thickness(0,4,0,4)};
            card.Children.Add(libraryPanel);
            var actions=new WrapPanel();
            actions.Children.Add(Action("Scan again",()=>ScanGames(true)));
            actions.Children.Add(Action("+   Add another game",AddGame));
            card.Children.Add(actions);
            ShowLibrary();
            return card;
        }
        // Only for the pictures of the app (tests): lets a test-mode window show the games' own icons, which it leaves out otherwise.
        internal static bool ShowGameIcons;
        void ScanGames(bool force)
        {
            if(TestMode||libraryScanning||(libraryScanned&&!force))return;
            libraryScanning=true;
            libraryNote.Text="Looking for your games\u2026";
            Task.Run(()=>GameLibrary.Scan()).ContinueWith(task=>Dispatcher.BeginInvoke(new Action(()=>
            {
                libraryScanning=false;libraryScanned=true;
                libraryGames=task.Status==TaskStatus.RanToCompletion?task.Result:new List<LibraryGame>();
                ShowLibrary();
            })));
        }
        internal void SetLibrary(List<LibraryGame> games){libraryGames=games;libraryScanned=true;ShowLibrary();}
        GameProfile ProfileFor(LibraryGame game){return preferences.Profiles.FirstOrDefault(p=>string.Equals(p.Executable,game.Executable,StringComparison.OrdinalIgnoreCase));}
        void ShowLibrary()
        {
            if(libraryPanel==null)return;
            libraryPanel.Children.Clear();tilePlay.Clear();
            libraryNote.Text=libraryScanning?"Looking for your games\u2026":libraryGames.Count==0?(libraryScanned?"No games found from Steam, Epic, GOG, EA, Ubisoft, Battle.net, Riot or Xbox. Use Add another game for anything else.":""):"";
            libraryNoteRow.Visibility=libraryNote.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
            foreach(var game in libraryGames)libraryPanel.Children.Add(LibraryTile(game));
        }
        FrameworkElement LibraryTile(LibraryGame game)
        {
            bool added=ProfileFor(game)!=null;
            var face=new Grid {Width=46,Height=46,HorizontalAlignment=HorizontalAlignment.Center};
            var placeholder=Design.Icon("Profiles",26);
            placeholder.Opacity=.5;
            placeholder.HorizontalAlignment=HorizontalAlignment.Center;placeholder.VerticalAlignment=VerticalAlignment.Center;
            var picture=new Image {Stretch=Stretch.Uniform};
            RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.HighQuality);
            face.Children.Add(placeholder);
            face.Children.Add(picture);
            if(game.Picture!=null){picture.Source=game.Picture;placeholder.Visibility=Visibility.Collapsed;}
            else if(!TestMode||ShowGameIcons)GameLibrary.LoadIcon(game.Executable,Dispatcher,source=>{picture.Source=source;placeholder.Visibility=Visibility.Collapsed;});
            // The top of the tile only shows the game (it is not a button, so clicking it does not feel like starting the game).
            // Under it, two clear buttons: one gives the game a profile (or opens its settings once it has one), one starts the game.
            var content=new StackPanel {HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(6,12,6,8)};
            content.Children.Add(face);
            content.Children.Add(new TextBlock {Text=game.Name,FontSize=12,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=104,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,8,0,0)});
            content.Children.Add(new TextBlock {Text=game.Store,FontSize=11,Foreground=Muted,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,2,0,0)});
            var profileButton=Action(added?"\u2713  Profile":"+  Add profile",()=>
            {
                var existing=ProfileFor(game);
                if(existing==null){AddLibraryGame(game);return;}
                openProfileSettings.Add(existing.Name);RefreshProfiles();SetStatus("Game settings for "+existing.Name+" are open under Your profiles");
            });
            profileButton.Margin=new Thickness(8,0,8,6);profileButton.Padding=new Thickness(0,7,0,7);profileButton.MinHeight=0;profileButton.FontSize=12;
            profileButton.HorizontalContentAlignment=HorizontalAlignment.Center;profileButton.HorizontalAlignment=HorizontalAlignment.Stretch;
            System.Windows.Automation.AutomationProperties.SetName(profileButton,(added?"Profile for ":"Add profile for ")+game.Name);
            var play=Action("\u25B6  Play",()=>PlayGame(game.Launch,game.Executable,game.Name));
            tilePlay.Add(new PlayButton {Button=play,Executable=game.Executable});ShowPlayState(tilePlay[tilePlay.Count-1]);
            play.Margin=new Thickness(8,0,8,8);play.Padding=new Thickness(0,7,0,7);play.MinHeight=0;play.FontSize=12;play.HorizontalContentAlignment=HorizontalAlignment.Center;play.HorizontalAlignment=HorizontalAlignment.Stretch;
            System.Windows.Automation.AutomationProperties.SetName(play,"Play "+game.Name);
            var column=new StackPanel();
            // A profile is added from the slot under Your profiles; the tile only plays the game.
            column.Children.Add(content);column.Children.Add(play);
            return new Border {Width=124,Margin=new Thickness(0,0,8,8),CornerRadius=new CornerRadius(12),Background=added?selectionSurface:optionSurface,Child=column,Tag=game.Name};
        }
        internal string lastLaunch;
        // Starts a game the way its launcher would. Lucent never closes or changes a game that is running.
        void PlayGame(string launch,string executable,string name)
        {
            if(TestMode){lastLaunch=string.IsNullOrEmpty(launch)?executable:launch;SetStatus("Starting "+name);MarkStarting(executable);return;}
            string problem=GameLibrary.Start(launch,executable);
            SetStatus(problem??"Starting "+name+"\u2026");
            if(problem==null)MarkStarting(executable);
        }
        // ---- What the Play buttons say: Play, then Starting while the launcher gets going, then Playing while the game is running. The last two are green.
        sealed class PlayButton{public Button Button;public string Executable;}
        readonly List<PlayButton> tilePlay=new List<PlayButton>(),rowPlay=new List<PlayButton>();
        readonly Dictionary<string,DateTime> startingUntil=new Dictionary<string,DateTime>();
        HashSet<string> runningGames=new HashSet<string>();
        // Tests replace this so no process is looked at.
        internal Func<string,bool> IsGameRunning=executable=>IsRunning(executable);
        static readonly SolidColorBrush PlayGreen=new SolidColorBrush(Color.FromRgb(36,138,76));
        static string GameKey(string executable){return (executable??"").ToLowerInvariant();}
        void MarkStarting(string executable)
        {
            Count(c=>c.GamesStarted++);
            startingUntil[GameKey(executable)]=DateTime.UtcNow.AddSeconds(25);
            ShowPlayStates();
        }
        void ShowPlayStates(){foreach(var entry in tilePlay.Concat(rowPlay).ToArray())ShowPlayState(entry);}
        void ShowPlayState(PlayButton entry)
        {
            string key=GameKey(entry.Executable);DateTime until;
            bool running=runningGames.Contains(key),starting=!running&&startingUntil.TryGetValue(key,out until)&&DateTime.UtcNow<until;
            var button=entry.Button;
            if(running||starting)
            {
                button.Content=running?"\u25CF  Playing":"Starting\u2026";
                button.Background=PlayGreen;button.Foreground=Brushes.White;
            }
            else
            {
                button.Content="\u25B6  Play";
                button.ClearValue(Button.BackgroundProperty);button.ClearValue(Button.ForegroundProperty);
            }
        }
        // Looks at which of the listed games are running, off the UI thread, and updates the buttons.
        internal async Task RefreshPlayStates()
        {
            if(closing)return;
            var games=tilePlay.Concat(rowPlay).Select(p=>p.Executable).Where(e=>!string.IsNullOrEmpty(e)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(games.Count==0)return;
            var check=IsGameRunning;
            var found=await Task.Run(()=>games.Where(e=>check(e)).Select(GameKey).ToList());
            if(closing)return;
            runningGames=new HashSet<string>(found);
            foreach(string key in found)startingUntil.Remove(key);
            ShowPlayStates();
        }
        void AddLibraryGame(LibraryGame game)
        {
            if(ProfileFor(game)!=null){SetStatus(game.Name+" already has its own look. Change it under Your looks.");return;}
            string name=game.Name;int copy=2;
            while(preferences.Profiles.Any(p=>p.Name==name||name=="Desktop"))name=game.Name+" ("+copy++ +")";
            preferences.Profiles.Add(new GameProfile {Name=name,Executable=game.Executable,Launch=game.Launch??"",Look=current.Copy()});
            SaveSoon();
            RefreshProfiles();
            SetStatus("Added "+name+" with your current colors");
        }
        // Keeps the settings in use now as a profile that is not tied to a game (no program file, so it never switches by itself).
        // A name that is already a saved profile of this kind updates it; any other clash gets a number.
        internal void SaveCurrentAsProfile(string text)
        {
            string name=(text??"").Trim();
            if(name.Length==0)name="My profile "+(preferences.Profiles.Count(p=>string.IsNullOrEmpty(p.Executable))+1);
            var same=preferences.Profiles.FirstOrDefault(p=>string.IsNullOrEmpty(p.Executable)&&p.Name==name);
            if(same!=null){same.Look=current.Copy();SaveSoon();SetStatus("Updated "+name);RefreshProfiles();return;}
            string unique=name;int copy=2;
            while(unique=="Desktop"||preferences.Profiles.Any(p=>p.Name==unique))unique=name+" ("+copy++ +")";
            preferences.Profiles.Add(new GameProfile {Name=unique,Executable="",Look=current.Copy()});
            SaveSoon();
            RefreshProfiles();
            SetStatus("Saved "+unique);
        }
        void AddGame()
        {
            var open=new Microsoft.Win32.OpenFileDialog {Filter="Game executable|*.exe",Title="Choose game executable"};
            if(open.ShowDialog()!=true)return;
            preferences.Profiles.Add(new GameProfile {Name=Path.GetFileNameWithoutExtension(open.FileName),Executable=open.FileName,Look=current.Copy()});
            SaveSoon();
            RefreshProfiles();
        }
        void RefreshProfiles()
        {
            if(profilesList==null)return;
            profilesList.Children.Clear();rowPlay.Clear();
            profilesList.Children.Add(ProfileRow("Desktop","Your normal look, used whenever no game is running",null));
            foreach(var saved in preferences.Profiles.ToArray())profilesList.Children.Add(ProfileRow(saved.Name,string.IsNullOrEmpty(saved.Executable)?"Saved settings, not tied to a game":Path.GetFileName(saved.Executable),saved));
            // Always last in the list: a button to add a game's profile (another one below each one added).
            {
                // A slot the shape of a profile row, with a plus.
                var slot=new Grid {Margin=new Thickness(0,0,0,8),MinHeight=64};
                var edge=new Border {CornerRadius=new CornerRadius(14),BorderBrush=new SolidColorBrush(Color.FromArgb(46,255,255,255)),BorderThickness=new Thickness(1),Background=new SolidColorBrush(Color.FromArgb(0,255,255,255))};
                slot.Children.Add(edge);
                var line=new DockPanel {Margin=new Thickness(14,12,14,12)};
                var plus=new Grid {Width=40,Height=40,Margin=new Thickness(0,0,14,0),VerticalAlignment=VerticalAlignment.Center};
                plus.Children.Add(new System.Windows.Shapes.Ellipse {Fill=new SolidColorBrush(Color.FromArgb(18,255,255,255))});
                var mark=new System.Windows.Shapes.Path {Data=Geometry.Parse("M12,6 L12,18 M6,12 L18,12"),Stroke=new SolidColorBrush(Color.FromArgb(200,255,255,255)),StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Width=16,Height=16,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
                plus.Children.Add(mark);
                DockPanel.SetDock(plus,Dock.Left);line.Children.Add(plus);
                var words=new StackPanel {VerticalAlignment=VerticalAlignment.Center};
                words.Children.Add(new TextBlock {Text="Add game profile",FontSize=14,FontWeight=FontWeights.SemiBold});
                line.Children.Add(words);
                slot.Children.Add(line);
                // The whole slot is a button: it lights under the pointer and opens the list of your games.
                // (the light is on the rounded edge's own background, so its corners stay round)
                slot.Cursor=System.Windows.Input.Cursors.Hand;
                slot.MouseEnter+=(s,e)=>((SolidColorBrush)edge.Background).Color=Color.FromArgb(14,255,255,255);
                slot.MouseLeave+=(s,e)=>((SolidColorBrush)edge.Background).Color=Color.FromArgb(0,255,255,255);
                slot.MouseLeftButtonUp+=(s,e)=>{e.Handled=true;PickGameForProfile(slot);};
                System.Windows.Automation.AutomationProperties.SetName(slot,"Add a game profile");
                profilesList.Children.Add(slot);
            }
            ShowLibrary();
        }
        // A list under the empty slot: your games without a profile, with their icons. Picking one gives it a profile (as Add profile on its tile does);
        // "Another game" opens the file picker for anything else.
        void PickGameForProfile(FrameworkElement under)
        {
            if(!libraryScanned)ScanGames(false);
            var rows=new StackPanel {Margin=new Thickness(6)};
            var popup=new System.Windows.Controls.Primitives.Popup {PlacementTarget=under,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom,StaysOpen=false,AllowsTransparency=true,PopupAnimation=System.Windows.Controls.Primitives.PopupAnimation.Fade,VerticalOffset=6};
            Func<FrameworkElement,string,Action,Border> row=(face,title,run)=>
            {
                var dock=new DockPanel();
                face.Margin=new Thickness(0,0,12,0);face.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(face,Dock.Left);dock.Children.Add(face);
                dock.Children.Add(new TextBlock {Text=title,FontSize=14,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis});
                var item=new Border {CornerRadius=new CornerRadius(10),Padding=new Thickness(10,8,10,8),Background=new SolidColorBrush(Color.FromArgb(0,255,255,255)),Cursor=System.Windows.Input.Cursors.Hand,Child=dock};
                item.MouseEnter+=(s,e)=>((SolidColorBrush)item.Background).Color=Color.FromArgb(24,255,255,255);
                item.MouseLeave+=(s,e)=>((SolidColorBrush)item.Background).Color=Color.FromArgb(0,255,255,255);
                item.MouseLeftButtonUp+=(s,e)=>{e.Handled=true;popup.IsOpen=false;run();};
                System.Windows.Automation.AutomationProperties.SetName(item,title);
                return item;
            };
            var games=libraryGames.Where(g=>ProfileFor(g)==null).ToList();
            if(games.Count==0)
                rows.Children.Add(new TextBlock {Text=libraryScanning?"Looking for your games…":"No games found on this PC.",FontSize=13,Foreground=Muted,Margin=new Thickness(10,8,10,8)});
            foreach(var game in games)
            {
                var chosen=game;
                var icon=new Image {Width=28,Height=28};RenderOptions.SetBitmapScalingMode(icon,BitmapScalingMode.HighQuality);
                var face=new Grid {Width=28,Height=28};var mark=Design.Icon("Profiles",18);face.Children.Add(mark);face.Children.Add(icon);
                if(!TestMode||ShowGameIcons)GameLibrary.LoadIcon(chosen.Executable,Dispatcher,source=>{icon.Source=source;mark.Visibility=Visibility.Collapsed;});
                rows.Children.Add(row(face,chosen.Name,()=>AddLibraryGame(chosen)));
            }
            var other=Design.Icon("Search",18);
            rows.Children.Add(row(new Grid {Width=28,Height=28,Children={other}},"Another game…",AddGame));
            var card=new Border {Background=new SolidColorBrush(Color.FromRgb(28,28,31)),BorderBrush=new SolidColorBrush(Color.FromArgb(40,255,255,255)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),
                Width=Math.Max(260,Math.Min(420,under.ActualWidth)),Child=new ScrollViewer {MaxHeight=340,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=rows},
                Effect=new System.Windows.Media.Effects.DropShadowEffect {BlurRadius=24,ShadowDepth=6,Opacity=.5,Color=Colors.Black}};
            popup.Child=new Border {Padding=new Thickness(0,0,16,16),Child=card};
            popup.IsOpen=true;
        }
        // One look: who it is for, whether it is the one in use, and the few things you can do with it.
        FrameworkElement ProfileRow(string name,string detail,GameProfile profile)
        {
            bool active=activeProfile==name;
            Look look=profile==null?preferences.Desktop:profile.Look;
            var row=new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            var icon=Design.Icon(profile==null?"Monitor":"Profiles",20);
            icon.HorizontalAlignment=HorizontalAlignment.Center;
            icon.VerticalAlignment=VerticalAlignment.Center;
            // A game shows its own icon once it has been read from the program's file.
            var iconFace=new Grid();
            iconFace.Children.Add(icon);
            if(profile!=null&&!TestMode&&!string.IsNullOrEmpty(profile.Executable))
            {
                var gameIcon=new Image {Width=28,Height=28,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
                RenderOptions.SetBitmapScalingMode(gameIcon,BitmapScalingMode.HighQuality);
                iconFace.Children.Add(gameIcon);
                GameLibrary.LoadIcon(profile.Executable,Dispatcher,source=>{gameIcon.Source=source;icon.Visibility=Visibility.Collapsed;});
            }
            row.Children.Add(new Border {Width=40,Height=40,CornerRadius=new CornerRadius(12),Background=new SolidColorBrush(Color.FromArgb((byte)(active?46:22),255,255,255)),Margin=new Thickness(0,0,14,0),VerticalAlignment=VerticalAlignment.Center,Child=iconFace});
            var labels=new StackPanel {VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};
            labels.Children.Add(new TextBlock {Text=name,FontSize=15,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis});
            var sub=new TextBlock {Text=detail,FontSize=12,Foreground=Muted,TextTrimming=TextTrimming.CharacterEllipsis};
            if(profile!=null)sub.ToolTip=profile.Executable;
            labels.Children.Add(sub);
            Grid.SetColumn(labels,1);
            row.Children.Add(labels);
            var actions=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            Grid.SetColumn(actions,2);
            row.Children.Add(actions);
            if(active)
                // Centered on the row whatever else sits beside it; stretching it to the height of the buttons left its text at the top.
                actions.Children.Add(new Border {CornerRadius=new CornerRadius(9),Background=Brushes.White,Padding=new Thickness(12,0,12,0),Height=30,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock {Text="In use",FontSize=12,FontWeight=FontWeights.Bold,Foreground=Brushes.Black,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}});
            else
            {
                var use=Action("Use",()=>LoadLook(look,name));
                use.Padding=new Thickness(16,7,16,7);use.MinHeight=0;use.Margin=new Thickness(0,0,8,0);
                actions.Children.Add(use);
            }
            if(profile!=null)
            {
                if(!string.IsNullOrEmpty(profile.Executable))
                {
                    var playRow=Action("\u25B6  Play",()=>PlayGame(profile.Launch,profile.Executable,profile.Name));
                    rowPlay.Add(new PlayButton {Button=playRow,Executable=profile.Executable});ShowPlayState(rowPlay[rowPlay.Count-1]);
                    playRow.Padding=new Thickness(14,7,14,7);playRow.MinHeight=0;playRow.Margin=new Thickness(0,0,6,0);
                    System.Windows.Automation.AutomationProperties.SetName(playRow,"Play "+profile.Name);
                    actions.Children.Add(playRow);
                }
                if(!string.IsNullOrEmpty(profile.Executable))
                {
                    // Opens the game's own settings (resolution and crosshair) under the row.
                    bool open=openProfileSettings.Contains(profile.Name);
                    var more=Action(open?"Done":"Game settings",()=>
                    {
                        if(!openProfileSettings.Remove(profile.Name))openProfileSettings.Add(profile.Name);
                        RefreshProfiles();
                    });
                    more.Padding=new Thickness(14,7,14,7);more.MinHeight=0;more.Margin=new Thickness(0,0,6,0);
                    System.Windows.Automation.AutomationProperties.SetName(more,"Game settings for "+profile.Name);
                    actions.Children.Add(more);
                }
                var update=Action("Update look",()=>
                {
                    profile.Look=current.Copy();
                    SaveSoon();
                    SetStatus("Saved the current colors for "+profile.Name);
                });
                update.Padding=new Thickness(14,7,14,7);update.MinHeight=0;update.Margin=new Thickness(0,0,6,0);
                actions.Children.Add(update);
                var remove=Action("",()=>
                {
                    preferences.Profiles.Remove(profile);
                    SaveSoon();
                    if(activeProfile==profile.Name)LoadLook(preferences.Desktop,"Desktop");
                    else RefreshProfiles();
                });
                remove.Style=(Style)Resources["TrashAction"];
                remove.Content=Design.Icon("Trash",16);
                remove.Width=36;remove.Height=32;remove.MinHeight=0;remove.Padding=new Thickness(0);remove.Margin=new Thickness(0);
                remove.HorizontalContentAlignment=HorizontalAlignment.Center;
                remove.ToolTip="Remove "+profile.Name;
                System.Windows.Automation.AutomationProperties.SetName(remove,"Remove "+profile.Name);
                actions.Children.Add(remove);
            }
            UIElement body=row;
            if(profile!=null&&!string.IsNullOrEmpty(profile.Executable)&&openProfileSettings.Contains(profile.Name))
            {
                var both=new StackPanel();both.Children.Add(row);both.Children.Add(GameSettingsPanel(profile));body=both;
            }
            return new Border {Background=active?selectionSurface:optionSurface,CornerRadius=new CornerRadius(14),Padding=new Thickness(14,12,12,12),Margin=new Thickness(0,0,0,8),Child=body};
        }
        readonly HashSet<string> openProfileSettings=new HashSet<string>();
        // The parts of a look that are not neutral, in words: "Vibrance 150%", "Night Vision 60%, Smart shadows", "Color pop: Red".
        internal static List<string> LookChanges(Look look)
        {
            var list=new List<string>();
            Func<double,string> pct=v=>Math.Round(v).ToString(System.Globalization.CultureInfo.InvariantCulture)+"%";
            if(look.Saturation>Look.MinimumVibrance+.5)list.Add("Vibrance "+pct(look.Saturation));
            if(look.Red>.5)list.Add("Red filter "+pct(look.Red));
            if(look.Dim>.5)list.Add("Screen dim "+pct(look.Dim));
            string vision=StateStore.CanonicalVision(look.Vision);
            if(vision!="Normal")list.Add(vision+(look.VisionStrength<99.5?" "+pct(look.VisionStrength):""));
            if(look.DarkEnabled)list.Add("Night Vision "+pct(look.DarkStrength)+(look.DarkColor!="Green"?", "+look.DarkColor:""));
            if(look.PopColor!="Off")list.Add("Color pop: "+look.PopColor);
            if(look.RadarOn)list.Add("Sound radar");
            return list;
        }
        // A game's own settings: the resolution of the main display and a crosshair, each used only while the game runs.
        FrameworkElement GameSettingsPanel(GameProfile profile)
        {
            var panel=new StackPanel {Margin=new Thickness(54,12,0,2)};
            // What the profile's look changes, only what differs from neutral (100% vibrance and the like is left out).
            var changed=LookChanges(profile.Look);
            var looksLine=Text(changed.Count==0?"Colors: neutral, nothing changed":"Colors: "+string.Join("  ·  ",changed.ToArray()),13,true);
            looksLine.Margin=new Thickness(0,0,0,2);panel.Children.Add(looksLine);
            var hint=Text("Used while "+profile.Name+" runs, and put back when it closes. Update look saves what you have now.",12);
            hint.Foreground=Muted;panel.Children.Add(hint);
            // Resolution: the sizes the main display supports at its current refresh rate.
            var primary=DisplayService.GetDisplays().FirstOrDefault(d=>d.IsPrimary);
            var now=primary==null||TestMode?null:DisplayService.GetCurrentMode(primary.DeviceName);
            var sizes=new List<string>();
            if(now!=null)sizes=DisplayService.GetModes(primary.DeviceName).Where(m=>m.Frequency==now.Frequency&&m.BitsPerPixel==now.BitsPerPixel)
                .GroupBy(m=>new{m.Width,m.Height}).Select(g=>g.First()).OrderByDescending(m=>m.Width*m.Height).Select(m=>m.Width+" × "+m.Height).ToList();
            string chosen=profile.ResolutionWidth>0?profile.ResolutionWidth+" × "+profile.ResolutionHeight:(now!=null?now.Width+" × "+now.Height:"");
            if(chosen.Length>0&&!sizes.Contains(chosen))sizes.Insert(0,chosen);
            var size=new ComboBox {ItemsSource=sizes,SelectedItem=chosen,MinHeight=36,Width=170,HorizontalAlignment=HorizontalAlignment.Left,FontSize=13,FontWeight=FontWeights.SemiBold,ItemTemplate=Design.TextTemplate(null),IsEnabled=profile.UseResolution,Margin=new Thickness(0,0,0,6)};
            System.Windows.Automation.AutomationProperties.SetName(size,"Resolution for "+profile.Name);
            Action<string> keepSize=text=>
            {
                int w,h;var parts=(text??"").Split('×');
                if(parts.Length==2&&int.TryParse(parts[0].Trim(),out w)&&int.TryParse(parts[1].Trim(),out h)){profile.ResolutionWidth=w;profile.ResolutionHeight=h;SaveSoon();}
            };
            size.SelectionChanged+=(s,e)=>keepSize(size.SelectedItem as string);
            panel.Children.Add(Toggle("Set the resolution",profile.UseResolution,v=>
            {
                profile.UseResolution=v;size.IsEnabled=v;
                if(v&&profile.ResolutionWidth==0)keepSize(size.SelectedItem as string);
                SaveSoon();
            }));
            panel.Children.Add(size);
            // Crosshair: a copy of a crosshair kept with the game, shown or hidden while it runs.
            var picture=PixelPicture();
            var pictureBox=new Border {Width=56,Height=56,CornerRadius=new CornerRadius(9),Background=new SolidColorBrush(Color.FromArgb(80,0,0,0)),Child=picture,Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};
            Action showPicture=()=>{var spec=(profile.Crosshair??CrosshairLook).Copy();spec.OffsetX=spec.OffsetY=0;ShowCrosshairPicture(picture,spec,48);};
            showPicture();
            var crossRow=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,4),IsEnabled=profile.UseCrosshair};
            crossRow.Children.Add(pictureBox);
            var take=Action("Use my current crosshair",()=>{profile.Crosshair=CrosshairLook.Copy();showPicture();SaveSoon();SetStatus(profile.Name+" will use this crosshair");});
            take.VerticalAlignment=VerticalAlignment.Center;
            crossRow.Children.Add(take);
            var show=Toggle("Show the crosshair",profile.CrosshairOn,v=>{profile.CrosshairOn=v;SaveSoon();});
            show.IsEnabled=profile.UseCrosshair;
            panel.Children.Add(Toggle("Use a crosshair for this game",profile.UseCrosshair,v=>
            {
                profile.UseCrosshair=v;crossRow.IsEnabled=show.IsEnabled=v;
                if(v&&profile.Crosshair==null){profile.Crosshair=CrosshairLook.Copy();showPicture();}
                SaveSoon();
            }));
            panel.Children.Add(crossRow);
            panel.Children.Add(show);
            return panel;
        }
        string lastAutoProfile;
        // Auto-switch only reacts when a game starts or exits, so a profile picked by hand stays put in between.
        async void PollProfiles()
        {
            if(!preferences.AutoProfiles||profilePolling)return;
            profilePolling=true;
            try
            {
                var profiles=preferences.Profiles.ToArray();
                var match=await Task.Run(()=>FindRunningProfile(profiles));
                if(closing)return;
                string running=match==null?null:match.Executable;
                if(running==lastAutoProfile)return;
                lastAutoProfile=running;
                // A game closed (or another took its place): what its profile changed goes back first.
                RestoreAfterGame();
                string name=match==null?"Desktop":match.Name;
                if(name!=activeProfile)LoadLook(match==null?preferences.Desktop:match.Look,name);
                if(match!=null)ApplyGameExtras(match);
            }
            finally{profilePolling=false;}
        }
        // ---- What a game's profile changes beyond the look, and how it is put back.
        // Kept while the game runs: the main display's mode before the switch, and the crosshair (and whether it was shown) before it.
        sealed class GameRestore{public string Device;public int Width,Height,SetWidth,SetHeight;public CrosshairSettings Crosshair;public bool CrosshairWasOn,CrosshairChanged;}
        GameRestore gameRestore;
        void ApplyGameExtras(GameProfile game)
        {
            var restore=new GameRestore();var applied=new List<string>();
            if(game.UseResolution&&game.ResolutionWidth>0&&!TestMode)
            {
                var primary=DisplayService.GetDisplays().FirstOrDefault(d=>d.IsPrimary);
                var before=primary==null?null:DisplayService.GetCurrentMode(primary.DeviceName);
                if(before!=null&&(before.Width!=game.ResolutionWidth||before.Height!=game.ResolutionHeight))
                {
                    string problem=SetModeQuietly(primary.DeviceName,game.ResolutionWidth,game.ResolutionHeight);
                    if(problem==null){restore.Device=primary.DeviceName;restore.Width=before.Width;restore.Height=before.Height;restore.SetWidth=game.ResolutionWidth;restore.SetHeight=game.ResolutionHeight;applied.Add(game.ResolutionWidth+" × "+game.ResolutionHeight);}
                    else SetStatus(game.Name+": "+problem);
                }
                else if(before!=null)applied.Add(game.ResolutionWidth+" × "+game.ResolutionHeight);
            }
            if(game.UseCrosshair&&game.Crosshair!=null)
            {
                restore.Crosshair=preferences.Crosshair.Copy();restore.CrosshairWasOn=crosshair!=null;restore.CrosshairChanged=true;
                SetCrosshair(game.Crosshair.Copy(),game.CrosshairOn);
                applied.Add(game.CrosshairOn?"crosshair":"crosshair off");
            }
            if(current.DarkEnabled)applied.Add("Night Vision");
            if(current.Saturation>Look.MinimumVibrance+.5)applied.Add("vibrance "+Math.Round(current.Saturation)+"%");
            gameRestore=restore;
            if(preferences.ProfileToast&&!TestMode)GameToast.Show("Applied: "+game.Name+" profile",string.Join("  ·  ",applied.ToArray()),game.Executable);
        }
        // Puts back what the last game's profile changed. The resolution goes back only if it is still the one the profile set (you may have changed it since).
        void RestoreAfterGame()
        {
            var restore=gameRestore;gameRestore=null;
            if(restore==null)return;
            if(restore.Device!=null&&!TestMode)
            {
                var now=DisplayService.GetCurrentMode(restore.Device);
                if(now!=null&&now.Width==restore.SetWidth&&now.Height==restore.SetHeight)SetModeQuietly(restore.Device,restore.Width,restore.Height);
            }
            if(restore.CrosshairChanged)SetCrosshair(restore.Crosshair,restore.CrosshairWasOn);
        }
        // A display size on one display, keeping its refresh rate and color depth, with no questions (it is put back later). Returns why it failed, or null.
        string SetModeQuietly(string device,int width,int height)
        {
            var before=DisplayService.GetCurrentMode(device);if(before==null)return "the display is not available";
            var target=DisplayService.FindResolutionOnlyMode(DisplayService.GetModes(device),width,height,before);
            if(target==null)return width+" × "+height+" is not available at "+before.Frequency+" Hz";
            using(var gate=new Mutex(false,LegacyNames.CustomTrialMutex))
            {
                bool owns=false;
                try
                {
                    try{owns=gate.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
                    if(!owns)return "another display test is active";
                    var result=DisplayService.Apply(target,before,false,false);
                    if(!result.IsSuccess)return result.ErrorMessage;
                    Count(c=>c.ResolutionSwitches++);
                    RefreshDisplayViews();
                    return null;
                }
                finally{if(owns)gate.ReleaseMutex();}
            }
        }
        // Replaces the crosshair everywhere (the page's controls, the overlay) and shows or hides it.
        void SetCrosshair(CrosshairSettings spec,bool on)
        {
            preferences.Crosshair=spec;
            SyncCrosshairControls();RefreshCrosshairViews();
            if(crosshair!=null)ConfigureCrosshair();
            if(on!=(crosshair!=null))ToggleCrosshair();
        }
        // Closing the app while a game runs: the crosshair kept for the desktop is the one saved, not the game's.
        void RestoreCrosshairBeforeSaving()
        {
            if(gameRestore!=null&&gameRestore.CrosshairChanged)preferences.Crosshair=gameRestore.Crosshair;
        }
        internal static GameProfile FindRunningProfile(IEnumerable<GameProfile> profiles)
        {
            // A saved profile that is not tied to a game has no program and never matches.
            return profiles.FirstOrDefault(profile=>!string.IsNullOrEmpty(profile.Executable)&&IsRunning(profile.Executable));
        }
        static bool IsRunning(string executable)
        {
            Process[] processes;
            try{processes=Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));}
            catch(ArgumentException){return false;}
            try
            {
                // Never read a game's modules: anti-cheat blocks it, and it would touch the protected process.
                // The image path needs only limited rights. If even that is denied, the name match stands.
                return processes.Any(process=>
                {
                    string path=ProcessIdentity.ImagePath(process.Id);
                    return path==null||string.Equals(path,executable,StringComparison.OrdinalIgnoreCase);
                });
            }
            finally{foreach(var process in processes)process.Dispose();}
        }
        void LoadLook(Look value,string name)
        {
            if(name!="Desktop"&&name!=activeProfile)Count(c=>c.ProfileSwitches++);
            current=value.Copy();
            if(current.DarkEnabled&&!preferences.RiskAccepted)current.DarkEnabled=false;
            activeProfile=name;
            building=true;
            foreach(var entry in sliders)
            {
                entry.Value.Value=LookValue(entry.Key);
            }
            building=false;
            darkBoostToggle.IsChecked=current.DarkEnabled;
            if(radarToggle!=null)radarToggle.IsChecked=current.RadarOn;
            UpdateLookChoices();
            applyLook=true;
            UpdateEffects();
            RefreshProfiles();
            UpdateHome();
            SetStatus("Loaded "+name+" profile");
        }
        readonly List<Button> visionChoices=new List<Button>(),darkColorChoices=new List<Button>();
        // The strength of the color blindness aid shows only while one of the aids is chosen.
        StackPanel visionStrengthBox;
        void UpdateLookChoices()
        {
            if(visionStrengthBox!=null)visionStrengthBox.Visibility=StateStore.CanonicalVision(current.Vision)=="Normal"?Visibility.Collapsed:Visibility.Visible;
            foreach(var chip in visionChoices)StyleChoice(chip,Equals(chip.Tag,current.Vision));
            foreach(var chip in darkColorChoices)StyleChoice(chip,Equals(chip.Tag,current.DarkColor));
            foreach(var chip in popChoices)StyleChoice(chip,Equals(chip.Tag,current.PopColor));
            foreach(var chip in zoomSharpChoices)StyleChoice(chip,Equals(chip.Tag,current.ZoomSharp));
            if(zoomManualBox!=null)zoomManualBox.Visibility=current.ZoomSharp=="Manual"?Visibility.Visible:Visibility.Collapsed;
            foreach(var chip in radarSizeChoices)StyleChoice(chip,Equals(chip.Tag,current.RadarSize));
            foreach(var chip in radarPlaceChoices)StyleChoice(chip,Equals(chip.Tag,current.RadarPlace));
        }
        double LookValue(string key)
        {
            switch(key)
            {
                case "Vibrance":return current.Saturation;
                case "Red Filter":return current.Red;
                case "Screen Dim":return current.Dim;
                case "Strength":return current.DarkStrength;
                case "Vision strength":return current.VisionStrength;
                case "Black level":return current.BlackLevel;
                case "Zoom level":return current.Zoom;
                case "Sharpness":return current.ZoomSharpness;
                case "Pop strength":return current.PopStrength;
                case "Radar sensitivity":return current.RadarSensitivity;
                case "Radar opacity":return current.RadarOpacity;
                default:return 0;
            }
        }
    }
}
