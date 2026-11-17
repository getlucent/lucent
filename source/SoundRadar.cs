using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using Forms=System.Windows.Forms;

namespace Lucent
{
    // How the radar looks and reacts. Sensitivity is 0 to 100, size and place are the words shown in the app.
    internal sealed class RadarOptions
    {
        public double Sensitivity=60;
        // How strongly it shows over the game, 10 to 100 percent.
        public double Opacity=100;
        public string Size="Medium",Place="Center";
        internal int Pixels {get{return Size=="Small"?170:Size=="Large"?320:240;}}
    }

    // Turns the left/right/surround levels of the sound being played into eight directions around you, and draws them as glowing arcs.
    // It only reads how loud each speaker channel is. It does not capture, record or hear any audio.
    internal static class SoundDirections
    {
        // Sector 0 is straight ahead, then clockwise: ahead-right, right, behind-right, behind, behind-left, left, ahead-left.
        public const int Sectors=8;
        // Each channel lights the sector it sits in strongly and its neighbors a little. Channel order is the one Windows uses for 2, 4, 6 and 8 speakers.
        static readonly double[][] Spread=
        {
            /*front left*/ new[]{.25,0,0,0,0,0,.45,1},
            /*front right*/ new[]{.25,1,.45,0,0,0,0,0},
            /*center*/ new[]{1,.3,0,0,0,0,0,.3},
            /*back left*/ new[]{0,0,0,0,.3,1,.2,0},
            /*back right*/ new[]{0,0,.2,1,.3,0,0,0},
            /*side left*/ new[]{0,0,0,0,0,.3,1,.3},
            /*side right*/ new[]{0,.3,1,.3,0,0,0,0},
            /*left of a stereo pair*/ new[]{0,0,0,0,0,.3,1,.55},
            /*right of a stereo pair*/ new[]{0,.55,1,.3,0,0,0,0}
        };
        // Which of those a channel is, by how many channels there are. -1 means it is not a direction (the bass channel).
        static int[] Layout(int channels)
        {
            switch(channels)
            {
                case 1:return new[]{2};
                case 2:return new[]{7,8};
                case 4:return new[]{0,1,3,4};
                case 6:return new[]{0,1,2,-1,3,4};
                case 8:return new[]{0,1,2,-1,3,4,5,6};
                default:
                    var all=new int[channels];
                    for(int i=0;i<channels;i++)all[i]=i%2==0?7:8;
                    return all;
            }
        }
        // The peak of each channel in, the strongest push into each sector out.
        public static double[] Map(float[] peaks)
        {
            var sectors=new double[Sectors];
            if(peaks==null||peaks.Length==0)return sectors;
            var layout=Layout(peaks.Length);
            for(int channel=0;channel<peaks.Length&&channel<layout.Length;channel++)
            {
                if(layout[channel]<0)continue;
                var spread=Spread[layout[channel]];
                double level=Math.Max(0,Math.Min(1,peaks[channel]));
                for(int s=0;s<Sectors;s++)sectors[s]=Math.Max(sectors[s],level*spread[s]);
            }
            return sectors;
        }
        // Quiet background sound should not light anything. Sensitivity moves the point where sound starts to show.
        public static double Shape(double level,double sensitivity)
        {
            double floorLevel=.02+(1-Math.Max(0,Math.Min(1,sensitivity/100)))*.28;
            if(level<=floorLevel)return 0;
            return Math.Min(1,Math.Pow((level-floorLevel)/(1-floorLevel),.6)*1.15);
        }
    }

    internal static class RadarRenderer
    {
        // How many steps each direction's meter has, from the inside out.
        internal const int Steps=6;
        // One step of a direction's meter: a short piece of ring between two radii and two angles.
        static GraphicsPath Piece(float middle,float from,float to,float start,float sweep)
        {
            var path=new GraphicsPath();
            path.AddArc(middle-to,middle-to,to*2,to*2,start,sweep);
            path.AddArc(middle-from,middle-from,from*2,from*2,start+sweep,-sweep);
            path.CloseFigure();
            return path;
        }
        // Draws the radar for these levels (0 to 1 per direction) into a transparent picture of the given size: a ring of eight short meters around an
        // empty middle (the crosshair stays clear). A direction with sound shows its meter, filled from the inside out the louder it is; the steps
        // are white, the outer ones amber and red, so a loud sound close by reads at a glance. Silence draws nothing at all.
        public static Bitmap Render(double[] levels,int size)
        {
            var bitmap=new Bitmap(size,size,PixelFormat.Format32bppPArgb);
            using(var g=Graphics.FromImage(bitmap))
            {
                g.SmoothingMode=SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float middle=size/2f,outer=size/2f-4,thickness=size*.2f,inner=outer-thickness,gap=Math.Max(2f,size/80f);
                float step=(thickness-gap*(Steps-1))/Steps;
                double loudest=0;foreach(double l in levels)loudest=Math.Max(loudest,l);
                if(loudest<.02)return bitmap;
                // A thin circle inside the meters and a small mark for straight ahead, faint, only while something sounds.
                int faint=(int)(18+50*Math.Min(1,loudest));
                using(var pen=new Pen(Color.FromArgb(faint,255,255,255),1.2f))g.DrawEllipse(pen,middle-(inner-7),middle-(inner-7),(inner-7)*2,(inner-7)*2);
                using(var brush=new SolidBrush(Color.FromArgb(faint+40,255,255,255)))
                {
                    float tip=middle-(inner-7)+3,half=Math.Max(3f,size/60f);
                    g.FillPolygon(brush,new[]{new PointF(middle,tip-half*1.6f),new PointF(middle-half,tip+half*.4f),new PointF(middle+half,tip+half*.4f)});
                }
                for(int sector=0;sector<levels.Length;sector++)
                {
                    double level=Math.Max(0,Math.Min(1,levels[sector]));
                    if(level<.02)continue;
                    // Sector 0 is straight up; each covers 45 degrees with a gap on each side.
                    float start=-90+sector*45-22.5f+4f,sweep=45-8f;
                    int lit=Math.Max(1,(int)Math.Ceiling(level*Steps-.001));
                    // One soft glow behind the lit part of the meter, in the color of its outermost lit step.
                    {
                        Color top=lit>=Steps?Color.FromArgb(255,112,96):lit>=Steps-2?Color.FromArgb(255,196,98):Color.FromArgb(245,245,247);
                        float spread=Math.Max(3f,size/45f);
                        using(var halo=Piece(middle,inner-spread,inner+lit*(step+gap)-gap+spread,start-1.5f,sweep+3f))
                        using(var brush=new SolidBrush(Color.FromArgb((int)(18+34*level),top)))g.FillPath(brush,halo);
                    }
                    for(int i=0;i<Steps;i++)
                    {
                        float from=inner+i*(step+gap),to=from+step;
                        using(var path=Piece(middle,from,to,start,sweep))
                        {
                            if(i>=lit)
                            {
                                // The rest of the meter, faint, so its length reads.
                                using(var brush=new SolidBrush(Color.FromArgb((int)(26+30*level),255,255,255)))g.FillPath(brush,path);
                                continue;
                            }
                            Color ink=i>=Steps-1?Color.FromArgb(255,112,96):i>=Steps-3?Color.FromArgb(255,196,98):Color.FromArgb(245,245,247);
                            using(var brush=new SolidBrush(Color.FromArgb((int)(150+105*level),ink)))g.FillPath(brush,path);
                        }
                    }
                }
            }
            return bitmap;
        }
    }

    // A window that shows a picture with its own transparency, lets every click through and never takes focus.
    internal sealed class RadarOverlay:Forms.Form
    {
        [StructLayout(LayoutKind.Sequential)]struct Point32{public int X,Y;}
        [StructLayout(LayoutKind.Sequential)]struct Size32{public int W,H;}
        [StructLayout(LayoutKind.Sequential,Pack=1)]struct Blend{public byte Op,Flags,Alpha,Format;}
        [DllImport("user32.dll",SetLastError=true)]static extern bool UpdateLayeredWindow(IntPtr window,IntPtr screenDc,ref Point32 position,ref Size32 size,IntPtr sourceDc,ref Point32 source,int key,ref Blend blend,int flags);
        [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
        [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr window,IntPtr dc);
        [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
        [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr obj);

        public RadarOverlay()
        {
            FormBorderStyle=Forms.FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=Forms.FormStartPosition.Manual;
            AutoScaleMode=Forms.AutoScaleMode.None;Enabled=false;TabStop=false;
        }
        protected override bool ShowWithoutActivation {get{return true;}}
        // Layered (0x80000), transparent to the mouse (0x20), a tool window (0x80) and never activated (0x08000000).
        protected override Forms.CreateParams CreateParams
        {
            get{var cp=base.CreateParams;cp.ExStyle|=0x080800A0;cp.Style|=0x08000000;return cp;}
        }
        protected override void WndProc(ref Forms.Message m)
        {
            if(m.Msg==0x0084){m.Result=(IntPtr)(-1);return;}
            if(m.Msg==0x0021){m.Result=(IntPtr)3;return;}
            base.WndProc(ref m);
        }
        // Shows this picture with its top left at the given place on screen.
        // How see-through the whole radar is, 0 to 255.
        public byte Alpha=255;
        public void Present(Bitmap picture,Point at)
        {
            if(!IsHandleCreated)CreateControl();
            IntPtr screen=GetDC(IntPtr.Zero),memory=CreateCompatibleDC(screen),hbitmap=picture.GetHbitmap(Color.FromArgb(0)),old=IntPtr.Zero;
            try
            {
                old=SelectObject(memory,hbitmap);
                var size=new Size32{W=picture.Width,H=picture.Height};var from=new Point32();var where=new Point32{X=at.X,Y=at.Y};
                var blend=new Blend{Op=0,Flags=0,Alpha=Alpha,Format=1};
                UpdateLayeredWindow(Handle,screen,ref where,ref size,memory,ref from,0,ref blend,2);
            }
            finally
            {
                if(old!=IntPtr.Zero)SelectObject(memory,old);
                DeleteObject(hbitmap);DeleteDC(memory);ReleaseDC(IntPtr.Zero,screen);
            }
            if(!Visible)
            {
                base.Show();
                // Shown without activation, it can end up beneath another topmost window; this lifts it above them without moving or activating anything.
                SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x0001|0x0002|0x0010|0x0040);
            }
        }
    }

    // Watches how loud each speaker channel is and shows where loud sounds are coming from.
    internal sealed class SoundRadar:IDisposable
    {
        [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface Enumerator
        {int EnumAudioEndpoints(int flow,uint mask,out object devices);int GetDefaultAudioEndpoint(int flow,int role,out Device device);}
        [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface Device
        {int Activate(ref Guid id,uint context,IntPtr activation,[MarshalAs(UnmanagedType.IUnknown)]out object value);}
        [ComImport,Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface Meter
        {
            int GetPeakValue(out float value);
            int GetMeteringChannelCount(out uint count);
            int GetChannelsPeakValues(uint count,[Out,MarshalAs(UnmanagedType.LPArray,SizeParamIndex=0)]float[] values);
        }

        volatile bool stopping;
        volatile RadarOptions options=new RadarOptions();
        Thread worker;
        public event Action<string> Failed;
        // Test hooks: where to draw, and the levels to show instead of reading the speakers.
        internal Forms.Screen Target=Forms.Screen.PrimaryScreen;
        internal double[] FixedLevels;
        internal long Frames;
        internal IntPtr OverlayHandle;
        internal Point Origin;

        public void Apply(RadarOptions next)
        {
            options=next;
            if(worker!=null)return;
            worker=new Thread(Run){IsBackground=true,Name="Lucent sound radar"};
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }
        public void Dispose()
        {
            stopping=true;
            if(worker!=null&&worker!=Thread.CurrentThread)worker.Join(2500);
        }
        // Where the picture goes for this place, size and screen.
        internal static Point Place(Rectangle screen,string place,int pixels)
        {
            int x=screen.Left+(screen.Width-pixels)/2;
            switch(place)
            {
                case "Top":return new Point(x,screen.Top+40);
                case "Bottom":return new Point(x,screen.Bottom-pixels-150);
                default:return new Point(x,screen.Top+(screen.Height-pixels)/2);
            }
        }
        void Run()
        {
            object enumerator=null,endpoint=null,meterObject=null;
            RadarOverlay overlay=null;
            try
            {
                Meter meter=null;
                if(FixedLevels==null)
                {
                    enumerator=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
                    Device device;
                    Marshal.ThrowExceptionForHR(((Enumerator)enumerator).GetDefaultAudioEndpoint(0,1,out device));
                    endpoint=device;
                    Guid meterId=typeof(Meter).GUID;
                    Marshal.ThrowExceptionForHR(device.Activate(ref meterId,23,IntPtr.Zero,out meterObject));
                    meter=(Meter)meterObject;
                }
                overlay=new RadarOverlay();
                overlay.CreateControl();
                OverlayHandle=overlay.Handle;
                var shown=new double[SoundDirections.Sectors];
                var clock=System.Diagnostics.Stopwatch.StartNew();
                double last=clock.Elapsed.TotalSeconds;
                string drawnFor=null;bool drawn=false;
                while(!stopping)
                {
                    Forms.Application.DoEvents();
                    var now=options;
                    double[] raw;
                    if(FixedLevels!=null)raw=FixedLevels;
                    else
                    {
                        uint count;
                        if(meter.GetMeteringChannelCount(out count)<0||count==0){Thread.Sleep(50);continue;}
                        var peaks=new float[count];
                        Marshal.ThrowExceptionForHR(meter.GetChannelsPeakValues(count,peaks));
                        raw=SoundDirections.Map(peaks);
                    }
                    double time=clock.Elapsed.TotalSeconds,step=Math.Min(.2,time-last);last=time;
                    bool changed=false;
                    for(int s=0;s<shown.Length;s++)
                    {
                        double target=SoundDirections.Shape(raw[s],now.Sensitivity);
                        // A sound shows up at once and fades over about a third of a second, so a footstep is a flash you can catch.
                        double next=target>=shown[s]?target:shown[s]*Math.Exp(-step/.18);
                        if(next<.015)next=0;
                        if(Math.Abs(next-shown[s])>.004)changed=true;
                        shown[s]=next;
                    }
                    string key=now.Size+"|"+now.Place+"|"+Math.Round(now.Opacity);
                    overlay.Alpha=(byte)Math.Round(255*Math.Max(.1,Math.Min(1,now.Opacity/100)));
                    if(changed||!drawn||key!=drawnFor)
                    {
                        var rect=Target.Bounds;
                        Origin=Place(rect,now.Place,now.Pixels);
                        using(var picture=RadarRenderer.Render(shown,now.Pixels))overlay.Present(picture,Origin);
                        drawn=true;drawnFor=key;
                        Interlocked.Increment(ref Frames);
                    }
                    Thread.Sleep(16);
                }
            }
            catch(Exception error)
            {
                if(!stopping&&Failed!=null)Failed("The sound radar stopped: "+error.Message);
            }
            finally
            {
                if(overlay!=null){try{overlay.Close();overlay.Dispose();}catch(Exception){}}
                foreach(object item in new[]{meterObject,endpoint,enumerator})if(item!=null)try{Marshal.ReleaseComObject(item);}catch(Exception){}
            }
        }
    }
}
