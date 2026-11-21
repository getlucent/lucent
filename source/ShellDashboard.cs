using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TextBlock=Lucent.SmoothText;

namespace Lucent
{
    // "Right now" on Home: the game being played (with its icon) and the song with its cover. It is one of the Home cards, so Customize Home can
    // move, resize or hide it.
    internal sealed partial class Shell
    {
        readonly List<Action> dashboardUpdaters=new List<Action>();
        static readonly Brush DashIdle=FrozenTone(Color.FromArgb(20,255,255,255));
        static Brush FrozenTone(Color color){var brush=new SolidColorBrush(color);brush.Freeze();return brush;}
        // Brought up to date every couple of seconds while Home shows.
        internal void RefreshDashboard(){foreach(var update in dashboardUpdaters)update();}
        Image homeCover;

        FrameworkElement HomeNowCard()
        {
            var card=Section("Right now","");
            var now=new UniformGrid {Columns=2,Margin=new Thickness(-8,10,-8,4)};
            // The game.
            var gameIcon=new Image {Width=52,Height=52,Stretch=Stretch.Uniform};
            RenderOptions.SetBitmapScalingMode(gameIcon,BitmapScalingMode.HighQuality);
            var gameMark=EmptyArt.Asleep(50);
            var gameFace=new Grid {Width=72,Height=72};
            gameFace.Children.Add(new Border {CornerRadius=new CornerRadius(16),Background=DashIdle});
            gameMark.HorizontalAlignment=HorizontalAlignment.Center;gameMark.VerticalAlignment=VerticalAlignment.Center;
            gameFace.Children.Add(gameMark);gameFace.Children.Add(gameIcon);
            var gameTitle=new TextBlock {FontSize=17,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
            var gameDetail=new TextBlock {FontSize=13,Foreground=Muted,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,3,0,0)};
            now.Children.Add(NowColumn(gameFace,gameTitle,gameDetail));
            // The song, with its cover.
            var songFace=new Grid {Width=72,Height=72,Clip=new RectangleGeometry(new Rect(0,0,72,72),16,16)};
            songFace.Children.Add(new Border {Background=DashIdle});
            var songMark=EmptyArt.Record(64);songMark.HorizontalAlignment=HorizontalAlignment.Center;songMark.VerticalAlignment=VerticalAlignment.Center;
            var songCover=new Image {Stretch=Stretch.UniformToFill};
            RenderOptions.SetBitmapScalingMode(songCover,BitmapScalingMode.HighQuality);
            songFace.Children.Add(songMark);songFace.Children.Add(songCover);
            homeCover=songCover;
            var songTitle=new TextBlock {FontSize=17,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
            var songDetail=new TextBlock {FontSize=13,Foreground=Muted,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,3,0,0)};
            var songColumn=NowColumn(songFace,songTitle,songDetail);
            songColumn.Cursor=Cursors.Hand;songColumn.ToolTip="Open the player";
            songColumn.MouseLeftButtonUp+=(s,e)=>{if(PanelTrack!=null)ToggleBigPlayer();};
            now.Children.Add(songColumn);
            card.Children.Add(now);
            string shownProgram=null;byte[] shownArt=null;
            Action showNow=()=>
            {
                bool game=!string.IsNullOrEmpty(playingNow);
                SetText(gameTitle,game?playingNow:"No game running");
                double today=game?preferences.Playtime.Where(d=>d.Date==DateTime.Today.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)&&string.Equals(d.Game,playingNow,StringComparison.OrdinalIgnoreCase)).Sum(d=>d.Minutes):0;
                string where=playingOnPlayStation?"On PlayStation":"Playing now";
                SetText(gameDetail,game?(today>=1?(playingOnPlayStation?"On PlayStation, ":"")+PlayLength(today)+" today":where):"Lucent notices when one starts");
                if(playingProgram!=shownProgram)
                {
                    shownProgram=playingProgram;gameIcon.Source=null;gameMark.Visibility=Visibility.Visible;
                    if(!playingOnPlayStation&&!string.IsNullOrEmpty(shownProgram)&&!TestMode){string asked=shownProgram;GameLibrary.LoadIcon(asked,Dispatcher,source=>{if(asked==shownProgram){gameIcon.Source=source;gameMark.Visibility=Visibility.Collapsed;}});}
                }
                var track=PanelTrack;bool song=track!=null&&!string.IsNullOrEmpty(track.Title);
                SetText(songTitle,song?track.Title:"Nothing playing");
                SetText(songDetail,song?(string.IsNullOrEmpty(track.Artist)?track.Source:track.Artist):"Play something in any app");
                byte[] art=song?track.Artwork:null;
                if(!MediaSession.SameArtwork(shownArt,art)){shownArt=art;songCover.Source=art==null?null:DecodeCover(art);songMark.Visibility=songCover.Source==null?Visibility.Visible:Visibility.Collapsed;}
            };
            dashboardUpdaters.Add(showNow);homeUpdaters.Add(showNow);
            return card;
        }
        // An empty place with a little life: a drawing beside a short hint.
        FrameworkElement EmptyRow(FrameworkElement art,TextBlock hint)
        {
            var row=new DockPanel {Margin=new Thickness(0,8,0,2)};
            art.Margin=new Thickness(0,0,16,0);art.VerticalAlignment=VerticalAlignment.Center;
            DockPanel.SetDock(art,Dock.Left);row.Children.Add(art);
            hint.VerticalAlignment=VerticalAlignment.Center;hint.TextWrapping=TextWrapping.Wrap;
            row.Children.Add(hint);
            return row;
        }
        FrameworkElement NowColumn(FrameworkElement face,TextBlock title,TextBlock detail)
        {
            var dock=new DockPanel {Margin=new Thickness(8,0,8,0),Background=Brushes.Transparent};
            face.Margin=new Thickness(0,0,14,0);face.VerticalAlignment=VerticalAlignment.Center;
            DockPanel.SetDock(face,Dock.Left);dock.Children.Add(face);
            var words=new StackPanel {VerticalAlignment=VerticalAlignment.Center};
            words.Children.Add(title);words.Children.Add(detail);
            dock.Children.Add(words);
            return dock;
        }
    }
}
