using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using TextBlock=Lucent.SmoothText;

namespace Lucent
{
    // Small drawings for the places that are empty: a record that turns slowly (nothing playing), a controller asleep (no game running), a controller
    // being searched for (no games found), a clock (no time played yet). They are white lines like the app's icons, and move gently, only while they
    // are on screen and at a low rate, so they cost next to nothing.
    internal static class EmptyArt
    {
        const string Pad="M7.2,7 L16.8,7 Q20.6,7 21.6,11.6 L22.4,16 Q22.8,19.4 19.9,19.4 Q18,19.4 17,17.6 L16.2,16.2 L7.8,16.2 L7,17.6 Q6,19.4 4.1,19.4 Q1.2,19.4 1.6,16 L2.4,11.6 Q3.4,7 7.2,7 Z M7.6,9.8 L7.6,13.4 M5.8,11.6 L9.4,11.6 M16.3,10.4 L16.3,10.5 M18.6,12.7 L18.6,12.8";
        static Brush Ink(byte alpha){var brush=new SolidColorBrush(Color.FromArgb(alpha,255,255,255));brush.Freeze();return brush;}
        // Runs the motions only while the drawing is on screen.
        static void WhileShown(FrameworkElement art,Action start,Action stop)
        {
            art.IsVisibleChanged+=(s,e)=>{if((bool)e.NewValue&&!Design.Instant)start();else stop();};
        }
        static DoubleAnimation Loop(double from,double to,double seconds,bool reverse=false,double delay=0)
        {
            var animation=new DoubleAnimation(from,to,TimeSpan.FromSeconds(seconds)){RepeatBehavior=RepeatBehavior.Forever,AutoReverse=reverse,BeginTime=TimeSpan.FromSeconds(delay)};
            if(reverse)animation.EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut};
            Timeline.SetDesiredFrameRate(animation,30);
            return animation;
        }
        static Path Lines(string data,double size,byte alpha=230,double thickness=1.65)
        {
            return new Path {Data=Geometry.Parse(data),Width=size,Height=size,Stretch=Stretch.Uniform,Stroke=Ink(alpha),StrokeThickness=thickness,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};
        }
        // A vinyl record: grooves, a label, and a spot of light that turns with it.
        internal static FrameworkElement Record(double size)
        {
            var art=new Grid {Width=size,Height=size};
            var disc=new Grid {Width=size*.86,Height=size*.86,RenderTransformOrigin=new Point(.5,.5)};
            var turn=new RotateTransform();disc.RenderTransform=turn;
            disc.Children.Add(new Ellipse {Fill=new RadialGradientBrush(Color.FromRgb(34,34,38),Color.FromRgb(14,14,16)),Stroke=Ink(60),StrokeThickness=1});
            foreach(double ring in new[]{.82,.68,.56})disc.Children.Add(new Ellipse {Width=size*.86*ring,Height=size*.86*ring,Stroke=Ink(22),StrokeThickness=1});
            disc.Children.Add(new Ellipse {Width=size*.3,Height=size*.3,Fill=Ink(200)});
            disc.Children.Add(new Ellipse {Width=size*.05,Height=size*.05,Fill=new SolidColorBrush(Color.FromRgb(20,20,22))});
            // The light on the grooves, off to one side, so the turning shows.
            var shine=new Path {Data=new PathGeometry(new[]{new PathFigure(new Point(size*.43,size*.43),new PathSegment[]{new LineSegment(new Point(size*.43,size*.04),false),new ArcSegment(new Point(size*.73,size*.13),new Size(size*.39,size*.39),0,false,SweepDirection.Clockwise,false)},true)}),
                Fill=new LinearGradientBrush(Color.FromArgb(40,255,255,255),Color.FromArgb(0,255,255,255),new Point(0,0),new Point(1,1))};
            disc.Children.Add(shine);
            art.Children.Add(disc);
            WhileShown(art,()=>turn.BeginAnimation(RotateTransform.AngleProperty,Loop(0,360,9)),()=>turn.BeginAnimation(RotateTransform.AngleProperty,null));
            return art;
        }
        // A controller asleep: it breathes, and two z's drift up from it.
        internal static FrameworkElement Asleep(double size)
        {
            var art=new Grid {Width=size,Height=size};
            var pad=Lines(Pad,size*.72,200);
            var breathe=new ScaleTransform(1,1);pad.RenderTransform=breathe;pad.RenderTransformOrigin=new Point(.5,.9);
            pad.VerticalAlignment=VerticalAlignment.Bottom;pad.HorizontalAlignment=HorizontalAlignment.Left;pad.Margin=new Thickness(0,0,0,size*.04);
            art.Children.Add(pad);
            var letters=new TextBlock[2];var lifts=new TranslateTransform[2];
            for(int i=0;i<2;i++)
            {
                lifts[i]=new TranslateTransform();
                letters[i]=new TextBlock {Text="z",FontSize=size*(i==0?.2:.15),FontWeight=FontWeights.Bold,Foreground=Ink(220),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,size*(i==0?.22:.06),size*(i==0?.06:.18),0),RenderTransform=lifts[i],Opacity=0};
                art.Children.Add(letters[i]);
            }
            WhileShown(art,()=>
            {
                breathe.BeginAnimation(ScaleTransform.ScaleYProperty,Loop(1,.94,1.8,true));
                for(int i=0;i<2;i++)
                {
                    lifts[i].BeginAnimation(TranslateTransform.YProperty,Loop(0,-size*.18,2.4,false,i*1.2));
                    var fade=new DoubleAnimationUsingKeyFrames {RepeatBehavior=RepeatBehavior.Forever,BeginTime=TimeSpan.FromSeconds(i*1.2),Duration=TimeSpan.FromSeconds(2.4)};
                    fade.KeyFrames.Add(new LinearDoubleKeyFrame(0,KeyTime.FromPercent(0)));fade.KeyFrames.Add(new LinearDoubleKeyFrame(1,KeyTime.FromPercent(.3)));fade.KeyFrames.Add(new LinearDoubleKeyFrame(0,KeyTime.FromPercent(1)));
                    Timeline.SetDesiredFrameRate(fade,30);
                    letters[i].BeginAnimation(UIElement.OpacityProperty,fade);
                }
            },()=>{breathe.BeginAnimation(ScaleTransform.ScaleYProperty,null);for(int i=0;i<2;i++){lifts[i].BeginAnimation(TranslateTransform.YProperty,null);letters[i].BeginAnimation(UIElement.OpacityProperty,null);}});
            return art;
        }
        // A controller with a magnifier looking around over it.
        internal static FrameworkElement Searching(double size)
        {
            var art=new Grid {Width=size,Height=size};
            var pad=Lines(Pad,size*.7,120);pad.HorizontalAlignment=HorizontalAlignment.Left;pad.VerticalAlignment=VerticalAlignment.Bottom;
            art.Children.Add(pad);
            var glass=Lines("M10.5,4 A6.5,6.5 0 1 0 10.5,17 A6.5,6.5 0 1 0 10.5,4 M15.3,15.3 L21,21",size*.5,240,2);
            glass.HorizontalAlignment=HorizontalAlignment.Right;glass.VerticalAlignment=VerticalAlignment.Top;
            var look=new TranslateTransform();glass.RenderTransform=look;
            art.Children.Add(glass);
            WhileShown(art,()=>{look.BeginAnimation(TranslateTransform.XProperty,Loop(0,-size*.22,1.6,true));look.BeginAnimation(TranslateTransform.YProperty,Loop(0,size*.1,1.1,true));},
                ()=>{look.BeginAnimation(TranslateTransform.XProperty,null);look.BeginAnimation(TranslateTransform.YProperty,null);});
            return art;
        }
        // A clock whose hand goes round.
        internal static FrameworkElement Clock(double size)
        {
            var art=new Grid {Width=size,Height=size};
            art.Children.Add(new Ellipse {Width=size*.8,Height=size*.8,Stroke=Ink(200),StrokeThickness=Math.Max(1.6,size/34)});
            for(int i=0;i<12;i+=3)
            {
                var tick=new Rectangle {Width=Math.Max(1.5,size/40),Height=size*.08,Fill=Ink(120),VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,size*.15,0,0),RenderTransformOrigin=new Point(.5,(size*.35)/(size*.08)),RenderTransform=new RotateTransform(i*30)};
                art.Children.Add(tick);
            }
            var hour=new Rectangle {Width=Math.Max(2,size/26),Height=size*.2,RadiusX=1,RadiusY=1,Fill=Ink(230),VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,size*.3,0,0),RenderTransformOrigin=new Point(.5,1),RenderTransform=new RotateTransform(60)};
            var minuteTurn=new RotateTransform();
            var minute=new Rectangle {Width=Math.Max(1.6,size/34),Height=size*.28,RadiusX=1,RadiusY=1,Fill=Ink(230),VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,size*.22,0,0),RenderTransformOrigin=new Point(.5,1),RenderTransform=minuteTurn};
            art.Children.Add(hour);art.Children.Add(minute);
            art.Children.Add(new Ellipse {Width=size*.07,Height=size*.07,Fill=Ink(240)});
            WhileShown(art,()=>minuteTurn.BeginAnimation(RotateTransform.AngleProperty,Loop(0,360,6)),()=>minuteTurn.BeginAnimation(RotateTransform.AngleProperty,null));
            return art;
        }
    }
}
