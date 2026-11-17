using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Forms=System.Windows.Forms;

namespace Lucent
{
    // The mouse pointer, drawn by Lucent above the GPU overlay (Smart shadows, Color pop).
    // While Windows' own color effects are on, Windows draws the pointer into the picture of the screen itself. The overlay shows that picture a
    // frame or two late, so a moving pointer showed twice: Windows' live one and a copy trailing behind it in the overlay. With the overlay up,
    // the system pointer is hidden (so it is not in the picture) and this small see-through window shows the same pointer where the mouse is now.
    internal sealed class OverlayPointer:Forms.Form
    {
        [StructLayout(LayoutKind.Sequential)]struct CursorInfo{public int Size,Flags;public IntPtr Cursor;public Point Where;}
        [StructLayout(LayoutKind.Sequential)]struct IconInfo{public bool IsIcon;public int HotX,HotY;public IntPtr Mask,Color;}
        [StructLayout(LayoutKind.Sequential)]struct BitmapHeader{public int Type,Width,Height,WidthBytes;public short Planes,BitsPixel;public IntPtr Bits;}
        [StructLayout(LayoutKind.Sequential)]struct Point32{public int X,Y;}
        [StructLayout(LayoutKind.Sequential)]struct Size32{public int W,H;}
        [StructLayout(LayoutKind.Sequential,Pack=1)]struct Blend{public byte Op,Flags,Alpha,Format;}
        [DllImport("user32.dll")]static extern bool GetCursorInfo(ref CursorInfo info);
        [DllImport("user32.dll")]static extern bool GetIconInfo(IntPtr icon,out IconInfo info);
        [DllImport("user32.dll")]static extern bool DrawIconEx(IntPtr dc,int x,int y,IntPtr icon,int w,int h,int step,IntPtr brush,int flags);
        [DllImport("gdi32.dll")]static extern int GetObject(IntPtr bitmap,int size,out BitmapHeader header);
        [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr o);
        [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr w);
        [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr w,IntPtr dc);
        [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr o);
        [DllImport("user32.dll")]static extern bool UpdateLayeredWindow(IntPtr w,IntPtr dst,ref Point32 at,ref Size32 size,IntPtr src,ref Point32 from,int key,ref Blend blend,int flags);
        [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr w,IntPtr after,int x,int y,int cx,int cy,uint flags);
        [DllImport("user32.dll")]static extern bool SetWindowDisplayAffinity(IntPtr w,uint affinity);

        sealed class Shape {public Bitmap Picture;public int HotX,HotY;}
        readonly Dictionary<IntPtr,Shape> shapes=new Dictionary<IntPtr,Shape>();
        IntPtr shown;Point placed=new Point(int.MinValue,0);bool showing;

        public OverlayPointer()
        {
            FormBorderStyle=Forms.FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=Forms.FormStartPosition.Manual;
            AutoScaleMode=Forms.AutoScaleMode.None;Enabled=false;TabStop=false;
        }
        protected override bool ShowWithoutActivation {get{return true;}}
        // Layered, transparent to the mouse, a tool window, never activated.
        protected override Forms.CreateParams CreateParams {get{var cp=base.CreateParams;cp.ExStyle|=0x080800A0;cp.Style|=0x08000000;return cp;}}
        protected override void WndProc(ref Forms.Message m)
        {
            if(m.Msg==0x0084){m.Result=(IntPtr)(-1);return;}
            base.WndProc(ref m);
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Kept out of screen pictures, so the overlay never captures it back.
            SetWindowDisplayAffinity(Handle,0x11);
        }
        // Follows the pointer: its shape (made once per shape) and place. Hidden when no pointer is shown (a game hides it).
        public void Follow()
        {
            var info=new CursorInfo{Size=Marshal.SizeOf(typeof(CursorInfo))};
            if(!GetCursorInfo(ref info)||(info.Flags&1)==0||info.Cursor==IntPtr.Zero){Conceal();return;}
            Shape shape;
            if(!shapes.TryGetValue(info.Cursor,out shape)){shape=Make(info.Cursor);shapes[info.Cursor]=shape;}
            if(shape==null){Conceal();return;}
            var at=new Point(info.Where.X-shape.HotX,info.Where.Y-shape.HotY);
            if(info.Cursor!=shown||!showing){Draw(shape.Picture,at);shown=info.Cursor;placed=at;}
            else if(at!=placed){SetWindowPos(Handle,new IntPtr(-1),at.X,at.Y,0,0,0x0001|0x0010);placed=at;}
            if(!showing){Show();showing=true;SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x0001|0x0002|0x0010|0x0040);}
        }
        public void Conceal(){if(showing){Hide();showing=false;}}
        // Puts the pointer back on top (the overlay was just raised).
        public void Raise(){if(showing)SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x0001|0x0002|0x0010);}
        // The pointer drawn on black and on white: from the two, each pixel's color and see-through amount (this also handles the old
        // black-and-white pointers, like the text beam, which have no see-through amount of their own).
        static Shape Make(IntPtr cursor)
        {
            IconInfo icon;
            if(!GetIconInfo(cursor,out icon))return null;
            try
            {
                BitmapHeader mask;GetObject(icon.Mask,Marshal.SizeOf(typeof(BitmapHeader)),out mask);
                int w=Math.Max(1,mask.Width),h=icon.Color==IntPtr.Zero?Math.Max(1,mask.Height/2):Math.Max(1,mask.Height);
                using(var onBlack=new Bitmap(w,h,PixelFormat.Format32bppArgb))
                using(var onWhite=new Bitmap(w,h,PixelFormat.Format32bppArgb))
                {
                    foreach(var pair in new[]{Tuple.Create(onBlack,Color.Black),Tuple.Create(onWhite,Color.White)})
                        using(var g=Graphics.FromImage(pair.Item1))
                        {
                            g.Clear(pair.Item2);
                            IntPtr dc=g.GetHdc();
                            try{DrawIconEx(dc,0,0,cursor,w,h,0,IntPtr.Zero,3);}finally{g.ReleaseHdc(dc);}
                        }
                    var picture=new Bitmap(w,h,PixelFormat.Format32bppArgb);
                    var b=onBlack.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                    var wh=onWhite.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                    var o=picture.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                    var bp=new byte[b.Stride*h];var wp=new byte[wh.Stride*h];var op=new byte[o.Stride*h];
                    Marshal.Copy(b.Scan0,bp,0,bp.Length);Marshal.Copy(wh.Scan0,wp,0,wp.Length);
                    for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                    {
                        int i=y*b.Stride+x*4,j=y*wh.Stride+x*4,k=y*o.Stride+x*4;
                        int alpha=255-Math.Max(0,Math.Min(255,((wp[j]-bp[i])+(wp[j+1]-bp[i+1])+(wp[j+2]-bp[i+2]))/3));
                        op[k+3]=(byte)alpha;
                        // Premultiplied, as the layered window wants it: the color on black is already the color times its amount.
                        op[k]=bp[i];op[k+1]=bp[i+1];op[k+2]=bp[i+2];
                    }
                    Marshal.Copy(op,0,o.Scan0,op.Length);
                    onBlack.UnlockBits(b);onWhite.UnlockBits(wh);picture.UnlockBits(o);
                    return new Shape {Picture=picture,HotX=icon.HotX,HotY=icon.HotY};
                }
            }
            catch(Exception){return null;}
            finally{if(icon.Mask!=IntPtr.Zero)DeleteObject(icon.Mask);if(icon.Color!=IntPtr.Zero)DeleteObject(icon.Color);}
        }
        void Draw(Bitmap picture,Point at)
        {
            IntPtr screen=GetDC(IntPtr.Zero),memory=CreateCompatibleDC(screen),hbitmap=IntPtr.Zero,old=IntPtr.Zero;
            try
            {
                // The picture's pixels are already premultiplied; keep them as they are.
                var data=picture.LockBits(new Rectangle(0,0,picture.Width,picture.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                using(var raw=new Bitmap(picture.Width,picture.Height,data.Stride,PixelFormat.Format32bppPArgb,data.Scan0))hbitmap=raw.GetHbitmap(Color.FromArgb(0));
                picture.UnlockBits(data);
                old=SelectObject(memory,hbitmap);
                var size=new Size32{W=picture.Width,H=picture.Height};var from=new Point32();var where=new Point32{X=at.X,Y=at.Y};
                var blend=new Blend{Op=0,Flags=0,Alpha=255,Format=1};
                UpdateLayeredWindow(Handle,screen,ref where,ref size,memory,ref from,0,ref blend,2);
            }
            finally
            {
                if(old!=IntPtr.Zero)SelectObject(memory,old);
                if(hbitmap!=IntPtr.Zero)DeleteObject(hbitmap);
                DeleteDC(memory);ReleaseDC(IntPtr.Zero,screen);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing)foreach(var shape in shapes.Values)if(shape!=null)shape.Picture.Dispose();
            base.Dispose(disposing);
        }
    }
}
