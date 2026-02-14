using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SharpKVM
{
    public static class CursorManager
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left; public int top; public int right; public int bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct CGPoint { public double x; public double y; }

        [DllImport("user32.dll")] private static extern bool ClipCursor(ref RECT lpRect);
        [DllImport("user32.dll")] private static extern bool ClipCursor(IntPtr lpRect);
        [DllImport("user32.dll")] private static extern bool SetSystemCursor(IntPtr hcur, uint id);
        [DllImport("user32.dll")] private static extern IntPtr CreateCursor(IntPtr hInst, int xHotSpot, int yHotSpot, int nWidth, int nHeight, byte[] pvANDPlane, byte[] pvXORPlane);
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);
        
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern int CGDisplayHideCursor(uint display);
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern int CGDisplayShowCursor(uint display);
        
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern void CGWarpMouseCursorPosition(CGPoint newCursorPosition);

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern int CGAssociateMouseAndMouseCursorPosition(bool connected);

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern IntPtr CGEventCreateMouseEvent(IntPtr source, uint mouseType, CGPoint mouseCursorPosition, int mouseButton);

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern IntPtr CGEventSourceCreate(int sourceState);

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern void CGEventPost(uint tap, IntPtr ev);

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern void CFRelease(IntPtr obj);

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern void CGEventSetIntegerValueField(IntPtr ev, int field, long value);

        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern void CGEventSetLocation(IntPtr ev, CGPoint pos);

        private static bool _isHidden = false;
        private const uint SPI_SETCURSORS = 0x0057;
        private const uint OCR_NORMAL = 32512;

        public static void LockToRect(Rect bounds)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                RECT r = new RECT { left = (int)bounds.X, top = (int)bounds.Y, right = (int)(bounds.X + 1), bottom = (int)(bounds.Y + 1) };
                ClipCursor(ref r);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                CGWarpMouseCursorPosition(new CGPoint { x = bounds.X, y = bounds.Y });
            }
        }

        public static void SendMacRawClick(double x, double y, int button, bool isDown, int clickCount = 1)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
            try {
                uint type = 0;
                if (button == 0) type = isDown ? 1u : 2u; // Left MouseDown = 1, MouseUp = 2
                else if (button == 1) type = isDown ? 3u : 4u; // Right MouseDown = 3, MouseUp = 4
                else if (button == 2) type = isDown ? 25u : 26u; // Other MouseDown = 25, MouseUp = 26

                if (type == 0) return;

                CGPoint pos = new CGPoint { x = x, y = y };
                // [v7.1] kCGEventSourceStateCombinedSessionState(0) ????IntPtr.Zero ????
                // Zoom ?怨밴묶?癒?퐣 ?ル슦紐닷첎? ?????袁⑷맒??獄쎻뫗???띾┛ ?袁る맙
                IntPtr mouseEvent = CGEventCreateMouseEvent(IntPtr.Zero, type, pos, button);
                if (mouseEvent != IntPtr.Zero) {
                    CGEventSetIntegerValueField(mouseEvent, 1, clickCount);

                    CGEventPost(0, mouseEvent); 
                    CFRelease(mouseEvent);
                }
            } catch {}
        }

        // [v6.7] 筌???猷?筌왖?癒?뱽 ?袁る립 ??쇱뵠?怨뺥닏 筌롫뗄苑???곕떽? (Zoom ?癒곕늄 ?袁⑷맒 ??욧퍙)
        public static void SendMacRawMove(double x, double y)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
            try {
                CGPoint pos = new CGPoint { x = x, y = y };
                IntPtr mouseEvent = CGEventCreateMouseEvent(IntPtr.Zero, 5u, pos, 0);
                if (mouseEvent != IntPtr.Zero) {
                    CGEventPost(0, mouseEvent);
                    CFRelease(mouseEvent);
                }
            } catch {}
        }

        // [v6.4] 筌???뺤삋域?筌왖?癒?뱽 ?袁る립 ??쇱뵠?怨뺥닏 筌롫뗄苑???곕떽?
        public static void SendMacRawDrag(double x, double y, int button)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
            try {
                uint type = 0;
                if (button == 0) type = 6u; // Left MouseDragged = 6
                else if (button == 1) type = 7u; // Right MouseDragged = 7
                else if (button == 2) type = 27u; // Other MouseDragged = 27

                if (type == 0) return;

                CGPoint pos = new CGPoint { x = x, y = y };
                IntPtr mouseEvent = CGEventCreateMouseEvent(IntPtr.Zero, type, pos, button);
                if (mouseEvent != IntPtr.Zero) {
                    CGEventPost(0, mouseEvent);
                    CFRelease(mouseEvent);
                }
            } catch {}
        }

        public static void Unlock()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ClipCursor(IntPtr.Zero);
            }
        }

        public static void Hide()
        {
            try {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    if (_isHidden) return;
                    byte[] andPlane = new byte[128]; byte[] xorPlane = new byte[128];
                    for(int i=0; i<128; i++) andPlane[i] = 0xFF; 
                    IntPtr transparentCursor = CreateCursor(IntPtr.Zero, 0, 0, 32, 32, andPlane, xorPlane);
                    SetSystemCursor(transparentCursor, OCR_NORMAL);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                    CGDisplayHideCursor(0);
                }
            } catch {}
            _isHidden = true;
        }

        public static void Show()
        {
            try {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                    CGAssociateMouseAndMouseCursorPosition(true);
                    for(int i=0; i<5; i++) CGDisplayShowCursor(0);
                }
            } catch {}
            _isHidden = false;
        }
    }

}
