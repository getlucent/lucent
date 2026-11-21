using System;
using System.Collections.Generic;
using System.Windows;
using TextBlock=Lucent.SmoothText;

namespace Lucent
{
    // Compact mode (Settings > Appearance): tighter spacing and smaller headings, so more fits on a small screen. It changes at once, without
    // building the pages again: the cards, headings and switches remember their own spacing and are drawn closer together from it.
    internal sealed partial class Shell
    {
        sealed class CompactItem {public FrameworkElement Element;public Thickness Margin,Applied;public TextBlock Title;public double Size;}
        readonly List<CompactItem> compactItems=new List<CompactItem>();
        const double CompactSpace=.66,CompactText=.86;
        // Registers something whose spacing (and heading size) follows compact mode.
        internal T Compactable<T>(T element,TextBlock title=null) where T:FrameworkElement
        {
            var item=new CompactItem {Element=element,Margin=element.Margin,Applied=element.Margin,Title=title,Size=title==null?0:title.FontSize};
            compactItems.Add(item);
            if(preferences!=null&&preferences.Compact)Squeeze(item,true);
            return element;
        }
        void Squeeze(CompactItem item,bool on)
        {
            // A page that set its own spacing after it was made keeps that as its own.
            if(item.Element.Margin!=item.Applied)item.Margin=item.Element.Margin;
            var m=item.Margin;double k=on?CompactSpace:1;
            item.Applied=new Thickness(Math.Round(m.Left*k),Math.Round(m.Top*k),Math.Round(m.Right*k),Math.Round(m.Bottom*k));
            item.Element.Margin=item.Applied;
            if(item.Title!=null)item.Title.FontSize=on?Math.Round(item.Size*CompactText):item.Size;
        }
        internal void ApplyCompact()
        {
            bool on=preferences.Compact;
            foreach(var item in compactItems)Squeeze(item,on);
            PlaceDock();
        }
    }
}
