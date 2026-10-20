using System.Runtime.InteropServices;

namespace AutoClickerMac.Platform;

/// <summary>macOS 시스템 프레임워크 P/Invoke 모음.</summary>
internal static class MacNative
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string LibSystem = "/usr/lib/libSystem.dylib";

    // CGEventType
    public const uint kCGEventLeftMouseDown = 1;
    public const uint kCGEventLeftMouseUp = 2;
    public const uint kCGEventRightMouseDown = 3;
    public const uint kCGEventRightMouseUp = 4;
    public const uint kCGEventKeyDown = 10;
    public const uint kCGEventKeyUp = 11;
    public const uint kCGEventFlagsChanged = 12;
    public const uint kCGEventOtherMouseDown = 25;
    public const uint kCGEventOtherMouseUp = 26;
    public const uint kCGEventTapDisabledByTimeout = 0xFFFFFFFE;
    public const uint kCGEventTapDisabledByUserInput = 0xFFFFFFFF;

    // CGMouseButton
    public const uint kCGMouseButtonLeft = 0;
    public const uint kCGMouseButtonRight = 1;
    public const uint kCGMouseButtonCenter = 2;

    // CGEventField
    public const uint kCGMouseEventClickState = 1;
    public const uint kCGKeyboardEventAutorepeat = 8;
    public const uint kCGKeyboardEventKeycode = 9;

    // CGEventFlags
    public const ulong kCGEventFlagMaskShift = 0x00020000;
    public const ulong kCGEventFlagMaskControl = 0x00040000;
    public const ulong kCGEventFlagMaskAlternate = 0x00080000;
    public const ulong kCGEventFlagMaskCommand = 0x00100000;

    // CGEventTapLocation / Placement / Options
    public const uint kCGHIDEventTap = 0;
    public const uint kCGSessionEventTap = 1;
    public const uint kCGHeadInsertEventTap = 0;
    public const uint kCGEventTapOptionListenOnly = 1;

    public const uint kCFStringEncodingUTF8 = 0x08000100;

    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X;
        public double Y;

        public CGPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr CGEventTapCallBack(IntPtr proxy, uint type, IntPtr cgEvent, IntPtr userInfo);

    // ---------- CoreGraphics: 마우스 이벤트 ----------

    [DllImport(CoreGraphics)]
    public static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(CoreGraphics)]
    public static extern CGPoint CGEventGetLocation(IntPtr cgEvent);

    [DllImport(CoreGraphics)]
    public static extern IntPtr CGEventCreateMouseEvent(IntPtr source, uint mouseType, CGPoint mouseCursorPosition, uint mouseButton);

    [DllImport(CoreGraphics)]
    public static extern void CGEventSetIntegerValueField(IntPtr cgEvent, uint field, long value);

    [DllImport(CoreGraphics)]
    public static extern long CGEventGetIntegerValueField(IntPtr cgEvent, uint field);

    [DllImport(CoreGraphics)]
    public static extern ulong CGEventGetFlags(IntPtr cgEvent);

    [DllImport(CoreGraphics)]
    public static extern void CGEventPost(uint tap, IntPtr cgEvent);

    [DllImport(CoreGraphics)]
    public static extern int CGWarpMouseCursorPosition(CGPoint newCursorPosition);

    // ---------- CoreGraphics: 키보드 감시 (이벤트 탭) ----------

    [DllImport(CoreGraphics)]
    public static extern IntPtr CGEventTapCreate(uint tap, uint place, uint options, ulong eventsOfInterest, CGEventTapCallBack callback, IntPtr userInfo);

    [DllImport(CoreGraphics)]
    public static extern void CGEventTapEnable(IntPtr tap, [MarshalAs(UnmanagedType.I1)] bool enable);

    // ---------- CoreFoundation: 런루프 ----------

    [DllImport(CoreFoundation)]
    public static extern void CFRelease(IntPtr cf);

    [DllImport(CoreFoundation)]
    public static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, long order);

    [DllImport(CoreFoundation)]
    public static extern IntPtr CFRunLoopGetCurrent();

    [DllImport(CoreFoundation)]
    public static extern void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);

    [DllImport(CoreFoundation)]
    public static extern void CFRunLoopRun();

    [DllImport(CoreFoundation)]
    public static extern void CFRunLoopStop(IntPtr runLoop);

    [DllImport(CoreFoundation)]
    public static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string cStr, uint encoding);

    // ---------- 권한 (손쉬운 사용 / 입력 모니터링) ----------

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool AXIsProcessTrusted();

    // 이벤트 주입 권한(손쉬운 사용) 확인/요청. 요청하면 시스템 설정 목록에 앱이 등록된다.
    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CGPreflightPostEventAccess();

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CGRequestPostEventAccess();

    // 키 입력 감시 권한(입력 모니터링) 확인/요청.
    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CGPreflightListenEventAccess();

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CGRequestListenEventAccess();

    // ---------- 정밀 대기 ----------

    [StructLayout(LayoutKind.Sequential)]
    public struct MachTimebaseInfo
    {
        public uint Numer;
        public uint Denom;
    }

    [DllImport(LibSystem)]
    public static extern ulong mach_absolute_time();

    [DllImport(LibSystem)]
    public static extern int mach_timebase_info(out MachTimebaseInfo info);

    [DllImport(LibSystem)]
    public static extern int mach_wait_until(ulong deadline);
}
