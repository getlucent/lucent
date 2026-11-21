using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lucent;
using ResolutionSwitcher;
// The focused UI tests: only the part of the interface being worked on right now, so a run takes seconds instead of minutes.
// Now: the player bar growing out of the player buttons' glass, and the glass of both. (The full suite, test-ui.cmd, still covers everything before a release.)
// Besides the checks, it records the bar at every frame while it opens and closes (its shape, and how see-through it and its content are)
// into release\test-data\focus\motion.txt, so the motion can be read frame by frame.
class FocusUiTests
{
 static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static object Field(object value,string name){var f=value.GetType().GetField(name,Private);if(f==null)throw new Exception("No field "+name);return f.GetValue(value);}
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
 static async Task Until(Func<bool> done,int ms=3000){var watch=System.Diagnostics.Stopwatch.StartNew();while(!done()&&watch.ElapsedMilliseconds<ms)await Task.Delay(15);}
 [STAThread]static int Main(string[] args)
 {
  var app=new Application();int exit=0;
  app.Dispatcher.BeginInvoke(new Action(async()=>{try{if(args.Length>0&&args[0]=="sunset-cpu")await SunsetCpu();else if(args.Length>0&&args[0]=="shell-cpu")await ShellCpu();else if(args.Length>0&&args[0]=="sunset-shell-cpu")await SunsetShellCpu();else await Run();}catch(Exception error){Console.WriteLine("FAIL: "+error.Message);Console.WriteLine(error);exit=1;}finally{app.Shutdown();}}));app.Run();return exit;
 }
 static async Task Run()
 {
  StateStore.Folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data","focus-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(StateStore.Folder);
  string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data","focus");Directory.CreateDirectory(output);
  var shell=new Shell(true){WindowStartupLocation=WindowStartupLocation.Manual,Left=-20000,Top=-20000,Width=1180,Height=760};shell.Show();
  await Task.Delay(900);
  var bar=(PlayerBar)Field(shell,"playerBar");var arrow=(Button)Field(shell,"playerArrow");var dock=(Border)Field(shell,"playerDock");
  var pill=(SolidColorBrush)Field(shell,"dockPillBrush");var dockGlass=(LiquidGlass)Field(shell,"dockGlass");var barGlass=(LiquidGlass)Field(shell,"barGlass");
  var content=(UIElement)Field(bar,"barContent");
  Check(PlayerBar.ScreenRate>=60,"The bar's animations know the monitor's refresh rate ("+PlayerBar.ScreenRate+" Hz)");
  Check(ReferenceEquals(dockGlass.Tint,bar.Surface)&&ReferenceEquals(barGlass.Tint,bar.Surface),"The buttons' glass and the bar's glass both wear the bar's color");
  Check(Field(bar,"face")!=null&&((UIElement)Field(bar,"barGlassLayers")).Visibility==Visibility.Collapsed,"The bar wears the buttons' glass instead of its old layers");
  shell.SetMediaAvailable(true);await Task.Delay(400);
  bar.Core.SetTrack(new MediaTrack{Title="Focus track",Artist="Focus artist",Source="Fixture",Playing=true,CanPause=true,CanPlay=true,CanNext=true,CanPrevious=true,CanSeek=true,Duration=200,Position=60});
  Check(dock.Visibility==Visibility.Visible&&pill.Color.A==22&&dockGlass.Opacity>.99,"With media, the buttons show on their glass");
  Func<Point> dockAt=()=>dock.TransformToAncestor(shell).Transform(new Point());Point before=dockAt();
  var dockInBar=((Func<Rect?>)Field(bar,"GrowFrom"))();
  var log=new StringBuilder();
  // Opening.
  arrow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  var opening=await Record(shell,bar,content,dockGlass,log,"open",900);
  File.WriteAllText(Path.Combine(output,"motion.txt"),log.ToString());
  Console.WriteLine(string.Format("After opening: visible {0}, opacity {1:0.00}, clip {2}, content {3:0.00}, pill in bar {4}",bar.Visibility,bar.Opacity,bar.Clip==null?"none":((RectangleGeometry)bar.Clip).Rect.ToString(),content.Opacity,dockInBar));
  Check(bar.Visibility==Visibility.Visible&&bar.Opacity>.99&&bar.Clip==null&&content.Opacity>.99,"The bar ends fully open, unclipped, with its content shown");
  Check(opening.Count>10&&opening.First().Clip!=null,"The bar starts as a clipped shape ("+opening.Count+" frames recorded)");
  if(dockInBar!=null){var first=opening.First(f=>f.Clip!=null).Clip.Value;Check(Math.Abs(first.Right-dockInBar.Value.Right)<20&&first.Width<dockInBar.Value.Width*2.5,"The bar starts from the buttons' pill: "+first+" vs pill "+dockInBar.Value);}
  // Smoothness: the shape's width should change a little each frame, never jump.
  var widths=opening.Where(f=>f.Clip!=null).Select(f=>f.Clip.Value.Width).ToList();
  double biggest=0;for(int i=1;i<widths.Count;i++)biggest=Math.Max(biggest,Math.Abs(widths[i]-widths[i-1]));
  Check(widths.Count>8&&biggest<bar.ActualWidth*.35,"The bar widens in small steps (largest step "+Math.Round(biggest)+" px of "+Math.Round(bar.ActualWidth)+")");
  Check((dockAt()-before).Length<.5,"The buttons stay put while the bar opens");
  var early=opening.Where(f=>f.Ms<70).ToList();
  Check(early.Count>3&&early.All(f=>f.ContentOpacity<.05),"The bar's content stays hidden while the glass starts to grow (no flash): "+string.Join(",",early.Take(6).Select(f=>f.ContentOpacity.ToString("0.00")).ToArray()));
  double lastOpacity=0;bool dipped=false;foreach(var f in opening){if(f.ContentOpacity<lastOpacity-.05)dipped=true;lastOpacity=f.ContentOpacity;}
  Check(!dipped,"The bar's content only fades in while opening, never back out");
  Check(opening.Where(f=>f.Ms>160).All(f=>f.DockOpacity<.01),"The pill's own glass and border are gone a moment into opening (no box inside the bar)");
  double rise=0;for(int i=1;i<opening.Count;i++)rise=Math.Max(rise,opening[i].DockOpacity-opening[i-1].DockOpacity);
  Check(rise<.01,"The pill only fades away while the bar opens, never back");
  await Until(()=>pill.Color.A==0&&dockGlass.Opacity<.01);
  Check(pill.Color.A==0&&dockGlass.Opacity<.01,"The buttons' own glass fades once the bar is up");
  // Closing.
  arrow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  var closing=await Record(shell,bar,content,dockGlass,log,"close",700);
  var dipFrames=closing.Where(f=>f.Clip!=null&&f.BarOpacity<.99).ToList();
  Check(dipFrames.Count==0,"The bar stays fully solid while the pill fades in over it (no see-through moment): "+dipFrames.Count+" frames dipped");
  double pillWidth=closing.Where(f=>f.Clip!=null).Select(f=>f.Clip.Value.Width).DefaultIfEmpty(150).Min();
  Check(closing.Where(f=>f.Clip!=null&&f.Clip.Value.Width>pillWidth+2).All(f=>f.DockOpacity<.01),"The pill's glass and border stay hidden while the bar is still wider than the pill: "+string.Join("; ",closing.Where(f=>f.Clip!=null&&f.Clip.Value.Width>pillWidth+2&&f.DockOpacity>=.01).Take(4).Select(f=>f.Ms.ToString("0")+"ms w"+f.Clip.Value.Width.ToString("0")+" pill "+f.DockOpacity.ToString("0.00")).ToArray())+" (pill "+pillWidth+")");
  double jump=0;for(int i=1;i<closing.Count;i++)jump=Math.Max(jump,closing[i].DockOpacity-closing[i-1].DockOpacity);
  Check(jump<.35,"The pill's border fades in at the end of closing instead of appearing at once (largest step "+jump.ToString("0.00")+")");
  await Until(()=>bar.Visibility==Visibility.Collapsed&&dockGlass.Opacity>.99);
  Check(bar.Visibility==Visibility.Collapsed&&dockGlass.Opacity>.99&&pill.Color.A==22,"Closing draws the bar back into the buttons' glass");
  Check(content.Opacity>.99&&bar.Clip==null,"After closing, the bar is reset for next time");
  // Switching players: bar open, then the big player (the bar steps aside), then the mini player (the big player closes and the bar must stay away).
  arrow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>bar.Visibility==Visibility.Visible&&bar.Clip==null);
  var big=(Button)Field(shell,"bigPlayerButton");var mini=(Button)Field(shell,"playerDockButton");
  big.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>bar.Visibility==Visibility.Collapsed,2500);
  Check(bar.Visibility==Visibility.Collapsed,"Opening the big player puts the bar away");
  mini.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  var trace=new StringBuilder();var watch=System.Diagnostics.Stopwatch.StartNew();bool popped=false;
  while(watch.ElapsedMilliseconds<1500){if(bar.Visibility==Visibility.Visible)popped=true;var g=bar.Clip as RectangleGeometry;trace.Append(string.Format("{0:0}ms {1} op {2:0.00} clip {3}; ",watch.ElapsedMilliseconds,bar.Visibility,bar.Opacity,g==null?"none":g.Rect.Width.ToString("0")));await Task.Delay(100);}
  Check(!popped&&bar.Visibility==Visibility.Collapsed&&Field(shell,"mediaPlayer")!=null,"Switching from the big player to the mini player keeps the bar away the whole time (it must not pop up in between): "+trace);
  mini.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(400);
  File.WriteAllText(Path.Combine(output,"motion.txt"),log.ToString());
  Console.WriteLine("Frames and motion log: "+output);
  shell.Close();
 }
 // How much CPU the Vice Sunset background costs on its own: a window of just the sunset, measured moving and then hidden.
 static async Task SunsetCpu()
 {
  var sunset=new SunsetBackdrop();
  var window=new Window {Width=1180,Height=760,Content=sunset,WindowStartupLocation=WindowStartupLocation.CenterScreen,ShowActivated=false,Title="sunset cpu"};
  window.Show();await Task.Delay(1500);
  var me=System.Diagnostics.Process.GetCurrentProcess();
  Func<Task<double>> measure=async()=>{me.Refresh();double a=me.TotalProcessorTime.TotalMilliseconds;await Task.Delay(4000);me.Refresh();return (me.TotalProcessorTime.TotalMilliseconds-a)/40;};
  double moving=await measure();
  sunset.Visibility=Visibility.Collapsed;await Task.Delay(500);
  double still=await measure();
  Console.WriteLine(string.Format("Sunset moving: {0:0.0}% of a core, hidden: {1:0.0}%",moving,still));
  window.Close();
 }
 // The whole window's CPU when idle, then with parts of it switched off one at a time, to see what costs what.
 static async Task ShellCpu()
 {
  StateStore.Folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data","focus-cpu-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(StateStore.Folder);
  var shell=new Shell(true){WindowStartupLocation=WindowStartupLocation.CenterScreen,Width=1180,Height=760,ShowActivated=false};shell.Show();
  await Task.Delay(2500);
  var me=System.Diagnostics.Process.GetCurrentProcess();
  Func<string,Task> measure=async name=>{await Task.Delay(600);me.Refresh();double a=me.TotalProcessorTime.TotalMilliseconds;await Task.Delay(4000);me.Refresh();Console.WriteLine(string.Format("{0,-34} {1,5:0.0}% of a core",name,(me.TotalProcessorTime.TotalMilliseconds-a)/40));};
  await measure("idle, Home page");
  shell.SetMediaAvailable(true);await Task.Delay(400);
  await measure("with the player buttons");
  ((Button)Field(shell,"playerArrow")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(1000);
  await measure("with the player bar open");
  foreach(var name in new[]{"tabGlass","buttonsGlass","dockGlass","barGlass"}){var g=Field(shell,name) as UIElement;if(g!=null)g.Visibility=Visibility.Collapsed;}
  await measure("all glass hidden");
  ((UIElement)Field(shell,"flowHost")).Visibility=Visibility.Collapsed;((UIElement)Field(shell,"ambientBackdrop")).Visibility=Visibility.Collapsed;
  await measure("backgrounds hidden too");
  ((UIElement)Field(shell,"pageHost")).Visibility=Visibility.Collapsed;
  await measure("pages hidden too");
  shell.Close();
 }
 // The whole window with the Vice Sunset moving behind it, then parts switched off or cached one at a time.
 static async Task SunsetShellCpu()
 {
  StateStore.Folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data","focus-cpu-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(StateStore.Folder);
  var shell=new Shell(true){WindowStartupLocation=WindowStartupLocation.CenterScreen,Width=1180,Height=760,ShowActivated=false};shell.Show();
  await Task.Delay(2500);
  var me=System.Diagnostics.Process.GetCurrentProcess();
  Func<string,Task> measure=async name=>{double best=999;for(int r=0;r<2;r++){await Task.Delay(800);me.Refresh();double a=me.TotalProcessorTime.TotalMilliseconds;await Task.Delay(5000);me.Refresh();best=Math.Min(best,(me.TotalProcessorTime.TotalMilliseconds-a)/50);}Console.WriteLine(string.Format("{0,-40} {1,5:0.0}% of a core",name,best));};
  var flowHost=(UIElement)Field(shell,"flowHost");var sunset=(SunsetBackdrop)Field(shell,"sunsetBackdrop");var flow=(UIElement)Field(shell,"flowBackdrop");
  shell.SetMediaAvailable(true);await Task.Delay(300);
  ((Button)Field(shell,"playerArrow")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(1000);
  await measure("no sunset (bar open)");
  flowHost.Visibility=Visibility.Visible;flow.Visibility=Visibility.Collapsed;sunset.Visibility=Visibility.Visible;
  typeof(Shell).GetMethod("RefreshGlassSources",Private).Invoke(shell,new object[]{true});
  await measure("sunset moving");
  foreach(var name in new[]{"tabGlass","buttonsGlass","dockGlass","barGlass"}){var g=Field(shell,name) as UIElement;if(g!=null)g.Visibility=Visibility.Collapsed;}
  await measure("sunset, all glass hidden");
  foreach(var name in new[]{"tabGlass","buttonsGlass","dockGlass","barGlass"}){var g=Field(shell,name) as UIElement;if(g!=null)g.Visibility=Visibility.Visible;}
  var page=(UIElement)Field(shell,"pageHost");page.Visibility=Visibility.Collapsed;
  await measure("sunset, glass, pages hidden");
  page.Visibility=Visibility.Visible;
  ((UIElement)Field(shell,"ambientBackdrop")).Visibility=Visibility.Collapsed;
  await measure("sunset, still colors hidden");
  shell.Close();
 }
 sealed class Frame {public double Ms;public Rect? Clip;public double BarOpacity,ContentOpacity,DockOpacity;}
 // Notes the bar's state every few milliseconds and keeps a picture of the bottom of the window at each step, as one tall sheet.
 // Notes the bar's state at every frame WPF draws (no pictures here: drawing a picture of the window is slow and would itself stall the motion).
 static async Task<System.Collections.Generic.List<Frame>> Record(Shell shell,PlayerBar bar,UIElement content,UIElement dockGlass,StringBuilder log,string name,int ms)
 {
  var frames=new System.Collections.Generic.List<Frame>();
  var watch=System.Diagnostics.Stopwatch.StartNew();
  EventHandler tick=(s,e)=>
  {
   var shape=bar.Clip as RectangleGeometry;
   var f=new Frame{Ms=watch.Elapsed.TotalMilliseconds,Clip=shape==null?(Rect?)null:shape.Rect,BarOpacity=bar.Opacity,ContentOpacity=content.Opacity,DockOpacity=dockGlass.Opacity};
   frames.Add(f);
   log.AppendLine(string.Format("{0} {1,6:0.0} ms  clip {2,-28} bar {3:0.00}  content {4:0.00}  pill glass {5:0.00}",name,f.Ms,f.Clip.HasValue?string.Format("{0:0},{1:0} {2:0}x{3:0}",f.Clip.Value.X,f.Clip.Value.Y,f.Clip.Value.Width,f.Clip.Value.Height):"none",f.BarOpacity,f.ContentOpacity,f.DockOpacity));
  };
  CompositionTarget.Rendering+=tick;
  await Task.Delay(ms);
  CompositionTarget.Rendering-=tick;
  double span=frames.Count>1?frames.Last().Ms-frames.First().Ms:0,gap=0;
  for(int i=1;i<frames.Count;i++)gap=Math.Max(gap,frames[i].Ms-frames[i-1].Ms);
  log.AppendLine(string.Format("{0}: {1} frames in {2:0} ms ({3:0} per second), longest gap {4:0.0} ms",name,frames.Count,span,span>0?frames.Count*1000/span:0,gap));
  Console.WriteLine(string.Format("{0}: {1} frames, {2:0} per second, longest gap {3:0.0} ms",name,frames.Count,span>0?frames.Count*1000/span:0,gap));
  return frames;
 }
 // Pictures of the bottom of the window while the bar moves, as one tall sheet (slow to make, so taken on a separate open and close).
 static async Task Pictures(Shell shell,Button arrow,string name,int count,int every)
 {
  var root=(FrameworkElement)shell.Content;int w=(int)root.ActualWidth,h=(int)root.ActualHeight,top=Math.Max(0,h-130);
  var strips=new System.Collections.Generic.List<BitmapSource>();
  arrow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  var watch=System.Diagnostics.Stopwatch.StartNew();
  for(int i=0;i<count;i++)
  {
   while(watch.ElapsedMilliseconds<i*every)await Task.Delay(5);
   var full=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);full.Render(root);
   strips.Add(new CroppedBitmap(full,new Int32Rect(0,top,w,h-top)));
  }
  int sh=strips[0].PixelHeight;var sheet=new DrawingVisual();
  using(var dc=sheet.RenderOpen()){for(int i=0;i<strips.Count;i++)dc.DrawImage(strips[i],new Rect(0,i*sh,w,sh));}
  var target=new RenderTargetBitmap(w,sh*strips.Count,96,96,PixelFormats.Pbgra32);target.Render(sheet);
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(target));
  using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data","focus",name+"-frames.png")))encoder.Save(file);
  await Task.Delay(900);
 }
}
