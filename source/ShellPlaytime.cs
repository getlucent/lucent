using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TextBlock=Lucent.SmoothText;

namespace Lucent
{
    // Playtime: how long each game runs, per day, kept on this PC only. Every 30 seconds Lucent looks at which game is running (the same way game profiles
    // and the Discord status find it: a game with a profile, a game Discord knows when the Discord status is on, or a game the launchers installed) and adds
    // that time to today's entry. Home shows this week's total, the most played game and a bar for each of the last seven days.
    internal sealed partial class Shell
    {
        readonly DispatcherTimer playtimeTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(30)};
        bool readingPlaytime;
        DateTime lastPlaytimeTick;
        string playingNow,playingProgram;
        void StartPlaytime()
        {
            playtimeTimer.Tick+=async(s,e)=>await TrackPlaytime();
            lastPlaytimeTick=DateTime.UtcNow;
            playtimeTimer.Start();
            ResolvePlaytimeIcons();
        }
        async Task TrackPlaytime()
        {
            if(readingPlaytime||closing||!preferences.TrackPlaytime)return;
            readingPlaytime=true;
            try
            {
                // The time since the last look (at most a minute, so a sleeping PC does not count as play).
                var now=DateTime.UtcNow;double minutes=Math.Min(1,(now-lastPlaytimeTick).TotalMinutes);lastPlaytimeTick=now;
                var profiles=preferences.Profiles.ToArray();bool discordList=preferences.DiscordOn;
                var game=await Task.Run(()=>RunningGameNow(profiles,discordList));
                if(closing)return;
                playingNow=game==null?null:game.Name;playingProgram=game==null?null:game.Executable;
                if(game==null||minutes<=0)return;
                // An entry that had no program yet gets it now, everywhere it appears.
                if(!string.IsNullOrEmpty(game.Executable))
                    foreach(var day in preferences.PlayTotals.Concat(preferences.Playtime))
                        if(string.IsNullOrEmpty(day.Executable)&&string.Equals(day.Game,game.Name,StringComparison.OrdinalIgnoreCase))day.Executable=game.Executable;
                AddPlaytime(preferences.Playtime,DateTime.Today,game.Name,game.Executable,minutes);
                AddTotal(preferences.PlayTotals,game.Name,game.Executable,minutes);
                if(statsRefresh!=null&&settingsCategory=="Stats")statsRefresh();
                SaveSoon();
                UpdateHome();
            }
            catch(Exception){}
            finally{readingPlaytime=false;}
        }
        // The game running right now, found by program names and folders only (a game is never opened or read).
        static DiscordGame RunningGameNow(GameProfile[] profiles,bool discordList)
        {
            var profile=FindRunningProfile(profiles);
            if(profile!=null)return new DiscordGame {Name=profile.Name,Executable=profile.Executable};
            if(discordList)
            {
                var names=RunningProgramNames();
                var known=DiscordGameIcons.FindRunning(names,ProgramPath);
                if(known!=null)
                {
                    // Where its program is, so its own icon can be shown (only the path is read).
                    string path=null;
                    foreach(string name in names)
                        if(ReferenceEquals(DiscordGameIcons.Lookup(name+".exe",null),known)){path=ProgramPath(name);if(path!=null)break;}
                    return new DiscordGame {Name=known.Name,Executable=path??InstalledExecutableFor(known.Name)};
                }
            }
            var installed=InstalledGameRunning();
            if(installed==null)return null;
            string shown=installed.Name,leaf=System.IO.Path.GetFileName(InstalledGames.Folder(installed.Executable)??"");
            if(shown==shown.ToUpperInvariant()&&leaf!=shown&&string.Equals(leaf,shown,StringComparison.OrdinalIgnoreCase))shown=leaf;
            return new DiscordGame {Name=shown,Executable=installed.Executable};
        }
        // The program of an installed game with this name (or whose folder has this name), from the launchers' records; null when none.
        static string InstalledExecutableFor(string game)
        {
            if(string.IsNullOrEmpty(game))return null;
            try
            {
                InstalledGameRunning();   // reads the launchers' records when they are missing or old
                var list=installedFolders;if(list==null)return null;
                foreach(var entry in list)
                {
                    string leaf=System.IO.Path.GetFileName(entry.Key.TrimEnd('\\'));
                    if(string.Equals(entry.Value.Name,game,StringComparison.OrdinalIgnoreCase)||string.Equals(leaf,game,StringComparison.OrdinalIgnoreCase))return entry.Value.Executable;
                }
            }
            catch(Exception){}
            return null;
        }
        // Games saved before their program was known get it now (once, in the background), so their icons show on Home and in Stats.
        bool iconsResolved;
        async void ResolvePlaytimeIcons()
        {
            if(iconsResolved||TestMode)return;iconsResolved=true;
            var missing=preferences.PlayTotals.Concat(preferences.Playtime).Where(d=>string.IsNullOrEmpty(d.Executable)).Select(d=>d.Game).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(missing.Count==0)return;
            var found=await Task.Run(()=>missing.Select(g=>new KeyValuePair<string,string>(g,InstalledExecutableFor(g))).Where(p=>p.Value!=null).ToList());
            if(closing||found.Count==0)return;
            foreach(var pair in found)
                foreach(var day in preferences.PlayTotals.Concat(preferences.Playtime))
                    if(string.IsNullOrEmpty(day.Executable)&&string.Equals(day.Game,pair.Key,StringComparison.OrdinalIgnoreCase))day.Executable=pair.Value;
            SaveSoon();UpdateHome();
            if(statsRefresh!=null)statsRefresh();
        }
        internal static void AddPlaytime(List<PlayDay> days,DateTime date,string game,string executable,double minutes)
        {
            string key=date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            var day=days.FirstOrDefault(d=>d.Date==key&&string.Equals(d.Game,game,StringComparison.OrdinalIgnoreCase));
            if(day==null){day=new PlayDay {Date=key,Game=game,Executable=executable??""};days.Add(day);}
            day.Minutes=Math.Min(1440,day.Minutes+minutes);
            if(string.IsNullOrEmpty(day.Executable)&&!string.IsNullOrEmpty(executable))day.Executable=executable;
        }
        internal static void AddTotal(List<PlayDay> totals,string game,string executable,double minutes)
        {
            var total=totals.FirstOrDefault(t=>string.Equals(t.Game,game,StringComparison.OrdinalIgnoreCase));
            if(total==null){total=new PlayDay {Game=game,Executable=executable??""};totals.Add(total);}
            total.Minutes+=minutes;
            if(string.IsNullOrEmpty(total.Executable)&&!string.IsNullOrEmpty(executable))total.Executable=executable;
        }
        // A friendly title for how much has been played in all.
        internal static string PlayTitle(double minutes)
        {
            if(minutes>=60*500)return "Veteran";
            if(minutes>=60*100)return "Dedicated";
            if(minutes>=60*20)return "Regular player";
            if(minutes>=60)return "Getting into it";
            return "Warming up";
        }
        // The last seven days (oldest first, today last): minutes per day, and the week's total and most played game.
        internal sealed class PlayWeek {public double[] Days=new double[7];public string[] Labels=new string[7];public double Total;public string TopGame,TopExecutable;public double TopMinutes;}
        internal static PlayWeek Week(List<PlayDay> days,DateTime today)
        {
            var week=new PlayWeek();var perGame=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);var exe=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<7;i++)
            {
                var date=today.AddDays(i-6);string key=date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                week.Labels[i]=i==6?"Today":date.ToString("ddd",CultureInfo.CurrentCulture);
                foreach(var day in days.Where(d=>d.Date==key))
                {
                    week.Days[i]+=day.Minutes;
                    double so;perGame.TryGetValue(day.Game,out so);perGame[day.Game]=so+day.Minutes;
                    if(!string.IsNullOrEmpty(day.Executable))exe[day.Game]=day.Executable;
                }
                week.Total+=week.Days[i];
            }
            if(perGame.Count>0)
            {
                var top=perGame.OrderByDescending(p=>p.Value).First();
                week.TopGame=top.Key;week.TopMinutes=top.Value;string path;exe.TryGetValue(top.Key,out path);week.TopExecutable=path;
            }
            return week;
        }
        // "3 h 12 min", "45 min", "under a minute".
        internal static string PlayLength(double minutes)
        {
            int m=(int)Math.Round(minutes);
            if(m<1)return "under a minute";
            return m<60?m+" min":(m/60)+" h "+(m%60).ToString("00",CultureInfo.InvariantCulture)+" min";
        }
        FrameworkElement HomePlaytimeCard()
        {
            var card=Section("Playtime","");
            var total=Text("",30,true);total.Margin=new Thickness(0,2,0,0);
            var totalCaption=Text("this week",13);totalCaption.Foreground=Muted;totalCaption.Margin=new Thickness(0,0,0,10);
            card.Children.Add(total);card.Children.Add(totalCaption);
            // The most played game, with its own icon when Lucent knows its program file.
            var topRow=new DockPanel {Margin=new Thickness(0,0,0,12)};
            var topIcon=new Image {Width=28,Height=28,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center};
            RenderOptions.SetBitmapScalingMode(topIcon,BitmapScalingMode.HighQuality);
            var topMark=Design.Icon("Profiles",20);topMark.Margin=new Thickness(4,0,14,0);topMark.VerticalAlignment=VerticalAlignment.Center;
            var topFace=new Grid();topFace.Children.Add(topMark);topFace.Children.Add(topIcon);
            DockPanel.SetDock(topFace,Dock.Left);topRow.Children.Add(topFace);
            var topWords=new StackPanel {VerticalAlignment=VerticalAlignment.Center};
            var topLabel=new TextBlock {Text="Most played",FontSize=12,Foreground=Muted};
            var topName=new TextBlock {FontSize=15,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
            topWords.Children.Add(topLabel);topWords.Children.Add(topName);topRow.Children.Add(topWords);
            card.Children.Add(topRow);
            // A bar for each of the last seven days, today on the right and brightest.
            var chart=new Grid {Height=96};
            var bars=new Border[7];var dayLabels=new TextBlock[7];
            for(int i=0;i<7;i++)
            {
                chart.ColumnDefinitions.Add(new ColumnDefinition());
                var column=new Grid {Margin=new Thickness(4,0,4,0)};
                column.RowDefinitions.Add(new RowDefinition());column.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
                bars[i]=new Border {CornerRadius=new CornerRadius(5),VerticalAlignment=VerticalAlignment.Bottom,Height=3,Background=new SolidColorBrush(Color.FromArgb((byte)(i==6?235:120),255,255,255))};
                dayLabels[i]=new TextBlock {FontSize=11,Foreground=Muted,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,6,0,0)};
                Grid.SetRow(dayLabels[i],1);
                column.Children.Add(bars[i]);column.Children.Add(dayLabels[i]);
                Grid.SetColumn(column,i);chart.Children.Add(column);
            }
            card.Children.Add(chart);
            var empty=Text("Play a game and your time shows up here. Lucent only looks at which game is running, and keeps this on your PC.",13);
            empty.Foreground=Muted;
            var emptyRow=EmptyRow(EmptyArt.Clock(48),empty);
            card.Children.Add(emptyRow);
            string shownIcon=null;
            homeUpdaters.Add(()=>
            {
                var week=Week(preferences.Playtime,DateTime.Today);
                bool any=week.Total>=1;
                SetText(total,any?PlayLength(week.Total):"0 min");
                topRow.Visibility=any?Visibility.Visible:Visibility.Collapsed;
                emptyRow.Visibility=any?Visibility.Collapsed:Visibility.Visible;
                if(any)
                {
                    SetText(topName,week.TopGame+"  ·  "+PlayLength(week.TopMinutes));
                    if(week.TopExecutable!=shownIcon)
                    {
                        shownIcon=week.TopExecutable;topIcon.Source=null;topMark.Visibility=Visibility.Visible;
                        if(!TestMode&&!string.IsNullOrEmpty(shownIcon))GameLibrary.LoadIcon(shownIcon,Dispatcher,source=>{topIcon.Source=source;topMark.Visibility=Visibility.Collapsed;});
                    }
                }
                double most=Math.Max(30,week.Days.Max());
                for(int i=0;i<7;i++)
                {
                    bars[i].Height=Math.Max(3,70*week.Days[i]/most);
                    bars[i].ToolTip=week.Labels[i]+": "+(week.Days[i]>=1?PlayLength(week.Days[i]):"no games");
                    SetText(dayLabels[i],week.Labels[i]);
                }
            });
            return card;
        }
    }
}
