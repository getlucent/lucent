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
    // The Resolution page and everything that reads or changes a display mode.
    internal sealed partial class Shell
    {
        FrameworkElement ResolutionPage()
        {
            var stack=new StackPanel {Margin=new Thickness(0,16,0,5)};
            var title=Text(TabTitle("Resolution"),32,true);title.Margin=new Thickness(0,0,0,22);stack.Children.Add(Compactable(title,title));
            var select=new ComboBox {ItemsSource=displays,SelectedItem=selected,Margin=new Thickness(0,0,0,16),MinHeight=42,FontSize=14,FontWeight=FontWeights.SemiBold,ItemTemplate=Design.TextTemplate(null)};
            displaySelect=select;
            stack.Children.Add(select);
            var modesPanel=new StackPanel();
            stack.Children.Add(modesPanel);
            Action refresh=null;
            refresh=()=>
            {
                if(selected==null)return;
                lastDisplaySignature=DisplaySignature();
                modesPanel.Children.Clear();
                var currentMode=DisplayService.GetCurrentMode(selected.DeviceName);
                if(currentMode==null)return;
                var modes=DisplayService.GetModes(selected.DeviceName);
                // What the display is doing, the choices that change it, and the tools around it.
                var main=new StackPanel();
                main.Children.Add(DisplayHero(modes,currentMode));
                main.Children.Add(ResolutionChoices(modes,currentMode,refresh));
                var aside=new StackPanel();
                aside.Children.Add(BuildResolutionPair(modes,currentMode,refresh));
                aside.Children.Add(BuildScaling(selected.DeviceName));
                aside.Children.Add(CustomResolutionCard());
                modesPanel.Children.Add(ResponsiveColumns(main,aside,1.55));
            };
            resolutionRefresh=refresh;
            select.SelectionChanged+=(s,e)=>
            {
                var chosen=select.SelectedItem as DisplayInfo;
                if(chosen!=null)SelectDisplay(chosen);
            };
            refresh();
            return stack;
        }
        // The nearest everyday name for a shape, so the sizes can be grouped the way people think of them.
        static string AspectName(int width,int height)
        {
            double ratio=(double)width/height;
            var known=new[]{new KeyValuePair<string,double>("16:9",16/9d),new KeyValuePair<string,double>("16:10",1.6),new KeyValuePair<string,double>("21:9",64/27d),new KeyValuePair<string,double>("4:3",4/3d),new KeyValuePair<string,double>("5:4",1.25),new KeyValuePair<string,double>("3:2",1.5),new KeyValuePair<string,double>("1:1",1)};
            foreach(var shape in known)if(Math.Abs(ratio-shape.Value)<.02)return shape.Key;
            return "Other";
        }
        // The display drawn to scale in its own shape, beside the numbers that describe it.
        FrameworkElement DisplayHero(List<DisplayMode> modes,DisplayMode mode)
        {
            var card=Section("Active display","What Windows reports for this display right now.");
            double aspect=(double)mode.Width/mode.Height,width=Math.Min(220,132*aspect),height=width/aspect;
            var screen=new Border {Width=width,Height=height,CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(2),BorderBrush=new SolidColorBrush(Color.FromArgb(110,255,255,255)),Background=new LinearGradientBrush(Color.FromRgb(52,58,72),Color.FromRgb(20,22,28),60)};
            screen.Child=new TextBlock {Text=AspectName(mode.Width,mode.Height),FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromArgb(150,255,255,255)),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            var drawing=new StackPanel {HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Width=232};
            drawing.Children.Add(screen);
            drawing.Children.Add(new Border {Width=10,Height=9,Background=new SolidColorBrush(Color.FromArgb(70,255,255,255))});
            drawing.Children.Add(new Border {Width=58,Height=4,CornerRadius=new CornerRadius(2),Background=new SolidColorBrush(Color.FromArgb(90,255,255,255))});
            var numbers=new StackPanel {VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,0,0)};
            var size=Text(SizeLabel(mode),32,true);
            size.FontFamily=Design.DisplayFont;
            size.Margin=new Thickness(0,0,0,10);
            numbers.Children.Add(size);
            var facts=new WrapPanel();
            foreach(string fact in new[]{mode.BitsPerPixel+"-bit color",AspectName(mode.Width,mode.Height),(mode.Width*mode.Height/1000000d).ToString("0.00")+" MP"})
                facts.Children.Add(new Border {Background=optionSurface,CornerRadius=new CornerRadius(9),Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,6,6),Child=new TextBlock {Text=fact,FontSize=12,FontWeight=FontWeights.SemiBold}});
            numbers.Children.Add(facts);
            numbers.Children.Add(RefreshPicker(modes,mode));
            var row=new Grid {Margin=new Thickness(0,14,0,6)};
            row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(drawing);
            Grid.SetColumn(numbers,1);
            row.Children.Add(numbers);
            card.Children.Add(row);
            return card;
        }
        // Sizes grouped by shape, favorites first and the display's own shape ahead of the rest.
        FrameworkElement ResolutionChoices(List<DisplayMode> modes,DisplayMode currentMode,Action refresh)
        {
            var card=Section("Resolution","");
            var sizes=modes.GroupBy(x=>new{x.Width,x.Height}).Select(g=>g.First()).ToList();
            var favorites=sizes.Where(m=>preferences.Favorites.Contains(m.Width+"x"+m.Height)).OrderByDescending(m=>m.Width*m.Height).ToList();
            string own=AspectName(currentMode.Width,currentMode.Height);
            var groups=sizes.GroupBy(m=>AspectName(m.Width,m.Height)).Select(g=>new{Name=g.Key,Items=g.OrderByDescending(m=>m.Width*m.Height).ToList()})
                .OrderByDescending(g=>g.Name==own?1:0).ThenBy(g=>g.Name=="Other"?1:0).ThenByDescending(g=>g.Items.Count).ToList();
            Action<string,List<DisplayMode>> add=(name,items)=>
            {
                if(name!=null)
                {
                    var heading=Text(name,12,true);
                    heading.Foreground=Muted;
                    heading.Margin=new Thickness(0,12,0,6);
                    card.Children.Add(heading);
                }
                var chips=new WrapPanel();
                foreach(var mode in items)chips.Children.Add(ResolutionChip(mode,currentMode,refresh));
                card.Children.Add(chips);
            };
            if(favorites.Count>0)add("Favorites",favorites);
            // One shape at a time instead of one long list: a row of shape tabs (the display's own shape first), and only that shape's sizes under it.
            if(groups.Count==0)return card;
            if(resolutionTab==null||!groups.Any(g=>g.Name==resolutionTab))resolutionTab=groups[0].Name;
            var tabsHeading=Text("All sizes",12,true);tabsHeading.Foreground=Muted;tabsHeading.Margin=new Thickness(0,14,0,6);
            card.Children.Add(tabsHeading);
            var tabs=new WrapPanel {Margin=new Thickness(0,0,0,10)};
            foreach(var group in groups)
            {
                string name=group.Name;bool on=name==resolutionTab;
                var tab=Action((on?"✓  ":"")+name+"  "+group.Items.Count,()=>{resolutionTab=name;refresh();});
                tab.Padding=new Thickness(14,7,14,7);tab.Margin=new Thickness(0,0,8,8);
                if(on)tab.Background=selectionSurface;
                System.Windows.Automation.AutomationProperties.SetName(tab,"Sizes "+name);
                tabs.Children.Add(tab);
            }
            card.Children.Add(tabs);
            add(null,groups.First(g=>g.Name==resolutionTab).Items);
            return card;
        }
        // The shape tab picked in Resolution (kept while the page is rebuilt).
        string resolutionTab;
        // The whole chip lights while the pointer is on the size. The star beside it lights only itself.
        Border ResolutionChip(DisplayMode mode,DisplayMode currentMode,Action refresh)
        {
            int w=mode.Width,h=mode.Height;string id=w+"x"+h;
            bool isActive=w==currentMode.Width&&h==currentMode.Height;
            var row=new DockPanel();
            var favorite=Action("",()=>
            {
                if(preferences.Favorites.Contains(id))preferences.Favorites.Remove(id);else preferences.Favorites.Add(id);
                SaveSoon();
                refresh();
            });
            // No box behind the star: the star itself brightens and glows under the pointer, and a favorite stays lit.
            bool starred=preferences.Favorites.Contains(id);
            var star=new TextBlock {Text=starred?"\u2605":"\u2606",FontSize=15,Opacity=starred?.9:.5,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            favorite.Style=(Style)Resources["PlainAction"];
            favorite.Content=star;
            favorite.Tag="Favorite";
            favorite.ToolTip="Toggle favorite";favorite.Padding=new Thickness(9,0,9,0);favorite.Margin=new Thickness(0);
            favorite.MouseEnter+=(s,e)=>
            {
                star.Effect=new System.Windows.Media.Effects.DropShadowEffect {Color=Colors.White,BlurRadius=11,ShadowDepth=0,Opacity=.85};
                Design.Animate(star,OpacityProperty,star.Opacity,1,100);
            };
            favorite.MouseLeave+=(s,e)=>
            {
                star.Effect=null;
                Design.Animate(star,OpacityProperty,star.Opacity,starred?.9:.5,160);
            };            DockPanel.SetDock(favorite,Dock.Right);
            row.Children.Add(favorite);
            var modeButton=Action((isActive?"✓  ":"")+w+" × "+h,()=>{ApplyResolution(w,h);refresh();});
            modeButton.Style=(Style)Resources["PlainAction"];
            modeButton.Padding=new Thickness(14,11,0,11);
            modeButton.Margin=new Thickness(0);
            modeButton.HorizontalAlignment=HorizontalAlignment.Stretch;
            modeButton.HorizontalContentAlignment=HorizontalAlignment.Left;
            row.Children.Add(modeButton);
            var lift=new Border {CornerRadius=new CornerRadius(10),Background=new SolidColorBrush(Color.FromArgb(30,255,255,255)),Opacity=0,IsHitTestVisible=false};
            modeButton.MouseEnter+=(s,e)=>Design.Animate(lift,OpacityProperty,lift.Opacity,1,100);
            modeButton.MouseLeave+=(s,e)=>Design.Animate(lift,OpacityProperty,lift.Opacity,0,160);
            var layers=new Grid();
            layers.Children.Add(row);
            layers.Children.Add(lift);
            return new Border {Width=162,Margin=new Thickness(0,0,8,8),CornerRadius=new CornerRadius(10),Background=isActive?selectionSurface:optionSurface,Child=layers,Tag=lift};
        }
        // Only the rates this display supports at its size and color depth, as a dropdown beside what it is doing now.
        FrameworkElement RefreshPicker(List<DisplayMode> modes,DisplayMode mode)
        {
            var rates=modes.Where(x=>x.Width==mode.Width&&x.Height==mode.Height&&x.BitsPerPixel==mode.BitsPerPixel).Select(x=>x.Frequency).Distinct().OrderByDescending(x=>x).Select(x=>x+" Hz").ToList();
            string now=mode.Frequency+" Hz";
            if(!rates.Contains(now))rates.Insert(0,now);
            var panel=new StackPanel {Margin=new Thickness(0,6,0,0)};
            var caption=Text("Refresh rate",12,true);
            caption.Foreground=Muted;
            caption.Margin=new Thickness(0,0,0,6);
            panel.Children.Add(caption);
            var pick=new ComboBox {ItemsSource=rates,SelectedItem=now,MinHeight=40,Width=176,HorizontalAlignment=HorizontalAlignment.Left,FontSize=14,FontWeight=FontWeights.SemiBold,ItemTemplate=Design.TextTemplate(null),ToolTip="Only the rates this display supports at this size"};
            System.Windows.Automation.AutomationProperties.SetName(pick,"Refresh rate");
            bool busy=false;
            pick.SelectionChanged+=async(s,e)=>
            {
                string choice=pick.SelectedItem as string;int rate;
                if(busy||choice==null||!int.TryParse(choice.Replace(" Hz",""),out rate)||rate==mode.Frequency||TestMode)return;
                busy=true;
                try
                {
                    string device=selected.DeviceName;
                    var result=await Task.Factory.StartNew(()=>RefreshRateTransaction.ApplySupported(device,rate));
                    if(!result.Kept){Message(result.Error);pick.SelectedItem=now;}
                    else
                    {
                        RefreshSound.Play();
                        RefreshDisplayViews();
                        SetStatus("Refresh rate applied: "+rate+" Hz");
                    }
                }
                finally{busy=false;}
            };
            panel.Children.Add(pick);
            return panel;
        }
        FrameworkElement CustomResolutionCard()
        {
            var card=Section("Custom resolutions","Add a size your driver does not list.");
            card.Children.Add(Action("Open custom resolutions…",()=>
            {
                if(TestMode)return;
                using(var dialog=new CustomResolutionDialog())dialog.ShowDialog();
            }));
            return card;
        }
        // Video and Resolution show the same choice, and every display action reads it from here.
        void SelectDisplay(DisplayInfo display)
        {
            if(display==null||ReferenceEquals(display,selected))return;
            selected=display;
            if(displaySelect!=null&&!ReferenceEquals(displaySelect.SelectedItem,display))displaySelect.SelectedItem=display;
            foreach(Button card in monitorCards.Children)StyleMonitorCard(card,ReferenceEquals(card.Tag,selected));
            displayStatus.Text=CurrentModeText();
            if(resolutionRefresh!=null)resolutionRefresh();
        }
        static string DisplayModeText(DisplayMode mode){return mode==null?"Unavailable":mode.ResolutionText+" · "+mode.Frequency+" Hz";}
        string DisplaySignature()
        {
            return string.Join("|",DisplayService.GetDisplays().Select(x=>x.DeviceName+"="+DisplayModeText(DisplayService.GetCurrentMode(x.DeviceName))));
        }
        void ListenForDisplayChanges(){Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=OnDisplaySettingsChanged;}
        void OnDisplaySettingsChanged(object sender,EventArgs args){Dispatcher.BeginInvoke(new Action(RefreshDisplayViews));}
        // Resolution can change from a hotkey, the tray, a game or Windows itself, so every view re-reads it.
        void RefreshDisplayViews()
        {
            if(closing)return;
            var fresh=DisplayService.GetDisplays();
            if(!fresh.Select(x=>x.DeviceName).SequenceEqual(displays.Select(x=>x.DeviceName)))
            {
                RebuildDisplayPages(fresh);
                return;
            }
            foreach(var label in monitorModeLabels)label.Value.Text=DisplayModeText(DisplayService.GetCurrentMode(label.Key.DeviceName));
            displayStatus.Text=CurrentModeText();
            if(resolutionRefresh!=null&&lastDisplaySignature!=DisplaySignature())resolutionRefresh();
            UpdateHome();
        }
        void RebuildDisplayPages(List<DisplayInfo> fresh)
        {
            string device=selected==null?null:selected.DeviceName;
            displays=fresh;
            selected=fresh.FirstOrDefault(x=>x.DeviceName==device)??fresh.FirstOrDefault(x=>x.IsPrimary)??fresh.FirstOrDefault();
            building=true;
            visionChoices.Clear();
            pages["Video"]=VideoPage();
            pages["Resolution"]=ResolutionPage();
            RefreshProfiles();
            building=false;
            foreach(string key in new[]{"Video","Resolution"}){ContentControl holder;if(pageHolders.TryGetValue(key,out holder))holder.Content=pages[key];}
        }
        void ApplyResolution(int w,int h,bool returning=false)
        {
            if(TestMode||selected==null)return;
            var before=DisplayService.GetCurrentMode(selected.DeviceName);
            var target=DisplayService.FindResolutionOnlyMode(DisplayService.GetModes(selected.DeviceName),w,h,before);
            if(target==null)
            {
                Message("That resolution is not reported at the current refresh rate and color depth.");
                return;
            }
            using(var gate=new Mutex(false,LegacyNames.CustomTrialMutex))
            {
                bool owns=false;
                try
                {
                    try
                    {
                        owns=gate.WaitOne(0);
                    }
                    catch(AbandonedMutexException)
                    {
                        owns=true;
                    }
                    if(!owns)
                    {
                        Message("Another display test is active. Finish it first.");
                        return;
                    }
                    var result=DisplayService.Apply(target,before,false,false);
                    if(!result.IsSuccess)Message(result.ErrorMessage);
                    else
                    {
                        if(returning)SwitchSound.PlayBack();
                        else SwitchSound.Play();
                        RefreshDisplayViews();
                        Count(c=>c.ResolutionSwitches++);SetStatus("Applied "+target.ResolutionText+" · "+before.Frequency+" Hz");
                    }
                }
                finally
                {
                    if(owns)gate.ReleaseMutex();
                }
            }
        }
        Button displaySwitchField;
        ComboBox displaySelect;WrapPanel monitorCards;Action resolutionRefresh;string lastDisplaySignature;
        readonly Dictionary<DisplayInfo,TextBlock> monitorModeLabels=new Dictionary<DisplayInfo,TextBlock>();
        void TogglePair()
        {
            if(selected==null)return;
            var m=DisplayService.GetCurrentMode(selected.DeviceName);
            if(m==null)return;
            bool first=m.Width==preferences.PairAWidth&&m.Height==preferences.PairAHeight;
            ApplyResolution(first?preferences.PairBWidth:preferences.PairAWidth,first?preferences.PairBHeight:preferences.PairAHeight,!first);
        }
        FrameworkElement BuildResolutionPair(List<DisplayMode> modes,DisplayMode currentMode,Action refresh)
        {
            var pair=Section("Quick switch","");
            var available=modes.Where(m=>m.Frequency==currentMode.Frequency&&m.BitsPerPixel==currentMode.BitsPerPixel)
                .GroupBy(m=>new{m.Width,m.Height}).Select(g=>g.First()).OrderByDescending(m=>m.Width*m.Height).ToList();
            var a=new ComboBox {ItemsSource=available,DisplayMemberPath="ResolutionText",SelectedItem=available.FirstOrDefault(m=>m.Width==preferences.PairAWidth&&m.Height==preferences.PairAHeight),MinHeight=40,FontSize=15,FontWeight=FontWeights.SemiBold};
            var b=new ComboBox {ItemsSource=available,DisplayMemberPath="ResolutionText",SelectedItem=available.FirstOrDefault(m=>m.Width==preferences.PairBWidth&&m.Height==preferences.PairBHeight),MinHeight=40,FontSize=15,FontWeight=FontWeights.SemiBold};
            a.DisplayMemberPath=b.DisplayMemberPath="";a.ItemTemplate=b.ItemTemplate=Design.TextTemplate("ResolutionText");
            // The two sizes side by side, their names above them and a swap mark between the two boxes, level with them.
            var sides=new Grid {Margin=new Thickness(0,8,0,14)};
            sides.ColumnDefinitions.Add(new ColumnDefinition());sides.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});sides.ColumnDefinitions.Add(new ColumnDefinition());
            sides.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});sides.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            // The size in use is marked with a small white check beside its name.
            Func<FrameworkElement> inUse=()=>
            {
                var mark=new Grid {Width=16,Height=16,VerticalAlignment=VerticalAlignment.Center,ToolTip="In use"};
                mark.Children.Add(new System.Windows.Shapes.Ellipse {Fill=new SolidColorBrush(Color.FromRgb(245,245,247))});
                mark.Children.Add(new System.Windows.Shapes.Path {Data=Geometry.Parse("M4.6,8.3 L7,10.6 L11.4,5.6"),Stroke=Brushes.Black,StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round});
                return mark;
            };
            Func<string,FrameworkElement,int,DockPanel> head=(name,state,column)=>
            {
                var line=new DockPanel {Margin=new Thickness(2,0,2,7)};
                DockPanel.SetDock(state,Dock.Right);line.Children.Add(state);
                var label=Text(name,11,true);label.Foreground=Muted;label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new Thickness(0);line.Children.Add(label);
                Grid.SetColumn(line,column);sides.Children.Add(line);
                return line;
            };
            var aState=inUse();var bState=inUse();
            head("FIRST",aState,0);head("SECOND",bState,2);
            Grid.SetRow(a,1);sides.Children.Add(a);
            Grid.SetRow(b,1);Grid.SetColumn(b,2);sides.Children.Add(b);
            var swap=Design.Icon("Swap",20);swap.Margin=new Thickness(12,0,12,0);swap.VerticalAlignment=VerticalAlignment.Center;swap.Opacity=.7;
            Grid.SetRow(swap,1);Grid.SetColumn(swap,1);sides.Children.Add(swap);
            pair.Children.Add(sides);
            var hint=Text("",13);hint.Foreground=Muted;
            // The switch button on the left and the shortcut on the right, on one line, centered on each other.
            var actions=new DockPanel();pair.Children.Add(actions);
            var toggle=Action("Switch resolution",()=>
            {
                TogglePair();
                refresh();
            });
            toggle.Style=(Style)Resources["PrimaryAction"];toggle.VerticalAlignment=VerticalAlignment.Center;toggle.Margin=new Thickness(0,0,16,0);
            DockPanel.SetDock(toggle,Dock.Left);actions.Children.Add(toggle);
            var shortcut=new StackPanel {VerticalAlignment=VerticalAlignment.Center};actions.Children.Add(shortcut);
            // The shortcut is set here, in a button, and stays the same one as in Settings.
            // This card is rebuilt when the displays change; the old button must not stay in the list of fields that follow the shortcut.
            List<Button> switchFields;
            if(shortcutFields.TryGetValue("Switch resolution",out switchFields)&&displaySwitchField!=null)switchFields.Remove(displaySwitchField);
            BindRow(shortcut,"Switch resolution",()=>preferences.SwitchKey,v=>preferences.SwitchKey=v,TogglePair,"Shortcut");
            if(shortcut.Children.Count>0)
            {
                var line=(DockPanel)shortcut.Children[0];line.Margin=new Thickness(0);
                // The word sits on the button's middle line (its letters hang a little high on their own).
                foreach(var child in line.Children){var word=child as TextBlock;if(word!=null)word.Margin=new Thickness(0,3,0,0);}
                foreach(var child in line.Children){var key=child as Button;if(key!=null)key.Margin=new Thickness(0);}
            }
            displaySwitchField=shortcutFields["Switch resolution"][shortcutFields["Switch resolution"].Count-1];
            pair.Children.Add(hint);
            Action update=()=>
            {
                var first=a.SelectedItem as DisplayMode;var second=b.SelectedItem as DisplayMode;
                bool valid=first!=null&&second!=null&&(first.Width!=second.Width||first.Height!=second.Height);
                toggle.IsEnabled=valid;
                hint.Text=valid?"":"Pick two different sizes.";
                hint.Visibility=valid?Visibility.Collapsed:Visibility.Visible;
                bool onFirst=first!=null&&currentMode.Width==first.Width&&currentMode.Height==first.Height;
                bool onSecond=!onFirst&&second!=null&&currentMode.Width==second.Width&&currentMode.Height==second.Height;
                aState.Visibility=onFirst?Visibility.Visible:Visibility.Hidden;bState.Visibility=onSecond?Visibility.Visible:Visibility.Hidden;
                if(valid)toggle.Content="Switch to "+SizeLabel(onFirst?second:first);
            };
            a.SelectionChanged+=(s,e)=>
            {
                var m=a.SelectedItem as DisplayMode;
                if(m!=null)
                {
                    preferences.PairAWidth=m.Width;
                    preferences.PairAHeight=m.Height;
                    SaveSoon();
                }
                update();
            };
            b.SelectionChanged+=(s,e)=>
            {
                var m=b.SelectedItem as DisplayMode;
                if(m!=null)
                {
                    preferences.PairBWidth=m.Width;
                    preferences.PairBHeight=m.Height;
                    SaveSoon();
                }
                update();
            };
            update();return pair;
        }
        static string SizeLabel(DisplayMode mode){return mode.Width+" × "+mode.Height;}

        FrameworkElement BuildScaling(string device)
        {
            var card=Section("Scaling","How a lower resolution fills the selected display.");
            var options=new UniformGrid {Columns=2,Margin=new Thickness(0,8,0,8)};card.Children.Add(options);
            var note=Text("Reading display scaling…",12);note.Foreground=Muted;card.Children.Add(note);
            int[] values={5,3,2,8};string[] names={"Aspect ratio","No scaling","Full screen","Integer"};
            var buttons=new List<Button>();
            Action<int> highlight=value=>
            {
                for(int i=0;i<buttons.Count;i++)
                {
                    bool active=value==values[i]||(value==6&&values[i]==5)||(value==7&&values[i]==3)||(value==1&&values[i]==2);
                    buttons[i].Content=(active?"✓  ":"")+names[i];
                    buttons[i].Background=active?selectionSurface:optionSurface;
                }
            };
            for(int i=0;i<values.Length;i++)
            {
                int value=values[i];string label=names[i];
                var button=Action(label,()=>{});
                button.IsEnabled=false;
                button.HorizontalAlignment=HorizontalAlignment.Stretch;
                buttons.Add(button);
                options.Children.Add(button);
                button.Click+=async(s,e)=>
                {
                    if(TestMode)return;
                    options.IsEnabled=false;note.Text="Applying "+label.ToLowerInvariant()+"…";
                    try
                    {
                        int actual=await Task.Run(()=>
                        {
                            using(var gate=new Mutex(false,LegacyNames.CustomTrialMutex))
                            {
                                bool owned=false;
                                try
                                {
                                    try{owned=gate.WaitOne(0);}catch(AbandonedMutexException){owned=true;}
                                    if(!owned)throw new InvalidOperationException("Finish the active display test first.");
                                    using(var config=new NvidiaScalingConfig(device))config.ApplyScaling(value);
                                    using(var config=new NvidiaScalingConfig(device))return config.CurrentScaling;
                                }
                                finally{if(owned)gate.ReleaseMutex();}
                            }
                        });
                        highlight(actual);
                        note.Text=value==8&&actual!=8?"Integer requested; the driver reports a different mode. Check NVIDIA Control Panel.":label+" applied to this display.";
                    }
                    catch(Exception error){note.Text="Scaling unchanged or not confirmed: "+error.Message;}
                    finally{options.IsEnabled=true;}
                };
            }
            bool queried=false;
            card.Loaded+=async(s,e)=>
            {
                if(queried)return;queried=true;
                if(TestMode){note.Text="Scaling is not applied in test mode.";queried=false;return;}
                try
                {
                    int currentScale=0;bool[] supported=await Task.Run(()=>
                    {
                        using(var config=new NvidiaScalingConfig(device))
                        {
                            currentScale=config.CurrentScaling;
                            return values.Select(v=>!config.SharesSource).ToArray();
                        }
                    });
                    for(int i=0;i<buttons.Count;i++)
                    {
                        buttons[i].IsEnabled=supported[i];
                        buttons[i].ToolTip=supported[i]?names[i]+" on the selected display":"Not supported by this display configuration";
                    }
                    highlight(currentScale);
                    note.Text=supported.Any(v=>v)?"NVIDIA GPU scaling · unsupported modes are rejected when applied.":"Independent scaling is unavailable for cloned displays.";
                }
                catch(Exception error){note.Text="Scaling unavailable for this display.";note.ToolTip=error.Message;}
                finally{queried=false;}
            };
            return card;
        }
    }
}
