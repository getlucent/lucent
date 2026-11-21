using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Lucent
{
    // Dark liquid glass for small floating surfaces (the top bar): what is behind it is seen blurred through it, darkened; light catches its rim (brightest
    // along the top and a little along the bottom, like the edge of a thick pane), a soft sheen falls from the top left, and a soft shadow lifts it off the
    // window. The blur is a copy of only the strip behind the surface, so it stays cheap: blurring the whole window is what made settings lag.
    internal sealed class LiquidGlass:Grid
    {
        const double Bleed=28;
        readonly double radius;
        readonly Grid blur=new Grid {Margin=new Thickness(-Bleed),IsHitTestVisible=false,Effect=new BlurEffect {Radius=22,RenderingBias=RenderingBias.Performance,KernelType=KernelType.Gaussian}};
        readonly Grid glass=new Grid {IsHitTestVisible=false};
        readonly Border holder=new Border();
        Visual[] sources;VisualBrush[] brushes;
        internal LiquidGlass(double cornerRadius)
        {
            radius=cornerRadius;
            // A soft shadow under the glass, drawn once and kept as a picture. The shape casting it sits well inside the glass, so no dark edge of it shows below.
            var shadow=new Border {CornerRadius=new CornerRadius(radius),Background=new SolidColorBrush(Color.FromArgb(60,0,0,0)),IsHitTestVisible=false,Margin=new Thickness(4,4,4,3),
                Effect=new DropShadowEffect {BlurRadius=20,ShadowDepth=3,Direction=270,Opacity=.35,Color=Colors.Black,RenderingBias=RenderingBias.Performance},CacheMode=new BitmapCache()};
            Children.Add(shadow);
            // The blurred copy is kept as a picture on the graphics card, so it is made again only when what is behind the glass changes, not at every frame
            // something else in the window moves (copying and blurring it each frame was most of the cost of a moving background).
            glass.Children.Add(blur);
            // Light, see-through dark glass: a little more of the dark laid on toward the bottom.
            var tint=new LinearGradientBrush(new GradientStopCollection{new GradientStop(Color.FromArgb(58,22,22,28),0),new GradientStop(Color.FromArgb(84,10,10,14),1)},new Point(0,0),new Point(0,1));tint.Freeze();
            tintLayer=new Border {Background=tint,IsHitTestVisible=false};
            glass.Children.Add(tintLayer);
            // The sheen: light falling on the glass from the top left, fading out by the middle.
            // (It falls straight down: run corner to corner across a wide bar, its few faint levels of white spread into broad steps that showed as slanted lines.)
            var sheen=new LinearGradientBrush(new GradientStopCollection{new GradientStop(Color.FromArgb(34,255,255,255),0),new GradientStop(Color.FromArgb(8,255,255,255),.45),new GradientStop(Color.FromArgb(0,255,255,255),.7)},new Point(0,0),new Point(0,1));sheen.Freeze();
            glass.Children.Add(new Border {Background=sheen,IsHitTestVisible=false});
            // A thin bright line just inside the top edge (the light the curved rim catches), and a fainter one along the bottom, as thick glass bends light at both edges.
            var top=new LinearGradientBrush(new GradientStopCollection{new GradientStop(Color.FromArgb(0,255,255,255),0),new GradientStop(Color.FromArgb(70,255,255,255),.25),new GradientStop(Color.FromArgb(70,255,255,255),.75),new GradientStop(Color.FromArgb(0,255,255,255),1)},new Point(0,0),new Point(1,0));top.Freeze();
            glass.Children.Add(new Border {Height=1,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(radius*.6,1.5,radius*.6,0),Background=top,IsHitTestVisible=false});
            var bottom=new LinearGradientBrush(new GradientStopCollection{new GradientStop(Color.FromArgb(0,255,255,255),0),new GradientStop(Color.FromArgb(22,255,255,255),.5),new GradientStop(Color.FromArgb(0,255,255,255),1)},new Point(0,0),new Point(1,0));bottom.Freeze();
            glass.Children.Add(new Border {Height=1,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(radius,0,radius,1.5),Background=bottom,IsHitTestVisible=false});
            Children.Add(glass);
            Children.Add(Design.GlassEdge(radius));
            Children.Add(holder);
            SizeChanged+=(s,e)=>{if(ActualWidth>0&&ActualHeight>0)glass.OpacityMask=PlayerBar.RoundedMask(ActualWidth,ActualHeight,radius);Fit();};
            LayoutUpdated+=(s,e)=>Fit();
        }
        internal UIElement Content {get{return holder.Child;}set{holder.Child=value;}}
        // The color laid over what is seen through the glass (dark by default); the player buttons use the player bar's own color so the two match.
        readonly Border tintLayer;
        internal Brush Tint {get{return tintLayer.Background;}set{tintLayer.Background=value;}}
        // What is seen through the glass: each source is copied for the strip behind the glass and blurred (the window's colors, then the moving colors on top).
        internal void SetSources(params Visual[] behind)
        {
            sources=behind;brushes=new VisualBrush[behind.Length];
            blur.Children.Clear();
            for(int i=0;i<behind.Length;i++)
            {
                brushes[i]=new VisualBrush(behind[i]) {ViewboxUnits=BrushMappingMode.Absolute,Stretch=Stretch.Fill};
                blur.Children.Add(new System.Windows.Shapes.Rectangle {Fill=brushes[i],IsHitTestVisible=false});
            }
        }
        internal bool BlurOn {set{blur.Visibility=value?Visibility.Visible:Visibility.Collapsed;}}
        void Fit()
        {
            if(sources==null||ActualWidth<=0||ActualHeight<=0)return;
            for(int i=0;i<sources.Length;i++)
            {
                try
                {
                    var area=TransformToVisual(sources[i]).TransformBounds(new Rect(-Bleed,-Bleed,ActualWidth+2*Bleed,ActualHeight+2*Bleed));
                    if(area.Width>0&&area.Height>0&&brushes[i].Viewbox!=area)brushes[i].Viewbox=area;
                }
                catch(InvalidOperationException){}
            }
        }
    }
}
