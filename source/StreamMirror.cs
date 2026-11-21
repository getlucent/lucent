using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Forms=System.Windows.Forms;

namespace Lucent
{
    // "Show on screen share": the colors and Night Vision drawn where screen capture can see them.
    // Windows' fullscreen color effect (what Lucent normally uses) is applied after capture, so OBS, Discord and the Snipping Tool never see it,
    // and the GPU overlay hides itself from capture (it reads the screen, so it would read itself). Here each display gets a window of its own
    // that shows the desktop through the Windows magnifier engine at 1x with the color matrix applied, and leaves itself out of what it shows.
    // Capture sees that window, so it sees the colors. While it runs, the fullscreen effect is kept neutral so the colors are not applied twice.
    // It costs about a frame of delay: what is on the screen is a copy of the desktop, refreshed once per screen refresh.
    internal sealed class StreamMirror : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]struct RECT {public int Left,Top,Right,Bottom;}
        [DllImport("Magnification.dll")]static extern bool MagInitialize();
        [DllImport("Magnification.dll")]static extern bool MagSetWindowSource(IntPtr hwnd,RECT rect);
        [DllImport("Magnification.dll")]static extern bool MagSetWindowTransform(IntPtr hwnd,float[] matrix);
        [DllImport("Magnification.dll")]static extern bool MagSetColorEffect(IntPtr hwnd,float[] effect);
        [DllImport("Magnification.dll")]static extern bool MagSetWindowFilterList(IntPtr hwnd,int mode,int count,IntPtr[] list);
        [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern IntPtr CreateWindowEx(int exStyle,string className,string name,int style,int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
        [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")]static extern bool InvalidateRect(IntPtr hwnd,IntPtr rect,bool erase);
        [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
        [DllImport("user32.dll")]static extern bool SetLayeredWindowAttributes(IntPtr hwnd,uint key,byte alpha,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
        [DllImport("dwmapi.dll")]static extern int DwmFlush();
        const int WS_CHILD=0x40000000,WS_VISIBLE=0x10000000,MW_FILTERMODE_EXCLUDE=0;
        static readonly IntPtr HWND_TOPMOST=new IntPtr(-1);

        // A borderless window over one display that takes no clicks and never takes focus.
        sealed class Host : Forms.Form
        {
            public Host(System.Drawing.Rectangle bounds)
            {
                FormBorderStyle=Forms.FormBorderStyle.None;ShowInTaskbar=false;StartPosition=Forms.FormStartPosition.Manual;Bounds=bounds;
                BackColor=System.Drawing.Color.Black;TopMost=true;Text="Lucent screen share";
            }
            protected override bool ShowWithoutActivation{get{return true;}}
            protected override Forms.CreateParams CreateParams
            {
                get
                {
                    var p=base.CreateParams;
                    // layered + transparent (clicks pass through) + tool window (no taskbar, no Alt+Tab) + no activate + topmost
                    p.ExStyle|=0x80000|0x20|0x80|0x08000000|0x8;
                    return p;
                }
            }
        }
        sealed class Screen {public Host Host;public IntPtr Magnifier;public RECT Source;}

        readonly Thread thread;
        volatile bool stop;
        float[] wanted=ColorMath.Identity(),applied;
        readonly object gate=new object();
        readonly List<IntPtr> raise=new List<IntPtr>();
        internal string Problem;
        // Called (on the mirror's thread) once its windows are up, with their handles, so other overlays can be put back above them.
        internal Action<IntPtr[]> Ready;

        internal StreamMirror()
        {
            thread=new Thread(Run){IsBackground=true,Name="Lucent screen share mirror",Priority=ThreadPriority.AboveNormal};
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        internal void SetMatrix(float[] matrix){lock(gate)wanted=(float[])matrix.Clone();}
        // Windows that must stay above the mirror (the zoom overlay and its pointer are left out of capture, so the mirror cannot show them).
        internal void KeepAbove(IntPtr handle){if(handle==IntPtr.Zero)return;lock(gate){if(!raise.Contains(handle))raise.Add(handle);}}

        void Run()
        {
            var screens=new List<Screen>();
            try
            {
                if(!MagInitialize())throw new InvalidOperationException("Windows' magnifier could not start.");
                IntPtr instance=GetModuleHandle(null);
                foreach(var display in Forms.Screen.AllScreens)
                {
                    var b=display.Bounds;var host=new Host(b);host.Show();
                    SetLayeredWindowAttributes(host.Handle,0,255,2);
                    var mag=CreateWindowEx(0,"Magnifier","LucentMirror",WS_CHILD|WS_VISIBLE,0,0,b.Width,b.Height,host.Handle,IntPtr.Zero,instance,IntPtr.Zero);
                    if(mag==IntPtr.Zero)throw new InvalidOperationException("Windows' magnifier window could not be made.");
                    MagSetWindowTransform(mag,new float[]{1,0,0, 0,1,0, 0,0,1});
                    screens.Add(new Screen {Host=host,Magnifier=mag,Source=new RECT {Left=b.Left,Top=b.Top,Right=b.Right,Bottom=b.Bottom}});
                }
                // Every mirror leaves out every mirror (one display's window must not show up in another's).
                var hosts=screens.ConvertAll(s=>s.Host.Handle).ToArray();
                foreach(var s in screens)if(!MagSetWindowFilterList(s.Magnifier,MW_FILTERMODE_EXCLUDE,hosts.Length,hosts))throw new InvalidOperationException("The mirror could not leave itself out.");
                if(Ready!=null)Ready(hosts);
                int frame=0;
                while(!stop)
                {
                    float[] m;IntPtr[] above;
                    lock(gate){m=wanted;above=raise.ToArray();}
                    if(applied==null||!ColorMath.Equal(m,applied)){foreach(var s in screens)MagSetColorEffect(s.Magnifier,m);applied=m;}
                    foreach(var s in screens){MagSetWindowSource(s.Magnifier,s.Source);InvalidateRect(s.Magnifier,IntPtr.Zero,false);}
                    // About twice a second the mirrors go back on top, with the windows that must stay above them over them.
                    if(++frame%30==0)
                    {
                        foreach(var s in screens)SetWindowPos(s.Host.Handle,HWND_TOPMOST,0,0,0,0,0x0001|0x0002|0x0010);
                        foreach(var h in above)SetWindowPos(h,HWND_TOPMOST,0,0,0,0,0x0001|0x0002|0x0010);
                    }
                    Forms.Application.DoEvents();
                    // once per screen refresh
                    if(DwmFlush()<0)Thread.Sleep(4);
                }
            }
            catch(Exception e){Problem=e.Message;}
            finally
            {
                foreach(var s in screens){try{if(s.Magnifier!=IntPtr.Zero)DestroyWindow(s.Magnifier);s.Host.Close();s.Host.Dispose();}catch(Exception){}}
            }
        }
        internal bool Running{get{return thread.IsAlive&&Problem==null;}}
        public void Dispose(){stop=true;if(Thread.CurrentThread!=thread)thread.Join(1500);}

        // Night Vision as a color matrix (the mirror cannot run the GPU's per-pixel shadow lift): a lift of the dark end, the black level as a floor,
        // and the tint, all straight lines that come close to the shader's curve over most of the range. Row-vector, like ColorMath.
        internal static float[] NightVision(double strength,double black,float[] tint)
        {
            float l=(float)Math.Max(0,Math.Min(1,strength)),f=(float)(.2*Math.Max(0,Math.Min(1,black)));
            float gain=1+.15f*l,offset=.24f*l;
            gain/=1-f;offset=(offset-f)/(1-f);
            var m=ColorMath.Identity();
            for(int c=0;c<3;c++){float t=tint==null?1:tint[c];m[c*5+c]=gain*t;m[20+c]=offset*t;}
            return m;
        }
    }
}
