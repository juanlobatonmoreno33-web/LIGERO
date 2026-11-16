using System;
using System.Runtime.InteropServices;

namespace Ligero
{
    /// <summary>Llamadas directas a Windows. Sin dependencias externas.</summary>
    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ANIMATIONINFO
        {
            public uint cbSize;
            public int iMinAnimate;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EmptyWorkingSet(IntPtr process);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SpiAnim(uint action, uint param, ref ANIMATIONINFO value, uint winIni);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SpiGetBool(uint action, uint param, ref int value, uint winIni);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SpiSetBool(uint action, uint param, IntPtr value, uint winIni);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam,
            uint flags, uint timeout, out IntPtr result);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MoveFileEx(string existing, string target, uint flags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHQueryRecycleBin(string rootPath, IntPtr info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHEmptyRecycleBin(IntPtr hwnd, string rootPath, uint flags);

        // ---- Aspecto de la ventana (tema oscuro, efecto translúcido de Windows 11) ----

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS { public int Left, Right, Top, Bottom; }

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("dwmapi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        [DllImport("uxtheme.dll")]
        public static extern int BufferedPaintInit();

        [DllImport("uxtheme.dll")]
        public static extern IntPtr BeginBufferedPaint(IntPtr hdc, ref RECT target, int format, IntPtr paintParams, out IntPtr memDc);

        [DllImport("uxtheme.dll")]
        public static extern int EndBufferedPaint(IntPtr buffer, [MarshalAs(UnmanagedType.Bool)] bool update);

        [DllImport("uxtheme.dll")]
        public static extern int GetBufferedPaintBits(IntPtr buffer, out IntPtr bits, out int rowWidth);

        [DllImport("uxtheme.dll")]
        public static extern int BufferedPaintSetAlpha(IntPtr buffer, IntPtr rect, byte alpha);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("uxtheme.dll", EntryPoint = "#135")]
        static extern int SetPreferredAppMode(int mode);

        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        static extern void FlushMenuThemes();

        [DllImport("uxtheme.dll", EntryPoint = "#133")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllowDarkModeForWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool allow);

        /// <summary>Pone las barras de desplazamiento de una lista en claro u oscuro.</summary>
        public static void ThemeScrollBars(IntPtr hwnd, bool dark)
        {
            try { AllowDarkModeForWindow(hwnd, dark); } catch { }
            try { SetWindowTheme(hwnd, dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
            try { SendMessage(hwnd, 0x031A, IntPtr.Zero, IntPtr.Zero); } catch { }   // WM_THEMECHANGED
        }

        /// <summary>
        /// Indica a Windows si los elementos que dibuja él (barras de desplazamiento, menús)
        /// deben ser claros u oscuros en esta aplicación. Si la versión de Windows no lo admite, no pasa nada.
        /// </summary>
        public static void SetAppMode(bool dark)
        {
            try
            {
                SetPreferredAppMode(dark ? 2 : 3);   // 2 = forzar oscuro, 3 = forzar claro
                FlushMenuThemes();
            }
            catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PAINTSTRUCT
        {
            public IntPtr hdc;
            public int fErase;
            public RECT rcPaint;
            public int fRestore;
            public int fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] rgbReserved;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT paint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT paint);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        public static extern void CopyMemory(IntPtr dest, IntPtr src, IntPtr size);

        // ---- Nitidez en pantallas con escala (125 %, 150 %...) y con varios monitores ----

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetProcessDpiAwarenessContext(IntPtr context);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromPoint(POINT point, uint flags);

        [DllImport("shcore.dll")]
        static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

        /// <summary>
        /// Pide a Windows que no estire la ventana como una imagen: la app se dibuja ella misma
        /// a la escala de cada monitor. Debe llamarse antes de crear ninguna ventana.
        /// </summary>
        public static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext((IntPtr)(-4))) return; } catch { }   // por monitor (v2)
            try { if (SetProcessDpiAwarenessContext((IntPtr)(-3))) return; } catch { }   // por monitor
            try { SetProcessDPIAware(); } catch { }                                      // Windows antiguos
        }

        /// <summary>Puntos por pulgada del monitor que contiene ese punto (0 si no se puede saber).</summary>
        public static int DpiAt(int x, int y)
        {
            try
            {
                POINT p = new POINT();
                p.X = x;
                p.Y = y;
                uint dx, dy;
                if (GetDpiForMonitor(MonitorFromPoint(p, 2), 0, out dx, out dy) == 0) return (int)dx;
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Ajusta el marco de la ventana: barra de título clara u oscura, fondo translúcido
        /// (1 = ninguno, 2 = Mica, 3 = acrílico) y cuánto de la zona superior se deja ver a través.
        /// En versiones de Windows que no lo admiten, las llamadas se ignoran sin más.
        /// </summary>
        public static void SetChrome(IntPtr hwnd, bool dark, int backdrop, int glassLeft, int glassTop, int captionColorRef, int textColorRef)
        {
            int t = textColorRef;
            DwmSetWindowAttribute(hwnd, 36, ref t, 4);            // DWMWA_TEXT_COLOR
            int v = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref v, 4);            // DWMWA_USE_IMMERSIVE_DARK_MODE
            int b = backdrop;
            DwmSetWindowAttribute(hwnd, 38, ref b, 4);            // DWMWA_SYSTEMBACKDROP_TYPE
            MARGINS m = new MARGINS();
            m.Left = glassLeft;
            m.Top = glassTop;
            DwmExtendFrameIntoClientArea(hwnd, ref m);
            int c = captionColorRef;
            DwmSetWindowAttribute(hwnd, 35, ref c, 4);            // DWMWA_CAPTION_COLOR
        }

        /// <summary>Avisa a las ventanas abiertas de que cambió un ajuste, sin quedarse colgado.</summary>
        public static void BroadcastSetting(string area)
        {
            IntPtr res;
            SendMessageTimeout((IntPtr)0xffff, 0x001A, IntPtr.Zero, area, 0x0002, 100, out res);
        }

        /// <summary>Tamaño y número de elementos de la papelera de reciclaje (todas las unidades).</summary>
        public static bool QueryRecycleBin(out long size, out long items)
        {
            size = 0;
            items = 0;
            bool x64 = IntPtr.Size == 8;
            int cb = x64 ? 24 : 20;
            IntPtr mem = Marshal.AllocHGlobal(cb);
            try
            {
                for (int i = 0; i < cb; i++) Marshal.WriteByte(mem, i, 0);
                Marshal.WriteInt32(mem, 0, cb);
                if (SHQueryRecycleBin(null, mem) != 0) return false;
                size = Marshal.ReadInt64(mem, x64 ? 8 : 4);
                items = Marshal.ReadInt64(mem, x64 ? 16 : 12);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(mem);
            }
        }
    }
}
