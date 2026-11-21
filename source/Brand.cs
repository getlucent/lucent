using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace ResolutionSwitcher
{
    internal static class Brand
    {
        // The Lucent mark: a ring of light with a bead where it opens, on a 32-unit grid.
        // The in-app logo draws the same geometry (Art.Logo), so the icon, tray and window all match.
        internal const float RingStart=-12,RingSweep=294,RingWidth=4.4f,BeadX=23.43f,BeadY=8.57f,BeadRadius=3.1f;
        public static void DrawIcon(Graphics g,RectangleF r)
        {
            var saved=g.Save();
            g.SmoothingMode=SmoothingMode.AntiAlias;
            float scale=Math.Min(r.Width,r.Height)/32f;
            g.TranslateTransform(r.X+(r.Width-32*scale)/2,r.Y+(r.Height-32*scale)/2);
            g.ScaleTransform(scale,scale);
            using(var pen=new Pen(Color.White,RingWidth){StartCap=LineCap.Round,EndCap=LineCap.Round})g.DrawArc(pen,5.5f,5.5f,21f,21f,RingStart,RingSweep);
            using(var fill=new SolidBrush(Color.White))g.FillEllipse(fill,BeadX-BeadRadius,BeadY-BeadRadius,BeadRadius*2,BeadRadius*2);
            g.Restore(saved);
        }
        public static void DrawBadge(Graphics g,RectangleF r)
        {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=RoundedPanel.RoundedPath(Rectangle.Round(r),(int)(r.Width*.22f)))using(var fill=new SolidBrush(Color.FromArgb(8,8,9)))g.FillPath(fill,path);
            DrawIcon(g,new RectangleF(r.X+r.Width*.16f,r.Y+r.Height*.16f,r.Width*.68f,r.Height*.68f));
        }
        // The Vice Sunset icon at any size, drawn from the picture kept in the program (Art.SunsetPicture makes the window and taskbar icon from it; SunsetIcon the tray icon).
        internal static System.Drawing.Bitmap SunsetBitmap(int size)
        {
            var bitmap=new System.Drawing.Bitmap(size,size,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using(var stream=new System.IO.MemoryStream(Convert.FromBase64String(SunsetArt.Png)))using(var art=new Bitmap(stream))using(var g=Graphics.FromImage(bitmap))using(var edges=new System.Drawing.Imaging.ImageAttributes())
            {
                // Mirrored edges, so shrinking does not blur the picture's border into the transparent outside.
                edges.SetWrapMode(WrapMode.TileFlipXY);
                g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.CompositingQuality=CompositingQuality.HighQuality;
                g.DrawImage(art,new Rectangle(0,0,size,size),0,0,art.Width,art.Height,GraphicsUnit.Pixel,edges);
            }
            if(size<=64)Sharpen(bitmap,size<=32?.55:.35);
            return bitmap;
        }
        // A light unsharp mask for the small sizes: shrinking a picture softens it, and a little sharpening brings back the crisp edge of the mark.
        static void Sharpen(System.Drawing.Bitmap bitmap,double amount)
        {
            int w=bitmap.Width,h=bitmap.Height;
            var rect=new Rectangle(0,0,w,h);
            var data=bitmap.LockBits(rect,System.Drawing.Imaging.ImageLockMode.ReadWrite,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var src=new byte[data.Stride*h];System.Runtime.InteropServices.Marshal.Copy(data.Scan0,src,0,src.Length);
            var dst=(byte[])src.Clone();
            for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++)for(int c=0;c<3;c++)
            {
                int i=y*data.Stride+x*4+c;double sum=0;
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)sum+=src[i+dy*data.Stride+dx*4];
                double v=src[i]+amount*(src[i]-sum/9);
                dst[i]=(byte)Math.Max(0,Math.Min(255,Math.Round(v)));
            }
            System.Runtime.InteropServices.Marshal.Copy(dst,0,data.Scan0,dst.Length);
            bitmap.UnlockBits(data);
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
        // The Vice icon as a real icon file: one picture per size Windows asks for (16 to 256 pixels), each shrunk on its own from the artwork, so the taskbar and
        // the tray get a picture made for their size instead of a big one shrunk on the fly (which looked soft next to other apps).
        static byte[] sunsetIco;
        internal static byte[] SunsetIco()
        {
            if(sunsetIco!=null)return sunsetIco;
            int[] sizes={16,20,24,32,40,48,64,96,128,256};
            var images=new System.Collections.Generic.List<byte[]>();
            foreach(int size in sizes)
            {
                using(var bitmap=SunsetBitmap(size))using(var png=new System.IO.MemoryStream())
                {
                    bitmap.Save(png,System.Drawing.Imaging.ImageFormat.Png);
                    images.Add(png.ToArray());
                }
            }
            using(var ico=new System.IO.MemoryStream())using(var writer=new System.IO.BinaryWriter(ico))
            {
                writer.Write((short)0);writer.Write((short)1);writer.Write((short)sizes.Length);
                int offset=6+16*sizes.Length;
                for(int i=0;i<sizes.Length;i++)
                {
                    writer.Write((byte)(sizes[i]>=256?0:sizes[i]));writer.Write((byte)(sizes[i]>=256?0:sizes[i]));
                    writer.Write((byte)0);writer.Write((byte)0);writer.Write((short)1);writer.Write((short)32);
                    writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length;
                }
                foreach(var image in images)writer.Write(image);
                writer.Flush();
                return sunsetIco=ico.ToArray();
            }
        }
        // The tray's icon at the size it is drawn, picked from the icon file.
        public static Icon SunsetIcon(int size)
        {
            using(var stream=new System.IO.MemoryStream(SunsetIco()))return new Icon(stream,size,size);
        }
        public static void DrawWordmark(Graphics g,RectangleF r)
        {
            using(var font=new Font(Synthwave.FontFamily,Math.Max(8,r.Height*.3f),FontStyle.Bold))using(var b=new SolidBrush(Color.White))using(var sf=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString("Lucent",font,b,r,sf);
        }
    }
    internal sealed class BrandWordmark:Control
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            Brand.DrawWordmark(e.Graphics,ClientRectangle);
        }
    }
}
