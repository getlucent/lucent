using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Lucent
{
    internal static class Art
    {
        // The Lucent mark: a ring of light with a bead where it opens. Brand.DrawIcon draws the same geometry for the icon and tray.
        public static Viewbox Logo(double size)
        {
            var canvas=new Canvas {Width=32,Height=32};
            canvas.Children.Add(new Path {Data=Geometry.Parse("M26.27,13.82 A10.5,10.5 0 1 1 18.18,5.73"),Stroke=Brushes.White,StrokeThickness=ResolutionSwitcher.Brand.RingWidth,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});
            var bead=new Ellipse {Width=ResolutionSwitcher.Brand.BeadRadius*2,Height=ResolutionSwitcher.Brand.BeadRadius*2,Fill=Brushes.White};
            Canvas.SetLeft(bead,ResolutionSwitcher.Brand.BeadX-ResolutionSwitcher.Brand.BeadRadius);
            Canvas.SetTop(bead,ResolutionSwitcher.Brand.BeadY-ResolutionSwitcher.Brand.BeadRadius);
            canvas.Children.Add(bead);
            return new Viewbox {Width=size,Height=size,Child=canvas};
        }
        // The Vice Sunset version: the white mark on a rounded sunset tile, as the app icon has while the theme is on.
        public static Border SunsetLogo(double size)
        {
            // The mark alone, as in the plain logo, but drawn in the icon's sunset colors corner to corner (blue at the top left, through violet and pink, to
            // orange at the bottom right), with a faint pink glow.
            var ink=new LinearGradientBrush {MappingMode=BrushMappingMode.Absolute,StartPoint=new Point(5,5),EndPoint=new Point(27,27)};
            ink.GradientStops.Add(new GradientStop(Color.FromRgb(86,117,248),0));
            ink.GradientStops.Add(new GradientStop(Color.FromRgb(151,103,247),.25));
            ink.GradientStops.Add(new GradientStop(Color.FromRgb(214,62,214),.48));
            ink.GradientStops.Add(new GradientStop(Color.FromRgb(255,70,160),.68));
            ink.GradientStops.Add(new GradientStop(Color.FromRgb(255,146,137),.85));
            ink.GradientStops.Add(new GradientStop(Color.FromRgb(255,178,97),1));
            ink.Freeze();
            var canvas=new Canvas {Width=32,Height=32};
            canvas.Children.Add(new System.Windows.Shapes.Path {Data=Geometry.Parse("M26.27,13.82 A10.5,10.5 0 1 1 18.18,5.73"),Stroke=ink,StrokeThickness=ResolutionSwitcher.Brand.RingWidth,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});
            // The bead sits where the gradient is violet pink.
            var bead=new System.Windows.Shapes.Ellipse {Width=ResolutionSwitcher.Brand.BeadRadius*2,Height=ResolutionSwitcher.Brand.BeadRadius*2,Fill=new SolidColorBrush(Color.FromRgb(196,84,232))};
            Canvas.SetLeft(bead,ResolutionSwitcher.Brand.BeadX-ResolutionSwitcher.Brand.BeadRadius);
            Canvas.SetTop(bead,ResolutionSwitcher.Brand.BeadY-ResolutionSwitcher.Brand.BeadRadius);
            canvas.Children.Add(bead);
            return new Border {Width=size,Height=size,Child=new Viewbox {Width=size,Height=size,Child=canvas},HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,
                Effect=new System.Windows.Media.Effects.DropShadowEffect {Color=Color.FromRgb(255,80,170),BlurRadius=8,ShadowDepth=0,Opacity=.45}};
        }
        public static System.Windows.Media.Imaging.BitmapSource SunsetPicture(int size)
        {
            using(var bitmap=ResolutionSwitcher.Brand.SunsetBitmap(size))
            {
                var data=bitmap.LockBits(new System.Drawing.Rectangle(0,0,size,size),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    var source=System.Windows.Media.Imaging.BitmapSource.Create(size,size,96,96,PixelFormats.Bgra32,null,data.Scan0,data.Stride*size,data.Stride);
                    source.Freeze();return source;
                }
                finally{bitmap.UnlockBits(data);}
            }
        }
        public static string Glyph(string name)
        {
            switch(name){
                case "Video":return "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M12,3 L12,21 M6,7 L12,7 M4,12 L12,12 M6,17 L12,17";
                case "Monitor":return "M2,3 L22,3 22,17 2,17Z M12,17 L12,22 M7,22 L17,22";
                case "Compare":return "M2,5 L22,5 22,19 2,19Z M12,5 L12,19";
                case "Vibrance":return "M12,2 C6,2 2,6 2,12 C2,18 6,22 11,22 C15,22 12,17 16,16 C18,15 22,16 22,11 C22,6 18,2 12,2Z M7,12 A1,1 0 1 0 7,10 A1,1 0 1 0 7,12 M11,8 A1,1 0 1 0 11,6 A1,1 0 1 0 11,8 M17,9 A1,1 0 1 0 17,7 A1,1 0 1 0 17,9";
                case "Eye":return "M1,12 Q12,-2 23,12 Q12,26 1,12Z M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8";
                case "Vision":return "M12,3 A5,5 0 1 0 12,13 A5,5 0 1 0 12,3 M7,10 A5,5 0 1 0 7,20 A5,5 0 1 0 7,10 M17,10 A5,5 0 1 0 17,20 A5,5 0 1 0 17,10";
                case "Sun":return "M12,7 A5,5 0 1 0 12,17 A5,5 0 1 0 12,7 M12,1 L12,4 M12,20 L12,23 M1,12 L4,12 M20,12 L23,12 M4,4 L6,6 M18,18 L20,20 M20,4 L18,6 M6,18 L4,20";
                case "Dim":return "M19,15 C10,17 7,9 10,3 C2,5 1,15 7,19 C12,23 18,21 21,15Z";
                case "Warning":return "M12,2 L23,21 1,21Z M12,8 L12,14 M12,17 L12,18";
                case "Resolution":return "M2,3 L22,3 22,17 2,17Z M12,17 L12,22 M7,22 L17,22 M5,7 L8,7 M5,7 L5,10 M19,13 L16,13 M19,13 L19,10";
                case "Backgrounds":return "M3,3 L21,3 21,21 3,21Z M4,17 L9,12 13,16 16,13 20,17 M15,8 A1,1 0 1 0 15,6 A1,1 0 1 0 15,8";
                case "Profiles":return "M12,2 L23,8 12,14 1,8Z M2,13 L12,19 22,13 M2,18 L12,24 22,18";
                case "Crosshair":return "M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 M12,0 L12,8 M12,16 L12,24 M0,12 L8,12 M16,12 L24,12";
                case "Game Boost":return "M14,1 L4,14 12,14 10,23 21,9 13,9Z";
                case "Visual tools":return "M10,2 A8,8 0 1 0 10,18 A8,8 0 1 0 10,2 M16,16 L23,23";
                default:return "M12,3 L14,6 18,5 19,9 22,11 19,14 20,18 16,19 13,22 10,19 6,20 5,16 2,13 5,10 4,6 8,5Z M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8";
            }
        }
        public static Viewbox Icon(string name,double size)
        {
            var icon=Design.Icon(name,size);
            icon.Margin=new Thickness(0,0,12,0);
            return icon;
        }
    }
}
