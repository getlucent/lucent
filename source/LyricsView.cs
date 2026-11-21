using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TextBlock=Lucent.SmoothText;

namespace Lucent
{
    internal sealed class LyricLine
    {
        public double Time;
        public string Text;
        // When each word starts, from lyrics that time their words ("<01:02.30>word"); null when only the line is timed.
        public double[] Words;
    }

    internal sealed class LyricsResult
    {
        public List<LyricLine> Lines=new List<LyricLine>();
        // True when every line carries the time it is sung. Otherwise the text is shown as it is, without following the song.
        public bool Synced;
    }

    // Finds lyrics for the song that is playing. Nothing here runs until the lyrics view is switched on, because looking
    // a song up sends its title, artist and length to lrclib.net, a free public lyrics database.
    internal static class LyricsService
    {
        static readonly Dictionary<string,LyricsResult> cache=new Dictionary<string,LyricsResult>();
        static readonly CultureInfo Invariant=CultureInfo.InvariantCulture;

        // Turns the text of a .lrc file ("[01:23.45] words") into lines in time order. Plain text becomes lines without times.
        internal static LyricsResult Parse(string synced,string plain)
        {
            var timed=new List<LyricLine>();
            if(!string.IsNullOrWhiteSpace(synced))
            {
                foreach(string raw in synced.Replace("\r","").Split('\n'))
                {
                    var stamps=Regex.Matches(raw,@"\[(\d+):(\d+(?:[.:]\d+)?)\]");
                    if(stamps.Count==0)continue;
                    string words=Regex.Replace(raw,@"\[[^\]]*\]","");
                    string text=Regex.Replace(words,@"<\d+:\d+(?:[.:]\d+)?>","").Trim();
                    if(text.Length==0)continue;
                    // A line sung once can carry the times of its words.
                    double[] wordTimes=stamps.Count==1?WordTimes(words,text):null;
                    foreach(Match stamp in stamps)
                    {
                        double minutes,seconds;
                        if(!double.TryParse(stamp.Groups[1].Value,NumberStyles.Float,Invariant,out minutes)||!double.TryParse(stamp.Groups[2].Value.Replace(':','.'),NumberStyles.Float,Invariant,out seconds))continue;
                        timed.Add(new LyricLine {Time=minutes*60+seconds,Text=text,Words=wordTimes});
                    }
                }
                if(timed.Count>0)return new LyricsResult {Lines=timed.OrderBy(x=>x.Time).ToList(),Synced=true};
            }
            if(!string.IsNullOrWhiteSpace(plain))
            {
                var lines=plain.Replace("\r","").Split('\n').Select(x=>x.Trim()).ToList();
                while(lines.Count>0&&lines[0].Length==0)lines.RemoveAt(0);
                while(lines.Count>0&&lines[lines.Count-1].Length==0)lines.RemoveAt(lines.Count-1);
                if(lines.Count>0)return new LyricsResult {Lines=lines.Select(x=>new LyricLine {Time=-1,Text=x}).ToList(),Synced=false};
            }
            return null;
        }
        // The start of each word of a line written "<00:12.10>Some <00:12.60>words", one time per word of the plain text; null when the line has no
        // word times or they don't match its words. Several words after one time share the time to the next one.
        static double[] WordTimes(string tagged,string text)
        {
            var tags=Regex.Matches(tagged,@"<(\d+):(\d+(?:[.:]\d+)?)>");
            if(tags.Count==0)return null;
            var times=new List<double>();
            for(int i=0;i<tags.Count;i++)
            {
                double minutes,seconds;
                if(!double.TryParse(tags[i].Groups[1].Value,NumberStyles.Float,Invariant,out minutes)||!double.TryParse(tags[i].Groups[2].Value.Replace(':','.'),NumberStyles.Float,Invariant,out seconds))return null;
                double at=minutes*60+seconds;
                int from=tags[i].Index+tags[i].Length,to=i+1<tags.Count?tags[i+1].Index:tagged.Length;
                var said=Words(tagged.Substring(from,to-from));
                if(said.Length==0)continue;
                double next=at+.4*said.Length;
                if(i+1<tags.Count){double m2,s2;if(double.TryParse(tags[i+1].Groups[1].Value,NumberStyles.Float,Invariant,out m2)&&double.TryParse(tags[i+1].Groups[2].Value.Replace(':','.'),NumberStyles.Float,Invariant,out s2))next=m2*60+s2;}
                for(int k=0;k<said.Length;k++)times.Add(at+(next-at)*k/said.Length);
            }
            return times.Count==Words(text).Length?times.ToArray():null;
        }
        // The words of a line, split at any kind of space (the same way the line lights them).
        internal static string[] Words(string text)
        {
            var words=new List<string>();
            var word=new System.Text.StringBuilder();
            foreach(char c in text??"")
            {
                if(char.IsWhiteSpace(c)){if(word.Length>0){words.Add(word.ToString());word.Clear();}}
                else word.Append(c);
            }
            if(word.Length>0)words.Add(word.ToString());
            return words.ToArray();
        }
        // "Song (feat. Someone) - 2011 Remaster" searches better as "Song".
        internal static string CleanTitle(string title)
        {
            string clean=Regex.Replace(title??"",@"\s*[\(\[][^\)\]]*[\)\]]","");
            clean=Regex.Replace(clean,@"\s+-\s+.*(remaster|version|edit|live|mix|from).*$","",RegexOptions.IgnoreCase).Trim();
            return clean.Length==0?(title??"").Trim():clean;
        }
        internal static string FirstArtist(string artist)
        {
            if(string.IsNullOrWhiteSpace(artist))return "";
            return Regex.Split(artist,@"\s*(?:,|&|;|\sfeat\.?\s|\sx\s|\swith\s)\s*",RegexOptions.IgnoreCase)[0].Trim();
        }
        internal static LyricsResult Find(string title,string artist,double duration)
        {
            if(string.IsNullOrWhiteSpace(title))return null;
            string key=((artist??"")+"|"+title).ToLowerInvariant();
            lock(cache){LyricsResult known;if(cache.TryGetValue(key,out known))return known;}
            LyricsResult found=null;
            try
            {
                found=Search("track_name="+Uri.EscapeDataString(CleanTitle(title))+(FirstArtist(artist).Length>0?"&artist_name="+Uri.EscapeDataString(FirstArtist(artist)):""),duration);
                if(found==null)found=Search("q="+Uri.EscapeDataString((FirstArtist(artist)+" "+CleanTitle(title)).Trim()),duration);
            }
            catch(Exception){return null;}
            lock(cache)cache[key]=found;
            return found;
        }
        static LyricsResult Search(string query,double duration)
        {
            ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
            var request=(HttpWebRequest)WebRequest.Create("https://lrclib.net/api/search?"+query);
            request.UserAgent="Lucent (Windows desktop app)";
            request.Timeout=8000;
            request.ReadWriteTimeout=8000;
            string json;
            using(var response=request.GetResponse())
            using(var reader=new System.IO.StreamReader(response.GetResponseStream(),System.Text.Encoding.UTF8))json=reader.ReadToEnd();
            return Choose(json,duration);
        }
        // Of the answers, the one with timed lyrics whose length is closest to the song's; plain text only if nothing is timed.
        internal static LyricsResult Choose(string json,double duration)
        {
            var items=new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.DeserializeObject(json) as object[];
            if(items==null)return null;
            LyricsResult best=null;double bestScore=double.MaxValue;
            foreach(var item in items.OfType<IDictionary<string,object>>())
            {
                object synced,plain,length;
                item.TryGetValue("syncedLyrics",out synced);item.TryGetValue("plainLyrics",out plain);item.TryGetValue("duration",out length);
                var result=Parse(synced as string,plain as string);
                if(result==null)continue;
                double gap=0;
                try{gap=duration>1&&length!=null?Math.Abs(Convert.ToDouble(length,Invariant)-duration):0;}catch(Exception){}
                if(duration>1&&gap>8)continue;
                double score=gap+(result.Synced?0:1000);
                if(score<bestScore){best=result;bestScore=score;}
            }
            return best;
        }
    }

    // The lyrics beside the player: the line being sung is bright, the others dim, and the lines glide so the current one stays in view.
    internal sealed class LyricsView:Grid
    {
        // The lines live on a canvas. In a grid cell the list would be clipped to the cell's height, and since the clip travels with
        // the list as it scrolls, only the first screenful of lines would ever be drawn.
        readonly Canvas stage=new Canvas();
        readonly StackPanel list=new StackPanel();
        readonly TranslateTransform scroll=new TranslateTransform();
        readonly TextBlock note=new TextBlock {FontSize=20,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Foreground=new SolidColorBrush(Color.FromArgb(150,255,255,255))};
        readonly List<Border> items=new List<Border>();
        List<LyricLine> lines=new List<LyricLine>();
        bool synced;
        // The lines glide and fade at the rate of the screen they are on. WPF moves things 60 times a second unless told otherwise, so on a fast screen
        // (144, 240, 300 Hz) a line moved only at every fifth refresh. But WPF makes its frames to the beat of the main screen: drawn 300 times a second
        // on a 60 Hz second screen, that screen showed whichever frame happened to be newest, and the glide came out in uneven steps. Moving at the
        // rate of the screen the lyrics are on gives that screen one even step per refresh.
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
        struct MonitorInfo {public int Size;public int L,T,R,B,WL,WT,WR,WB;public uint Flags;[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr,SizeConst=32)]public string Device;}
        [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
        // For measuring: a fixed rate instead of the screen's (0: the screen's).
        internal static int ForceRate;
        IntPtr rateMonitor;int rate=60;
        internal int Rate
        {
            get
            {
                if(ForceRate>0)return ForceRate;
                try
                {
                    var source=PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource;
                    if(source==null)return rate;
                    var monitor=MonitorFromWindow(source.Handle,2);
                    if(monitor==rateMonitor)return rate;
                    rateMonitor=monitor;
                    var info=new MonitorInfo {Size=System.Runtime.InteropServices.Marshal.SizeOf(typeof(MonitorInfo))};
                    var mode=GetMonitorInfo(monitor,ref info)?ResolutionSwitcher.DisplayService.GetCurrentMode(info.Device):null;
                    rate=mode==null?60:Math.Max(30,Math.Min(500,mode.Frequency));
                }
                catch(Exception){}
                return rate;
            }
        }
        void Fade(UIElement item,double to,int milliseconds)
        {
            if(Design.Instant)milliseconds=0;
            var fade=new DoubleAnimation(item.Opacity,to,TimeSpan.FromMilliseconds(milliseconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
            Timeline.SetDesiredFrameRate(fade,Rate);
            item.BeginAnimation(OpacityProperty,fade);
        }
        // How a line change glides: like Liner's, it starts gently, speeds up and settles (an abrupt start reads as a jump), over about 0.7 s.
        internal static int GlideMs=680;
        internal static bool GlideInOut=true;
        int active=-1;
        double manual;
        internal Action<double> Seek=seconds=>{};
        internal int ActiveIndex {get{return active;}}
        internal int LineCount {get{return lines.Count;}}
        internal bool Synced {get{return synced;}}
        internal string Note {get{return note.Visibility==Visibility.Visible?note.Text:null;}}
        internal double ScrollOffset {get{return scroll.Y;}}
        internal IList<Border> Items {get{return items;}}
        internal const double Dim=.38,UpNext=1;
        // Whether the line being sung lights word by word (a setting, on unless switched off).
        internal static Func<bool> WordByWord=()=>true;
        // When each word of each line starts and ends, in seconds.
        double[][] wordFrom=new double[0][],wordTo=new double[0][];
        // The size of a line in pixels, and whether lines are centered. The big player keeps 30 and left; the mini player lets the user choose.
        double lineSize=30;bool centered;
        internal double LineSize {get{return lineSize;}}
        internal bool Centered {get{return centered;}}
        internal void SetStyle(double size,bool center)
        {
            lineSize=Math.Max(10,Math.Min(60,size));centered=center;
            note.HorizontalAlignment=center?HorizontalAlignment.Center:HorizontalAlignment.Left;note.TextAlignment=center?TextAlignment.Center:TextAlignment.Left;
            foreach(var item in items)
            {
                var label=(SungLine)item.Child;label.FontSize=lineSize;label.TextAlignment=centered?TextAlignment.Center:TextAlignment.Left;
                item.Padding=new Thickness(centered?14:0,Math.Max(8,lineSize*.5),centered?14:28,Math.Max(8,lineSize*.5));
            }
            if(lines.Count==0)return;
            // The notes of the pauses follow the new size.
            if(synced&&wordFrom.Length==lines.Count)MakeInterludes();
            list.UpdateLayout();Follow(false);
        }

        public LyricsView()
        {
            Background=Brushes.Transparent;
            // The lines are drawn as they are, not kept as small pictures (see SmoothText): a picture of a line that slides by fractions of a pixel
            // is resampled every frame and goes soft, and large lyrics are where that shows.
            // (Each line is kept as a small picture, as text is everywhere in the app, unless the window says otherwise (the mini player does, because it scales).
            // A picture slides and fades at no cost; drawing every line again at each step made changing lines stutter while music played.)
            UseLayoutRounding=true;
            list.RenderTransform=scroll;
            stage.Children.Add(list);
            Children.Add(stage);
            Children.Add(note);
            // Lines fade out toward the top and bottom edges.
            // The fade is measured in pixels of what is visible (see SizeChanged below): a mask relative to the element's bounds is stretched over
            // the whole scrolling list, which left the first and last lines, and any line near the ends of a long list, dim.
            SizeChanged+=(s,e)=>
            {
                // The view's edge is a little loose to the sides, so a letter that reaches past the line's width (a wide last letter) is not cut off.
                Clip=new RectangleGeometry(new Rect(-24,0,ActualWidth+48,ActualHeight));
                // While the window that holds the lyrics is changing size (their view opening or closing), laying the lines out again at every step was what
                // made it stutter: they wait and are laid out once at the end (see Thaw). A view that has never been laid out is laid out at once.
                if(Frozen&&laidWidth>0){return;}
                Refit();
            };
        }
        // True while the lyrics should keep their layout (the window is being resized by an animation).
        internal bool Frozen {get;set;}
        double laidWidth,maskedHeight;
        void Refit()
        {
            if(ActualHeight>0&&Math.Abs(ActualHeight-maskedHeight)>.01)
            {
                maskedHeight=ActualHeight;
                var fade=new LinearGradientBrush(new GradientStopCollection{new GradientStop(Color.FromArgb(0,0,0,0),0),new GradientStop(Color.FromArgb(255,0,0,0),.14),new GradientStop(Color.FromArgb(255,0,0,0),.8),new GradientStop(Color.FromArgb(0,0,0,0),1)},new Point(0,0),new Point(0,ActualHeight)){MappingMode=BrushMappingMode.Absolute};
                fade.Freeze();OpacityMask=fade;
            }
            // The list is as wide as the view. (This compares with the list's own width, not with the last width seen: after the view was hidden and shown again a stale
            // comparison left the list at the zero width it was given while hidden, and the lines could not be seen until the next song.)
            if(ActualWidth>0&&(double.IsNaN(list.Width)||Math.Abs(list.Width-ActualWidth)>.01)){laidWidth=ActualWidth;note.MaxWidth=Math.Max(100,ActualWidth);list.Width=ActualWidth;list.UpdateLayout();}
            Follow(false);
        }
        // The window has its final size: the lines are laid out once.
        internal void Thaw(){Frozen=false;Refit();}        internal void ShowMessage(string text)
        {
            Reset();
            note.Text=text;
            note.Visibility=Visibility.Visible;
        }
        internal void Clear()
        {
            Reset();
            note.Visibility=Visibility.Collapsed;
        }
        void Reset()
        {
            lines=new List<LyricLine>();items.Clear();list.Children.Clear();
            interludes.Clear();activeInterlude=null;glideItems.Clear();
            wordFrom=new double[0][];wordTo=new double[0][];
            synced=false;active=-1;manual=0;
            glideOn=false;scroll.BeginAnimation(TranslateTransform.YProperty,null);scroll.Y=0;
        }
        internal void SetLyrics(LyricsResult result)
        {
            Reset();
            if(result==null||result.Lines.Count==0){ShowMessage("No lyrics found for this song");return;}
            note.Visibility=Visibility.Collapsed;
            lines=result.Lines;synced=result.Synced;
            foreach(var line in lines)
            {
                var label=new SungLine {Text=line.Text.Length==0?" ":line.Text,FontSize=lineSize,TextAlignment=centered?TextAlignment.Center:TextAlignment.Left};
                var item=new Border {Background=Brushes.Transparent,Padding=new Thickness(centered?14:0,Math.Max(8,lineSize*.5),centered?14:28,Math.Max(8,lineSize*.5)),Opacity=synced?Dim:.85,Child=label,Visibility=Visibility.Hidden,Cursor=synced?Cursors.Hand:Cursors.Arrow,RenderTransform=new TranslateTransform()};
                double time=line.Time;
                if(synced)
                {
                    // Pointing at a line that is not lit lifts it a little. The line being sung, and the first line before the song reaches it, stay fully lit.
                    item.MouseEnter+=(s,e)=>{if(!Lit(items.IndexOf(item)))Fade(item,.62,110);};
                    item.MouseLeave+=(s,e)=>{Fade(item,Lit(items.IndexOf(item))?1:Dim,200);};
                    item.MouseLeftButtonUp+=(s,e)=>{Seek(time);e.Handled=true;};
                }
                items.Add(item);list.Children.Add(item);
            }
            // Before the first line is sung (the start of a song, or after a seek back) the first line is the one coming up: it is shown at full strength,
            // with none of its words lit yet (lit whole, it emptied out and filled again the moment it started).
            if(synced&&items.Count>0){items[0].Opacity=UpNext;if(WordByWord())((SungLine)items[0].Child).Light(-1,0);}
            if(synced){TimeWords();RefreshBlur();}
            // (While the view is hidden its width is 0: giving the list that width left every line squeezed to nothing when the view came back.)
            if(ActualWidth>0)list.Width=ActualWidth;
            UpdateLayout();
            Refit();
        }
        // (Lines start hidden and keep their room; Follow shows the ones around the place the view rests, so only those are drawn.)
        // Whether a line is shown fully lit: the one being sung, or, before the song reaches the first line, the first one.
        bool Lit(int index){return index>=0&&activeInterlude==null&&(index==active||(active<0&&index==0));}
        // When each word is sung. Lyrics that time their words say so; most time only their lines, and then the words of a line share the time it takes
        // to sing it, each by its length. That time is the gap to the next line, but no longer than the words take at an easy pace, so a line before
        // a long pause is lit by the time its words end instead of creeping through the pause.
        // How many syllables a word has, near enough: its groups of vowels (a silent e at the end not counted). Words without Latin vowels count by length.
        internal static int Syllables(string word)
        {
            string w=(word??"").ToLowerInvariant();
            int groups=0;bool inVowel=false,anyLetter=false;
            foreach(char c in w)
            {
                if(char.IsLetter(c))anyLetter=true;
                bool vowel="aeiouyàáâäèéêëìíîïòóôöùúûüýěů".IndexOf(c)>=0;
                if(vowel&&!inVowel)groups++;
                inVowel=vowel;
            }
            if(groups>1&&w.EndsWith("e")&&!w.EndsWith("le")&&!w.EndsWith("ee"))groups--;
            if(groups==0)return anyLetter?Math.Max(1,w.Length/3):1;
            return groups;
        }
        // How long a word tends to be held compared with its syllables.
        internal static double Held(string word)
        {
            string w=System.Text.RegularExpressions.Regex.Replace((word??"").ToLowerInvariant(),@"[^a-z]","");
            if(System.Text.RegularExpressions.Regex.IsMatch(w,@"^(o+h+|a+h+|oo+|aa+|w+o+a+h*|w+h+o+a+|m+|mm+h*|uh+|ee+)$"))return 3;
            if(System.Text.RegularExpressions.Regex.IsMatch(w,@"^(ya+h*|yea+h*|ye+|ay+e*|he+y+|yo+|na+h*|la+)$"))return 1.6;
            return 1;
        }
        // When each word is sung. Lyrics that time their words say so. Most time only their lines: then the song's own pace is worked out from the lines
        // that follow each other closely (how long a syllable takes in this song: a fast rap and a slow ballad differ by three times), each line takes as
        // long as its syllables at that pace (never past the next line), and its words share that time by their syllables.
        void TimeWords()
        {
            wordFrom=new double[lines.Count][];wordTo=new double[lines.Count][];
            var paces=new List<double>();
            for(int i=0;i+1<lines.Count;i++)
            {
                int syllables=LyricsService.Words(lines[i].Text).Sum(w=>Syllables(w));
                double gap=lines[i+1].Time-lines[i].Time;
                if(syllables>=3&&gap>.4&&gap<6)paces.Add(gap/syllables);
            }
            paces.Sort();
            // A low share of the gaps: a gap holds the singing and often a breath after it.
            double pace=paces.Count>=3?paces[(int)(paces.Count*.3)]:.22;
            pace=Math.Max(.09,Math.Min(.45,pace));
            for(int i=0;i<lines.Count;i++)
            {
                var words=LyricsService.Words(lines[i].Text);
                int count=words.Length;
                double start=lines[i].Time,next=i+1<lines.Count?lines[i+1].Time:start+8;
                var from=new double[count];var to=new double[count];
                if(lines[i].Words!=null&&lines[i].Words.Length==count)
                {
                    for(int k=0;k<count;k++){from[k]=lines[i].Words[k];to[k]=k+1<count?lines[i].Words[k+1]:Math.Min(next,from[k]+Math.Max(.3,Syllables(words[k])*pace*1.4));}
                }
                else if(count>0)
                {
                    // Sounds that are held ("ooh", "oh", "woah", "mm") get three times the room, "ya", "yeah", "ay" half again as much.
                    var weights=words.Select(w=>(Syllables(w)+.3)*Held(w)).ToArray();
                    double total=weights.Sum(),done=0;
                    int syllables=words.Sum(w=>Syllables(w));
                    double natural=weights.Sum()*pace+.12;
                    // A short line is often drawn out into the pause after it ("ooh, ya" sung as "oooooh ya"): the fewer its syllables, the more of the
                    // pause it takes. A long line keeps close to its own pace.
                    double room=Math.Max(0,next-start-.08-natural);
                    // (Not into a real pause: that is where the note of the pause goes.)
                    bool pauseAfter=next-start-natural-.35>=PauseLong;
                    double stretch=pauseAfter?0:syllables<=3?.75:syllables<=6?.4:.12;
                    double span=Math.Max(.3,Math.Min(next-start-.08,natural+Math.Min(4,room*stretch)));
                    for(int k=0;k<count;k++)
                    {
                        from[k]=start+span*done/total;
                        done+=weights[k];
                        to[k]=start+span*done/total;
                    }
                }
                wordFrom[i]=from;wordTo[i]=to;
            }
            MakeInterludes();
        }

        // ---- The pauses: where nothing is sung for a while (an intro, a solo), a music note stands in the list like a line and fills up as the next line nears.
        sealed class Interlude {public Border Item;public double Start,End;public int After;public RectangleGeometry Fill;public double Height;public System.Windows.Media.Effects.DropShadowEffect Glow;}
        readonly List<Interlude> interludes=new List<Interlude>();
        Interlude activeInterlude;
        const double PauseLong=3.2;
        static readonly Geometry NoteShape=MakeNote();
        // The eighth note as Windows' symbol font draws it (the classic note: round head, stem, flag), like Liner's.
        static Geometry MakeNote()
        {
            try
            {
                var text=new FormattedText("♪",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI Symbol"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),100,Brushes.White,1.0);
                var glyph=text.BuildGeometry(new Point());
                if(!glyph.Bounds.IsEmpty){glyph.Freeze();return glyph;}
            }
            catch(Exception){}
            var g=new GeometryGroup {FillRule=FillRule.Nonzero};
            g.Children.Add(new EllipseGeometry(new Point(9,17.6),3.4,2.7,new RotateTransform(-20,9,17.6)));
            g.Children.Add(Geometry.Parse("M11.3,17.2 L11.3,3.6 L12.9,3.6 L12.9,17.2 Z"));
            g.Children.Add(Geometry.Parse("M12.9,3.6 Q17.6,4.9 18.8,9.6 Q16.2,7.4 12.9,7.7 Z"));
            g.Freeze();return g;
        }
        void MakeInterludes()
        {
            foreach(var old in interludes)list.Children.Remove(old.Item);
            interludes.Clear();activeInterlude=null;
            if(!synced||lines.Count==0)return;
            for(int i=-1;i<lines.Count-1;i++)
            {
                double ended=i<0?0:(wordTo[i].Length>0?wordTo[i][wordTo[i].Length-1]:lines[i].Time+1);
                double start=i<0?.5:ended+.35,end=lines[i+1].Time;
                if(end-start<PauseLong)continue;
                double side=lineSize*1.4;
                var dim=new System.Windows.Shapes.Path {Data=NoteShape,Fill=new SolidColorBrush(Color.FromArgb(80,255,255,255)),Stretch=Stretch.Uniform,Width=side,Height=side};
                var fill=new RectangleGeometry(new Rect(0,side,side,0));
                var lit=new System.Windows.Shapes.Path {Data=NoteShape,Fill=Brushes.White,Stretch=Stretch.Uniform,Width=side,Height=side,Clip=fill};
                // It glows as it fills: faint at the start of the pause, strongly once it is full.
                var glow=new System.Windows.Media.Effects.DropShadowEffect {Color=Colors.White,ShadowDepth=0,BlurRadius=6,Opacity=0,RenderingBias=System.Windows.Media.Effects.RenderingBias.Performance};
                var litHolder=new Grid {Width=side,Height=side,Effect=glow};litHolder.Children.Add(lit);
                var face=new Grid {Width=side,Height=side,HorizontalAlignment=centered?HorizontalAlignment.Center:HorizontalAlignment.Left};
                face.Children.Add(dim);face.Children.Add(litHolder);
                var item=new Border {Background=Brushes.Transparent,Padding=new Thickness(centered?14:0,Math.Max(8,lineSize*.45),centered?14:28,Math.Max(8,lineSize*.45)),Opacity=Dim,Child=face,Visibility=Visibility.Hidden,RenderTransform=new TranslateTransform()};
                var pause=new Interlude {Item=item,Start=start,End=end,After=i,Fill=fill,Height=side,Glow=glow};
                interludes.Add(pause);
                // In the list after the line it follows (before the first line for an intro).
                int at=i<0?0:list.Children.IndexOf(items[i])+1;
                list.Children.Insert(at,item);
            }
        }
        Interlude PauseAt(double seconds)
        {
            foreach(var pause in interludes)if(seconds>=pause.Start&&seconds<pause.End-.25)return pause;
            return null;
        }
        // Lights the words of the line being sung up to this moment.
        void LightWords(double seconds)
        {
            if(active<0||active>=items.Count||active>=wordFrom.Length)return;
            var line=(SungLine)items[active].Child;
            if(!WordByWord()){line.LightAll();return;}
            var from=wordFrom[active];var to=wordTo[active];
            if(from.Length==0||from.Length!=line.WordCount){line.LightAll();return;}
            int word=-1;double part=0;
            for(int k=0;k<from.Length;k++)
            {
                if(seconds<from[k])break;
                word=k;part=to[k]>from[k]?(seconds-from[k])/(to[k]-from[k]):1;
            }
            if(word==from.Length-1&&part>=1)word=from.Length;
            else if(word>=0&&part>=1){word++;part=0;}
            line.Light(word,part);
        }
        // The players tell the time of the song about fifteen times a second (more would redraw them too often). Between those, the lyrics carry the time
        // on themselves at every refresh, so the words light and the lines change smoothly. When the players stop telling (paused, closed), this stops too.
        // Everything moves at the refreshes of the screen the lyrics are on (see ScreenBeat): the time is carried on, the words light, and a glide takes its step.
        double heardSeconds,heardAt,litSeconds;bool moving;
        ScreenBeat beat;object beatToken;bool framing;
        // The time is carried on only while the song moves: paused, the players keep telling the same moment, and carrying it on from there made the
        // words run ahead a third of a second and snap back, over and over.
        // (Only a little past the last word from the players, who tell 15 times a second: at a pause the words can't run on past where it stopped.)
        bool Hearing {get{return moving&&synced&&lines.Count>0&&ScreenBeat.Now-heardAt<=.2;}}
        internal void Update(double seconds)
        {
            double now=ScreenBeat.Now,elapsed=now-heardAt,advance=seconds-heardSeconds;
            // Moving when the song went on about as far as the time that passed. A jump (a seek) or the first word after a while waits for the next one to tell.
            moving=elapsed<2&&advance>.001&&advance>elapsed*.4&&advance<elapsed*2.5+.05;
            heardSeconds=seconds;heardAt=now;
            Step(seconds);
            if(IsVisible)Listen();
        }
        // Starts following the beat of this view's screen (or WPF's own frames where that screen's refresh can't be waited for).
        void Listen()
        {
            Rate.ToString();   // finds the screen this view is on
            var wanted=rateMonitor==IntPtr.Zero?null:ScreenBeat.For(rateMonitor);
            if(wanted!=null&&!wanted.Works)wanted=null;
            if(beatToken!=null&&ReferenceEquals(wanted,beat))return;
            StopListening();
            if(wanted!=null){beat=wanted;beatToken=wanted.Listen(OnBeat,Dispatcher);}
            else if(!framing){framing=true;CompositionTarget.Rendering+=OnFrame;}
        }
        void StopListening()
        {
            if(beatToken!=null){beat.Stop(beatToken);beatToken=null;beat=null;}
            if(framing){framing=false;CompositionTarget.Rendering-=OnFrame;}
        }
        // A refresh of the screen: what is shown at the next one is set now.
        void OnBeat(double stamp)
        {
            if(beat!=null&&!beat.Works){StopListening();if(IsVisible)Listen();return;}
            Tick(stamp+(beat==null?1/60.0:beat.Period));
        }
        void OnFrame(object sender,EventArgs e)
        {
            double now=ScreenBeat.Now;
            if(now-lastFrame<.9/Rate)return;
            lastFrame=now;
            Tick(now);
        }
        double lastFrame;
        void Tick(double shown)
        {
            if(!IsVisible){StopGlide(true);StopListening();return;}
            bool hearing=Hearing;
            if(hearing)Step(heardSeconds+Math.Min(.15,shown-heardAt));
            bool gliding=GlideStep(shown);
            if(!hearing&&!gliding)StopListening();
        }
        // A glide moved by hand at each beat. The list is put where it goes at once, and each line starts that far back and travels the rest on its own,
        // a moment after the line above it, so a line change ripples down the lyrics line by line (like Liner) instead of moving them as one block.
        sealed class GlideItem {public TranslateTransform Move;public double From,Delay;public SungLine Line;}
        readonly List<GlideItem> glideItems=new List<GlideItem>();
        bool glideOn,glideInOut;double glideStart,glideLength;
        const double Ripple=.032,RippleMost=.26;
        static double Ease(double p,bool inOut){return inOut?(p<.5?4*p*p*p:1-Math.Pow(-2*p+2,3)/2):1-Math.Pow(1-p,3);}
        bool GlideStep(double shown)
        {
            if(!glideOn)return false;
            bool going=false;
            // (The lines keep their in-between positions, so the motion is even; their pictures are kept at twice the resolution, so text drawn
            // between pixels stays sharp. See SungLine.)
            foreach(var item in glideItems)
            {
                double p=Math.Max(0,Math.Min(1,(shown-glideStart-item.Delay)/glideLength));
                item.Move.Y=item.From*(1-Ease(p,glideInOut));
                if(p<1)going=true;
            }
            if(!going)StopGlide(false);
            return true;
        }
        void StopGlide(bool jump)
        {
            if(!glideOn)return;
            glideOn=false;
            foreach(var item in glideItems){item.Move.Y=0;if(item.Line!=null)item.Line.SetMoving(false);}
            glideItems.Clear();
            Cull(scroll.Y,scroll.Y);
        }
        static TranslateTransform MoveOf(UIElement child)
        {
            var move=child.RenderTransform as TranslateTransform;
            if(move==null||move.IsFrozen){move=new TranslateTransform();child.RenderTransform=move;}
            return move;
        }
        // The line being sung at this moment. A little early, so a line is lit as its first word starts.
        void Step(double seconds)
        {
            if(!synced||lines.Count==0)return;
            int index=-1,low=0,high=lines.Count-1;
            while(low<=high){int mid=(low+high)/2;if(lines[mid].Time<=seconds+.25){index=mid;low=mid+1;}else high=mid-1;}
            // A long pause: the line before it rests and the note in its place takes the light, filling up as the next line nears.
            var pause=PauseAt(seconds);
            if(!ReferenceEquals(pause,activeInterlude))
            {
                var was=activeInterlude;activeInterlude=pause;
                // A pause that is over: its note dims, empty again and without its glow.
                if(was!=null){Fade(was.Item,Dim,320);was.Fill.Rect=new Rect(0,was.Height,was.Height,0);was.Glow.Opacity=0;}
                if(pause!=null)
                {
                    if(active>=0&&active<items.Count){((SungLine)items[active].Child).LightAll();Glow(active,false);Fade(items[active],Dim,320);}
                    else if(items.Count>0)Fade(items[0],Dim,320);
                    Fade(pause.Item,1,320);
                    Follow(true,GlideMs);
                }
                else if(index==active)
                {
                    // Back out of a pause without a new line (a seek): the line lights again.
                    if(active>=0){Fade(items[active],1,260);Glow(active,true);}else if(items.Count>0)Fade(items[0],UpNext,260);
                    Follow(true,GlideMs);
                }
            }
            if(pause!=null)
            {
                double p=Math.Max(0,Math.Min(1,(seconds-pause.Start)/Math.Max(.1,pause.End-.25-pause.Start)));
                pause.Fill.Rect=new Rect(0,pause.Height*(1-p),pause.Height,pause.Height*p);
                if(!BigPlayerView.PerformanceMode()){pause.Glow.Opacity=.15+.8*p*p;pause.Glow.BlurRadius=6+pause.Height*.6*p;}
            }
            if(index==active)
            {
                if(pause!=null)return;
                // The lit words never go back by a hair: at a pause the player learns the exact moment a little late, and stepping back to it looked like a glitch.
                // (A real jump back, a seek, is more than half a second.)
                if(seconds<litSeconds&&litSeconds-seconds<.5)return;
                litSeconds=seconds;
                LightWords(seconds+.1);return;
            }
            litSeconds=seconds;
            int before=active;
            active=index;
            manual=0;
            if(before>=0&&before<items.Count){((SungLine)items[before].Child).LightAll();Glow(before,false);}
            if(pause!=null){Follow(true,GlideMs);return;}
            LightWords(seconds+.1);
            if(before>=0&&before<items.Count)Fade(items[before],Dim,260);
            if(active>=0){Fade(items[active],1,260);Glow(active,true);}
            else if(items.Count>0){Fade(items[0],UpNext,260);if(WordByWord())((SungLine)items[0].Child).Light(-1,0);RefreshBlur();}
            Follow(true,GlideMs);
        }
        // The line being sung has a soft glow around its sung letters (left out in Performance mode).
        void Glow(int index,bool on)
        {
            if(index<0||index>=items.Count)return;
            ((SungLine)items[index].Child).SetGlow(on&&!BigPlayerView.PerformanceMode());
            RefreshBlur();
        }
        // The lines around the one being sung (or the note of a pause) are softly blurred, more the further away; the one in focus is sharp.
        void RefreshBlur()
        {
            if(!synced||items.Count==0)return;
            // While you scroll through the lyrics yourself everything is sharp, so you can read it; the blur comes back with the next line.
            bool off=BigPlayerView.PerformanceMode()||Math.Abs(manual)>.5;
            double focus=activeInterlude!=null?activeInterlude.After+.5:Math.Max(0,active);
            double unit=Math.Max(1,lineSize/22);
            for(int i=0;i<items.Count;i++)
            {
                double away=Math.Abs(i-focus);
                // Two levels: a touch of blur next to the line in focus, more beyond.
                double radius=off||away<.6?0:away<1.6?unit*1.3:unit*2.8;
                ((SungLine)items[i].Child).SetBlur(radius);
            }
            // The note of a pause sits between two lines and is blurred the same way by how far it is from the focus (sharp while it is the pause being held).
            foreach(var pause in interludes)
            {
                double away=Math.Abs(pause.After+.5-focus);
                double radius=Math.Round((off||away<.6?0:away<1.6?unit*1.3:unit*2.8)*2)/2;
                var face=pause.Item.Child as FrameworkElement;if(face==null)continue;
                var current=face.Effect as System.Windows.Media.Effects.BlurEffect;
                if(radius<=0){if(current!=null){face.Effect=null;face.CacheMode=null;}continue;}
                if(current!=null&&Math.Abs(current.Radius-radius)<.01)continue;
                face.Effect=new System.Windows.Media.Effects.BlurEffect {Radius=radius,KernelType=System.Windows.Media.Effects.KernelType.Gaussian,RenderingBias=System.Windows.Media.Effects.RenderingBias.Performance};
                face.CacheMode=new BitmapCache();
            }
        }
        // Slides the lines so the active one rests about a third of the way down.
        // Lines far above or below what is on screen are not drawn at all (they keep their room, so nothing moves). Drawing every line of a long song
        // each frame while the lyrics slide made scrolling stutter.
        void Cull(double from,double to)
        {
            double margin=ActualHeight*.5,low=-Math.Max(from,to)-margin,high=ActualHeight-Math.Min(from,to)+margin,top=0;
            // Every row of the list, the lines and the notes of the pauses.
            foreach(UIElement child in list.Children)
            {
                var row=child as FrameworkElement;if(row==null)continue;
                double bottom=top+row.ActualHeight;
                var wanted=bottom>=low&&top<=high?Visibility.Visible:Visibility.Hidden;
                if(row.Visibility!=wanted)row.Visibility=wanted;
                top=bottom;
            }
        }
        void Follow(bool animate,int milliseconds=520)
        {
            if(items.Count==0||ActualHeight<=0)return;
            double target;
            // The top and bottom of the view fade out, so the line in use rests where it is fully lit; until the first line, that place is for the first line.
            if(!synced)target=ActualHeight*.16;
            else
            {
                // The note of a pause while it plays, otherwise the line in use.
                FrameworkElement item=activeInterlude!=null?(FrameworkElement)activeInterlude.Item:items[Math.Max(0,active)];
                double top=item.TranslatePoint(new Point(),list).Y-MoveOf(item).Y;
                target=ActualHeight*.34-(top+item.ActualHeight/2);
            }
            target+=manual;
            // Rest on a whole pixel, so the text lands on the pixel grid and is sharp once it has arrived.
            double pixel=VisualTreeHelper.GetDpi(this).DpiScaleY;target=Math.Round(target*pixel)/pixel;
            // (Where the lines are right now, also partway through a glide.)
            double from=scroll.Y;
            scroll.BeginAnimation(TranslateTransform.YProperty,null);scroll.Y=from;
            Cull(from,target);
            if(!animate||!IsVisible||Design.Instant)
            {
                glideOn=false;foreach(var g in glideItems){g.Move.Y=0;if(g.Line!=null)g.Line.SetMoving(false);}glideItems.Clear();
                scroll.Y=target;Cull(target,target);return;
            }
            // A line change starts gently and settles (see GlideMs) and ripples down the lines; the wheel answers at once, all together.
            glideInOut=milliseconds==GlideMs&&GlideInOut;
            double ripple=glideInOut?Ripple:0;
            // Each row starts where it shows now (also partway through an earlier glide) and travels to its place; the rows on screen go one after another.
            var wasMoving=new List<SungLine>();foreach(var g in glideItems)if(g.Line!=null)wasMoving.Add(g.Line);
            glideItems.Clear();
            int order=0;
            foreach(UIElement child in list.Children)
            {
                var move=MoveOf(child);
                double start=from+move.Y-target;
                var border=child as Border;var line=border==null?null:border.Child as SungLine;
                if(child.Visibility!=Visibility.Visible){move.Y=0;if(line!=null)line.SetMoving(false);continue;}
                // A sharp line glides from its finer picture (a blurred one needs none).
                if(line!=null)line.SetMoving(!line.Blurred);
                glideItems.Add(new GlideItem {Move=move,From=start,Delay=Math.Min(RippleMost,order*ripple),Line=line});
                move.Y=start;order++;
            }
            foreach(var line in wasMoving)if(!glideItems.Any(g=>ReferenceEquals(g.Line,line)))line.SetMoving(false);
            scroll.Y=target;
            glideStart=ScreenBeat.Now;glideLength=milliseconds/1000.0;glideOn=true;
            Listen();
        }
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if(items.Count==0)return;
            manual+=e.Delta*.6;
            if(!synced)manual=Math.Min(0,Math.Max(-Math.Max(0,list.ActualHeight-ActualHeight*.7),manual));
            RefreshBlur();
            Follow(true,170);
            e.Handled=true;
        }
    }

    // One lyric line that can light up word by word. The words already sung are bright, the words still to come are dim, and the word being sung
    // lights from left to right as it is sung. Not being sung, the whole line is drawn in full light (the line's own opacity dims it).
    // The text is drawn twice, dim and bright, each kept as a picture like all text in the app. The bright one sits in a holder that only shows it up
    // to the edge of what has been sung: lighting the words moves a few rectangles and never draws the text again. Where each word is is measured
    // once, when the line is first lit, not at every frame.
    internal sealed class SungLine:FrameworkElement
    {
        // The pictures the line is kept as. At rest at the screen's own resolution (sharpest); while it glides at twice that, since a line drawn
        // between pixels from a picture at the screen's resolution went soft (the lines lower down, still moving in the ripple, looked blurred).
        static readonly BitmapCache Sharp=NewCache(1),Fine=NewCache(2);
        static BitmapCache NewCache(double scale){var cache=new BitmapCache{SnapsToDevicePixels=true,RenderAtScale=scale};cache.Freeze();return cache;}
        static readonly Brush DimInk=Frozen(Color.FromArgb(112,255,255,255));
        static Brush Frozen(Color color){var brush=new SolidColorBrush(color);brush.Freeze();return brush;}
        // The window that scales its contents (the mini player) keeps the pictures at twice the detail all the time, so they stay sharp when it is
        // enlarged. (It used to turn the pictures off and draw every line again at each frame, which made the mini player lag.)
        public static readonly DependencyProperty CrispProperty=SmoothText.CrispProperty.AddOwner(typeof(SungLine),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.Inherits,(d,e)=>((SungLine)d).Recache()));
        // face holds everything: the dim text, and the holder that shows the bright text (with its glow) up to what has been sung.
        readonly ContainerVisual face=new ContainerVisual();
        readonly DrawingVisual dim=new DrawingVisual(),bright=new DrawingVisual(),aura=new DrawingVisual();
        readonly ContainerVisual holder=new ContainerVisual();
        bool moving;double blur;
        System.Windows.Media.Effects.BlurEffect blurEffect;
        string text=" ";double fontSize=30;TextAlignment alignment=TextAlignment.Left;
        FormattedText laid;string laidKey;
        int[] wordStart=new int[0],wordLength=new int[0];
        // Each word's box (a little larger than its letters), measured once per layout.
        Rect[] boxes;
        int litWord=-2;double litPart=-1,litEdge=-1;
        // The part of the word being sung: one rectangle whose right side follows the song.
        readonly RectangleGeometry edge=new RectangleGeometry();

        public SungLine()
        {
            AddVisualChild(face);
            face.Children.Add(dim);face.Children.Add(holder);
            holder.Children.Add(aura);holder.Children.Add(bright);
            aura.Opacity=0;
            Recache();
            dim.Opacity=0;
            IsVisibleChanged+=(s,e)=>{if((bool)e.NewValue&&laidKey==null)InvalidateVisual();};
        }
        void Recache()
        {
            var cache=moving||(bool)GetValue(CrispProperty)?Fine:Sharp;
            dim.CacheMode=cache;bright.CacheMode=cache;aura.CacheMode=cache;
            // A blurred line is kept as one picture with its blur, so gliding it costs nothing. (There are only two levels of blur, so a line change
            // makes only the few lines that change level again.)
            face.CacheMode=blur>0?((bool)GetValue(CrispProperty)?Fine:Sharp):null;
        }
        internal bool Blurred {get{return blur>0;}}
        protected override int VisualChildrenCount {get{return 1;}}
        protected override Visual GetVisualChild(int index){return face;}
        // While the line glides (twice the detail), and at rest (the screen's own).
        internal void SetMoving(bool on){if(on==moving)return;moving=on;Recache();}
        // Lines away from the one being sung are softly blurred, more the further away they are (0: sharp).
        internal void SetBlur(double radius)
        {
            radius=Math.Round(radius*2)/2;
            if(Math.Abs(radius-blur)<.01)return;
            blur=radius;
            if(blur<=0){face.Effect=null;}
            else
            {
                if(blurEffect==null)blurEffect=new System.Windows.Media.Effects.BlurEffect {KernelType=System.Windows.Media.Effects.KernelType.Gaussian,RenderingBias=System.Windows.Media.Effects.RenderingBias.Performance};
                blurEffect.Radius=blur;face.Effect=blurEffect;
            }
            Recache();
        }
        // A soft glow around the letters already sung (only the line being sung has it).
        internal void SetGlow(bool on){aura.Opacity=on?1:0;}
        public string Text
        {
            get{return text;}
            set
            {
                text=string.IsNullOrEmpty(value)?" ":value;
                var starts=new List<int>();var lengths=new List<int>();
                for(int i=0;i<text.Length;)
                {
                    while(i<text.Length&&char.IsWhiteSpace(text[i]))i++;
                    int start=i;
                    while(i<text.Length&&!char.IsWhiteSpace(text[i]))i++;
                    if(i>start){starts.Add(start);lengths.Add(i-start);}
                }
                wordStart=starts.ToArray();wordLength=lengths.ToArray();
                laidKey=null;InvalidateMeasure();InvalidateVisual();
            }
        }
        internal int WordCount {get{return wordStart.Length;}}
        public double FontSize {get{return fontSize;}set{fontSize=value;laidKey=null;InvalidateMeasure();InvalidateVisual();}}
        public TextAlignment TextAlignment {get{return alignment;}set{alignment=value;laidKey=null;InvalidateVisual();}}
        FormattedText Format(double width)
        {
            var format=new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(Design.DisplayFont,FontStyles.Normal,FontWeights.Bold,FontStretches.Normal),fontSize,Brushes.White,null,TextFormattingMode.Ideal,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if(width>0&&!double.IsInfinity(width))format.MaxTextWidth=width;
            format.TextAlignment=alignment;
            return format;
        }
        protected override Size MeasureOverride(Size available)
        {
            var format=Format(available.Width);
            return new Size(Math.Ceiling(format.WidthIncludingTrailingWhitespace),Math.Ceiling(format.Height));
        }
        protected override void OnRender(DrawingContext dc)
        {
            // Slightly over the given width, so a rounded layout cannot rewrap the last word.
            double width=RenderSize.Width+1;
            string key=text+"\u0001"+Math.Round(width,2)+"\u0001"+fontSize+"\u0001"+(int)alignment;
            if(key==laidKey)return;
            // Lines far from the one being sung are drawn only once they show (see SmoothText).
            if(!IsVisible){laidKey=null;return;}
            laid=Format(width);
            var outline=laid.BuildGeometry(new Point());
            if(outline.CanFreeze)outline.Freeze();
            // Outlines alone read lighter than other apps' text on dark; a hairline restores the weight.
            using(var draw=dim.RenderOpen())draw.DrawGeometry(DimInk,new Pen(DimInk,.2),outline);
            using(var draw=bright.RenderOpen())draw.DrawGeometry(Brushes.White,new Pen(Brushes.White,.2),outline);
            // The glow: the letters' outlines drawn wide and faint a few times, so it hugs each letter instead of lighting a box around the line.
            using(var draw=aura.RenderOpen())
                foreach(var ring in new[]{new[]{fontSize*.26,7},new[]{fontSize*.16,12},new[]{fontSize*.08,22}})
                    draw.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromArgb((byte)ring[1],255,255,255)),ring[0]){LineJoin=PenLineJoin.Round},outline);
            laidKey=key;
            boxes=null;
            int word=litWord;double part=litPart;litWord=-2;
            if(word!=-2)Light(word,part);
        }
        // Where each word is, measured on the laid-out text once (asking the text where a word is costs a layout of the line).
        void MeasureWords()
        {
            boxes=new Rect[wordStart.Length];
            for(int k=0;k<wordStart.Length;k++)
            {
                var shape=laid.BuildHighlightGeometry(new Point(),wordStart[k],wordLength[k]);
                var box=shape==null?Rect.Empty:shape.Bounds;
                // Larger all round, so a letter that reaches past its box (a bold f, a j) and the glow around the letters are not cut.
                double pad=Math.Max(4,fontSize*.16);
                boxes[k]=box.IsEmpty?new Rect():new Rect(box.Left-pad,box.Top-pad,box.Width+pad*2,box.Height+pad*2);
            }
        }
        // The whole line in full light (it is not being sung).
        internal void LightAll(){if(litWord==-2)return;litWord=-2;litPart=-1;holder.Clip=null;dim.Opacity=0;}
        // The words before word are sung, word itself is sung for part (0 to 1), the rest are still to come. word -1: nothing yet.
        internal void Light(int word,double part)
        {
            part=Math.Max(0,Math.Min(1,part));
            if(laid==null||wordStart.Length==0){litWord=word;litPart=part;holder.Clip=null;dim.Opacity=0;return;}
            if(word>=wordStart.Length){if(litWord!=word){litWord=word;litPart=part;holder.Clip=null;dim.Opacity=0;}return;}
            if(boxes==null)MeasureWords();
            if(word!=litWord)
            {
                // A new word: the words before it are lit whole (they change only once per word), and the edge starts on this one.
                litWord=word;litEdge=-1;
                var clip=new GeometryGroup {FillRule=FillRule.Nonzero};
                for(int k=0;k<word&&k<boxes.Length;k++){var whole=new RectangleGeometry(boxes[k]);whole.Freeze();clip.Children.Add(whole);}
                clip.Children.Add(edge);
                holder.Clip=clip;
                dim.Opacity=1;
            }
            litPart=part;
            // The edge moves only when it moves on screen (by a quarter of a pixel or more).
            var box=word>=0&&word<boxes.Length?boxes[word]:new Rect();
            double inset=Math.Max(4,fontSize*.16);
            double width=word<0?0:part<=0?0:part>=1?box.Width:inset+(box.Width-inset*2)*part;
            if(Math.Abs(width-litEdge)<.25)return;
            litEdge=width;
            edge.Rect=new Rect(box.Left,box.Top,Math.Max(0,width),box.Height);
        }
    }
}
