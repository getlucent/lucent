using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lucent
{
    // What a command takes after its name: nothing, a number ("vibrance 150"), or on / off ("crosshair off"; without it, it switches).
    internal enum PaletteArgument {None,Number,OnOff}

    // One thing the command palette can do.
    internal sealed class PaletteCommand
    {
        public string Title;            // "Vibrance"
        public string Hint;             // "Set vibrance, 100–300%"
        public string Icon;             // a Design.Icon name
        public string[] Words=new string[0];   // other names it answers to ("saturation", "color")
        public PaletteArgument Argument;
        public double Min,Max;
        public string Unit="";          // "%"
        public Action<string> Run;      // the argument as typed (a clamped number, "on", "off" or "")
    }

    // A command as it would run for what was typed: its title with the argument ("Vibrance 150%"), and what it is given.
    internal sealed class PaletteMatch
    {
        public PaletteCommand Command;
        public string Argument="";
        public string Title;
        public string Hint;
        public bool Ready=true;         // false while a number is still missing
        public double Score;
    }

    // Reads what is typed into the palette: the words pick the command (any word, any order, the start of a word is enough), and a number or on / off
    // at the end is what it is given. "vib 150", "vibrance 150%", "set vibrance to 150", "crosshair off", "night vision" all work.
    internal static class CommandPalette
    {
        static readonly HashSet<string> Filler=new HashSet<string>{"set","to","turn","the","a","at","switch","go","open","show"};
        internal static List<PaletteMatch> Match(IEnumerable<PaletteCommand> commands,string typed)
        {
            var tokens=Regex.Split((typed??"").Trim().ToLowerInvariant(),@"\s+").Where(t=>t.Length>0).ToList();
            // What it is given: a number at the end (with or without %), or on / off.
            string number=null,onOff=null;
            if(tokens.Count>0)
            {
                string last=tokens[tokens.Count-1].TrimEnd('%','x','×');
                double value;
                if(double.TryParse(last.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&tokens.Count>0){number=value.ToString(CultureInfo.InvariantCulture);tokens.RemoveAt(tokens.Count-1);}
                else if(last=="on"||last=="off"){onOff=last;tokens.RemoveAt(tokens.Count-1);}
            }
            var words=tokens.Where(t=>!Filler.Contains(t)).ToList();
            if(words.Count==0&&tokens.Count>0)words=tokens;
            var found=new List<PaletteMatch>();
            int order=0;
            foreach(var command in commands)
            {
                order++;
                var names=Regex.Split((command.Title+" "+string.Join(" ",command.Words)).ToLowerInvariant(),@"[^a-z0-9]+").Where(n=>n.Length>0).ToArray();
                double score=0;bool all=true;
                foreach(var word in words)
                {
                    double best=0;
                    for(int i=0;i<names.Length;i++)
                    {
                        if(names[i]==word)best=Math.Max(best,3);
                        else if(names[i].StartsWith(word))best=Math.Max(best,2+word.Length/(double)names[i].Length);
                        else if(word.Length>=3&&names[i].Contains(word))best=Math.Max(best,1);
                    }
                    if(best==0){all=false;break;}
                    score+=best;
                }
                if(!all)continue;
                // A number only goes to a command that takes one, on / off only to a switch.
                if(number!=null&&command.Argument!=PaletteArgument.Number)continue;
                if(onOff!=null&&command.Argument!=PaletteArgument.OnOff)continue;
                // The title itself matching beats only a keyword matching.
                var titleWords=Regex.Split(command.Title.ToLowerInvariant(),@"[^a-z0-9]+");
                if(words.Count>0&&titleWords.Any(t=>t.StartsWith(words[0])))score+=1;
                var match=new PaletteMatch {Command=command,Score=score-order*.001,Hint=command.Hint};
                if(command.Argument==PaletteArgument.Number)
                {
                    if(number==null){match.Ready=false;match.Title=command.Title+" …";match.Hint=command.Hint+" — type a number";}
                    else
                    {
                        double value=Math.Max(command.Min,Math.Min(command.Max,double.Parse(number,CultureInfo.InvariantCulture)));
                        match.Argument=value.ToString(CultureInfo.InvariantCulture);
                        match.Title=command.Title+" "+Math.Round(value,1).ToString(CultureInfo.InvariantCulture)+command.Unit;
                    }
                }
                else if(command.Argument==PaletteArgument.OnOff)
                {
                    match.Argument=onOff??"";
                    match.Title=command.Title+(onOff==null?"":" "+onOff);
                }
                else match.Title=command.Title;
                found.Add(match);
            }
            return found.OrderByDescending(m=>m.Score).ToList();
        }
        // On, off, or the other way round from now, for a switch.
        internal static bool Wanted(string argument,bool now){return argument=="on"?true:argument=="off"?false:!now;}
    }
}
