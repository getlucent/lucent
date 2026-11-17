using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;
using SharpDX.DXGI;

namespace Lucent
{
    // The refresh of one screen, as a beat the app's thread can move things to. WPF makes its frames to the beat of the main screen, so on a second
    // screen with another rate (the main one 300 Hz, the second 60 Hz) some refreshes got two new frames and some none, and a gliding line moved in
    // uneven steps. Moving lyrics once at each refresh of the screen they are on gives every refresh exactly one step, as browsers do.
    // A thread waits for the screen's refresh (DXGI) and tells the listeners; it runs only while someone listens.
    internal sealed class ScreenBeat
    {
        static readonly Stopwatch clock=Stopwatch.StartNew();
        // Seconds on the clock the beats are stamped with.
        internal static double Now {get{return clock.Elapsed.TotalSeconds;}}
        static readonly Dictionary<IntPtr,ScreenBeat> beats=new Dictionary<IntPtr,ScreenBeat>();
        internal static ScreenBeat For(IntPtr monitor)
        {
            lock(beats)
            {
                ScreenBeat beat;
                if(!beats.TryGetValue(monitor,out beat))beats[monitor]=beat=new ScreenBeat(monitor);
                return beat;
            }
        }
        readonly IntPtr monitor;
        readonly List<Listener> listeners=new List<Listener>();
        Thread thread;
        // False once the screen's refresh could not be waited for (no such screen); the listeners then fall back to WPF's own frames.
        internal bool Works=true;
        // The time between refreshes, in seconds, measured.
        internal double Period=1/60.0;
        sealed class Listener {public Action<double> Act;public Dispatcher Dispatcher;public int Waiting;}
        ScreenBeat(IntPtr monitor){this.monitor=monitor;}
        internal object Listen(Action<double> act,Dispatcher dispatcher)
        {
            var listener=new Listener {Act=act,Dispatcher=dispatcher};
            lock(listeners)
            {
                listeners.Add(listener);
                if(thread==null){thread=new Thread(Run){IsBackground=true,Name="Lucent screen beat",Priority=ThreadPriority.AboveNormal};thread.Start();}
            }
            return listener;
        }
        internal void Stop(object token){lock(listeners)listeners.Remove(token as Listener);}
        void Run()
        {
            try
            {
                using(var factory=new Factory1())
                {
                    Output output=null;
                    foreach(var adapter in factory.Adapters1)
                    {
                        foreach(var candidate in adapter.Outputs)
                        {
                            if(output==null&&candidate.Description.MonitorHandle==monitor)output=candidate;else candidate.Dispose();
                        }
                        adapter.Dispose();
                    }
                    if(output==null){Works=false;return;}
                    using(output)
                    {
                        double last=0;
                        while(true)
                        {
                            Listener[] now;
                            lock(listeners){if(listeners.Count==0){thread=null;return;}now=listeners.ToArray();}
                            output.WaitForVerticalBlank();
                            double stamp=Now;
                            if(last>0){double gap=stamp-last;if(gap>.0015&&gap<.05)Period=Period*.9+gap*.1;}
                            last=stamp;
                            foreach(var listener in now)
                            {
                                // A listener still busy with the last beat is not sent another (they would pile up while the app is busy).
                                if(Interlocked.CompareExchange(ref listener.Waiting,1,0)!=0)continue;
                                var l=listener;
                                try{l.Dispatcher.BeginInvoke(DispatcherPriority.Send,new Action(()=>{l.Waiting=0;l.Act(stamp);}));}
                                catch(Exception){l.Waiting=0;}
                            }
                        }
                    }
                }
            }
            catch(Exception){Works=false;}
            finally{lock(listeners){if(thread==Thread.CurrentThread)thread=null;}}
        }
    }
}
