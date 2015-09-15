using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using TouchScript.Utils.Attributes;

namespace TouchScript.InputSources
{
    [AddComponentMenu("TouchScript/Input Sources/X11 Touch Input")]
    public sealed class X11TouchInput : InputSource
    {
        public Tags Tags = new Tags(Tags.INPUT_TOUCH);

        bool _isInitialized = false;
        TouchHandler _handler;
        Queue<XTouchEvent> _queue = new Queue<XTouchEvent>();
        Dictionary<int, int> _xToInternalId = new Dictionary<int, int>();

        protected override void OnEnable()
        {
            if (Application.platform != RuntimePlatform.LinuxPlayer &&
                Application.platform != RuntimePlatform.LinuxEditor)
            {
                enabled = false;
                return;
            }

            base.OnEnable();

            _queue.Clear();
            _handler = new TouchHandler();
            _isInitialized = true;
        }

        protected override void OnDisable()
        {
            if (_isInitialized)
            {
                _handler.Dispose();
                _handler = null;
            }

            base.OnDisable();
        }

        protected override void Update()
        {
            if (_handler != null) {
                _handler.Pump(_queue);
                while (_queue.Count > 0) {
                    var t = _queue.Dequeue();
                    int existingId;
                    switch (t.Type) {
                        case XTouchEventType.Begin:
                            _xToInternalId.Add(t.Id, beginTouch(new Vector2((float)t.X, Screen.height - (float)t.Y), Tags).Id);
                            break;
                        case XTouchEventType.Update:
                            if (_xToInternalId.TryGetValue(t.Id, out existingId)) {
                                moveTouch(existingId, new Vector2((float)t.X, Screen.height - (float)t.Y));
                            }
                            break;
                        case XTouchEventType.End:
                            if (_xToInternalId.TryGetValue(t.Id, out existingId)) {
                                _xToInternalId.Remove(t.Id);
                                endTouch(existingId);
                            }
                            break;
                    }
                }
            }
            
            base.Update();
        }

        struct XTouchEvent {
            public XTouchEventType Type;
            public int Id;
            public double X;
            public double Y;

            public XTouchEvent (XTouchEventType type, int id, double x, double y) {
                this.Type = type;
                this.Id = id;
                this.X = x;
                this.Y = y;
            }
        }

        enum XTouchEventType {
            Begin,
            Update,
            End
        }

        struct Coords {
            public double X;
            public double Y;

            public Coords (double x, double y) {
                this.X = x;
                this.Y = y;
            }
        }

        class TouchHandler : IDisposable {
            static readonly string DisplayName = Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";

            IntPtr _display, _event;
            int _xinputOpCode;
            Dictionary<int, Coords> _tracked;

            public TouchHandler () {
                // open display
                _display = XOpenDisplay (DisplayName);
                if (_display == IntPtr.Zero)
                    throw new Exception ("XOpenDisplay failed for " + DisplayName);

                int evt, err;
                if (!XQueryExtension (_display, "XInputExtension", out _xinputOpCode, out evt, out err))
                    throw new Exception ("XInputExtension not available");


                // signal interest in events
                XIEventMask eventMask;
                eventMask.deviceid = XIAllDevices;
                eventMask.mask_len = 3;
                eventMask.mask = Marshal.AllocHGlobal (3);
                var mask = new byte[] { 0, 0, 28 }; // XI_TouchBegin | XI_TouchUpdate | XI_TouchEnd
                Marshal.Copy (mask, 0, eventMask.mask, 3);
                var result = XISelectEvents (_display, XDefaultRootWindow (_display), ref eventMask, 1);
                Marshal.FreeHGlobal (eventMask.mask);
                if (result != 0)
                    throw new Exception ("XISelectEvents failed: " + result);

                _event = Marshal.AllocHGlobal (1024);
                _tracked = new Dictionary<int, Coords> ();
            }

            public void Pump (Queue<XTouchEvent> touches) {
                if (_display == IntPtr.Zero)
                    return;

                XFlush (_display);
                while (XEventsQueued (_display, QueuedAlready) > 0) {
                    XNextEvent (_display, _event);
                    var xevt = (XGenericEventCookie)Marshal.PtrToStructure (_event, typeof(XGenericEventCookie));
                    if (xevt.type == GenericEvent && xevt.extension == _xinputOpCode && XGetEventData (_display, ref xevt)) {
                        XIDeviceEvent xdevt;
                        switch (xevt.evtype) {
                            case XI_TouchBegin:
                                xdevt = (XIDeviceEvent)Marshal.PtrToStructure (xevt.data, typeof(XIDeviceEvent));
                                if (!_tracked.ContainsKey(xdevt.detail)) {
                                    _tracked.Add(xdevt.detail, new Coords(xdevt.event_x, xdevt.event_y));
                                    touches.Enqueue (new XTouchEvent (XTouchEventType.Begin, xdevt.detail, xdevt.event_x, xdevt.event_y));
                                }
                                break;
                            case XI_TouchUpdate:
                                xdevt = (XIDeviceEvent)Marshal.PtrToStructure (xevt.data, typeof(XIDeviceEvent));
                                Coords oldPos;
                                if (_tracked.TryGetValue(xdevt.detail, out oldPos) && oldPos.X != xdevt.event_x && oldPos.Y != xdevt.event_y) {
                                    _tracked [xdevt.detail] = new Coords (xdevt.event_x, xdevt.event_y);
                                    touches.Enqueue (new XTouchEvent (XTouchEventType.Update, xdevt.detail, xdevt.event_x, xdevt.event_y));
                                }
                                break;
                            case XI_TouchEnd:
                                xdevt = (XIDeviceEvent)Marshal.PtrToStructure (xevt.data, typeof(XIDeviceEvent));
                                if (_tracked.ContainsKey (xdevt.detail)) {
                                    _tracked.Remove (xdevt.detail);
                                    touches.Enqueue (new XTouchEvent (XTouchEventType.End, xdevt.detail, xdevt.event_x, xdevt.event_y));
                                }
                                break;
                        }
                        XFreeEventData (_display, ref xevt);
                    }
                }
            }

            public void Dispose () {
                if (_display != IntPtr.Zero) {
                    XCloseDisplay (_display);
                    _display = IntPtr.Zero;
                }
            }

            const int XIAllDevices = 0;
            const int XITouchClass = 8;
            const int QueuedAlready = 0;
            const int QueuedAfterReading = 1;
            const int GenericEvent = 35;
            const int XI_TouchBegin = 18;
            const int XI_TouchUpdate = 19;
            const int XI_TouchEnd = 20;

            [StructLayout(LayoutKind.Sequential)]
            struct XIEventMask {
                public int deviceid;
                public int mask_len;
                public IntPtr mask;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct XIDeviceInfo {
                public int deviceid;
                public string name;
                public int use;
                public int attachment;
                public bool enabled;
                public int num_classes;
                public IntPtr classes;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct XIAnyClassInfo {
                public int type;
                public int sourceid;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct XITouchClassInfo {
                public int type;
                public int sourceid;
                public int mode;
                public int num_touches;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct XGenericEventCookie {
                public int type;
                public IntPtr serial;
                public bool send_event;
                public IntPtr display;
                public int extension;
                public int evtype;
                public int cookie;
                public IntPtr data;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct XIDeviceEvent {
                public int type;
                public IntPtr serial;
                public bool send_event;
                public IntPtr display;
                public int extension;
                public int evtype;
                public ulong time;
                public int deviceid;
                public int sourceid;
                public int detail;
                public ulong root;
                public ulong evt;
                public ulong child;
                public double root_x;
                public double root_y;
                public double event_x;
                public double event_y;
                public int flags;
            }

            [DllImport("libX11")]
            static extern IntPtr XOpenDisplay (string display_name);

            [DllImport("libX11")]
            static extern int XCloseDisplay (IntPtr display);

            [DllImport("libX11")]
            static extern uint XDefaultRootWindow (IntPtr display);

            [DllImport("libX11")]
            static extern void XFlush (IntPtr display);

            [DllImport("libX11")]
            static extern int XPending (IntPtr display);

            [DllImport("libX11")]
            static extern int XEventsQueued (IntPtr display, int mode);

            [DllImport("libX11")]
            static extern int XNextEvent (IntPtr display, IntPtr event_return);

            [DllImport("libX11")]
            static extern bool XGetEventData (IntPtr display, ref XGenericEventCookie cookie);

            [DllImport("libX11")]
            static extern bool XFreeEventData (IntPtr display, ref XGenericEventCookie cookie);

            [DllImport("libX11")]
            static extern bool XQueryExtension (IntPtr display, string extension, out int opcode, out int evt, out int err);

            [DllImport("libXi")]
            static extern int XISelectEvents (IntPtr display, uint win, ref XIEventMask masks, int num_masks);

            [DllImport("libXi")]
            static extern IntPtr XIQueryDevice (IntPtr display, int deviceid, out int ndevices);

            [DllImport("libXi")]
            static extern int XIFreeDeviceInfo (IntPtr info);
        }
    }
}
