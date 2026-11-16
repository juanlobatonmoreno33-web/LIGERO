using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Ligero
{
    public static class Program
    {
        /// <summary>Lo rellena el lanzador de PowerShell para poder reabrirse como administrador.</summary>
        public static string ScriptPath;
        /// <summary>Arrancado con Windows: sin mostrar la ventana, con icono junto al reloj.</summary>
        public static bool Background;
        /// <summary>true cuando se ejecuta como Ligero.exe (no mediante el lanzador ni en pruebas).</summary>
        public static bool RealExe;
        public static EventWaitHandle ShowSignal;
        const string SignalName = "Ligero-Mostrar-Ventana";

        [STAThread]
        public static void Main()
        {
            using (Mutex m = new Mutex(false, "Ligero-Optimizador-Instancia"))
            {
                bool mine = false;
                try { mine = m.WaitOne(0); }
                catch (AbandonedMutexException) { mine = true; }
                if (!mine)
                {
                    // Ya hay un Ligero abierto (quizá oculto junto al reloj): se le pide que se muestre.
                    try { using (EventWaitHandle other = EventWaitHandle.OpenExisting(SignalName)) other.Set(); } catch { }
                    // Si en realidad se estaba cerrando para reabrirse como administrador, se espera a que termine.
                    try { mine = m.WaitOne(6000); }
                    catch (AbandonedMutexException) { mine = true; }
                    if (!mine) return;
                }
                try
                {
                    Native.EnableDpiAwareness();
                    RealExe = ScriptPath == null;
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "--segundo-plano") >= 0) Background = true;
                    try { ShowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName); } catch { }
                    Application.EnableVisualStyles();
                    try { Application.SetCompatibleTextRenderingDefault(false); } catch { }
                    Application.Run(new MainForm());
                }
                finally { m.ReleaseMutex(); }
            }
        }

        public static bool RestartAsAdmin()
        {
            ProcessStartInfo psi = ScriptPath != null
                ? new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ScriptPath + "\"")
                : new ProcessStartInfo(Application.ExecutablePath);
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            try { Process.Start(psi); return true; }
            catch { return false; }   // el usuario dijo que no en el aviso de Windows
        }
    }

    // ==================================================================
    //  Preferencias de aspecto (se guardan en aspecto.txt)
    // ==================================================================
    public static class Prefs
    {
        public static string Theme = "sistema";     // sistema | claro | oscuro
        public static string Accent = "azul";
        public static int CustomColor = 0x1E88E5;   // color de acento elegido a mano (RRGGBB)
        public static string Backdrop = "mica";     // ninguno | mica | acrilico
        public static bool Animations = true;
        public static bool BigText;
        public static bool SmartBoost;
        public static int Corners = 1;              // 0 rectas, 1 suaves, 2 redondas
        public static bool Compact;                 // barra lateral solo con iconos
        public static int StartPage;                // 0 Inicio, 1 Chequeo, 2 Memoria, 3 Disco
        public static bool AutoStart;               // arrancar con Windows en segundo plano
        public static bool Welcomed;                // ya se hizo el reconocimiento inicial
        public static string Tier = "";             // baja | media | alta
        public static string Specs = "";

        static string FilePath { get { return Path.Combine(Util.AppDir, "aspecto.txt"); } }

        /// <summary>Vuelve al aspecto de fábrica (no toca el arranque con Windows ni el reconocimiento).</summary>
        public static void Reset()
        {
            Theme = "sistema"; Accent = "azul"; Backdrop = "mica"; Animations = true; BigText = false;
            Corners = 1; Compact = false; StartPage = 0;
        }

        /// <summary>Ajusta la propia aplicación a la gama del equipo.</summary>
        public static void ApplyTier(string tier)
        {
            Tier = tier;
            if (tier == "alta") { Animations = true; Backdrop = "acrilico"; SmartBoost = false; }
            else if (tier == "baja") { Animations = false; Backdrop = "ninguno"; SmartBoost = true; }
            else { Animations = true; Backdrop = "ninguno"; SmartBoost = true; }
        }

        static bool Yes(string v) { return v == "si"; }
        static string YesNo(bool b) { return b ? "si" : "no"; }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    int n;
                    if (k == "tema" && (v == "sistema" || v == "claro" || v == "oscuro")) Theme = v;
                    else if (k == "acento" && Array.IndexOf(Ui.AccentNames, v) >= 0) Accent = v;
                    else if (k == "color" && int.TryParse(v, System.Globalization.NumberStyles.HexNumber, null, out n)) CustomColor = n & 0xFFFFFF;
                    else if (k == "fondo" && (v == "ninguno" || v == "mica" || v == "acrilico")) Backdrop = v;
                    else if (k == "animaciones") Animations = Yes(v);
                    else if (k == "textogrande") BigText = Yes(v);
                    else if (k == "inteligente") SmartBoost = Yes(v);
                    else if (k == "esquinas" && int.TryParse(v, out n)) Corners = Math.Max(0, Math.Min(2, n));
                    else if (k == "compacta") Compact = Yes(v);
                    else if (k == "paginainicial" && int.TryParse(v, out n)) StartPage = Math.Max(0, Math.Min(3, n));
                    else if (k == "arranque") AutoStart = Yes(v);
                    else if (k == "bienvenida") Welcomed = v == "hecha";
                    else if (k == "gama" && (v == "baja" || v == "media" || v == "alta")) Tier = v;
                    else if (k == "equipo") Specs = v;
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllLines(FilePath, new string[] {
                    "tema=" + Theme, "acento=" + Accent, "color=" + CustomColor.ToString("X6"), "fondo=" + Backdrop,
                    "animaciones=" + YesNo(Animations), "textogrande=" + YesNo(BigText), "inteligente=" + YesNo(SmartBoost),
                    "esquinas=" + Corners, "compacta=" + YesNo(Compact), "paginainicial=" + StartPage,
                    "arranque=" + YesNo(AutoStart), "bienvenida=" + (Welcomed ? "hecha" : "pendiente"),
                    "gama=" + Tier, "equipo=" + Specs });
            }
            catch { }
        }
    }

    /// <summary>Arranque de Ligero con Windows (entrada normal de inicio del usuario, sin permisos especiales).</summary>
    public static class AutoStart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Ligero";

        /// <summary>La orden que Windows ejecutará al iniciar sesión: abre Ligero oculto en la bandeja.</summary>
        public static string Command()
        {
            if (Program.ScriptPath != null)
                return "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + Program.ScriptPath + "\" -SegundoPlano";
            return "\"" + Application.ExecutablePath + "\" --segundo-plano";
        }

        public static void Set(bool on)
        {
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key))
            {
                if (on) k.SetValue(Name, Command());
                else k.DeleteValue(Name, false);
            }
        }

        public static string Current()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key))
                    return k == null ? null : k.GetValue(Name) as string;
            }
            catch { return null; }
        }
    }

    // ==================================================================
    //  Paleta y utilidades de dibujo compartidas por todos los controles
    // ==================================================================
    public static class Ui
    {
        public static Color Back, Text, Soft, Accent, Good, Warn, Amber, Bad, Panel, Button, ButtonHot, Border,
            Info, Primary, PrimaryHot, PrimaryText, RowHot, RowSel, Track;
        public static bool Dark, Anim = true, Glass;
        /// <summary>Cuanto tapa el color de la cabecera al fondo translucido (0 = nada, 255 = del todo).</summary>
        public static int GlassTint;
        public static float Scale = 1f;
        public static Font BoldFont, BigFont, IconFont;

        public static readonly string[] AccentNames = { "azul", "turquesa", "verde", "morado", "rosa", "naranja", "windows", "personal" };

        public static int P(int v) { return (int)Math.Round(v * Scale); }

        /// <summary>Cuánto se redondean las esquinas (según la preferencia: rectas, suaves o redondas).</summary>
        public static float Corner = 1f;
        public static float R(int v) { return v * Scale * Corner; }

        public static Color Mix(Color a, Color b, float t)
        {
            if (t < 0) t = 0; else if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Color AccentBase(string name)
        {
            switch (name)
            {
                case "turquesa": return Color.FromArgb(0, 135, 140);
                case "verde": return Color.FromArgb(22, 135, 60);
                case "morado": return Color.FromArgb(120, 80, 205);
                case "rosa": return Color.FromArgb(200, 55, 115);
                case "naranja": return Color.FromArgb(205, 95, 10);
                case "personal": return Color.FromArgb((Prefs.CustomColor >> 16) & 0xFF, (Prefs.CustomColor >> 8) & 0xFF, Prefs.CustomColor & 0xFF);
                case "windows":
                    try
                    {
                        object v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null);
                        if (v is int)
                        {
                            int c = (int)v;
                            return Color.FromArgb(c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF);
                        }
                    }
                    catch { }
                    return Color.FromArgb(0, 95, 184);
                default: return Color.FromArgb(0, 95, 184);
            }
        }

        /// <summary>¿Windows está en modo oscuro para las aplicaciones?</summary>
        public static bool SystemDark()
        {
            try
            {
                object v = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", null);
                if (v is int) return (int)v == 0;
            }
            catch { }
            return SystemColors.Window.GetBrightness() < 0.5f;
        }

        public static bool IsWin11
        {
            get
            {
                try
                {
                    object v = Microsoft.Win32.Registry.GetValue(
                        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber", null);
                    int build;
                    return int.TryParse(Convert.ToString(v), out build) && build >= 22000;
                }
                catch { return false; }
            }
        }

        /// <summary>Calcula todos los colores a partir de las preferencias.</summary>
        public static void Load()
        {
            Dark = Prefs.Theme == "oscuro" || (Prefs.Theme == "sistema" && SystemDark());
            Color a = AccentBase(Prefs.Accent);
            Primary = a;
            PrimaryHot = Mix(a, Color.White, 0.16f);
            PrimaryText = a.GetBrightness() > 0.62f ? Color.FromArgb(20, 20, 20) : Color.White;
            if (Dark)
            {
                Back = Color.FromArgb(30, 30, 30); Text = Color.FromArgb(242, 242, 242); Soft = Color.FromArgb(165, 165, 165);
                Accent = Mix(a, Color.White, 0.48f);
                Good = Color.FromArgb(108, 203, 95); Warn = Color.FromArgb(255, 160, 100);
                Amber = Color.FromArgb(255, 200, 90); Bad = Color.FromArgb(255, 125, 115);
                Panel = Color.FromArgb(38, 38, 38); Button = Color.FromArgb(52, 52, 52); ButtonHot = Color.FromArgb(70, 70, 70);
                Border = Color.FromArgb(62, 62, 62); Info = Mix(Back, a, 0.13f);
                RowHot = Color.FromArgb(42, 42, 42); RowSel = Mix(Back, a, 0.30f); Track = Color.FromArgb(58, 58, 58);
            }
            else
            {
                Back = Color.FromArgb(252, 252, 252); Text = Color.FromArgb(26, 26, 26); Soft = Color.FromArgb(98, 98, 98);
                Accent = Mix(a, Color.Black, 0.12f);
                Good = Color.FromArgb(16, 124, 16); Warn = Color.FromArgb(170, 60, 0);
                Amber = Color.FromArgb(150, 95, 0); Bad = Color.FromArgb(190, 30, 30);
                Panel = Color.FromArgb(243, 243, 243); Button = Color.FromArgb(240, 240, 240); ButtonHot = Color.FromArgb(226, 226, 226);
                Border = Color.FromArgb(218, 218, 218); Info = Mix(Color.White, a, 0.07f);
                RowHot = Color.FromArgb(244, 244, 244); RowSel = Mix(Color.White, a, 0.17f); Track = Color.FromArgb(224, 224, 224);
            }
            Anim = Prefs.Animations;
            Corner = Prefs.Corners == 0 ? 0.2f : (Prefs.Corners == 2 ? 1.9f : 1f);
            Glass = Prefs.Backdrop != "ninguno" && IsWin11;
            // En oscuro el fondo de Windows ya es oscuro y se deja ver entero; en claro se tinta para que el texto se lea.
            GlassTint = Dark ? (Prefs.Backdrop == "acrilico" ? 70 : 0) : 205;
        }

        public static GraphicsPath Round(float x, float y, float w, float h, float r)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(r * 2, Math.Min(w, h));
            if (d <= 0.5f) { p.AddRectangle(new RectangleF(x, y, w, h)); return p; }
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, float x, float y, float w, float h, float r)
        {
            using (GraphicsPath p = Round(x, y, w, h, r))
            using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Color c, float x, float y, float w, float h, float r)
        {
            using (GraphicsPath p = Round(x, y, w, h, r))
            using (Pen pen = new Pen(c)) g.DrawPath(pen, p);
        }

        public static Color ParentBack(Control c)
        {
            return c.Parent != null ? c.Parent.BackColor : Back;
        }

        static bool bufferedInit;

        /// <summary>
        /// Lleva a la ventana un dibujo con zonas transparentes conservando la transparencia,
        /// para que a través de ellas se vea el efecto translúcido de Windows.
        /// </summary>
        public static void BlitAlpha(Graphics target, Bitmap bmp)
        {
            bool done = false;
            IntPtr hdc = target.GetHdc();
            try
            {
                if (!bufferedInit) { Native.BufferedPaintInit(); bufferedInit = true; }
                Native.RECT rc = new Native.RECT();
                rc.Right = bmp.Width;
                rc.Bottom = bmp.Height;
                IntPtr mem;
                IntPtr buffer = Native.BeginBufferedPaint(hdc, ref rc, 2, IntPtr.Zero, out mem);
                if (buffer != IntPtr.Zero)
                {
                    IntPtr bits;
                    int row;
                    if (Native.GetBufferedPaintBits(buffer, out bits, out row) == 0 && bits != IntPtr.Zero)
                    {
                        BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                        try
                        {
                            for (int y = 0; y < bmp.Height; y++)
                                Native.CopyMemory(new IntPtr(bits.ToInt64() + (long)y * row * 4),
                                    new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), new IntPtr(bmp.Width * 4));
                        }
                        finally { bmp.UnlockBits(d); }
                        done = true;
                    }
                    Native.EndBufferedPaint(buffer, done);
                }
            }
            catch { }
            finally { target.ReleaseHdc(hdc); }
            if (!done) target.DrawImageUnscaled(bmp, 0, 0);
        }
    }

    // ==================================================================
    //  Animador: un único temporizador que solo corre mientras algo se mueve
    // ==================================================================
    public static class Anim
    {
        class Tween
        {
            public object Key;
            public int Ms;
            public long Start;
            public Action<float> Step;
            public Action Done;
        }

        static readonly List<Tween> items = new List<Tween>();
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static System.Windows.Forms.Timer timer;

        public static void Run(object key, int ms, Action<float> step) { Run(key, ms, step, null); }

        /// <summary>Lleva un valor de 0 a 1 en "ms" milisegundos, frenando al final. Sustituye a la animación anterior de la misma clave.</summary>
        public static void Run(object key, int ms, Action<float> step, Action done)
        {
            for (int i = items.Count - 1; i >= 0; i--)
                if (ReferenceEquals(items[i].Key, key)) items.RemoveAt(i);
            if (!Ui.Anim || ms <= 0)
            {
                step(1f);
                if (done != null) done();
                return;
            }
            Tween t = new Tween();
            t.Key = key; t.Ms = ms; t.Start = clock.ElapsedMilliseconds; t.Step = step; t.Done = done;
            items.Add(t);
            if (timer == null)
            {
                timer = new System.Windows.Forms.Timer();
                timer.Interval = 15;
                timer.Tick += Tick;
            }
            timer.Start();
        }

        static void Tick(object sender, EventArgs e)
        {
            long now = clock.ElapsedMilliseconds;
            foreach (Tween t in items.ToArray())
            {
                if (!items.Contains(t)) continue;
                float x = Math.Min(1f, (now - t.Start) / (float)t.Ms);
                float eased = 1f - (1f - x) * (1f - x) * (1f - x);
                try { t.Step(eased); } catch { x = 1f; }
                if (x >= 1f)
                {
                    items.Remove(t);
                    if (t.Done != null) { try { t.Done(); } catch { } }
                }
            }
            if (items.Count == 0 && timer != null) timer.Stop();
        }

        public static void Clear() { items.Clear(); }
    }

    // ==================================================================
    //  Controles propios
    // ==================================================================

    public delegate void WndProcRef(ref Message m);

    /// <summary>
    /// Con el efecto translúcido de Windows activo, lo que se pinta con el método clásico queda
    /// semitransparente. Esto hace que un control se pinte en un búfer y se marque como opaco,
    /// de modo que solo la barra de navegación deje ver el fondo.
    /// </summary>
    public static class Opaque
    {
        public static bool Paint(IntPtr hwnd, ref Message m, WndProcRef baseProc)
        {
            return Paint(hwnd, ref m, baseProc, null);
        }

        /// <summary>Igual, con un retoque final ("after") que se dibuja en el mismo bufer antes de marcarlo opaco.</summary>
        public static bool Paint(IntPtr hwnd, ref Message m, WndProcRef baseProc, Action<IntPtr> after)
        {
            if (m.Msg != 0x000F || !Ui.Glass || m.WParam != IntPtr.Zero) return false;
            Native.PAINTSTRUCT ps;
            IntPtr hdc = Native.BeginPaint(hwnd, out ps);
            try
            {
                Native.RECT rc;
                Native.GetClientRect(hwnd, out rc);
                IntPtr mem = IntPtr.Zero;
                IntPtr buffer = rc.Right > 0 && rc.Bottom > 0
                    ? Native.BeginBufferedPaint(hdc, ref rc, 2, IntPtr.Zero, out mem) : IntPtr.Zero;
                if (buffer == IntPtr.Zero) mem = hdc;
                Message pm = Message.Create(hwnd, 0x000F, mem, IntPtr.Zero);
                try
                {
                    baseProc(ref pm);
                    if (after != null) after(mem);
                }
                finally
                {
                    if (buffer != IntPtr.Zero)
                    {
                        Native.BufferedPaintSetAlpha(buffer, IntPtr.Zero, 255);
                        Native.EndBufferedPaint(buffer, true);
                    }
                }
            }
            finally { Native.EndPaint(hwnd, ref ps); }
            m.Result = IntPtr.Zero;
            return true;
        }
    }

    /// <summary>Página con doble búfer (sin parpadeo al redibujar).</summary>
    public class Page : Panel
    {
        public Page() { DoubleBuffered = true; }

        protected override void WndProc(ref Message m)
        {
            if (Opaque.Paint(Handle, ref m, base.WndProc)) return;
            base.WndProc(ref m);
        }
    }

    /// <summary>Texto simple.</summary>
    public class PlainLabel : Label
    {
        public PlainLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void WndProc(ref Message m)
        {
            if (Opaque.Paint(Handle, ref m, base.WndProc)) return;
            base.WndProc(ref m);
        }
    }

    /// <summary>Botón redondeado con transición de color al pasar el ratón.</summary>
    public class RoundButton : Button
    {
        protected override void WndProc(ref Message m)
        {
            if (Opaque.Paint(Handle, ref m, base.WndProc)) return;
            base.WndProc(ref m);
        }

        /// <summary>0 = normal, 1 = acción principal.</summary>
        public int Kind;
        float hot;
        bool down;

        public RoundButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
        }

        void Fade(float to)
        {
            float from = hot;
            Anim.Run(this, 150, delegate(float t) { hot = from + (to - from) * t; Invalidate(); });
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Fade(1f); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); down = false; Fade(0f); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); down = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color back = Ui.ParentBack(this);
            g.Clear(back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill, text, border;
            if (Kind == 1)
            {
                fill = Ui.Mix(Ui.Primary, Ui.PrimaryHot, hot);
                if (down) fill = Ui.Mix(fill, Color.Black, 0.18f);
                text = Ui.PrimaryText;
                border = fill;
            }
            else
            {
                fill = Ui.Mix(Ui.Button, Ui.ButtonHot, hot);
                if (down) fill = Ui.Mix(fill, Ui.Text, 0.10f);
                text = Ui.Text;
                border = Ui.Mix(Ui.Border, Ui.Soft, hot * 0.5f);
            }
            if (!Enabled)
            {
                fill = Ui.Mix(fill, back, 0.65f);
                text = Ui.Mix(text, back, 0.55f);
                border = Ui.Mix(border, back, 0.65f);
            }
            float r = Ui.R(7);
            Ui.FillRound(g, fill, 0.5f, 0.5f, Width - 1.5f, Height - 1.5f, r);
            Ui.DrawRound(g, border, 0.5f, 0.5f, Width - 1.5f, Height - 1.5f, r);
            if (Focused && ShowFocusCues)
                Ui.DrawRound(g, Ui.Accent, 2.5f, 2.5f, Width - 5.5f, Height - 5.5f, Math.Max(0f, r - 2));
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text, fill,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Recuadro de explicación con esquinas redondeadas.</summary>
    public class CardLabel : Label
    {
        protected override void WndProc(ref Message m)
        {
            if (Opaque.Paint(Handle, ref m, base.WndProc)) return;
            base.WndProc(ref m);
        }

        public CardLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Ui.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Ui.FillRound(g, BackColor, 0, 0, Width - 1, Height - 1, Ui.R(9));
            Rectangle r = new Rectangle(Padding.Left, Padding.Top, Width - Padding.Horizontal, Height - Padding.Vertical);
            string text = Text;
            int nl = text.IndexOf('\n');
            if (nl > 0 && Ui.BoldFont != null)
            {
                // Si el texto trae un titular en la primera línea, se destaca.
                string head = text.Substring(0, nl);
                int hh = TextRenderer.MeasureText(head, Ui.BoldFont).Height;
                TextRenderer.DrawText(g, head, Ui.BoldFont, new Rectangle(r.X, r.Y, r.Width, hh), ForeColor, BackColor,
                    TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                r = new Rectangle(r.X, r.Y + hh + Ui.P(2), r.Width, Math.Max(0, r.Height - hh - Ui.P(2)));
                text = text.Substring(nl + 1);
            }
            TextRenderer.DrawText(g, text, Font, r, ForeColor, BackColor,
                TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Barra de nivel que se desplaza con suavidad hasta su valor y cambia de color según la carga.</summary>
    public class Meter : Control
    {
        protected override void WndProc(ref Message m)
        {
            if (Opaque.Paint(Handle, ref m, base.WndProc)) return;
            base.WndProc(ref m);
        }

        int value;
        float shown;

        public Meter()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(0, Math.Min(100, value));
                if (v == this.value) return;
                this.value = v;
                float from = shown;
                Anim.Run(this, 500, delegate(float t) { shown = from + (v - from) * t; Invalidate(); });
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Ui.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float h = Height - 1, r = h / 2f;
            Ui.FillRound(g, Ui.Track, 0, 0, Width - 1, h, r);
            float w = (Width - 1) * shown / 100f;
            if (w < h) return;
            Color c = shown < 78 ? Ui.Primary : (shown < 90 ? Ui.Amber : Ui.Bad);
            Ui.FillRound(g, c, 0, 0, w, h, r);
        }
    }

    /// <summary>Interruptor deslizante con texto.</summary>
    public class Toggle : Control
    {
        protected override void WndProc(ref Message m)
        {
            if (Opaque.Paint(Handle, ref m, base.WndProc)) return;
            base.WndProc(ref m);
        }

        bool on;
        float pos, hot;
        readonly object hotKey = new object();
        public event EventHandler CheckedChanged;

        public Toggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return on; }
            set
            {
                if (value == on) return;
                on = value;
                float from = pos, to = on ? 1f : 0f;
                Anim.Run(this, 170, delegate(float t) { pos = from + (to - from) * t; Invalidate(); });
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>Cambia el estado sin animación ni aviso (para mostrar el valor guardado).</summary>
        public void SetSilently(bool value)
        {
            on = value;
            pos = value ? 1f : 0f;
            Invalidate();
        }

        void Fade(float to)
        {
            float from = hot;
            Anim.Run(hotKey, 140, delegate(float t) { hot = from + (to - from) * t; Invalidate(); });
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Fade(1f); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Fade(0f); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); Checked = !Checked; }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space) Checked = !Checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color back = Ui.ParentBack(this);
            g.Clear(back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = Ui.P(40), h = Ui.P(20), y = (Height - h) / 2f;
            Color track = Ui.Mix(Ui.Mix(Ui.Track, Ui.Soft, hot * 0.35f), Ui.Mix(Ui.Primary, Ui.PrimaryHot, hot), pos);
            Ui.FillRound(g, track, 0.5f, y, w, h, h / 2f);
            float knob = h - Ui.P(6) + hot * Ui.P(2);
            float kx = 0.5f + (h - knob) / 2f + pos * (w - h);
            using (SolidBrush b = new SolidBrush(pos > 0.5f ? Ui.PrimaryText : (Ui.Dark ? Color.FromArgb(230, 230, 230) : Color.White)))
                g.FillEllipse(b, kx, y + (h - knob) / 2f, knob, knob);
            Rectangle tr = new Rectangle((int)w + Ui.P(10), 0, Width - (int)w - Ui.P(10), Height);
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? Ui.Text : Ui.Soft, back,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Selector de una opción entre varias, con la marca deslizándose de una a otra.</summary>
    public class Segmented : Control
    {
        protected override void WndProc(ref Message m)
        {
            if (Opaque.Paint(Handle, ref m, base.WndProc)) return;
            base.WndProc(ref m);
        }

        string[] items = new string[0];
        int sel, hot = -1;
        float pill;
        public event EventHandler Changed;

        public Segmented()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public string[] Items { set { items = value ?? new string[0]; Invalidate(); } }

        public int SelectedIndex
        {
            get { return sel; }
            set
            {
                if (value == sel || value < 0 || value >= items.Length) return;
                sel = value;
                float from = pill, to = sel;
                Anim.Run(this, 200, delegate(float t) { pill = from + (to - from) * t; Invalidate(); }, delegate
                {
                    if (Changed != null) Changed(this, EventArgs.Empty);
                });
            }
        }

        public void SetSilently(int index)
        {
            sel = Math.Max(0, Math.Min(items.Length - 1, index));
            pill = sel;
            Invalidate();
        }

        int Hit(int x)
        {
            if (items.Length == 0 || Width <= 0) return -1;
            int i = x * items.Length / Width;
            return i < 0 || i >= items.Length ? -1 : i;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = Hit(e.X);
            if (h != hot) { hot = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = -1; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) SelectedIndex = Hit(e.X);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Ui.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (items.Length == 0) return;
            float r = Ui.R(8), pad = Ui.P(3);
            Ui.FillRound(g, Ui.Button, 0, 0, Width - 1, Height - 1, r);
            float iw = (Width - 1 - 2 * pad) / items.Length;
            Ui.FillRound(g, Ui.Primary, pad + pill * iw, pad, iw, Height - 1 - 2 * pad, Math.Max(0f, r - 2));
            for (int i = 0; i < items.Length; i++)
            {
                float near = 1f - Math.Min(1f, Math.Abs(pill - i));
                Color c = Ui.Mix(i == hot ? Ui.Text : Ui.Soft, Ui.PrimaryText, near);
                Rectangle tr = new Rectangle((int)(pad + i * iw), 0, (int)iw, Height);
                TextRenderer.DrawText(g, items[i], Font, tr, c,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>Fila de colores para elegir el color de acento.</summary>
    public class Swatches : Control
    {
        protected override void WndProc(ref Message m)
        {
            if (Opaque.Paint(Handle, ref m, base.WndProc)) return;
            base.WndProc(ref m);
        }

        Color[] colors = new Color[0];
        float[] grow = new float[0];
        object[] keys = new object[0];
        int sel, hot = -1;
        public event EventHandler Changed;

        public Swatches()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public Color[] Colors
        {
            set
            {
                colors = value ?? new Color[0];
                grow = new float[colors.Length];
                keys = new object[colors.Length];
                for (int i = 0; i < keys.Length; i++) keys[i] = new object();
                Invalidate();
            }
        }

        public int SelectedIndex { get { return sel; } }

        public void SetSilently(int index) { sel = index; Invalidate(); }

        int Step { get { return Ui.P(40); } }

        int Hit(int x)
        {
            int i = x / Step;
            return i < 0 || i >= colors.Length ? -1 : i;
        }

        void Fade(int i, float to)
        {
            if (i < 0 || i >= grow.Length) return;
            float from = grow[i];
            Anim.Run(keys[i], 150, delegate(float t) { grow[i] = from + (to - from) * t; Invalidate(); });
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = Hit(e.X);
            if (h == hot) return;
            Fade(hot, 0f);
            hot = h;
            Fade(hot, 1f);
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Fade(hot, 0f); hot = -1; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int h = Hit(e.X);
            if (e.Button != MouseButtons.Left || h < 0 || h == sel) return;
            sel = h;
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Ui.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float baseD = Ui.P(24);
            for (int i = 0; i < colors.Length; i++)
            {
                float d = baseD + grow[i] * Ui.P(5);
                float cx = i * Step + Step / 2f, cy = Height / 2f;
                using (SolidBrush b = new SolidBrush(colors[i])) g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
                if (i == sel)
                {
                    float ring = d + Ui.P(8);
                    using (Pen p = new Pen(Ui.Text, Math.Max(1.6f, Ui.Scale * 1.8f)))
                        g.DrawEllipse(p, cx - ring / 2, cy - ring / 2, ring, ring);
                }
            }
        }
    }

    /// <summary>Línea fina que se mueve mientras Ligero está trabajando.</summary>
    public class BusyLine : Control
    {
        protected override void WndProc(ref Message m)
        {
            if (Opaque.Paint(Handle, ref m, base.WndProc)) return;
            base.WndProc(ref m);
        }

        bool active;
        float phase;
        System.Windows.Forms.Timer timer;

        public BusyLine()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public bool Active
        {
            set
            {
                active = value;
                if (value && Ui.Anim)
                {
                    if (timer == null)
                    {
                        timer = new System.Windows.Forms.Timer();
                        timer.Interval = 25;
                        timer.Tick += delegate { phase += 0.022f; if (phase > 1f) phase -= 1f; Invalidate(); };
                    }
                    timer.Start();
                }
                else if (timer != null) timer.Stop();
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Ui.Back);
            if (!active) return;
            using (SolidBrush b = new SolidBrush(Ui.Accent))
            {
                if (!Ui.Anim) { e.Graphics.FillRectangle(b, ClientRectangle); return; }
                float seg = Width * 0.28f;
                float x = -seg + phase * (Width + seg);
                e.Graphics.FillRectangle(b, x, 0, seg, Height);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && timer != null) timer.Dispose();
            base.Dispose(disposing);
        }
    }


    /// <summary>Indicador circular que se rellena con suavidad hasta su valor.</summary>
    public class Ring : Control
    {
        int value;
        float shown;
        /// <summary>true = es una nota (cuanto más alta, mejor); false = es una carga (cuanto más alta, peor).</summary>
        public bool Score;
        public string Caption = "";

        public Ring()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void WndProc(ref Message m)
        {
            if (Opaque.Paint(Handle, ref m, base.WndProc)) return;
            base.WndProc(ref m);
        }

        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(0, Math.Min(100, value));
                if (v == this.value) return;
                this.value = v;
                float from = shown;
                Anim.Run(this, 800, delegate(float t) { shown = from + (v - from) * t; Invalidate(); });
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color back = Ui.ParentBack(this);
            g.Clear(back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float thick = Math.Max(6f, Math.Min(Width, Height) * 0.085f);
            float d = Math.Min(Width, Height) - thick - 2;
            float x = (Width - d) / 2f, y = (Height - d) / 2f;
            Color c = Score
                ? (shown >= 80 ? Ui.Good : (shown >= 55 ? Ui.Amber : Ui.Bad))
                : (shown < 78 ? Ui.Primary : (shown < 90 ? Ui.Amber : Ui.Bad));
            using (Pen track = new Pen(Ui.Track, thick)) g.DrawEllipse(track, x, y, d, d);
            if (shown > 0.5f)
                using (Pen arc = new Pen(c, thick))
                {
                    arc.StartCap = LineCap.Round;
                    arc.EndCap = LineCap.Round;
                    g.DrawArc(arc, x, y, d, d, -90, 360f * shown / 100f);
                }
            string number = ((int)Math.Round(shown)).ToString() + (Score ? "" : " %");
            Font big = Ui.BigFont ?? Font;
            int nh = TextRenderer.MeasureText(number, big).Height;
            int ch = Caption.Length > 0 ? TextRenderer.MeasureText(Caption, Font).Height : 0;
            int top = (Height - nh - ch) / 2;
            TextRenderer.DrawText(g, number, big, new Rectangle(0, top, Width, nh), Ui.Text, back,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (ch > 0)
                TextRenderer.DrawText(g, Caption, Font, new Rectangle(0, top + nh - Ui.P(2), Width, ch), Ui.Soft, back,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Tarjeta pulsable con icono, título y una línea de explicación.</summary>
    public class Tile : Control
    {
        public string Icon = "", Title = "", Subtitle = "";
        float hot;
        bool down;

        public Tile()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void WndProc(ref Message m)
        {
            if (Opaque.Paint(Handle, ref m, base.WndProc)) return;
            base.WndProc(ref m);
        }

        void Fade(float to)
        {
            float from = hot;
            Anim.Run(this, 170, delegate(float t) { hot = from + (to - from) * t; Invalidate(); });
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Fade(1f); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); down = false; Fade(0f); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); down = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color back = Ui.ParentBack(this);
            g.Clear(back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            Color fill = Ui.Mix(Ui.Mix(back, Ui.Text, 0.045f), Ui.Mix(back, Ui.Text, down ? 0.14f : 0.10f), hot);
            float r = Ui.R(10);
            Ui.FillRound(g, fill, 0.5f, 0.5f, Width - 1.5f, Height - 1.5f, r);
            Ui.DrawRound(g, Ui.Mix(Ui.Border, Ui.Accent, hot), 0.5f, 0.5f, Width - 1.5f, Height - 1.5f, r);
            int pad = Ui.P(14);
            int iconW = Ui.P(30);
            using (SolidBrush b = new SolidBrush(Ui.Accent))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Near;
                if (Ui.IconFont != null) g.DrawString(Icon, Ui.IconFont, b, new RectangleF(pad - Ui.P(2), pad + hot * -Ui.P(2), iconW + Ui.P(10), iconW + Ui.P(6)), sf);
            }
            int tx = pad + iconW + Ui.P(8);
            Font bold = Ui.BoldFont ?? Font;
            float th = bold.GetHeight(g);
            using (SolidBrush tb = new SolidBrush(Ui.Text))
            using (SolidBrush sb = new SolidBrush(Ui.Soft))
            using (StringFormat one = new StringFormat())
            using (StringFormat wrap = new StringFormat())
            {
                one.FormatFlags = StringFormatFlags.NoWrap;
                one.Trimming = StringTrimming.EllipsisCharacter;
                wrap.Trimming = StringTrimming.EllipsisWord;
                float tw = Width - tx - pad / 2f;
                g.DrawString(Title, bold, tb, new RectangleF(tx, pad - Ui.P(3), tw, th + 2), one);
                g.DrawString(Subtitle, Font, sb, new RectangleF(tx, pad - Ui.P(3) + th + Ui.P(2), tw, Height - pad - th - Ui.P(4)), wrap);
            }
        }
    }

    /// <summary>
    /// Barra lateral de navegación con iconos. Un indicador se desliza hasta la sección elegida.
    /// Se dibuja con transparencia real para dejar ver el efecto translúcido de Windows 11.
    /// </summary>
    public class SideBar : Control
    {
        string[] items = new string[0], icons = new string[0];
        Rectangle[] rects = new Rectangle[0];
        float[] hotAmt = new float[0];
        object[] keys = new object[0];
        int sel = -1, hot = -1;
        float indY, actHot;
        bool actOver;
        Rectangle actRect;
        readonly object actKey = new object(), indKey = new object();
        public Font BoldFont, TitleFont;
        public string Brand = "Ligero";
        public string ActionText = "";
        public bool ActionDone;
        public event Action<int> Selected;
        public event EventHandler ActionClick;

        public SideBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
        }

        public void SetItems(string[] names, string[] glyphs)
        {
            items = names ?? new string[0];
            icons = glyphs ?? new string[0];
            hotAmt = new float[items.Length];
            keys = new object[items.Length];
            for (int i = 0; i < keys.Length; i++) keys[i] = new object();
            Measure();
        }

        /// <summary>Las secciones van de arriba abajo; la última (Personalizar) queda fija al pie, bajo el botón de administrador.</summary>
        void Measure()
        {
            rects = new Rectangle[items.Length];
            int n = items.Length;
            if (n == 0) return;
            int top = Ui.P(56), side = Ui.P(8), bottom = Ui.P(10);
            int ih = Ui.P(36);
            int reserved = bottom + ih + Ui.P(8) + (ActionText.Length > 0 ? Ui.P(42) : 0);
            if (n > 1) ih = Math.Max(Ui.P(26), Math.Min(ih, (Height - top - reserved) / (n - 1)));
            for (int i = 0; i < n - 1; i++) rects[i] = new Rectangle(side, top + i * ih, Width - 2 * side, ih - Ui.P(2));
            rects[n - 1] = new Rectangle(side, Height - bottom - ih, Width - 2 * side, ih - Ui.P(2));
            actRect = ActionText.Length > 0
                ? new Rectangle(side + Ui.P(4), rects[n - 1].Y - Ui.P(42), Width - 2 * side - Ui.P(8), Ui.P(32))
                : Rectangle.Empty;
            if (sel >= 0 && sel < n) indY = rects[sel].Y;
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Measure(); }

        public int SelectedIndex
        {
            get { return sel; }
            set
            {
                if (value < 0 || value >= rects.Length || value == sel) return;
                bool first = sel < 0 || !IsHandleCreated;
                sel = value;
                float from = indY, to = rects[sel].Y;
                if (first) { indY = to; Invalidate(); return; }
                Anim.Run(indKey, 260, delegate(float t) { indY = from + (to - from) * t; Invalidate(); });
            }
        }

        int Hit(Point p)
        {
            for (int i = 0; i < rects.Length; i++) if (rects[i].Contains(p)) return i;
            return -1;
        }

        void Fade(int i, float to)
        {
            if (i < 0 || i >= hotAmt.Length) return;
            float from = hotAmt[i];
            Anim.Run(keys[i], 160, delegate(float t) { hotAmt[i] = from + (to - from) * t; Invalidate(); });
        }

        void SetActionOver(bool over)
        {
            if (over == actOver) return;
            actOver = over;
            float from = actHot, to = over ? 1f : 0f;
            Anim.Run(actKey, 160, delegate(float t) { actHot = from + (to - from) * t; Invalidate(); });
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = Hit(e.Location);
            if (h != hot) { Fade(hot, 0f); hot = h; Fade(hot, 1f); }
            bool over = !ActionDone && actRect.Contains(e.Location);
            SetActionOver(over);
            Cursor = h >= 0 || over ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Fade(hot, 0f);
            hot = -1;
            SetActionOver(false);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int h = Hit(e.Location);
            if (h >= 0) { if (Selected != null) Selected(h); }
            else if (!ActionDone && actRect.Contains(e.Location) && ActionClick != null) ActionClick(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int w = Width, h = Height;
            if (w <= 0 || h <= 0) return;
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (StringFormat near = new StringFormat())
                using (StringFormat center = new StringFormat())
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Ui.Glass ? Color.FromArgb(Ui.GlassTint, Ui.Panel) : Ui.Panel);
                    near.Alignment = StringAlignment.Near;
                    near.LineAlignment = StringAlignment.Center;
                    near.FormatFlags = StringFormatFlags.NoWrap;
                    near.Trimming = StringTrimming.EllipsisCharacter;
                    center.Alignment = StringAlignment.Center;
                    center.LineAlignment = StringAlignment.Center;
                    center.FormatFlags = StringFormatFlags.NoWrap;
                    Font bold = BoldFont ?? Font;
                    bool compact = w < Ui.P(120);

                    // Marca
                    float logo = Ui.P(24);
                    Ui.FillRound(g, Ui.Primary, Ui.P(18), Ui.P(15), logo, logo, Ui.P(6));
                    using (SolidBrush lb = new SolidBrush(Ui.PrimaryText))
                        g.DrawString("L", bold, lb, new RectangleF(Ui.P(18), Ui.P(15), logo, logo), center);
                    if (!compact)
                        using (SolidBrush tb = new SolidBrush(Ui.Text))
                            g.DrawString(Brand, TitleFont ?? bold, tb, new RectangleF(Ui.P(18) + logo + Ui.P(8), Ui.P(10), w, logo + Ui.P(10)), near);

                    for (int i = 0; i < items.Length; i++)
                    {
                        Rectangle r = rects[i];
                        float a = i == sel ? 1f : hotAmt[i] * 0.6f;
                        if (a > 0.01f)
                            Ui.FillRound(g, Color.FromArgb((int)(a * (Ui.Dark ? 34 : 22)), Ui.Text), r.X, r.Y, r.Width, r.Height, Ui.R(7));
                        Color c = i == sel ? Ui.Text : Ui.Mix(Ui.Soft, Ui.Text, hotAmt[i]);
                        using (SolidBrush b = new SolidBrush(i == sel ? Ui.Accent : c))
                            if (Ui.IconFont != null && i < icons.Length)
                                g.DrawString(icons[i], Ui.IconFont, b, compact
                                    ? new RectangleF(r.X, r.Y + Ui.P(1), r.Width, r.Height)
                                    : new RectangleF(r.X + Ui.P(8), r.Y + Ui.P(1), Ui.P(30), r.Height), center);
                        if (!compact)
                            using (SolidBrush b = new SolidBrush(c))
                                g.DrawString(items[i], i == sel ? bold : Font, b, new RectangleF(r.X + Ui.P(44), r.Y, r.Width - Ui.P(48), r.Height), near);
                    }
                    if (sel >= 0 && sel < rects.Length)
                    {
                        float ih = rects[sel].Height;
                        Ui.FillRound(g, Ui.Accent, Ui.P(8), indY + ih * 0.28f, Ui.P(3), ih * 0.44f, Ui.P(2));
                    }

                    if (ActionText.Length > 0)
                    {
                        if (!ActionDone)
                        {
                            Ui.FillRound(g, Color.FromArgb((int)(18 + actHot * 34), Ui.Text), actRect.X, actRect.Y, actRect.Width, actRect.Height, Ui.P(7));
                            Ui.DrawRound(g, Color.FromArgb(70, Ui.Text), actRect.X, actRect.Y, actRect.Width, actRect.Height, Ui.P(7));
                        }
                        using (SolidBrush b = new SolidBrush(ActionDone ? Ui.Good : Ui.Text))
                        {
                            if (compact && Ui.IconFont != null) g.DrawString("\uEA18", Ui.IconFont, b, actRect, center);
                            else g.DrawString(ActionText, Font, b, actRect, center);
                        }
                    }
                }
                if (Ui.Glass) Ui.BlitAlpha(e.Graphics, bmp);
                else e.Graphics.DrawImageUnscaled(bmp, 0, 0);
            }
        }
    }

    /// <summary>
    /// Lista con filas más altas, resaltado al pasar el ratón, casillas propias y
    /// cabecera plana. Todo lo pinta la app para que respete el tema elegido.
    /// </summary>
    public class SmoothList : ListView
    {
        class HeaderHook : NativeWindow
        {
            public SmoothList Owner;

            // Windows deja sin pintar el tramo de cabecera que sobra a la derecha de la ultima columna.
            void FillRest(Graphics g)
            {
                Native.RECT rc;
                Native.GetClientRect(Handle, out rc);
                int x = 0;
                foreach (ColumnHeader c in Owner.Columns) x += c.Width;
                if (x >= rc.Right) return;
                using (SolidBrush b = new SolidBrush(Ui.Back)) g.FillRectangle(b, x, 0, rc.Right - x, rc.Bottom);
                using (Pen p = new Pen(Ui.Border)) g.DrawLine(p, x, rc.Bottom - 1, rc.Right, rc.Bottom - 1);
            }

            void FillRest(IntPtr dc)
            {
                try { using (Graphics g = Graphics.FromHdc(dc)) FillRest(g); } catch { }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg != 0x000F) { base.WndProc(ref m); return; }
                if (Opaque.Paint(Handle, ref m, base.WndProc, FillRest)) return;
                base.WndProc(ref m);
                try { using (Graphics g = Graphics.FromHwnd(Handle)) FillRest(g); } catch { }
            }
        }

        HeaderHook header;

        // Bajo la última fila Windows dibuja guías de columna: se tapan con el color de fondo.
        void FillBelow(Graphics g)
        {
            int y;
            if (Items.Count > 0) y = Items[Items.Count - 1].Bounds.Bottom;
            else
            {
                Native.RECT hr = new Native.RECT();
                if (header != null) Native.GetClientRect(header.Handle, out hr);
                y = hr.Bottom;
            }
            if (y >= ClientSize.Height || y < 0) return;
            using (SolidBrush b = new SolidBrush(Ui.Back)) g.FillRectangle(b, 0, y, ClientSize.Width, ClientSize.Height - y);
        }

        void FillBelow(IntPtr dc)
        {
            try { using (Graphics g = Graphics.FromHdc(dc)) FillBelow(g); } catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg != 0x000F) { base.WndProc(ref m); return; }
            if (Opaque.Paint(Handle, ref m, base.WndProc, FillBelow)) return;
            base.WndProc(ref m);
            try { using (Graphics g = Graphics.FromHwnd(Handle)) FillBelow(g); } catch { }
        }

        int hot = -1;

        public SmoothList()
        {
            DoubleBuffered = true;
            OwnerDraw = true;
            View = View.Details;
            FullRowSelect = true;
            HideSelection = false;
            MultiSelect = false;
            BorderStyle = BorderStyle.None;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            ImageList rowHeight = new ImageList();
            rowHeight.ImageSize = new Size(1, Ui.P(27));
            SmallImageList = rowHeight;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.ThemeScrollBars(Handle, Ui.Dark);
            try
            {
                // La cabecera es una ventana aparte: tambien debe pintarse opaca.
                IntPtr head = Native.SendMessage(Handle, 0x101F, IntPtr.Zero, IntPtr.Zero);
                if (head != IntPtr.Zero) { header = new HeaderHook(); header.Owner = this; header.AssignHandle(head); }
            }
            catch { }
        }

        void InvalidateRow(int i)
        {
            if (i >= 0 && i < Items.Count) Invalidate(Items[i].Bounds);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            ListViewItem it = GetItemAt(Ui.P(30), e.Y);
            int i = it == null ? -1 : it.Index;
            if (i == hot) return;
            InvalidateRow(hot);
            hot = i;
            InvalidateRow(hot);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            InvalidateRow(hot);
            hot = -1;
        }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Ui.Back)) e.Graphics.FillRectangle(b, e.Bounds);
            using (Pen p = new Pen(Ui.Border))
                e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            Rectangle r = new Rectangle(e.Bounds.X + Ui.P(8), e.Bounds.Y, e.Bounds.Width - Ui.P(10), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, r, Ui.Soft,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e)
        {
            // En vista de detalle cada celda se pinta en OnDrawSubItem.
        }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = e.Bounds;
            if (e.ColumnIndex == 0) r = new Rectangle(r.X, r.Y, e.Header.Width, r.Height);
            Color bg = e.Item.Selected ? Ui.RowSel : (e.ItemIndex == hot ? Ui.RowHot : Ui.Back);
            using (SolidBrush b = new SolidBrush(bg)) g.FillRectangle(b, r);

            int x = r.X + Ui.P(8);
            if (e.ColumnIndex == 0 && CheckBoxes)
            {
                int s = Ui.P(16);
                float bx = r.X + Ui.P(4), by = r.Y + (r.Height - s) / 2f;
                SmoothingMode old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (e.Item.Checked)
                {
                    Ui.FillRound(g, Ui.Primary, bx, by, s, s, Ui.P(4));
                    using (Pen p = new Pen(Ui.PrimaryText, Math.Max(1.6f, Ui.Scale * 1.7f)))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        p.LineJoin = LineJoin.Round;
                        g.DrawLines(p, new PointF[] {
                            new PointF(bx + s * 0.24f, by + s * 0.52f), new PointF(bx + s * 0.43f, by + s * 0.70f),
                            new PointF(bx + s * 0.76f, by + s * 0.32f) });
                    }
                }
                else
                {
                    Ui.FillRound(g, Ui.Back, bx, by, s, s, Ui.P(4));
                    Ui.DrawRound(g, Ui.Soft, bx, by, s, s, Ui.P(4));
                }
                g.SmoothingMode = old;
                x = (int)bx + s + Ui.P(8);
            }
            Color fc = e.Item.UseItemStyleForSubItems ? e.Item.ForeColor : e.SubItem.ForeColor;
            TextRenderer.DrawText(g, e.SubItem.Text, Font, new Rectangle(x, r.Y, Math.Max(0, r.Right - x - Ui.P(4)), r.Height), fc,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>Páginas de la ventana. Al cambiar de página, la nueva entra deslizándose.</summary>
    public class Pager : Panel
    {
        protected override void WndProc(ref Message m)
        {
            if (Opaque.Paint(Handle, ref m, base.WndProc)) return;
            base.WndProc(ref m);
        }

        public readonly List<Panel> TabPages = new List<Panel>();
        int selected = -1;
        public event EventHandler SelectedIndexChanged;

        public Panel AddPage(string title)
        {
            Panel p = new Page();
            p.Text = title;
            p.Visible = false;
            p.SetBounds(0, 0, ClientSize.Width, ClientSize.Height);
            Controls.Add(p);
            TabPages.Add(p);
            if (selected < 0) Jump(0);
            return p;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (selected >= 0) TabPages[selected].SetBounds(0, 0, ClientSize.Width, ClientSize.Height);
        }

        /// <summary>Cambia de página sin animación ni aviso.</summary>
        public void Jump(int index)
        {
            if (index < 0 || index >= TabPages.Count) return;
            selected = index;
            for (int i = 0; i < TabPages.Count; i++)
            {
                if (i == selected) TabPages[i].SetBounds(0, 0, ClientSize.Width, ClientSize.Height);
                TabPages[i].Visible = i == selected;
            }
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value == selected || value < 0 || value >= TabPages.Count) return;
                if (selected >= 0) TabPages[selected].Visible = false;
                selected = value;
                Panel page = TabPages[selected];
                int slide = Ui.Anim && IsHandleCreated ? Ui.P(26) : 0;
                page.SetBounds(slide, 0, ClientSize.Width, ClientSize.Height);
                page.Visible = true;
                if (slide > 0)
                    Anim.Run(this, 200, delegate(float t)
                    {
                        if (TabPages[selected] == page && !page.IsDisposed) page.Left = (int)Math.Round(slide * (1f - t));
                    });
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public Panel SelectedTab { get { return selected >= 0 ? TabPages[selected] : null; } }
    }

    public class MainForm : Form
    {
        float dpiScale;      // escala del monitor donde está la ventana
        float sysScale;      // escala del monitor principal (la que usan las fuentes)
        bool pendingRebuild;
        float scale;
        Font fBold, fTitle;
        // Copia local de la paleta (ver Ui). La app pinta sus controles ella misma
        // para verse igual de bien con cualquier tema del sistema.
        Color cBack, cText, cSoft, cAccent, cGood, cWarn, cAmber, cBad, cPanel, cBorder, cInfo;

        public Pager tabs;
        SideBar nav;
        BusyLine busyLine;
        Label status;

        System.Windows.Forms.Timer timer;
        bool busy;

        // Memoria
        Panel pgMem;
        Label lblMem, lblTrimDesc, lblTrimRes, lblProcs, lblCloseDesc;
        Meter barMem;
        Button btnTrim, btnClose, btnKill, btnRefresh;
        ListView lvProcs;

        // Acelerar
        Panel pgFast;
        Label lblTwIntro, lblTwDesc;
        ListView lvTweaks;
        Button btnTwToggle, btnTwAll;

        // Inicio
        Panel pgStart;
        Label lblStartDesc;
        ListView lvStart;
        Button btnStart;

        // Disco
        Panel pgDisk;
        Label lblDisk, lblDiskDesc, lblCatDesc, lblCleanRes;
        ListView lvCats;
        Button btnScan, btnClean;
        List<Category> cats;


        // Programas
        Panel pgApps;
        Label lblAppsDesc, lblAppRisk;
        Toggle chkSys;
        ListView lvApps;
        Button btnUninstall, btnLeftovers, btnAppsRefresh;
        List<AppInfo> apps;
        bool showSystemApps;

        // Eliminar
        Panel pgDel;
        Label lblDelDesc, lblDelPath, lblDelTitle, lblDelText, lblDelRes;
        Button btnPickFile, btnPickFolder, btnDelete;
        string delPath;
        Verdict delVerdict;

        // Deshacer
        Panel pgUndo;
        Label lblUndoDesc, lblQuar;
        ListView lvHist;
        Button btnUndo, btnUndoAll, btnPurge;

        int P(int v) { return (int)Math.Round(v * scale); }

        DateTime inactiveSince = DateTime.MaxValue;
        bool trimmedWhileInactive;

        /// <summary>Limpieza de la memoria propia, fuera del hilo de la ventana.</summary>
        void Housekeep(bool trim)
        {
            ThreadPool.QueueUserWorkItem(delegate { Util.ReleaseMemory(trim); });
        }

        public MainForm()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) sysScale = g.DpiX / 96f;
            // La ventana se abre en el monitor donde está el ratón, a la escala de ese monitor.
            Screen screen = Screen.FromPoint(Cursor.Position);
            int dpi = Native.DpiAt(screen.Bounds.X + screen.Bounds.Width / 2, screen.Bounds.Y + screen.Bounds.Height / 2);
            dpiScale = dpi > 0 ? dpi / 96f : sysScale;
            Prefs.Load();
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            if (Program.ScriptPath == null)
            {
                try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            }
            ApplyPalette();

            Rectangle wa = screen.WorkingArea;
            ClientSize = new Size(Math.Min((int)(1150 * dpiScale), wa.Width - 40), Math.Min((int)(700 * dpiScale), wa.Height - 60));
            MinimumSize = new Size(Math.Min((int)(1000 * dpiScale), wa.Width), Math.Min((int)(620 * dpiScale), wa.Height));
            Location = new Point(wa.X + Math.Max(0, (wa.Width - Width) / 2), wa.Y + Math.Max(0, (wa.Height - Height) / 2));

            BuildAll();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 2000;
            timer.Tick += delegate
            {
                if (WindowState != FormWindowState.Minimized)
                {
                    if (tabs.SelectedTab == pgMem) RefreshMem();
                    else if (tabs.SelectedTab == pgHome) RefreshHome();
                }
                SmartBoostTick();
                // Medio minuto en segundo plano sin hacer nada: se suelta la memoria una vez.
                if (!busy && !trimmedWhileInactive && inactiveSince != DateTime.MaxValue && (DateTime.Now - inactiveSince).TotalSeconds > 30)
                {
                    trimmedWhileInactive = true;
                    Housekeep(true);
                }
            };

            Load += delegate { InitOnce(); };

            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                // Arrancado con Windows: cerrar la ventana solo la oculta; se sale desde el icono junto al reloj.
                if (Program.Background && !reallyClose && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                    Housekeep(true);
                    return;
                }
                if (busy)
                {
                    e.Cancel = true;
                    MessageBox.Show(this, "Ligero está terminando una tarea. Espera unos segundos y vuelve a cerrar.",
                        "Un momento", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            // Ligero no debe estorbar: cuando se minimiza o pasa a segundo plano, devuelve su memoria a Windows.
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized && !busy) Housekeep(true);
            };
            Deactivate += delegate { inactiveSince = DateTime.Now; trimmedWhileInactive = false; };
            Activated += delegate { inactiveSince = DateTime.MaxValue; };

            // Si Windows cambia de claro a oscuro (o de color) y Ligero lo sigue, se adapta al momento.
            Microsoft.Win32.UserPreferenceChangedEventHandler onLook = delegate
            {
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        bool themeChanged = Prefs.Theme == "sistema" && Ui.Dark != Ui.SystemDark();
                        bool accentChanged = Prefs.Accent == "windows" && Ui.AccentBase("windows") != Ui.Primary;
                        if (themeChanged || accentChanged) RebuildUI();
                    });
                }
                catch { }
            };
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += onLook;
            FormClosed += delegate { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= onLook; };
        }

        // ---------------- aspecto ----------------

        static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        /// <summary>Calcula colores, tamaños y fuentes a partir de las preferencias.</summary>
        void ApplyPalette()
        {
            scale = dpiScale * (Prefs.BigText ? 1.14f : 1f);
            Ui.Scale = scale;
            Ui.Load();
            Native.SetAppMode(Ui.Dark);
            cBack = Ui.Back; cText = Ui.Text; cSoft = Ui.Soft; cAccent = Ui.Accent; cGood = Ui.Good; cWarn = Ui.Warn;
            cAmber = Ui.Amber; cBad = Ui.Bad; cPanel = Ui.Panel; cBorder = Ui.Border; cInfo = Ui.Info;
            // Los puntos de una fuente se convierten con la escala del monitor principal:
            // se compensa para que el texto salga al tamaño del monitor donde está la ventana.
            float size = (Prefs.BigText ? 10.8f : 9.5f) * dpiScale / sysScale;
            Font = new Font("Segoe UI", size);
            fBold = new Font("Segoe UI Semibold", size);
            fTitle = new Font("Segoe UI Semibold", size * 1.42f);
            Ui.BoldFont = fBold;
            Ui.BigFont = new Font("Segoe UI Semibold", size * 2.5f);
            Ui.IconFont = new Font("Segoe MDL2 Assets", size * 1.2f);
            BackColor = cBack;
            ForeColor = cText;
            Text = "Ligero" + (Util.IsAdmin ? "  ·  administrador" : "");
            if (Program.ScriptPath != null)
            {
                try { Icon = MakeIcon(); } catch { }
            }
        }

        /// <summary>Crea todos los controles de la ventana con la paleta actual.</summary>
        void BuildAll()
        {
            pageIcons.Clear();
            tabs = new Pager();
            tabs.Dock = DockStyle.Fill;
            tabs.BackColor = cBack;
            Controls.Add(tabs);

            busyLine = new BusyLine();
            busyLine.Dock = DockStyle.Top;
            busyLine.Height = Math.Max(2, P(2));
            Controls.Add(busyLine);

            status = new PlainLabel();
            status.Dock = DockStyle.Bottom;
            status.Height = P(26);
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Padding = new Padding(P(12), 0, 0, 0);
            status.BackColor = cBack;
            status.ForeColor = cSoft;
            status.UseMnemonic = false;
            status.Text = "Listo. Nada se cambia hasta que pulses un botón, y casi todo se puede deshacer.";
            Controls.Add(status);

            nav = new SideBar();
            nav.Dock = DockStyle.Left;
            nav.Width = Prefs.Compact ? P(60) : P(204);
            nav.Font = Font;
            nav.BoldFont = fBold;
            nav.TitleFont = fTitle;
            Controls.Add(nav);
            tabs.BringToFront();

            BuildHome();
            BuildHealth();
            BuildMemory();
            BuildFast();
            BuildStartup();
            BuildDisk();
            BuildFiles();
            BuildApps();
            BuildDelete();
            BuildNet();
            BuildProtect();
            BuildTools();
            BuildUndo();
            BuildSettings();

            string[] titles = new string[tabs.TabPages.Count];
            for (int i = 0; i < titles.Length; i++) titles[i] = tabs.TabPages[i].Text;
            nav.ActionText = Util.IsAdmin ? "Modo administrador ✔" : "Abrir como administrador";
            nav.ActionDone = Util.IsAdmin;
            nav.SetItems(titles, pageIcons.ToArray());
            nav.SelectedIndex = tabs.SelectedIndex;
            nav.Selected += delegate(int i) { if (!busy) tabs.SelectedIndex = i; };
            nav.ActionClick += delegate
            {
                if (Util.IsAdmin || busy) return;
                if (Program.RestartAsAdmin()) Close();
            };
            tabs.SelectedIndexChanged += delegate { nav.SelectedIndex = tabs.SelectedIndex; OnTabChanged(); };
            ApplyChrome();
        }

        readonly List<string> pageIcons = new List<string>();

        static string Glyph(string title)
        {
            switch (title)
            {
                case "Inicio": return "";
                case "Chequeo": return "";
                case "Memoria": return "";
                case "Acelerar": return "";
                case "Arranque": return "";
                case "Disco": return "";
                case "Archivos": return "";
                case "Programas": return "";
                case "Eliminar": return "";
                case "Red": return "";
                case "Protección": return "";
                case "Herramientas": return "";
                case "Deshacer": return "";
                default: return "";
            }
        }

        void GoTo(Panel page)
        {
            if (!busy && page != null) tabs.SelectedIndex = tabs.TabPages.IndexOf(page);
        }

        Tile MakeTile(Control parent, string icon, string title, string subtitle, EventHandler onClick)
        {
            Tile t = new Tile();
            t.Icon = icon;
            t.Title = title;
            t.Subtitle = subtitle;
            t.Font = Font;
            t.Click += onClick;
            parent.Controls.Add(t);
            return t;
        }

        void Launch(string target, string args)
        {
            try
            {
                ProcessStartInfo psi = args == null ? new ProcessStartInfo(target) : new ProcessStartInfo(target, args);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo abrir: " + ex.Message, "Ligero", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // ============================ INICIO (resumen) ============================

        Panel pgHome;
        Ring ringMem;
        Label lblHomeTitle, lblHomeMem, lblHomeTemp, lblHomeDisk, lblBoostDesc, lblBoostRes, lblSmartDesc;
        Button btnBoost;
        Tile[] homeTiles;
        Toggle togSmart;
        long homeJunk = -1;
        bool junkScanning;
        DateTime lastSmart = DateTime.MinValue;

        void BuildHome()
        {
            pgHome = NewPage("Inicio");
            lblHomeTitle = Lab(pgHome, "Así está tu equipo ahora", fTitle, cText);
            ringMem = new Ring();
            ringMem.Font = Font;
            ringMem.Caption = "memoria en uso";
            pgHome.Controls.Add(ringMem);
            lblHomeMem = Lab(pgHome, "", fBold, cText);
            lblHomeTemp = Lab(pgHome, "Archivos temporales: calculando…", Font, cSoft);
            lblHomeDisk = Lab(pgHome, "", Font, cSoft);
            btnBoost = Btn(pgHome, "Optimizar ahora", OnBoost);
            Primary(btnBoost);
            lblBoostDesc = Lab(pgHome,
                "Qué hace: libera la memoria que los programas no están usando y retira los archivos temporales antiguos a la zona " +
                "de seguridad. No cierra nada, y la limpieza se puede deshacer.", Font, cSoft);
            lblBoostRes = Lab(pgHome, "", fBold, cGood);
            homeTiles = new Tile[] {
                MakeTile(pgHome, "", "Chequeo de salud", "Revisa todo de una vez y te dice qué mejorar", delegate { GoTo(pgHealth); }),
                MakeTile(pgHome, "", "Procesos", "Qué programas gastan más memoria ahora", delegate { GoTo(pgMem); }),
                MakeTile(pgHome, "", "Limpieza a fondo", "Temporales, cachés y restos de Windows", delegate { GoTo(pgDisk); }),
                MakeTile(pgHome, "", "Arranque", "Lo que se abre solo al encender el equipo", delegate { GoTo(pgStart); })
            };
            togSmart = new Toggle();
            togSmart.Font = fBold;
            togSmart.Text = "Optimización inteligente";
            togSmart.SetSilently(Prefs.SmartBoost);
            togSmart.CheckedChanged += delegate { Prefs.SmartBoost = togSmart.Checked; Prefs.Save(); };
            pgHome.Controls.Add(togSmart);
            lblSmartDesc = Lab(pgHome,
                "Mientras Ligero esté abierto, libera memoria por su cuenta cuando el uso pase del 85 % (como mucho una vez cada " +
                "10 minutos). No toca archivos ni cierra programas.", Font, cSoft);
            pgHome.Resize += delegate { LayoutHome(); };
        }

        void LayoutHome()
        {
            int w = pgHome.ClientSize.Width, h = pgHome.ClientSize.Height, m = P(20);
            if (w < 50 || h < 50) return;
            lblHomeTitle.SetBounds(m, P(14), w - 2 * m, P(32));
            int ring = P(156);
            ringMem.SetBounds(m, P(58), ring, ring);
            int x = m + ring + P(26);
            lblHomeMem.SetBounds(x, P(64), w - x - m, P(24));
            lblHomeTemp.SetBounds(x, P(90), w - x - m, P(22));
            lblHomeDisk.SetBounds(x, P(112), w - x - m, P(22));
            btnBoost.SetBounds(x, P(148), P(220), P(46));
            lblBoostDesc.SetBounds(x + P(236), P(142), w - x - P(236) - m, P(80));
            lblBoostRes.SetBounds(m, P(226), w - 2 * m, P(42));
            int gap = P(12), ty = P(276);
            int tw = (w - 2 * m - gap * (homeTiles.Length - 1)) / homeTiles.Length;
            for (int i = 0; i < homeTiles.Length; i++) homeTiles[i].SetBounds(m + i * (tw + gap), ty, tw, P(92));
            togSmart.SetBounds(m, ty + P(92) + P(22), w - 2 * m, P(26));
            lblSmartDesc.SetBounds(m, ty + P(92) + P(50), w - 2 * m, P(44));
        }

        void RefreshHome()
        {
            long total, avail;
            Memory.Read(out total, out avail);
            if (total > 0)
            {
                ringMem.Value = (int)((total - avail) * 100 / total);
                lblHomeMem.Text = "Memoria: " + Util.Size(total - avail) + " en uso de " + Util.Size(total) + "  ·  " + Util.Size(avail) + " libres";
            }
            try
            {
                DriveInfo d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                lblHomeDisk.Text = "Disco " + d.Name.TrimEnd('\\') + "  " + Util.Size(d.AvailableFreeSpace) + " libres de " + Util.Size(d.TotalSize);
            }
            catch { }
            if (homeJunk >= 0) lblHomeTemp.Text = "Archivos temporales: " + Util.Size(homeJunk) + " que se pueden retirar";
            else if (!junkScanning)
            {
                junkScanning = true;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    long j = 0;
                    try { j = Booster.JunkSize(); } catch { }
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            homeJunk = j;
                            junkScanning = false;
                            if (!lblHomeTemp.IsDisposed) lblHomeTemp.Text = "Archivos temporales: " + Util.Size(homeJunk) + " que se pueden retirar";
                        });
                    }
                    catch { junkScanning = false; }
                });
            }
        }

        void OnBoost(object sender, EventArgs e)
        {
            string msg = "";
            long junk = 0;
            Run("Optimizando: liberando memoria y retirando temporales…", delegate
            {
                msg = Booster.Run();
                junk = Booster.JunkSize();
            }, delegate
            {
                homeJunk = junk;
                lblBoostRes.Text = msg + (History.QuarantinedBytes() > 0 ? " La limpieza se puede deshacer en la pestaña Deshacer." : "");
                SetStatus("Optimización terminada.");
                RefreshHome();
            });
        }

        /// <summary>Optimización inteligente: libera memoria sola cuando el equipo va justo.</summary>
        void SmartBoostTick()
        {
            if (!Prefs.SmartBoost || busy || (DateTime.Now - lastSmart).TotalMinutes < 10) return;
            long total, before;
            Memory.Read(out total, out before);
            if (total <= 0 || (total - before) * 100 / total < 85) return;
            lastSmart = DateTime.Now;
            ThreadPool.QueueUserWorkItem(delegate
            {
                long after = before, t2;
                try
                {
                    Memory.Trim();
                    Thread.Sleep(500);
                    Memory.Read(out t2, out after);
                }
                catch { }
                long gain = Math.Max(0, after - before);
                try
                {
                    History.Add("ram", "Optimización inteligente", Util.Size(gain) + " de memoria liberados al superar el 85 % de uso", History.Info, "", 0, null);
                    BeginInvoke((MethodInvoker)delegate { SetStatus("Optimización inteligente: " + Util.Size(gain) + " de memoria liberados."); });
                }
                catch { }
            });
        }

        // ============================ CHEQUEO DE SALUD ============================

        Panel pgHealth;
        Ring ringScore;
        Label lblHealthTitle, lblHealthSum;
        Button btnHealthRun, btnHealthFix;
        ListView lvHealth;
        List<HealthItem> healthItems;
        int healthScore;

        void BuildHealth()
        {
            pgHealth = NewPage("Chequeo");
            ringScore = new Ring();
            ringScore.Font = Font;
            ringScore.Score = true;
            ringScore.Caption = "salud";
            pgHealth.Controls.Add(ringScore);
            lblHealthTitle = Lab(pgHealth, "Chequeo de salud", fTitle, cText);
            lblHealthSum = Lab(pgHealth,
                "Revisa de una vez la memoria, el disco, los programas de arranque, los ajustes recomendados y la protección. " +
                "No cambia nada: solo mira y te dice adónde ir para mejorar cada punto.", Font, cSoft);
            btnHealthRun = Btn(pgHealth, "Analizar ahora", OnHealthRun);
            Primary(btnHealthRun);
            lvHealth = LV(pgHealth, false, "Qué se revisó", 210, "Resultado", 640);
            btnHealthFix = Btn(pgHealth, "Ir a solucionarlo", OnHealthFix);
            if (healthItems != null) ShowHealth();
            pgHealth.Resize += delegate { LayoutHealth(); };
        }

        void LayoutHealth()
        {
            int w = pgHealth.ClientSize.Width, h = pgHealth.ClientSize.Height, m = P(20);
            if (w < 50 || h < 50) return;
            int ring = P(128);
            ringScore.SetBounds(m, P(14), ring, ring);
            int x = m + ring + P(24);
            lblHealthTitle.SetBounds(x, P(18), w - x - m, P(32));
            lblHealthSum.SetBounds(x, P(52), w - x - m, P(44));
            btnHealthRun.SetBounds(x, P(102), P(190), P(38));
            int by = h - P(46), top = P(158);
            lvHealth.SetBounds(m, top, w - 2 * m, Math.Max(P(60), by - top - P(10)));
            btnHealthFix.SetBounds(m, by, P(190), P(34));
        }

        void OnHealthRun(object sender, EventArgs e)
        {
            List<HealthItem> items = null;
            int score = 0;
            Run("Revisando el equipo…", delegate { items = Health.Check(out score); }, delegate
            {
                healthItems = items;
                healthScore = score;
                ShowHealth();
                SetStatus("Chequeo terminado.");
            });
        }

        void ShowHealth()
        {
            ringScore.Value = healthScore;
            int issues = 0;
            lvHealth.BeginUpdate();
            lvHealth.Items.Clear();
            foreach (HealthItem hi in healthItems)
            {
                if (hi.Level > 0) issues++;
                ListViewItem it = Item(hi.Level == 0 ? cText : RiskColor(hi.Level), hi.Title, (hi.Level == 0 ? "✔  " : "") + hi.Detail);
                it.Tag = hi;
                lvHealth.Items.Add(it);
            }
            lvHealth.EndUpdate();
            lblHealthSum.Text = issues == 0
                ? "Todo en orden: no hay nada que mejorar ahora mismo."
                : issues + (issues == 1 ? " punto se puede mejorar." : " puntos se pueden mejorar.") + " Toca uno y pulsa «Ir a solucionarlo».";
        }

        void OnHealthFix(object sender, EventArgs e)
        {
            if (lvHealth.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, healthItems == null ? "Pulsa primero «Analizar ahora»." : "Toca primero un punto de la lista.", "Ligero");
                return;
            }
            HealthItem hi = (HealthItem)lvHealth.SelectedItems[0].Tag;
            switch (hi.Target)
            {
                case "memoria": GoTo(pgMem); break;
                case "disco": GoTo(pgDisk); break;
                case "arranque": GoTo(pgStart); break;
                case "acelerar": GoTo(pgFast); break;
                case "proteccion": GoTo(pgProt); break;
                case "deshacer": GoTo(pgUndo); break;
            }
        }

        // ============================ ARCHIVOS ============================

        const int UnusedMinMB = 20;
        const int UnusedDays = 180;
        Panel pgFiles;
        Segmented segFiles;
        Label lblFilesDesc, lblFindRes;
        ListView lvFiles;
        Button btnFind, btnOpen, btnMove;
        int filesMode;

        void BuildFiles()
        {
            pgFiles = NewPage("Archivos");
            segFiles = new Segmented();
            segFiles.Font = Font;
            segFiles.Items = new string[] { "Grandes sin usar", "Duplicados", "Descargas" };
            segFiles.SetSilently(filesMode);
            segFiles.Changed += delegate
            {
                filesMode = segFiles.SelectedIndex;
                lvFiles.Items.Clear();
                UpdateFilesMode();
            };
            pgFiles.Controls.Add(segFiles);
            lblFilesDesc = Lab(pgFiles, "", Font, cSoft);
            btnFind = Btn(pgFiles, "Buscar", OnFind);
            lblFindRes = Lab(pgFiles, "", fBold, cText);
            lvFiles = LV(pgFiles, true, "Archivo", 300, "Ocupa", 90, "Último uso", 150, "Carpeta", 420);
            btnOpen = Btn(pgFiles, "Ver en su carpeta", OnOpenFolder);
            btnMove = Btn(pgFiles, "Retirar los marcados", OnMoveFiles);
            Primary(btnMove);
            UpdateFilesMode();
            pgFiles.Resize += delegate { LayoutFiles(); };
        }

        void UpdateFilesMode()
        {
            lblFindRes.Text = "";
            if (filesMode == 0)
            {
                lblFilesDesc.Text = "Busca en Descargas, Escritorio, Documentos, Vídeos, Música e Imágenes los archivos de más de " + UnusedMinMB +
                    " MB que llevas más de 6 meses sin abrir ni cambiar. Ligero solo te los enseña: tú marcas cuáles retirar.";
                btnFind.Text = "Buscar archivos sin usar";
                lvFiles.Columns[2].Text = "Último uso";
            }
            else if (filesMode == 1)
            {
                lblFilesDesc.Text = "Busca archivos repetidos (mismo contenido exacto, aunque tengan otro nombre) de más de 1 MB en tus carpetas " +
                    "personales. De cada grupo se propone conservar el más antiguo y vienen marcadas las copias sobrantes: revísalo antes de retirar.";
                btnFind.Text = "Buscar duplicados";
                lvFiles.Columns[2].Text = "Grupo";
            }
            else
            {
                lblFilesDesc.Text = "Todo lo que hay en tu carpeta Descargas de más de 1 MB, de mayor a menor. Suele llenarse de instaladores " +
                    "y archivos que ya no hacen falta. Marca lo que quieras retirar.";
                btnFind.Text = "Ver mis descargas";
                lvFiles.Columns[2].Text = "Descargado";
            }
        }

        void LayoutFiles()
        {
            int w = pgFiles.ClientSize.Width, h = pgFiles.ClientSize.Height, m = P(16);
            if (w < 50 || h < 50) return;
            segFiles.SetBounds(m, P(12), Math.Min(P(460), w - 2 * m), P(36));
            lblFilesDesc.SetBounds(m, P(56), w - 2 * m, P(44));
            btnFind.SetBounds(m, P(104), P(210), P(34));
            lblFindRes.SetBounds(m + P(222), P(110), w - 2 * m - P(222), P(24));
            int by = h - P(46);
            lvFiles.SetBounds(m, P(148), w - 2 * m, Math.Max(P(60), by - P(148) - P(8)));
            btnOpen.SetBounds(m, by, P(160), P(34));
            btnMove.SetBounds(m + P(168), by, P(200), P(34));
        }

        void OnFind(object sender, EventArgs e)
        {
            int mode = filesMode;
            List<OldFile> found = new List<OldFile>();
            List<string> third = new List<string>();
            List<bool> check = new List<bool>();
            Run(mode == 1 ? "Comparando archivos para encontrar duplicados… (puede tardar)" : "Buscando archivos…", delegate
            {
                if (mode == 1)
                {
                    foreach (DupFile d in Duplicates.Find(Finder.UserFolders(), 1))
                    {
                        OldFile o = new OldFile();
                        o.Path = d.Path; o.Size = d.Size; o.LastUse = d.Modified;
                        found.Add(o);
                        third.Add("Grupo " + d.Group + (d.Keep ? " · se conserva" : " · copia"));
                        check.Add(!d.Keep);
                    }
                }
                else
                {
                    found = mode == 0 ? Finder.FindUnused(Finder.UserFolders(), UnusedMinMB, UnusedDays) : Duplicates.Downloads();
                    foreach (OldFile o in found) { third.Add(o.LastUse.ToString("dd/MM/yyyy")); check.Add(false); }
                }
            }, delegate
            {
                long total = 0, extra = 0;
                lvFiles.BeginUpdate();
                lvFiles.Items.Clear();
                for (int i = 0; i < found.Count; i++)
                {
                    OldFile f = found[i];
                    ListViewItem it = Item(mode == 1 && !check[i] ? cSoft : cText,
                        Path.GetFileName(f.Path), Util.Size(f.Size), third[i], Path.GetDirectoryName(f.Path));
                    it.Tag = f;
                    it.Checked = check[i];
                    lvFiles.Items.Add(it);
                    total += f.Size;
                    if (check[i]) extra += f.Size;
                }
                lvFiles.EndUpdate();
                if (found.Count == 0) lblFindRes.Text = mode == 1 ? "No se encontraron archivos repetidos." : "No se encontró nada.";
                else if (mode == 1) lblFindRes.Text = "Las copias sobrantes ocupan " + Util.Size(extra) + ". Revisa las marcas antes de retirar.";
                else lblFindRes.Text = found.Count + " archivos que ocupan " + Util.Size(total) + ". Marca los que quieras retirar.";
                SetStatus("Búsqueda terminada.");
            });
        }

        void OnOpenFolder(object sender, EventArgs e)
        {
            if (lvFiles.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Toca primero un archivo de la lista.", "Ligero");
                return;
            }
            OldFile f = (OldFile)lvFiles.SelectedItems[0].Tag;
            try { Process.Start("explorer.exe", "/select,\"" + f.Path + "\""); } catch { }
        }

        void OnMoveFiles(object sender, EventArgs e)
        {
            List<string> paths = new List<string>();
            long total = 0;
            foreach (ListViewItem it in lvFiles.CheckedItems)
            {
                OldFile f = (OldFile)it.Tag;
                paths.Add(f.Path);
                total += f.Size;
            }
            if (paths.Count == 0)
            {
                MessageBox.Show(this, "Marca la casilla de los archivos que quieras retirar.", "Ligero");
                return;
            }
            if (MessageBox.Show(this,
                "Se retirarán " + paths.Count + " archivos (" + Util.Size(total) + ") a la zona de seguridad.\n\n" +
                "No se borran: puedes devolverlos a su sitio desde la pestaña Deshacer cuando quieras.\n\n¿Continuar?",
                "Retirar archivos", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            int moved = 0, failed = 0;
            long bytes = 0;
            Run("Retirando archivos…", delegate
            {
                moved = Finder.MoveFiles(paths, out bytes, out failed);
            }, delegate
            {
                for (int i = lvFiles.Items.Count - 1; i >= 0; i--)
                {
                    OldFile f = (OldFile)lvFiles.Items[i].Tag;
                    if (lvFiles.Items[i].Checked && !File.Exists(f.Path)) lvFiles.Items.RemoveAt(i);
                }
                lblFindRes.Text = moved + " archivos retirados (" + Util.Size(bytes) + ")." +
                    (failed > 0 ? " " + failed + " estaban en uso y no se tocaron." : "") + " Puedes recuperarlos en Deshacer.";
                SetStatus("Archivos retirados a la zona de seguridad.");
            });
        }

        // ============================ RED ============================

        Panel pgNet;
        Label lblNetDesc;
        Button btnNetRun, btnNetFix, btnNetDeep;
        ListView lvNet;
        List<NetItem> netItems;

        void BuildNet()
        {
            pgNet = NewPage("Red");
            lblNetDesc = Lab(pgNet,
                "Si internet va mal o no va, pulsa «Diagnosticar»: Ligero comprueba paso a paso la conexión, el router, la salida a " +
                "internet, los nombres de las webs y el proxy, y te dice en cuál está el fallo. Las comprobaciones no cambian nada.", Font, cSoft);
            btnNetRun = Btn(pgNet, "Diagnosticar la conexión", OnNetRun);
            Primary(btnNetRun);
            lvNet = LV(pgNet, false, "Comprobación", 200, "Resultado", 500, "Arreglo disponible", 200);
            btnNetFix = Btn(pgNet, "Aplicar el arreglo elegido", OnNetFix);
            btnNetDeep = Btn(pgNet, "Reparación a fondo…", OnNetDeep);
            if (netItems != null) ShowNet();
            pgNet.Resize += delegate { LayoutNet(); };
        }

        void LayoutNet()
        {
            int w = pgNet.ClientSize.Width, h = pgNet.ClientSize.Height, m = P(16);
            if (w < 50 || h < 50) return;
            lblNetDesc.SetBounds(m, P(10), w - 2 * m, P(46));
            btnNetRun.SetBounds(m, P(62), P(230), P(38));
            int by = h - P(46), top = P(112);
            lvNet.SetBounds(m, top, w - 2 * m, Math.Max(P(60), by - top - P(10)));
            btnNetFix.SetBounds(m, by, P(220), P(34));
            btnNetDeep.SetBounds(m + P(228), by, P(190), P(34));
        }

        void OnNetRun(object sender, EventArgs e)
        {
            List<NetItem> items = null;
            Run("Comprobando la conexión…", delegate { items = NetDoctor.Check(); }, delegate
            {
                netItems = items;
                ShowNet();
                int bad = 0;
                foreach (NetItem n in netItems) if (n.Level == 2) bad++;
                SetStatus(bad == 0 ? "Diagnóstico terminado: la conexión funciona." : "Diagnóstico terminado: hay " + bad + " fallo(s).");
            });
        }

        void ShowNet()
        {
            lvNet.BeginUpdate();
            lvNet.Items.Clear();
            foreach (NetItem n in netItems)
            {
                ListViewItem it = Item(n.Level == 0 ? cText : RiskColor(n.Level), n.Title, (n.Level == 0 ? "✔  " : "") + n.Detail, n.FixLabel);
                it.Tag = n;
                lvNet.Items.Add(it);
            }
            lvNet.EndUpdate();
        }

        void OnNetFix(object sender, EventArgs e)
        {
            NetItem n = lvNet.SelectedItems.Count > 0 ? (NetItem)lvNet.SelectedItems[0].Tag : null;
            if (n == null || n.Fix.Length == 0)
            {
                MessageBox.Show(this, n == null ? "Diagnostica primero y toca la fila que quieras arreglar."
                    : "Esta comprobación no tiene un arreglo automático.", "Ligero");
                return;
            }
            string msg = "";
            Run("Aplicando: " + n.FixLabel + "…", delegate { msg = NetDoctor.Fix(n.Fix); }, delegate
            {
                SetStatus(msg);
                MessageBox.Show(this, msg, "Ligero", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }

        void OnNetDeep(object sender, EventArgs e)
        {
            if (!Util.IsAdmin)
            {
                AskAdmin("La reparación a fondo reinicia componentes de red de Windows que están protegidos.");
                return;
            }
            if (MessageBox.Show(this,
                "La reparación a fondo devuelve los componentes de red de Windows a su estado de fábrica.\n\n" +
                "Úsala solo si nada más funciona. Se pierden los ajustes de red puestos a mano (dirección fija, DNS manual) y " +
                "algunos programas de VPN pueden necesitar reinstalarse. NO se puede deshacer y hay que reiniciar el equipo.\n\n¿Continuar?",
                "Reparación a fondo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            string msg = "";
            Run("Reiniciando los componentes de red…", delegate { msg = NetDoctor.Fix("winsock"); }, delegate
            {
                SetStatus(msg);
                MessageBox.Show(this, msg, "Ligero", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }

        // ============================ PROTECCIÓN ============================

        Panel pgProt;
        Label lblProtDesc, cardAv, cardFw, cardUp;
        Button btnProtRefresh, btnSecCenter, btnUpdates, btnTaskbar, btnDefaults;
        SecurityInfo secInfo;

        void BuildProtect()
        {
            pgProt = NewPage("Protección");
            lblProtDesc = Lab(pgProt,
                "Ligero no sustituye a tu antivirus. Aquí ves de un vistazo si la protección del equipo está en orden y tienes a mano " +
                "las reparaciones más habituales de Windows.", Font, cSoft);
            cardAv = Info(pgProt, "Antivirus\nComprobando…");
            cardFw = Info(pgProt, "Cortafuegos\nComprobando…");
            cardUp = Info(pgProt, "Actualizaciones de Windows\nComprobando…");
            btnProtRefresh = Btn(pgProt, "Volver a comprobar", delegate { LoadProtect(); });
            btnSecCenter = Btn(pgProt, "Abrir Seguridad de Windows", delegate { Launch("windowsdefender:", null); });
            btnUpdates = Btn(pgProt, "Buscar actualizaciones", delegate { Launch("ms-settings:windowsupdate", null); });
            btnDefaults = Btn(pgProt, "Apps predeterminadas", delegate { Launch("ms-settings:defaultapps", null); });
            btnTaskbar = Btn(pgProt, "Reparar barra de tareas", OnRestartExplorer);
            if (secInfo != null) ShowProtect();
            pgProt.Resize += delegate { LayoutProtect(); };
        }

        void LayoutProtect()
        {
            int w = pgProt.ClientSize.Width, h = pgProt.ClientSize.Height, m = P(16);
            if (w < 50 || h < 50) return;
            lblProtDesc.SetBounds(m, P(10), w - 2 * m, P(46));
            int ch = P(74), y = P(62);
            cardAv.SetBounds(m, y, w - 2 * m, ch);
            cardFw.SetBounds(m, y + ch + P(10), w - 2 * m, ch);
            cardUp.SetBounds(m, y + 2 * (ch + P(10)), w - 2 * m, ch);
            int by = y + 3 * (ch + P(10)) + P(6);
            btnSecCenter.SetBounds(m, by, P(230), P(36));
            btnUpdates.SetBounds(m + P(238), by, P(200), P(36));
            btnProtRefresh.SetBounds(m + P(446), by, P(170), P(36));
            btnTaskbar.SetBounds(m, by + P(46), P(230), P(36));
            btnDefaults.SetBounds(m + P(238), by + P(46), P(200), P(36));
        }

        void LoadProtect()
        {
            SecurityInfo info = null;
            Run("Comprobando la protección del equipo…", delegate { info = Security.Read(); }, delegate
            {
                secInfo = info;
                ShowProtect();
                SetStatus("Protección comprobada.");
            });
        }

        void ShowProtect()
        {
            SecurityInfo s = secInfo;
            if (!s.Known)
            {
                cardAv.ForeColor = cAmber;
                cardAv.Text = "Antivirus\nNo se pudo consultar. Compruébalo en «Seguridad de Windows».";
            }
            else if (s.AvOn)
            {
                cardAv.ForeColor = s.AvUpdated ? cGood : cAmber;
                cardAv.Text = "Antivirus  ·  " + (s.AvUpdated ? "protegido" : "desactualizado") + "\n" + s.Antivirus + " está activo" +
                    (s.AvUpdated ? " y con las definiciones al día." : ", pero sus definiciones de virus están anticuadas: ábrelo y actualízalo.");
            }
            else
            {
                cardAv.ForeColor = cBad;
                cardAv.Text = "Antivirus  ·  SIN PROTECCIÓN\nNo hay ningún antivirus activo. Abre «Seguridad de Windows» y activa la protección contra virus.";
            }
            cardFw.ForeColor = s.FirewallOn ? cGood : cBad;
            cardFw.Text = s.FirewallOn
                ? "Cortafuegos  ·  activo\n" + s.Firewall + " controla qué programas pueden recibir conexiones de la red."
                : "Cortafuegos  ·  DESACTIVADO\nCualquier programa puede recibir conexiones desde la red. Actívalo en «Seguridad de Windows».";
            cardUp.ForeColor = s.RebootPending ? cAmber : cText;
            cardUp.Text = s.RebootPending
                ? "Actualizaciones de Windows  ·  falta reiniciar\nHay actualizaciones instaladas a medias. Reinicia el equipo para terminarlas."
                : "Actualizaciones de Windows\nNo hay reinicios pendientes. Pulsa «Buscar actualizaciones» para ver si hay nuevas: mantener Windows al día es la mejor protección.";
        }

        void OnRestartExplorer(object sender, EventArgs e)
        {
            if (MessageBox.Show(this,
                "Qué hace: reinicia el Explorador de Windows, que es quien dibuja la barra de tareas, el menú Inicio y el escritorio. " +
                "Arregla la barra de tareas bloqueada, iconos que no aparecen o el menú Inicio que no abre.\n\n" +
                "La barra desaparecerá un par de segundos y se cerrarán las ventanas de carpetas abiertas. Tus programas no se cierran.\n\n¿Reiniciarlo?",
                "Reparar barra de tareas", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Run("Reiniciando el Explorador de Windows…", delegate
            {
                foreach (Process p in Process.GetProcessesByName("explorer"))
                {
                    try { p.Kill(); } catch { } finally { p.Dispose(); }
                }
                Thread.Sleep(2500);
                if (Process.GetProcessesByName("explorer").Length == 0) Process.Start("explorer.exe");
                History.Add("close", "Explorador de Windows reiniciado", "Reparación de la barra de tareas", History.Info, "", 0, null);
            }, delegate { SetStatus("Explorador de Windows reiniciado."); });
        }

        // ============================ HERRAMIENTAS ============================

        Panel pgTools;
        Label lblToolsDesc;
        Tile[] toolTiles;

        void BuildTools()
        {
            pgTools = NewPage("Herramientas");
            lblToolsDesc = Lab(pgTools,
                "Utilidades que ya vienen con Windows, a un clic. Ligero solo las abre: no instala nada ni se queda en segundo plano.", Font, cSoft);
            toolTiles = new Tile[] {
                MakeTile(pgTools, "", "Captura de pantalla", "Recorta una zona de la pantalla y la copia", delegate { Launch("ms-screenclip:", null); }),
                MakeTile(pgTools, "", "Calculadora", "La calculadora de Windows", delegate { Launch("calc.exe", null); }),
                MakeTile(pgTools, "", "Bloc de notas", "Para apuntar algo rápido", delegate { Launch("notepad.exe", null); }),
                MakeTile(pgTools, "", "Administrador de tareas", "Todo lo que está en marcha, al detalle", delegate { Launch("taskmgr.exe", null); }),
                MakeTile(pgTools, "", "Monitor de recursos", "Quién usa el disco, la red y la memoria", delegate { Launch("resmon.exe", null); }),
                MakeTile(pgTools, "", "Información del sistema", "Modelo, procesador, memoria instalada", delegate { Launch("msinfo32.exe", null); }),
                MakeTile(pgTools, "", "Almacenamiento", "El desglose de espacio de Windows", delegate { Launch("ms-settings:storagesense", null); }),
                MakeTile(pgTools, "", "Pantalla", "Resolución, escala y brillo", delegate { Launch("ms-settings:display", null); }),
                MakeTile(pgTools, "", "Batería y energía", "Qué gasta la batería y modo de ahorro", delegate { Launch("ms-settings:powersleep", null); }),
                MakeTile(pgTools, "", "Teclado en pantalla", "Si falla una tecla del teclado", delegate { Launch("osk.exe", null); }),
                MakeTile(pgTools, "", "Lupa", "Amplía una parte de la pantalla", delegate { Launch("magnify.exe", null); }),
                MakeTile(pgTools, "", "Restaurar sistema", "Volver a un punto anterior de Windows", delegate { Launch("rstrui.exe", null); })
            };
            pgTools.Resize += delegate { LayoutTools(); };
        }

        void LayoutTools()
        {
            int w = pgTools.ClientSize.Width, h = pgTools.ClientSize.Height, m = P(16);
            if (w < 50 || h < 50) return;
            lblToolsDesc.SetBounds(m, P(10), w - 2 * m, P(30));
            int cols = w > P(780) ? 4 : 3, gap = P(12);
            int tw = (w - 2 * m - gap * (cols - 1)) / cols, th = P(84);
            for (int i = 0; i < toolTiles.Length; i++)
                toolTiles[i].SetBounds(m + (i % cols) * (tw + gap), P(48) + (i / cols) * (th + gap), tw, th);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyChrome();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyChrome();
        }

        protected override void WndProc(ref Message m)
        {
            // WM_DPICHANGED: la ventana pasó a un monitor con otra escala. Se adapta para seguir nítida.
            if (m.Msg == 0x02E0)
            {
                try
                {
                    float fresh = (((long)m.WParam >> 16) & 0xFFFF) / 96f;
                    if (fresh > 0.4f && Math.Abs(fresh - dpiScale) > 0.01f)
                    {
                        Native.RECT rc = (Native.RECT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
                        dpiScale = fresh;
                        MinimumSize = Size.Empty;
                        SetBounds(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
                        Rectangle wa = Screen.FromRectangle(Bounds).WorkingArea;
                        MinimumSize = new Size(Math.Min((int)(1000 * dpiScale), wa.Width), Math.Min((int)(620 * dpiScale), wa.Height));
                        if (busy) pendingRebuild = true;
                        else BeginInvoke((MethodInvoker)RebuildUI);
                    }
                }
                catch { }
            }
            base.WndProc(ref m);
        }

        /// <summary>Barra de título clara u oscura y efecto translúcido de la cabecera.</summary>
        void ApplyChrome()
        {
            if (!IsHandleCreated || nav == null) return;
            try
            {
                int backdrop = !Ui.Glass ? 1 : (Prefs.Backdrop == "acrilico" ? 3 : 2);
                // Hay versiones de Windows que mantienen la barra de titulo oscura cuando el sistema esta en oscuro,
                // aunque se pida otro color. Para que su texto se lea siempre, en ese caso se deja en modo oscuro.
                bool titleDark = Ui.Dark || Ui.SystemDark();
                bool auto = (Ui.Glass && Ui.Dark) || titleDark != Ui.Dark;
                Native.SetChrome(Handle, titleDark, backdrop, Ui.Glass ? nav.Width : 0, 0,
                    auto ? unchecked((int)0xFFFFFFFF) : ColorRef(cPanel), unchecked((int)0xFFFFFFFF));
            }
            catch { }
        }

        void LayoutAll()
        {
            LayoutHome(); LayoutHealth(); LayoutMem(); LayoutFast(); LayoutStartup(); LayoutDisk(); LayoutFiles(); LayoutApps();
            LayoutDelete(); LayoutNet(); LayoutProtect(); LayoutTools(); LayoutUndo(); LayoutSettings();
        }

        /// <summary>Vuelve a crear la ventana con el aspecto elegido, conservando la pestaña y los datos ya cargados.</summary>
        void RebuildUI()
        {
            if (busy || IsDisposed) return;
            int index = tabs.SelectedIndex;
            Anim.Clear();
            SuspendLayout();
            List<Control> old = new List<Control>();
            foreach (Control c in Controls) old.Add(c);
            Controls.Clear();
            foreach (Control c in old) c.Dispose();
            delPath = null;
            delVerdict = null;
            ApplyPalette();
            BuildAll();
            tabs.Jump(index);
            nav.SelectedIndex = index;
            ResumeLayout(true);
            LayoutAll();
            RefreshHome();
            RefreshMem();
            RefreshProcs();
            ShowCats();
            if (apps != null) ShowApps();
            OnTabChanged();
            Invalidate(true);
            if (tourStep >= 0) ShowTourStep();
        }

        // ---------------- ayudas para construir la ventana ----------------

        Panel NewPage(string title)
        {
            Panel p = tabs.AddPage(title);
            pageIcons.Add(Glyph(title));
            p.BackColor = cBack;
            p.ForeColor = cText;
            p.Font = Font;
            return p;
        }

        ListViewItem Item(Color color, params string[] cells)
        {
            ListViewItem it = new ListViewItem(cells);
            it.ForeColor = color;
            it.BackColor = cBack;
            return it;
        }

        /// <summary>Botón destacado: la acción principal de cada pestaña.</summary>
        void Primary(Button b)
        {
            b.Font = fBold;
            RoundButton rb = b as RoundButton;
            if (rb != null) rb.Kind = 1;
        }

        Label Lab(Control parent, string text, Font f, Color c)
        {
            Label l = new PlainLabel();
            l.AutoSize = false;
            l.Text = text;
            l.Font = f;
            l.ForeColor = c;
            l.UseMnemonic = false;
            parent.Controls.Add(l);
            return l;
        }

        /// <summary>Recuadro de explicación ("qué es / qué hace").</summary>
        Label Info(Control parent, string text)
        {
            CardLabel l = new CardLabel();
            l.AutoSize = false;
            l.Text = text;
            l.Font = Font;
            l.ForeColor = cText;
            l.BackColor = cInfo;
            l.UseMnemonic = false;
            l.Padding = new Padding(P(14), P(10), P(14), P(10));
            parent.Controls.Add(l);
            return l;
        }

        Button Btn(Control parent, string text, EventHandler onClick)
        {
            RoundButton b = new RoundButton();
            b.Text = text;
            b.Font = Font;
            b.UseMnemonic = false;
            b.Click += onClick;
            parent.Controls.Add(b);
            return b;
        }

        ListView LV(Control parent, bool checks, params object[] cols)
        {
            SmoothList lv = new SmoothList();
            lv.CheckBoxes = checks;
            lv.BackColor = cBack;
            lv.ForeColor = cText;
            lv.Font = Font;
            for (int i = 0; i + 1 < cols.Length; i += 2)
                lv.Columns.Add((string)cols[i], P((int)cols[i + 1]));
            // La última columna se estira hasta el borde para que el resaltado de la fila llegue al final.
            int last = lv.Columns.Count - 1;
            int lastBase = lv.Columns[last].Width;
            lv.Resize += delegate
            {
                int used = 0;
                for (int i = 0; i < last; i++) used += lv.Columns[i].Width;
                int want = Math.Max(P(120), lv.Width - used - SystemInformation.VerticalScrollBarWidth - P(4));
                if (lv.Columns[last].Width != want) lv.Columns[last].Width = want;
            };
            // Marco fino alrededor de la lista, dibujado por la página.
            parent.Paint += delegate(object s, PaintEventArgs e)
            {
                if (lv.IsDisposed || !lv.Visible) return;
                Rectangle r = lv.Bounds;
                using (Pen p = new Pen(cBorder))
                    e.Graphics.DrawRectangle(p, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
            };
            EventHandler moved = delegate { if (!parent.IsDisposed) parent.Invalidate(); };
            lv.Move += moved;
            lv.Resize += moved;
            parent.Controls.Add(lv);
            return lv;
        }

        Color RiskColor(int level)
        {
            return level <= 0 ? cGood : (level == 1 ? cAmber : cBad);
        }

        void SetStatus(string text)
        {
            status.Text = text;
            Label target = status;
            Color from = cBack, to = cSoft;
            Anim.Run(target, 320, delegate(float t) { if (!target.IsDisposed) target.ForeColor = Ui.Mix(from, to, t); });
        }

        /// <summary>Ejecuta el trabajo pesado fuera de la ventana para que nunca se quede congelada.</summary>
        void Run(string message, Action work, Action done)
        {
            if (busy) return;
            busy = true;
            tabs.Enabled = false;
            UseWaitCursor = true;
            busyLine.Active = true;
            SetStatus(message);
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception err = null;
                try { work(); } catch (Exception ex) { err = ex; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        busy = false;
                        tabs.Enabled = true;
                        UseWaitCursor = false;
                        busyLine.Active = false;
                        if (err != null)
                        {
                            SetStatus("No se pudo completar la tarea.");
                            ShowError(err);
                        }
                        else
                        {
                            try { done(); } catch (Exception ex2) { ShowError(ex2); }
                        }
                        if (pendingRebuild && !busy)
                        {
                            pendingRebuild = false;
                            RebuildUI();
                        }
                        // Tras una tarea pesada quedan restos en memoria: se descartan.
                        if (!busy) Housekeep(WindowState == FormWindowState.Minimized);
                    });
                }
                catch { busy = false; }
            });
        }

        // ============================ PERSONALIZAR ============================

        Panel pgSet;
        Segmented segSetGroup, segTheme, segBack, segCorners, segSide, segStart;
        Swatches swAccent;
        Toggle togAnim, togBig, togAuto;
        Button btnResetLook, btnTour, btnRescan;
        Label lblSetIntro, lblProfile;
        int setGroup;
        readonly List<Label> rowTitles = new List<Label>(), rowDescs = new List<Label>();
        readonly List<Control> rowCtrls = new List<Control>();
        readonly List<int> rowGroups = new List<int>();

        Segmented Seg(string[] items, int selected, Action<int> changed)
        {
            Segmented s = new Segmented();
            s.Font = Font;
            s.Items = items;
            s.SetSilently(selected);
            s.Changed += delegate { changed(s.SelectedIndex); };
            return s;
        }

        void AddRow(int group, string title, string desc, Control c)
        {
            rowTitles.Add(Lab(pgSet, title, fBold, cText));
            rowDescs.Add(Lab(pgSet, desc, Font, cSoft));
            rowCtrls.Add(c);
            rowGroups.Add(group);
            pgSet.Controls.Add(c);
        }

        string ProfileText()
        {
            if (Prefs.Tier.Length == 0) return "Tu equipo\nTodavía no se ha analizado. Pulsa «Volver a analizar el equipo».";
            return "Tu equipo: gama " + Prefs.Tier + "\n" + Prefs.Specs + "\nLigero ajustó sus animaciones, el efecto translúcido y la optimización inteligente a esta gama.";
        }

        void BuildSettings()
        {
            pgSet = NewPage("Personalizar");
            rowTitles.Clear(); rowDescs.Clear(); rowCtrls.Clear(); rowGroups.Clear();
            lblSetIntro = Lab(pgSet, "Deja Ligero a tu gusto. Los cambios se aplican al momento y se recuerdan para la próxima vez.", Font, cSoft);
            segSetGroup = Seg(new string[] { "Aspecto", "Comportamiento" }, setGroup, delegate(int i) { setGroup = i; LayoutSettings(); });
            pgSet.Controls.Add(segSetGroup);

            // ---- Aspecto ----
            segTheme = Seg(new string[] { "Como Windows", "Claro", "Oscuro" },
                Prefs.Theme == "claro" ? 1 : (Prefs.Theme == "oscuro" ? 2 : 0), delegate(int i)
                {
                    Prefs.Theme = new string[] { "sistema", "claro", "oscuro" }[i];
                    ApplyLook();
                });
            AddRow(0, "Tema", "Claro, oscuro, o el mismo que tenga Windows en cada momento.", segTheme);

            swAccent = new Swatches();
            Color[] cols = new Color[Ui.AccentNames.Length];
            for (int i = 0; i < cols.Length; i++) cols[i] = Ui.AccentBase(Ui.AccentNames[i]);
            swAccent.Colors = cols;
            int current = Math.Max(0, Array.IndexOf(Ui.AccentNames, Prefs.Accent));
            swAccent.SetSilently(current);
            swAccent.Changed += delegate
            {
                string name = Ui.AccentNames[swAccent.SelectedIndex];
                if (name == "personal")
                {
                    using (ColorDialog d = new ColorDialog())
                    {
                        d.FullOpen = true;
                        d.Color = Ui.AccentBase("personal");
                        if (d.ShowDialog(this) != DialogResult.OK) { swAccent.SetSilently(current); return; }
                        Prefs.CustomColor = (d.Color.R << 16) | (d.Color.G << 8) | d.Color.B;
                    }
                }
                Prefs.Accent = name;
                ApplyLook();
            };
            AddRow(0, "Color de acento", "El color de botones, marcas y la sección activa. El penúltimo es el de Windows; el último lo eliges tú entre todos los colores.", swAccent);

            segBack = Seg(new string[] { "Sin efecto", "Mica", "Acrílico" },
                Prefs.Backdrop == "ninguno" ? 0 : (Prefs.Backdrop == "acrilico" ? 2 : 1), delegate(int i)
                {
                    Prefs.Backdrop = new string[] { "ninguno", "mica", "acrilico" }[i];
                    ApplyLook();
                });
            segBack.Enabled = Ui.IsWin11;
            AddRow(0, "Efecto translúcido", Ui.IsWin11
                ? "La barra lateral deja ver el fondo. Gasta algo de tarjeta gráfica; en equipos modestos es mejor «Sin efecto»."
                : "Disponible solo en Windows 11.", segBack);

            segCorners = Seg(new string[] { "Rectas", "Suaves", "Redondas" }, Prefs.Corners, delegate(int i) { Prefs.Corners = i; ApplyLook(); });
            AddRow(0, "Esquinas", "La forma de los botones, las tarjetas y los selectores.", segCorners);

            segSide = Seg(new string[] { "Con nombres", "Solo iconos" }, Prefs.Compact ? 1 : 0, delegate(int i) { Prefs.Compact = i == 1; ApplyLook(); });
            AddRow(0, "Barra lateral", "Con el nombre de cada sección, o estrecha con solo los iconos para dejar más sitio al contenido.", segSide);

            togBig = new Toggle();
            togBig.Font = Font;
            togBig.SetSilently(Prefs.BigText);
            togBig.Text = Prefs.BigText ? "Texto grande" : "Texto normal";
            togBig.CheckedChanged += delegate { Prefs.BigText = togBig.Checked; ApplyLook(); };
            AddRow(0, "Tamaño del texto", "Agranda las letras y los botones de toda la aplicación.", togBig);

            // ---- Comportamiento ----
            togAnim = new Toggle();
            togAnim.Font = Font;
            togAnim.SetSilently(Prefs.Animations);
            togAnim.Text = Prefs.Animations ? "Activadas" : "Desactivadas";
            togAnim.CheckedChanged += delegate
            {
                Prefs.Animations = togAnim.Checked;
                Ui.Anim = togAnim.Checked;
                togAnim.Text = togAnim.Checked ? "Activadas" : "Desactivadas";
                Prefs.Save();
            };
            AddRow(1, "Animaciones", "Transiciones al pasar el ratón, al cambiar de sección y en los indicadores. Si el equipo va justo, apágalas: todo funciona igual.", togAnim);

            segStart = Seg(new string[] { "Inicio", "Chequeo", "Memoria", "Disco" }, Prefs.StartPage, delegate(int i) { Prefs.StartPage = i; Prefs.Save(); });
            AddRow(1, "Sección al abrir", "La que aparece primero cada vez que abres Ligero.", segStart);

            togAuto = new Toggle();
            togAuto.Font = Font;
            togAuto.SetSilently(Prefs.AutoStart);
            togAuto.Text = Prefs.AutoStart ? "Arranca con Windows" : "No arranca solo";
            togAuto.CheckedChanged += delegate
            {
                bool on = togAuto.Checked;
                try
                {
                    AutoStart.Set(on);
                    Prefs.AutoStart = on;
                    if (on) Prefs.SmartBoost = true;   // para eso sirve tenerlo en segundo plano
                    Prefs.Save();
                    togAuto.Text = on ? "Arranca con Windows" : "No arranca solo";
                    if (!togSmart.IsDisposed) togSmart.SetSilently(Prefs.SmartBoost);
                    SetStatus(on ? "Ligero arrancará con Windows, oculto junto al reloj. Si mueves la carpeta de Ligero, vuelve a activar esta opción."
                                 : "Ligero ya no arrancará con Windows.");
                }
                catch (Exception ex)
                {
                    togAuto.SetSilently(!on);
                    ShowError(ex);
                }
            };
            AddRow(1, "Iniciar con Windows", "Ligero arranca solo al encender y se queda oculto junto al reloj, para que la optimización inteligente " +
                "trabaje sin tener que abrirlo. En reposo ocupa muy poca memoria. Activa también la optimización inteligente.", togAuto);

            lblProfile = Info(pgSet, ProfileText());
            btnRescan = Btn(pgSet, "Volver a analizar el equipo", delegate
            {
                if (MessageBox.Show(this, "Ligero volverá a medir el procesador, la memoria, el disco y la gráfica, y ajustará de nuevo sus animaciones, " +
                    "el efecto translúcido y la optimización inteligente a la gama que resulte. No cambia nada de Windows.\n\n¿Analizar?",
                    "Analizar el equipo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) RunRecon(false);
            });
            btnTour = Btn(pgSet, "Ver la introducción", delegate { StartTour(); });
            btnResetLook = Btn(pgSet, "Restablecer aspecto", delegate { Prefs.Reset(); ApplyLook(); });
            pgSet.Resize += delegate { LayoutSettings(); };
        }

        void LayoutSettings()
        {
            int w = pgSet.ClientSize.Width, h = pgSet.ClientSize.Height, m = P(16);
            if (w < 50 || h < 50) return;
            lblSetIntro.SetBounds(m, P(10), w - 2 * m, P(24));
            segSetGroup.SetBounds(m, P(40), Math.Min(P(320), w - 2 * m), P(36));
            int count = 0;
            for (int i = 0; i < rowCtrls.Count; i++) if (rowGroups[i] == setGroup) count++;
            int top = P(90), bottom = P(56) + (setGroup == 1 ? P(98) : 0);
            int cw = P(340);
            int cx = Math.Max(m + P(300), w - m - cw);
            int row = Math.Max(P(58), Math.Min(P(80), (h - top - bottom) / Math.Max(1, count)));
            int n = 0;
            for (int i = 0; i < rowCtrls.Count; i++)
            {
                bool show = rowGroups[i] == setGroup;
                rowTitles[i].Visible = rowDescs[i].Visible = rowCtrls[i].Visible = show;
                if (!show) continue;
                int y = top + n * row;
                rowTitles[i].SetBounds(m, y, cx - m - P(24), P(22));
                rowDescs[i].SetBounds(m, y + P(22), cx - m - P(24), row - P(26));
                rowCtrls[i].SetBounds(cx, y + P(4), cw, P(36));
                n++;
            }
            lblProfile.Visible = setGroup == 1;
            lblProfile.SetBounds(m, top + n * row + P(4), w - 2 * m, P(88));
            int by = h - P(46);
            btnResetLook.Visible = setGroup == 0;
            btnTour.Visible = btnRescan.Visible = setGroup == 1;
            btnResetLook.SetBounds(m, by, P(200), P(34));
            btnTour.SetBounds(m, by, P(190), P(34));
            btnRescan.SetBounds(m + P(198), by, P(240), P(34));
        }

        /// <summary>Guarda las preferencias y rehace la ventana con el aspecto nuevo.</summary>
        void ApplyLook()
        {
            Prefs.Save();
            if (IsHandleCreated) BeginInvoke((MethodInvoker)RebuildUI);
        }

        // ============================ RECONOCIMIENTO DEL EQUIPO ============================

        /// <summary>Para pruebas: no lanzar el reconocimiento ni la introducción al abrir por primera vez.</summary>
        public static bool SkipWelcome;

        /// <summary>Mide el equipo, decide su gama y ajusta Ligero a ella. Solo cambia ajustes de la propia aplicación.</summary>
        void RunRecon(bool tourAfter)
        {
            SystemProfile prof = null;
            Run("Reconociendo tu equipo: procesador, memoria, disco y gráfica…", delegate { prof = Recon.Scan(); }, delegate
            {
                Prefs.ApplyTier(prof.Tier);
                Prefs.Specs = prof.Summary;
                Prefs.Welcomed = true;
                Prefs.Save();
                History.Add("recon", "Reconocimiento del equipo: gama " + prof.Tier,
                    prof.Summary + " · Ligero ajustó su propio aspecto y comportamiento", History.Info, "", 0, null);
                RebuildUI();
                SetStatus("Equipo reconocido: gama " + prof.Tier + ". Ligero se ha ajustado a él.");
                if (tourAfter) StartTour();
            });
        }

        // ============================ INTRODUCCIÓN GUIADA ============================

        Panel tourCard;
        Label tourCount, tourTitle, tourText;
        Button tourNext, tourPrev, tourSkip, tourExtra;
        int tourStep = -1;
        List<string[]> tourSteps;

        string WelcomeText()
        {
            if (Prefs.Tier == "alta")
                return "Ligero ha reconocido tu equipo y es de gama alta, así que ha activado sus mejores efectos: fondo acrílico translúcido " +
                       "y todas las animaciones. Tu equipo no necesita recortes: úsalo para mantenerlo limpio y vigilado.";
            if (Prefs.Tier == "baja")
                return "Ligero ha reconocido tu equipo y es de gama baja, así que se ha puesto en su modo más ligero: sin animaciones ni " +
                       "transparencias, y con la optimización inteligente activada. Además puede ajustar Windows para aliviarlo (todo se puede deshacer).";
            if (Prefs.Tier == "media")
                return "Ligero ha reconocido tu equipo y es de gama media: mantiene las animaciones, quita el efecto translúcido y activa la " +
                       "optimización inteligente. Además puede ajustar Windows para que vaya más suelto (todo se puede deshacer).";
            return "Te enseño en un minuto qué hace cada sección. Puedes saltar la introducción cuando quieras y repetirla desde Personalizar.";
        }

        void StartTour()
        {
            tourSteps = new List<string[]>();
            tourSteps.Add(new string[] { "Inicio", "Bienvenido a Ligero", WelcomeText() });
            tourSteps.Add(new string[] { "Inicio", "Inicio", "De un vistazo: memoria en uso, archivos temporales y espacio en disco. «Optimizar ahora» libera memoria y retira temporales en un clic, y la limpieza se puede deshacer." });
            tourSteps.Add(new string[] { "Chequeo", "Chequeo de salud", "Revisa todo el equipo de una vez y le pone nota. Toca un punto de la lista y te lleva a la sección donde se arregla." });
            tourSteps.Add(new string[] { "Memoria", "Memoria", "Los programas que más memoria gastan ahora. Puedes cerrarlos con normalidad o forzar el cierre si se han quedado colgados." });
            tourSteps.Add(new string[] { "Acelerar", "Acelerar", "Ajustes de Windows que alivian al equipo. Cada uno explica qué hace, y todos se pueden deshacer. Los «recomendados» no tienen ninguna pega práctica." });
            tourSteps.Add(new string[] { "Arranque", "Arranque", "Lo que se abre solo al encender. Desactivar lo que no usas es de lo que más se nota: no se desinstala nada, solo deja de abrirse solo." });
            tourSteps.Add(new string[] { "Disco", "Disco", "Limpieza de temporales, cachés y restos de Windows. Nada se borra de golpe: va 7 días a una zona de seguridad por si quieres recuperarlo." });
            tourSteps.Add(new string[] { "Archivos", "Archivos", "Archivos grandes que ya no usas, duplicados y tu carpeta Descargas. Ligero solo te los enseña: tú decides cuáles retirar." });
            tourSteps.Add(new string[] { "Programas", "Programas", "Desinstala programas y limpia los restos que dejan. Antes te dice si quitarlo es seguro o si afecta a la seguridad o a otros programas." });
            tourSteps.Add(new string[] { "Eliminar", "Eliminar", "Elimina cualquier archivo o carpeta. Antes de tocar nada te avisa de a qué pertenece y qué pasaría, y lo eliminado se puede recuperar." });
            tourSteps.Add(new string[] { "Red", "Red", "Si internet falla, comprueba paso a paso la conexión, el router, la salida a internet y los nombres de las webs, y ofrece el arreglo." });
            tourSteps.Add(new string[] { "Protección", "Protección", "El estado del antivirus, el cortafuegos y las actualizaciones, con las reparaciones más habituales de Windows a mano." });
            tourSteps.Add(new string[] { "Herramientas", "Herramientas", "Utilidades que ya vienen con Windows, a un clic: captura de pantalla, calculadora, administrador de tareas y más." });
            tourSteps.Add(new string[] { "Deshacer", "Deshacer", "Todo lo que Ligero hace queda apuntado aquí. Puedes deshacer una acción, deshacerlas todas, o liberar definitivamente el espacio guardado." });
            tourSteps.Add(new string[] { "Personalizar", "Personalizar", "Tema, colores, esquinas, barra lateral, arranque con Windows… Aquí también puedes repetir esta introducción. ¡Listo!" });
            tourStep = 0;
            ShowTourStep();
        }

        void EnsureTourCard()
        {
            if (tourCard != null && !tourCard.IsDisposed) return;
            Panel card = new Page();
            card.BackColor = cInfo;
            card.Visible = false;
            card.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(cAccent, Math.Max(2f, scale * 2f)))
                    e.Graphics.DrawRectangle(p, 1, 1, card.Width - 3, card.Height - 3);
            };
            tourCard = card;
            tourCount = Lab(card, "", Font, cSoft);
            tourTitle = Lab(card, "", fTitle, cText);
            tourText = Lab(card, "", Font, cText);
            tourCount.BackColor = tourTitle.BackColor = tourText.BackColor = cInfo;
            tourSkip = Btn(card, "Saltar", delegate { EndTour(); });
            tourPrev = Btn(card, "Anterior", delegate { if (tourStep > 0) { tourStep--; ShowTourStep(); } });
            tourNext = Btn(card, "Siguiente", delegate
            {
                if (tourStep >= tourSteps.Count - 1) { EndTour(); return; }
                tourStep++;
                ShowTourStep();
            });
            Primary(tourNext);
            tourExtra = Btn(card, "Optimizar Windows para este equipo", delegate
            {
                EndTour();
                GoTo(pgFast);
                OnTweakAll(null, EventArgs.Empty);
            });
            Controls.Add(card);
            card.BringToFront();
        }

        void PlaceTourCard(bool animate)
        {
            if (tourCard == null || tourCard.IsDisposed || tourStep < 0) return;
            bool extra = tourExtra.Visible;
            int w = Math.Min(P(480), ClientSize.Width - nav.Width - P(40)), h = P(extra ? 258 : 214);
            int x = ClientSize.Width - w - P(22), y = ClientSize.Height - h - P(42);
            int pad = P(18);
            tourCount.SetBounds(pad, P(12), w - 2 * pad, P(20));
            tourTitle.SetBounds(pad, P(32), w - 2 * pad, P(32));
            tourText.SetBounds(pad, P(68), w - 2 * pad, P(86));
            int by = h - P(48);
            if (extra) tourExtra.SetBounds(pad, by - P(42), w - 2 * pad, P(34));
            tourSkip.SetBounds(pad, by, P(90), P(34));
            tourNext.SetBounds(w - pad - P(120), by, P(120), P(34));
            tourPrev.SetBounds(w - pad - P(120) - P(108), by, P(100), P(34));
            Panel card = tourCard;
            card.Visible = true;
            card.BringToFront();
            if (!animate) { card.SetBounds(x, y, w, h); return; }
            int from = y + P(26);
            card.SetBounds(x, from, w, h);
            Anim.Run(card, 220, delegate(float t) { if (!card.IsDisposed) card.Top = (int)Math.Round(from + (y - from) * t); });
        }

        void ShowTourStep()
        {
            if (tourStep < 0 || tourSteps == null) return;
            EnsureTourCard();
            string[] step = tourSteps[tourStep];
            for (int i = 0; i < tabs.TabPages.Count; i++)
                if (tabs.TabPages[i].Text == step[0] && tabs.SelectedIndex != i && !busy) tabs.SelectedIndex = i;
            tourCount.Text = "Introducción  ·  paso " + (tourStep + 1) + " de " + tourSteps.Count;
            tourTitle.Text = step[1];
            tourText.Text = step[2];
            tourPrev.Enabled = tourStep > 0;
            tourNext.Text = tourStep >= tourSteps.Count - 1 ? "Terminar" : "Siguiente";
            tourExtra.Visible = tourStep == 0 && (Prefs.Tier == "baja" || Prefs.Tier == "media");
            PlaceTourCard(true);
        }

        void EndTour()
        {
            tourStep = -1;
            if (tourCard != null && !tourCard.IsDisposed)
            {
                Controls.Remove(tourCard);
                tourCard.Dispose();
            }
            tourCard = null;
        }

        // ============================ ARRANQUE, SEGUNDO PLANO Y BANDEJA ============================

        bool inited, reallyClose;
        bool allowShow = !Program.Background;
        NotifyIcon tray;

        /// <summary>Icono propio (cuadro del color de acento con una L), para la ventana y la bandeja.</summary>
        Icon MakeIcon()
        {
            using (Bitmap b = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(b))
                using (Font f = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush t = new SolidBrush(Ui.PrimaryText))
                using (StringFormat sf = new StringFormat())
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    Ui.FillRound(g, Ui.Primary, 1, 1, 30, 30, 8);
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("L", f, t, new RectangleF(0, 0, 32, 31), sf);
                }
                return Icon.FromHandle(b.GetHicon());
            }
        }

        /// <summary>Lo que hay que hacer una sola vez al arrancar, se muestre la ventana o no.</summary>
        void InitOnce()
        {
            if (inited) return;
            inited = true;
            LayoutAll();
            RefreshHome();
            RefreshMem();
            RefreshProcs();
            timer.Start();
            // Antes de permitir nada: recuperar limpiezas que quedaran a medias en un cierre inesperado.
            try { History.AdoptOrphans(); } catch { }
            // El borrado de basura caducada puede tardar: en segundo plano para abrir al instante.
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { History.AutoPurge(7); } catch { }
            });

            Resize += delegate { PlaceTourCard(false); };
            FormClosed += delegate
            {
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
            };

            if (Prefs.StartPage > 0)
            {
                Panel[] starts = { pgHome, pgHealth, pgMem, pgDisk };
                int index = tabs.TabPages.IndexOf(starts[Prefs.StartPage]);
                tabs.Jump(index);
                nav.SelectedIndex = index;
                OnTabChanged();
            }

            // Si la carpeta de Ligero cambió de sitio, la orden de arranque con Windows se pone al día.
            if (Prefs.AutoStart && (Program.ScriptPath != null || Program.RealExe))
            {
                try { if (AutoStart.Current() != AutoStart.Command()) AutoStart.Set(true); } catch { }
            }

            if (Program.Background) SetupTray();

            // Otro Ligero que se intente abrir le pide a este que se muestre.
            if (Program.ShowSignal != null)
            {
                Thread waiter = new Thread((ThreadStart)delegate
                {
                    while (true)
                    {
                        try
                        {
                            Program.ShowSignal.WaitOne();
                            BeginInvoke((MethodInvoker)ShowFromTray);
                        }
                        catch { return; }
                    }
                });
                waiter.IsBackground = true;
                waiter.Start();
            }

            if (!Prefs.Welcomed && !Program.Background && !SkipWelcome)
                BeginInvoke((MethodInvoker)delegate { RunRecon(true); });
        }

        void SetupTray()
        {
            tray = new NotifyIcon();
            try { tray.Icon = MakeIcon(); } catch { tray.Icon = SystemIcons.Application; }
            tray.Text = "Ligero";
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Abrir Ligero", null, delegate { ShowFromTray(); });
            menu.Items.Add("Optimizar ahora", null, delegate { if (!busy) OnBoost(null, EventArgs.Empty); });
            menu.Items.Add("Salir de Ligero", null, delegate { reallyClose = true; Close(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowFromTray(); };
            tray.Visible = true;
        }

        void ShowFromTray()
        {
            allowShow = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            OnTabChanged();
        }

        /// <summary>Arrancado con Windows: la ventana no se muestra, pero todo queda en marcha.</summary>
        protected override void SetVisibleCore(bool value)
        {
            if (value && !allowShow)
            {
                if (!IsHandleCreated) CreateHandle();
                InitOnce();
                Housekeep(true);
                value = false;
            }
            base.SetVisibleCore(value);
        }

        static bool IsAccessError(Exception ex)
        {
            return ex is UnauthorizedAccessException || ex is System.Security.SecurityException;
        }

        void ShowError(Exception ex)
        {
            if (IsAccessError(ex) && !Util.IsAdmin) { AskAdmin("Windows no permite este cambio con tu usuario normal."); return; }
            MessageBox.Show(this, "No se pudo completar: " + ex.Message, "Ligero", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>Ofrece reabrir Ligero con permisos de administrador.</summary>
        void AskAdmin(string why)
        {
            if (MessageBox.Show(this, why + "\n\nHace falta abrir Ligero como administrador. Windows te pedirá permiso.\n\n¿Reabrir Ligero como administrador ahora?",
                "Permisos de administrador", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (Program.RestartAsAdmin()) Close();
        }

        void OnTabChanged()
        {
            Panel p = tabs.SelectedTab;
            if (p == pgHome) RefreshHome();
            else if (p == pgProt) { if (secInfo == null) LoadProtect(); }
            else if (p == pgMem) { RefreshMem(); RefreshProcs(); }
            else if (p == pgFast) RefreshTweaks();
            else if (p == pgStart) RefreshStartup();
            else if (p == pgDisk) RefreshDiskLabel();
            else if (p == pgApps) { if (apps == null) LoadApps(); }
            else if (p == pgUndo) RefreshHist();
        }

        // ============================ MEMORIA ============================

        void BuildMemory()
        {
            pgMem = NewPage("Memoria");
            lblMem = Lab(pgMem, "", fTitle, cText);
            barMem = new Meter();
            pgMem.Controls.Add(barMem);
            btnTrim = Btn(pgMem, "Liberar memoria ahora", OnTrim);
            Primary(btnTrim);
            lblTrimDesc = Lab(pgMem,
                "Qué hace: pide a Windows que saque de la memoria lo que los programas abiertos no están usando en este " +
                "momento. No cierra ni borra nada. Si un programa vuelve a necesitar esa memoria la recupera solo, así " +
                "que el efecto es temporal y no hay nada que deshacer.", Font, cSoft);
            lblTrimRes = Lab(pgMem, "", fBold, cGood);
            lblProcs = Lab(pgMem, "Programas que más memoria usan ahora (aproximado)", fBold, cText);
            lvProcs = LV(pgMem, false, "Programa", 320, "Memoria", 130, "Procesos", 90);
            btnClose = Btn(pgMem, "Cerrar el programa elegido", OnCloseProc);
            btnKill = Btn(pgMem, "Forzar cierre", OnKillProc);
            btnRefresh = Btn(pgMem, "Actualizar lista", delegate { RefreshMem(); RefreshProcs(); });
            lblCloseDesc = Lab(pgMem,
                "Qué hace: le pide que se cierre, igual que si pulsaras su X (te dejará guardar). Cerrar lo que no usas " +
                "es lo que más memoria libera de verdad. No se puede deshacer.", Font, cSoft);
            pgMem.Resize += delegate { LayoutMem(); };
        }

        void LayoutMem()
        {
            int w = pgMem.ClientSize.Width, h = pgMem.ClientSize.Height, m = P(16);
            if (w < 50 || h < 50) return;
            lblMem.SetBounds(m, P(12), w - 2 * m, P(30));
            barMem.SetBounds(m, P(48), w - 2 * m, P(9));
            btnTrim.SetBounds(m, P(74), P(210), P(42));
            lblTrimDesc.SetBounds(m + P(224), P(70), w - 2 * m - P(224), P(58));
            lblTrimRes.SetBounds(m, P(130), w - 2 * m, P(22));
            lblProcs.SetBounds(m, P(158), w - 2 * m, P(22));
            int by = h - P(52);
            lvProcs.SetBounds(m, P(182), w - 2 * m, Math.Max(P(60), by - P(182) - P(10)));
            btnClose.SetBounds(m, by, P(200), P(34));
            btnKill.SetBounds(m + P(208), by, P(130), P(34));
            btnRefresh.SetBounds(m + P(346), by, P(130), P(34));
            lblCloseDesc.SetBounds(m + P(488), by - P(4), w - 2 * m - P(488), P(54));
        }

        void RefreshMem()
        {
            long total, avail;
            Memory.Read(out total, out avail);
            if (total <= 0) return;
            long used = total - avail;
            int pct = (int)(used * 100 / total);
            lblMem.Text = "Memoria en uso: " + Util.Size(used) + " de " + Util.Size(total) + "  (" + pct + " %)   ·   Libre: " + Util.Size(avail);
            barMem.Value = Math.Max(0, Math.Min(100, pct));
        }

        void RefreshProcs()
        {
            string keep = lvProcs.SelectedItems.Count > 0 ? (string)lvProcs.SelectedItems[0].Tag : null;
            List<ProcGroup> top = Memory.Top(15);
            lvProcs.BeginUpdate();
            lvProcs.Items.Clear();
            foreach (ProcGroup g in top)
            {
                ListViewItem it = Item(Memory.IsProtected(g.Name) ? cSoft : cText, g.Name, Util.Size(g.Bytes), g.Count.ToString());
                it.Tag = g.Name;
                lvProcs.Items.Add(it);
                if (g.Name == keep) it.Selected = true;
            }
            lvProcs.EndUpdate();
        }

        void OnTrim(object sender, EventArgs e)
        {
            long before = 0, after = 0;
            int n = 0;
            Run("Liberando memoria…", delegate
            {
                long total;
                Memory.Read(out total, out before);
                n = Memory.Trim();
                Thread.Sleep(500);
                Memory.Read(out total, out after);
            }, delegate
            {
                long gain = after - before;
                string detail;
                if (gain > (4L << 20))
                {
                    detail = "Se liberaron " + Util.Size(gain) + " ajustando " + n + " programas.";
                    lblTrimRes.Text = "Hecho. " + detail + " Los programas la irán recuperando si la necesitan.";
                }
                else
                {
                    detail = "Windows ya tenía la memoria ajustada; no había casi nada que liberar.";
                    lblTrimRes.Text = "Hecho. " + detail;
                }
                History.Add("ram", "Liberar memoria", detail, History.Info, "", 0, null);
                SetStatus("Memoria revisada.");
                RefreshMem();
                RefreshProcs();
            });
        }

        void OnCloseProc(object sender, EventArgs e)
        {
            if (lvProcs.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Elige primero un programa de la lista.", "Ligero");
                return;
            }
            string name = (string)lvProcs.SelectedItems[0].Tag;
            if (Memory.IsProtected(name))
            {
                MessageBox.Show(this, "«" + name + "» es parte de Windows y no conviene cerrarlo. Ligero no lo toca.",
                    "Ligero", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this,
                "Se pedirá a «" + name + "» que se cierre, como si pulsaras su X.\n\n" +
                "Guarda antes tu trabajo. Esto no se puede deshacer (tendrás que volver a abrir el programa).\n\n¿Cerrar?",
                "Cerrar programa", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            int asked = Memory.CloseGroup(name);
            if (asked == 0)
                MessageBox.Show(this, "«" + name + "» no tiene ninguna ventana abierta que se pueda cerrar con normalidad. " +
                    "Ligero no fuerza el cierre de nada para no hacerte perder datos.", "Ligero");
            else
                History.Add("close", "Cerrar programa: " + name, "Se pidió cerrar " + asked + " ventana(s)", History.Info, "", 0, null);

            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 1500;
            t.Tick += delegate { t.Stop(); t.Dispose(); RefreshMem(); RefreshProcs(); };
            t.Start();
        }

        void OnKillProc(object sender, EventArgs e)
        {
            if (lvProcs.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Elige primero un programa de la lista.", "Ligero");
                return;
            }
            string name = (string)lvProcs.SelectedItems[0].Tag;
            if (Memory.IsProtected(name))
            {
                MessageBox.Show(this, "«" + name + "» es parte de Windows. Ligero no lo cierra a la fuerza: podría dejar el equipo inestable.",
                    "Ligero", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this,
                "«" + name + "» se cerrará de golpe, sin preguntarte nada.\n\n" +
                "Úsalo solo si el programa se ha quedado colgado: lo que no hayas guardado en él SE PIERDE y no se puede deshacer. " +
                "Si responde, es mejor «Cerrar el programa elegido».\n\n¿Forzar el cierre?",
                "Forzar cierre", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            int killed = 0;
            Run("Cerrando «" + name + "»…", delegate
            {
                killed = Memory.KillGroup(name);
                Thread.Sleep(700);
                if (killed > 0)
                    History.Add("close", "Cierre forzado: " + name, killed + " proceso(s) terminados", History.Info, "", 0, null);
            }, delegate
            {
                SetStatus(killed > 0 ? "«" + name + "» cerrado a la fuerza (" + killed + " procesos)." : "Windows no permitió cerrar «" + name + "».");
                RefreshMem();
                RefreshProcs();
            });
        }

        // ============================ ACELERAR ============================

        void BuildFast()
        {
            pgFast = NewPage("Acelerar");
            lblTwIntro = Lab(pgFast,
                "Ajustes de Windows que alivian a un equipo modesto. Toca uno para leer qué hace exactamente. Todos se pueden " +
                "deshacer: desde aquí mismo o desde la pestaña Deshacer. Los «recomendados» son los que no tienen ninguna pega práctica.",
                Font, cSoft);
            lvTweaks = LV(pgFast, false, "Ajuste", 500, "Estado", 110, "Recomendado", 110, "Necesita", 150);
            lvTweaks.SelectedIndexChanged += delegate { UpdateTweakPanel(); };
            lblTwDesc = Info(pgFast, "Toca un ajuste de la lista para ver qué hace.");
            btnTwToggle = Btn(pgFast, "Aplicar el elegido", OnTweakToggle);
            Primary(btnTwToggle);
            btnTwAll = Btn(pgFast, "Aplicar todos los recomendados", OnTweakAll);
            pgFast.Resize += delegate { LayoutFast(); };
        }

        void LayoutFast()
        {
            int w = pgFast.ClientSize.Width, h = pgFast.ClientSize.Height, m = P(16);
            if (w < 50 || h < 50) return;
            lblTwIntro.SetBounds(m, P(10), w - 2 * m, P(42));
            int by = h - P(46), descH = P(88), top = P(58);
            int listH = Math.Max(P(60), by - top - descH - P(16));
            lvTweaks.SetBounds(m, top, w - 2 * m, listH);
            lblTwDesc.SetBounds(m, top + listH + P(8), w - 2 * m, descH);
            btnTwToggle.SetBounds(m, by, P(220), P(34));
            btnTwAll.SetBounds(m + P(228), by, P(250), P(34));
        }

        Tweak SelectedTweak()
        {
            return lvTweaks.SelectedItems.Count > 0 ? (Tweak)lvTweaks.SelectedItems[0].Tag : null;
        }

        static bool Applied(Tweak t)
        {
            try { return t.IsApplied(); } catch { return false; }
        }

        void RefreshTweaks()
        {
            Tweak keep = SelectedTweak();
            lvTweaks.BeginUpdate();
            lvTweaks.Items.Clear();
            foreach (Tweak t in Tweaks.All())
            {
                bool on = Applied(t);
                bool locked = t.NeedsAdmin && !Util.IsAdmin;
                ListViewItem it = Item(on ? cGood : (locked ? cSoft : cText),
                    t.Title, on ? "✔ Aplicado" : "Sin aplicar", t.Recommended ? "Sí" : "", t.NeedsAdmin ? "Administrador" : "");
                it.Tag = t;
                lvTweaks.Items.Add(it);
                if (t == keep) it.Selected = true;
            }
            lvTweaks.EndUpdate();
            UpdateTweakPanel();
        }

        void UpdateTweakPanel()
        {
            Tweak t = SelectedTweak();
            if (t == null)
            {
                btnTwToggle.Text = "Aplicar el elegido";
                return;
            }
            lblTwDesc.Text = "Qué hace: " + t.Description +
                (t.NeedsAdmin && !Util.IsAdmin ? "  ·  Necesita abrir Ligero como administrador." : "");
            btnTwToggle.Text = Applied(t) ? "Volver a como estaba" : "Aplicar el elegido";
        }

        /// <summary>Aplica un ajuste y lo apunta en el historial. Se llama fuera del hilo de la ventana.</summary>
        static void ApplyTweak(Tweak t)
        {
            string prev = t.Apply();
            History.Add("tweak", t.Title, "Ajuste aplicado", History.Active, t.Id + "|" + prev, 0, null);
        }

        void OnTweakToggle(object sender, EventArgs e)
        {
            Tweak t = SelectedTweak();
            if (t == null)
            {
                MessageBox.Show(this, "Elige primero un ajuste de la lista.", "Ligero");
                return;
            }
            if (t.NeedsAdmin && !Util.IsAdmin)
            {
                AskAdmin("Este ajuste cambia una parte de Windows que está protegida.");
                return;
            }
            bool on = Applied(t);
            Run(on ? "Devolviendo el ajuste a como estaba…" : "Aplicando el ajuste…", delegate
            {
                if (!on) ApplyTweak(t);
                else
                {
                    string prefix = t.Id + "|";
                    Entry en = History.LastActive("tweak", delegate(Entry x) { return x.Data.StartsWith(prefix); });
                    if (en != null) Undo.Run(en);
                    if (t.IsApplied()) t.Restore(t.DefaultData);   // ya estaba así antes de Ligero: vuelve al valor normal de Windows
                }
            }, delegate
            {
                SetStatus((on ? "Devuelto a como estaba: " : "Aplicado: ") + t.Title + ".");
                RefreshTweaks();
            });
        }

        void OnTweakAll(object sender, EventArgs e)
        {
            List<Tweak> todo = new List<Tweak>();
            int needAdmin = 0;
            foreach (Tweak t in Tweaks.All())
            {
                if (!t.Recommended || Applied(t)) continue;
                if (t.NeedsAdmin && !Util.IsAdmin) { needAdmin++; continue; }
                todo.Add(t);
            }
            if (todo.Count == 0)
            {
                MessageBox.Show(this, needAdmin > 0
                    ? "Los recomendados que faltan (" + needAdmin + ") necesitan abrir Ligero como administrador."
                    : "Todos los ajustes recomendados ya están aplicados.", "Ligero");
                return;
            }
            string names = "";
            foreach (Tweak t in todo) names += "  •  " + t.Title + "\n";
            if (MessageBox.Show(this, "Se aplicarán estos " + todo.Count + " ajustes:\n\n" + names +
                "\nTodos se pueden deshacer después, uno a uno o con «Deshacer todo».\n\n¿Aplicarlos?",
                "Aplicar recomendados", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            int ok = 0, bad = 0;
            Run("Aplicando los ajustes recomendados…", delegate
            {
                foreach (Tweak t in todo)
                {
                    try { ApplyTweak(t); ok++; } catch { bad++; }
                }
            }, delegate
            {
                SetStatus(ok + " ajustes aplicados." + (bad > 0 ? " " + bad + " no se pudieron aplicar." : "") +
                    (needAdmin > 0 ? " Quedan " + needAdmin + " que necesitan modo administrador." : ""));
                RefreshTweaks();
            });
        }

        // ============================ INICIO ============================

        void BuildStartup()
        {
            pgStart = NewPage("Arranque");
            lblStartDesc = Lab(pgStart,
                "Programas que arrancan solos al encender el equipo. Qué hace: cada uno se abre solo y ocupa memoria aunque no lo " +
                "uses. Al desactivarlo NO se desinstala ni se borra: solo deja de abrirse solo (podrás abrirlo tú cuando quieras). " +
                "Es de lo que más se nota en un equipo lento. El efecto se ve al reiniciar.", Font, cSoft);
            lvStart = LV(pgStart, false, "Programa", 230, "¿Arranca solo?", 130, "De quién es", 250, "Qué abre", 560);
            lvStart.SelectedIndexChanged += delegate { UpdateStartButton(); };
            btnStart = Btn(pgStart, "Desactivar el elegido", OnToggleStartup);
            Primary(btnStart);
            pgStart.Resize += delegate { LayoutStartup(); };
        }

        void LayoutStartup()
        {
            int w = pgStart.ClientSize.Width, h = pgStart.ClientSize.Height, m = P(16);
            if (w < 50 || h < 50) return;
            lblStartDesc.SetBounds(m, P(10), w - 2 * m, P(62));
            int by = h - P(46);
            lvStart.SetBounds(m, P(78), w - 2 * m, Math.Max(P(50), by - P(78) - P(8)));
            btnStart.SetBounds(m, by, P(250), P(34));
        }

        void RefreshStartup()
        {
            string keep = null;
            if (lvStart.SelectedItems.Count > 0)
            {
                StartupItem s = (StartupItem)lvStart.SelectedItems[0].Tag;
                keep = s.Source + "|" + s.Name;
            }
            List<StartupItem> list;
            try { list = Startup.List(); } catch { list = new List<StartupItem>(); }
            lvStart.BeginUpdate();
            lvStart.Items.Clear();
            foreach (StartupItem s in list)
            {
                ListViewItem it = Item(s.Enabled ? cText : cSoft, s.Name, s.Enabled ? "Sí" : "No (desactivado)", s.SourceText, s.Command);
                it.Tag = s;
                lvStart.Items.Add(it);
                if (s.Source + "|" + s.Name == keep) it.Selected = true;
            }
            lvStart.EndUpdate();
            UpdateStartButton();
        }

        void UpdateStartButton()
        {
            bool enabled = true;
            if (lvStart.SelectedItems.Count > 0) enabled = ((StartupItem)lvStart.SelectedItems[0].Tag).Enabled;
            btnStart.Text = enabled ? "Desactivar el elegido" : "Volver a activar el elegido";
        }

        void OnToggleStartup(object sender, EventArgs e)
        {
            if (lvStart.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Elige primero un programa de la lista.", "Ligero");
                return;
            }
            StartupItem s = (StartupItem)lvStart.SelectedItems[0].Tag;
            string key = "|" + s.Source + "|" + s.Name;
            try
            {
                if (s.Enabled)
                {
                    string prev = Startup.SetEnabled(s.Source, s.Name, false);
                    History.Add("startup", "Ya no arranca solo: " + s.Name, "Sigue instalado; solo no se abre al encender",
                        History.Active, prev + key, 0, null);
                    SetStatus("«" + s.Name + "» ya no arrancará solo. Se nota al reiniciar.");
                }
                else
                {
                    Entry old = History.LastActive("startup", delegate(Entry x) { return x.Data.EndsWith(key); });
                    if (old != null) Undo.Run(old);
                    bool still = true;
                    foreach (StartupItem x in Startup.List())
                        if (x.Source == s.Source && x.Name == s.Name) still = !x.Enabled;
                    if (still)
                    {
                        string prev = Startup.SetEnabled(s.Source, s.Name, true);
                        History.Add("startup", "Vuelve a arrancar solo: " + s.Name, "", History.Active, prev + key, 0, null);
                    }
                    SetStatus("«" + s.Name + "» volverá a arrancar con Windows.");
                }
            }
            catch (Exception ex) { ShowError(ex); }
            RefreshStartup();
        }

        // ============================ DISCO ============================

        void BuildDisk()
        {
            pgDisk = NewPage("Disco");
            lblDisk = Lab(pgDisk, "", fTitle, cText);
            lblDiskDesc = Lab(pgDisk,
                "Cómo funciona: Ligero no borra nada de golpe. Mueve la basura a una «zona de seguridad» y la guarda 7 días. " +
                "Si algo deja de ir bien, ve a la pestaña Deshacer y todo vuelve a su sitio. El espacio se libera del todo " +
                "cuando pulses «Liberar espacio definitivamente» allí, o solo al pasar los 7 días.", Font, cSoft);
            lvCats = LV(pgDisk, true, "Qué se limpia", 400, "Ocupa", 110, "Archivos", 90, "¿Se puede deshacer?", 230);
            lvCats.SelectedIndexChanged += delegate
            {
                if (lvCats.SelectedItems.Count > 0)
                    lblCatDesc.Text = "Qué es: " + ((Category)lvCats.SelectedItems[0].Tag).Description;
            };
            lblCatDesc = Info(pgDisk, "Pulsa «Analizar» para ver cuánto ocupa cada cosa. Toca una fila para leer qué es exactamente.");
            btnScan = Btn(pgDisk, "Analizar", OnScan);
            btnClean = Btn(pgDisk, "Limpiar lo marcado", OnClean);
            Primary(btnClean);
            lblCleanRes = Lab(pgDisk, "", fBold, cGood);

            if (cats == null) cats = Cleaner.Build();
            foreach (Category c in cats)
            {
                ListViewItem it = Item(c.Undoable ? cText : cWarn, c.Title, "sin analizar", "", c.Undoable ? "Sí, durante 7 días" : "NO se puede deshacer");
                it.Tag = c;
                it.Checked = c.DefaultChecked;
                lvCats.Items.Add(it);
            }
            pgDisk.Resize += delegate { LayoutDisk(); };
        }

        void LayoutDisk()
        {
            int w = pgDisk.ClientSize.Width, h = pgDisk.ClientSize.Height, m = P(16);
            if (w < 50 || h < 50) return;
            lblDisk.SetBounds(m, P(12), w - 2 * m, P(30));
            lblDiskDesc.SetBounds(m, P(46), w - 2 * m, P(60));
            int by = h - P(46);
            int descH = P(84);
            int top = P(112);
            int listH = Math.Max(P(60), by - top - descH - P(16));
            lvCats.SetBounds(m, top, w - 2 * m, listH);
            lblCatDesc.SetBounds(m, top + listH + P(8), w - 2 * m, descH);
            btnScan.SetBounds(m, by, P(120), P(34));
            btnClean.SetBounds(m + P(128), by, P(190), P(34));
            lblCleanRes.SetBounds(m + P(330), by - P(4), w - 2 * m - P(330), P(44));
        }

        void RefreshDiskLabel()
        {
            try
            {
                DriveInfo d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                lblDisk.Text = "Disco " + d.Name.TrimEnd('\\') + "   " + Util.Size(d.AvailableFreeSpace) + " libres de " + Util.Size(d.TotalSize);
            }
            catch { lblDisk.Text = "Limpieza de disco"; }
        }

        void ShowCats()
        {
            foreach (ListViewItem it in lvCats.Items)
            {
                Category c = (Category)it.Tag;
                it.SubItems[1].Text = c.Size < 0 ? "sin analizar" : Util.Size(c.Size);
                it.SubItems[2].Text = c.Size < 0 ? "" : c.Count.ToString();
            }
            RefreshDiskLabel();
        }

        void OnScan(object sender, EventArgs e)
        {
            Run("Analizando el disco… (en discos lentos puede tardar un minuto)", delegate
            {
                foreach (Category c in cats) Cleaner.Scan(c);
            }, delegate
            {
                ShowCats();
                long total = 0;
                foreach (Category c in cats) if (c.Undoable) total += c.Size;
                SetStatus("Análisis terminado: " + Util.Size(total) + " de basura que se puede limpiar con seguridad." +
                    (Util.IsAdmin ? "" : " Como administrador se encuentra más."));
            });
        }

        void OnClean(object sender, EventArgs e)
        {
            List<Category> sel = new List<Category>();
            bool bin = false;
            foreach (ListViewItem it in lvCats.Items)
                if (it.Checked)
                {
                    sel.Add((Category)it.Tag);
                    if (((Category)it.Tag).Id == "bin") bin = true;
                }
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "Marca al menos una casilla de la lista.", "Ligero");
                return;
            }
            if (bin && MessageBox.Show(this,
                "Has marcado la Papelera de reciclaje.\n\nVaciarla NO se puede deshacer: lo que haya dentro se pierde para siempre.\n\n¿Seguro que quieres vaciarla?",
                "Papelera de reciclaje", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            string summary = "";
            Run("Limpiando… los archivos se mueven a la zona de seguridad", delegate
            {
                summary = Cleaner.Clean(sel);
                foreach (Category c in cats) Cleaner.Scan(c);
            }, delegate
            {
                ShowCats();
                lblCleanRes.Text = summary + (History.QuarantinedBytes() > 0 ? " Para deshacer o liberar el espacio ya: pestaña Deshacer." : "");
                SetStatus("Limpieza terminada.");
            });
        }

        // ============================ PROGRAMAS ============================

        void BuildApps()
        {
            pgApps = NewPage("Programas");
            lblAppsDesc = Lab(pgApps,
                "Desinstala programas sin dejar basura. Toca uno para ver qué pasaría si lo quitas. «Desinstalar» abre el desinstalador " +
                "del propio programa; cuando termine, «Buscar restos» encuentra las carpetas y la configuración que dejó atrás y te deja " +
                "retirarlas (eso sí se puede deshacer).", Font, cSoft);
            chkSys = new Toggle();
            chkSys.Text = "Mostrar también componentes del sistema y apps de la Tienda";
            chkSys.Font = Font;
            chkSys.SetSilently(showSystemApps);
            chkSys.CheckedChanged += delegate { showSystemApps = chkSys.Checked; LoadApps(); };
            pgApps.Controls.Add(chkSys);
            lvApps = LV(pgApps, false, "Programa", 300, "Fabricante", 170, "Ocupa", 80, "Tipo", 150, "Si lo quitas…", 320);
            lvApps.SelectedIndexChanged += delegate { UpdateAppPanel(); };
            lblAppRisk = Info(pgApps, "Toca un programa para ver si es seguro quitarlo.");
            btnUninstall = Btn(pgApps, "Desinstalar", OnUninstall);
            Primary(btnUninstall);
            btnLeftovers = Btn(pgApps, "Buscar restos", OnLeftovers);
            btnAppsRefresh = Btn(pgApps, "Actualizar lista", delegate { LoadApps(); });
            pgApps.Resize += delegate { LayoutApps(); };
        }

        void LayoutApps()
        {
            int w = pgApps.ClientSize.Width, h = pgApps.ClientSize.Height, m = P(16);
            if (w < 50 || h < 50) return;
            lblAppsDesc.SetBounds(m, P(10), w - 2 * m, P(60));
            chkSys.SetBounds(m, P(72), w - 2 * m, P(24));
            int by = h - P(46), descH = P(88), top = P(100);
            int listH = Math.Max(P(60), by - top - descH - P(16));
            lvApps.SetBounds(m, top, w - 2 * m, listH);
            lblAppRisk.SetBounds(m, top + listH + P(8), w - 2 * m, descH);
            btnUninstall.SetBounds(m, by, P(160), P(34));
            btnLeftovers.SetBounds(m + P(168), by, P(150), P(34));
            btnAppsRefresh.SetBounds(m + P(326), by, P(150), P(34));
        }

        AppInfo SelectedApp()
        {
            return lvApps.SelectedItems.Count > 0 ? (AppInfo)lvApps.SelectedItems[0].Tag : null;
        }

        void LoadApps()
        {
            bool all = chkSys.Checked;
            List<AppInfo> list = null;
            Run(all ? "Leyendo programas, componentes y apps de la Tienda…" : "Leyendo los programas instalados…", delegate
            {
                list = Apps.List(all);
            }, delegate
            {
                apps = list;
                ShowApps();
                SetStatus(apps.Count + " elementos instalados.");
            });
        }

        void ShowApps()
        {
            lvApps.BeginUpdate();
            lvApps.Items.Clear();
            foreach (AppInfo a in apps)
            {
                ListViewItem it = Item(cText, a.Name, a.Publisher, a.SizeBytes > 0 ? Util.Size(a.SizeBytes) : "", a.Kind, a.Risk.Title);
                it.UseItemStyleForSubItems = false;
                foreach (ListViewItem.ListViewSubItem si in it.SubItems) { si.ForeColor = cText; si.BackColor = cBack; }
                it.SubItems[4].ForeColor = RiskColor(a.Risk.Level);
                it.Tag = a;
                lvApps.Items.Add(it);
            }
            lvApps.EndUpdate();
            lblAppRisk.ForeColor = cText;
            lblAppRisk.Text = "Toca un programa para ver si es seguro quitarlo.";
        }

        void UpdateAppPanel()
        {
            AppInfo a = SelectedApp();
            if (a == null) return;
            lblAppRisk.ForeColor = RiskColor(a.Risk.Level);
            lblAppRisk.Text = a.Risk.Title + "\n" + a.Risk.Text;
        }

        void OnUninstall(object sender, EventArgs e)
        {
            AppInfo a = SelectedApp();
            if (a == null)
            {
                MessageBox.Show(this, "Elige primero un programa de la lista.", "Ligero");
                return;
            }
            if (a.IsStore && a.Locked)
            {
                MessageBox.Show(this, a.Risk.Title + "\n\n" + a.Risk.Text, "Ligero", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                return;
            }
            if (a.Risk.Level >= 1)
            {
                if (MessageBox.Show(this, "«" + a.Name + "»\n\n" + a.Risk.Title + "\n\n" + a.Risk.Text + "\n\n¿Desinstalarlo de todos modos?",
                    "Aviso antes de desinstalar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                if (a.Risk.Level >= 3 && MessageBox.Show(this,
                    "Última confirmación.\n\nQuitar «" + a.Name + "» puede dejar partes de Windows sin funcionar y Ligero no puede deshacer una desinstalación.\n\n¿Continuar?",
                    "Riesgo crítico", MessageBoxButtons.YesNo, MessageBoxIcon.Stop, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            }
            else if (MessageBox.Show(this, "¿Desinstalar «" + a.Name + "»?\n\n" + a.Risk.Text +
                "\n\nUna desinstalación no se puede deshacer desde Ligero (habría que volver a instalar el programa).",
                "Desinstalar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            string msg = "", restore = "";
            bool finished = false;
            Run("Preparando la desinstalación de «" + a.Name + "»…", delegate
            {
                if (!a.IsStore) restore = Apps.CreateRestorePoint(a.Name);
                finished = Apps.Uninstall(a, out msg);
                History.Add("uninstall", (finished ? "Desinstalado: " : "Desinstalador abierto: ") + a.Name,
                    a.Risk.Title + (restore.Length > 0 ? " · " + restore : ""), History.Info, "", 0, null);
            }, delegate
            {
                SetStatus(msg);
                MessageBox.Show(this, msg + (restore.Length > 0 ? "\n\n" + restore : ""), "Ligero", MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (finished) LoadApps();
            });
        }

        void OnLeftovers(object sender, EventArgs e)
        {
            AppInfo a = SelectedApp();
            if (a == null)
            {
                MessageBox.Show(this, "Elige primero el programa del que quieres buscar restos.", "Ligero");
                return;
            }
            bool still = false;
            List<Leftover> found = null;
            List<AppInfo> current = apps;
            Run("Buscando restos de «" + a.Name + "»…", delegate
            {
                still = Apps.StillInstalled(a);
                found = Apps.FindLeftovers(a, current);
            }, delegate
            {
                SetStatus("Búsqueda de restos terminada.");
                if (still && MessageBox.Show(this,
                    "«" + a.Name + "» todavía aparece como instalado.\n\nLo normal es pulsar antes «Desinstalar» y terminar su asistente. " +
                    "Si su desinstalador está roto o no existe, puedes retirar sus archivos a la fuerza: el programa dejará de funcionar.\n\n" +
                    "¿Buscar y retirar sus archivos igualmente?", "Sigue instalado",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                if (found.Count == 0)
                {
                    MessageBox.Show(this, "No se encontraron restos de «" + a.Name + "». Está limpio.", "Ligero");
                    return;
                }
                List<Leftover> chosen = PickLeftovers(a.Name, found);
                if (chosen == null || chosen.Count == 0) return;
                string result = "";
                Run("Retirando restos de «" + a.Name + "»…", delegate { result = Apps.RemoveLeftovers(a.Name, chosen); }, delegate
                {
                    SetStatus("Restos retirados.");
                    MessageBox.Show(this, result + "\n\nSi algo no va bien, puedes devolverlo todo desde la pestaña Deshacer.", "Ligero");
                    LoadApps();
                });
            });
        }

        /// <summary>Ventana donde el usuario revisa los restos encontrados antes de retirarlos.</summary>
        List<Leftover> PickLeftovers(string appName, List<Leftover> found)
        {
            using (Form f = new Form())
            {
                f.Text = "Restos de «" + appName + "»";
                f.Font = Font;
                f.BackColor = cBack;
                f.ForeColor = cText;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false;
                f.ShowInTaskbar = false;
                f.HandleCreated += delegate
                {
                    try { Native.SetChrome(f.Handle, Ui.Dark || Ui.SystemDark(), 1, 0, 0, unchecked((int)0xFFFFFFFF), unchecked((int)0xFFFFFFFF)); } catch { }
                };
                f.ClientSize = new Size(Math.Min(P(860), ClientSize.Width), P(440));
                f.MinimumSize = new Size(P(600), P(320));

                Label intro = Lab(f,
                    "Esto es lo que quedó en el equipo. Revisa la lista y desmarca lo que quieras conservar (por ejemplo, partidas " +
                    "guardadas). Las carpetas van a la zona de seguridad y de cada clave del registro se guarda una copia: todo se puede deshacer.",
                    Font, cSoft);
                ListView lv = LV(f, true, "Tipo", 80, "Dónde está", 430, "Ocupa", 90, "Qué es", 250);
                long total = 0;
                foreach (Leftover l in found)
                {
                    ListViewItem it = Item(cText, l.Kind, l.Path, l.Kind == "Carpeta" ? Util.Size(l.Size) : "", l.Why);
                    it.Tag = l;
                    it.Checked = true;
                    lv.Items.Add(it);
                    total += l.Size;
                }
                Label sum = Lab(f, found.Count + " elementos · " + Util.Size(total), fBold, cText);
                Button ok = Btn(f, "Retirar los marcados", delegate { f.DialogResult = DialogResult.OK; });
                Primary(ok);
                Button cancel = Btn(f, "Cancelar", delegate { f.DialogResult = DialogResult.Cancel; });
                Action layout = delegate
                {
                    int w = f.ClientSize.Width, h = f.ClientSize.Height, m = P(14);
                    intro.SetBounds(m, P(10), w - 2 * m, P(44));
                    lv.SetBounds(m, P(58), w - 2 * m, h - P(58) - P(58));
                    ok.SetBounds(m, h - P(46), P(200), P(34));
                    cancel.SetBounds(m + P(208), h - P(46), P(110), P(34));
                    sum.SetBounds(m + P(330), h - P(40), w - 2 * m - P(330), P(24));
                };
                f.Resize += delegate { layout(); };
                layout();
                if (f.ShowDialog(this) != DialogResult.OK) return null;
                List<Leftover> chosen = new List<Leftover>();
                foreach (ListViewItem it in lv.CheckedItems) chosen.Add((Leftover)it.Tag);
                return chosen;
            }
        }

        // ============================ ELIMINAR ============================

        void BuildDelete()
        {
            pgDel = NewPage("Eliminar");
            lblDelDesc = Lab(pgDel,
                "Elimina cualquier archivo o carpeta. Antes de tocar nada, Ligero te dice a qué pertenece y qué pasaría: si es tuyo, " +
                "si rompe un programa, si compromete la seguridad o si es parte de Windows. Lo eliminado va a la zona de seguridad, " +
                "así que se puede recuperar en Deshacer. También puedes arrastrar aquí un archivo o carpeta.", Font, cSoft);
            btnPickFile = Btn(pgDel, "Elegir un archivo…", delegate
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Title = "Archivo que quieres eliminar";
                    d.CheckFileExists = true;
                    d.DereferenceLinks = false;
                    if (d.ShowDialog(this) == DialogResult.OK) SetDeleteTarget(d.FileName);
                }
            });
            btnPickFolder = Btn(pgDel, "Elegir una carpeta…", delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.Description = "Carpeta que quieres eliminar";
                    d.ShowNewFolderButton = false;
                    if (d.ShowDialog(this) == DialogResult.OK) SetDeleteTarget(d.SelectedPath);
                }
            });
            lblDelPath = Lab(pgDel, "Todavía no has elegido nada.", fBold, cText);
            lblDelTitle = Lab(pgDel, "", fTitle, cText);
            lblDelText = Info(pgDel, "Elige un archivo o una carpeta, o arrástralo aquí. Ligero lo analizará y te dirá si es seguro " +
                "eliminarlo antes de hacer nada. Hay un pequeño grupo de cosas que Ligero se niega a eliminar aunque se lo pidas " +
                "(la carpeta de Windows entera, tu usuario completo, una unidad de disco…), porque dejarían el equipo inservible.");
            btnDelete = Btn(pgDel, "Eliminar", OnDelete);
            Primary(btnDelete);
            btnDelete.Enabled = false;
            lblDelRes = Lab(pgDel, "", fBold, cGood);

            pgDel.AllowDrop = true;
            pgDel.DragEnter += delegate(object s, DragEventArgs e)
            {
                e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) && !busy ? DragDropEffects.Copy : DragDropEffects.None;
            };
            pgDel.DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (dropped != null && dropped.Length > 0) SetDeleteTarget(dropped[0]);
            };
            pgDel.Resize += delegate { LayoutDelete(); };
        }

        void LayoutDelete()
        {
            int w = pgDel.ClientSize.Width, h = pgDel.ClientSize.Height, m = P(16);
            if (w < 50 || h < 50) return;
            lblDelDesc.SetBounds(m, P(10), w - 2 * m, P(62));
            btnPickFile.SetBounds(m, P(80), P(180), P(34));
            btnPickFolder.SetBounds(m + P(188), P(80), P(180), P(34));
            lblDelPath.SetBounds(m, P(128), w - 2 * m, P(44));
            lblDelTitle.SetBounds(m, P(176), w - 2 * m, P(32));
            int by = h - P(46);
            lblDelText.SetBounds(m, P(212), w - 2 * m, Math.Max(P(60), by - P(212) - P(12)));
            btnDelete.SetBounds(m, by, P(160), P(34));
            lblDelRes.SetBounds(m + P(172), by - P(4), w - 2 * m - P(172), P(44));
        }

        void SetDeleteTarget(string path)
        {
            Verdict v = null;
            long size = 0, count = 0;
            Run("Analizando qué es y a qué pertenece…", delegate
            {
                v = Risk.ForPath(path);
                if (!v.Refuse) Deleter.Measure(path, out size, out count);
            }, delegate
            {
                delPath = path;
                delVerdict = v;
                lblDelRes.Text = "";
                lblDelPath.Text = path + (v.Refuse ? "" : "\n" + Util.Size(size) + " en " + count + (count == 1 ? " archivo" : " archivos"));
                lblDelTitle.ForeColor = RiskColor(v.Level);
                lblDelTitle.Text = v.Title;
                lblDelText.Text = v.Text + (v.Refuse ? "" :
                    "\n\nSi lo eliminas, irá a la zona de seguridad y podrás devolverlo a su sitio desde la pestaña Deshacer.");
                btnDelete.Enabled = !v.Refuse;
                SetStatus("Análisis terminado.");
            });
        }

        void OnDelete(object sender, EventArgs e)
        {
            if (delPath == null || delVerdict == null || delVerdict.Refuse) return;
            string path = delPath;
            Verdict v = delVerdict;
            string name = Path.GetFileName(path.TrimEnd('\\'));
            if (v.Level == 0)
            {
                if (MessageBox.Show(this, "¿Eliminar «" + name + "»?\n\n" + v.Text + "\n\nPodrás recuperarlo en la pestaña Deshacer.",
                    "Eliminar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }
            else
            {
                if (MessageBox.Show(this, "«" + name + "»\n\n" + v.Title + "\n\n" + v.Text + "\n\n¿Eliminarlo de todos modos?",
                    "Aviso antes de eliminar", MessageBoxButtons.YesNo, v.Level >= 2 ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                if (v.Level >= 3 && MessageBox.Show(this,
                    "Última confirmación.\n\nEsto forma parte de Windows. Si el equipo deja de arrancar, no podrás abrir Ligero para deshacerlo.\n\n¿Continuar?",
                    "Riesgo crítico", MessageBoxButtons.YesNo, MessageBoxIcon.Stop, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            }

            string msg = "";
            List<string> failed = null;
            Run("Eliminando «" + name + "»…", delegate { msg = Deleter.Delete(path, v, out failed); }, delegate
            {
                lblDelRes.Text = msg;
                SetStatus("Hecho.");
                delPath = null;
                delVerdict = null;
                btnDelete.Enabled = false;
                if (failed == null || failed.Count == 0) return;

                if (v.Level >= 3)
                {
                    MessageBox.Show(this, failed.Count + " archivos no se movieron porque Windows los protege o los está usando. " +
                        "Al ser parte del sistema, Ligero no los fuerza.", "Ligero", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (!Util.IsAdmin)
                {
                    MessageBox.Show(this, failed.Count + " archivos no se pudieron mover: están en uso o piden permisos. " +
                        "Cierra el programa que los usa y repite, o abre Ligero como administrador para poder retirarlos al reiniciar.",
                        "Ligero", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (MessageBox.Show(this, failed.Count + " archivos están en uso ahora mismo.\n\n" +
                    "Ligero puede pedir a Windows que los retire en el próximo reinicio, antes de que ningún programa los abra. " +
                    "Irán igualmente a la zona de seguridad.\n\n¿Retirarlos al reiniciar?", "Archivos en uso",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                int n = Deleter.ScheduleOnReboot(failed, name);
                lblDelRes.Text = msg + " " + n + " se retirarán al reiniciar el equipo.";
            });
        }

        // ============================ DESHACER ============================

        void BuildUndo()
        {
            pgUndo = NewPage("Deshacer");
            lblUndoDesc = Lab(pgUndo,
                "Aquí queda apuntado todo lo que Ligero ha hecho. Elige una acción y pulsa «Deshacer» para dejarla como estaba, " +
                "o «Deshacer todo» para volver al punto de partida. «Liberar espacio definitivamente» borra de verdad lo guardado " +
                "en la zona de seguridad: ganas el espacio, pero eso ya no se podrá deshacer.", Font, cSoft);
            lvHist = LV(pgUndo, false, "Cuándo", 125, "Qué se hizo", 300, "Estado", 170, "Detalle", 600);
            btnUndo = Btn(pgUndo, "Deshacer la elegida", OnUndo);
            Primary(btnUndo);
            btnUndoAll = Btn(pgUndo, "Deshacer todo", OnUndoAll);
            btnPurge = Btn(pgUndo, "Liberar espacio definitivamente", OnPurge);
            lblQuar = Lab(pgUndo, "", fBold, cText);
            pgUndo.Resize += delegate { LayoutUndo(); };
        }

        void LayoutUndo()
        {
            int w = pgUndo.ClientSize.Width, h = pgUndo.ClientSize.Height, m = P(16);
            if (w < 50 || h < 50) return;
            lblUndoDesc.SetBounds(m, P(10), w - 2 * m, P(62));
            lblQuar.SetBounds(m, P(76), w - 2 * m, P(24));
            int by = h - P(46);
            lvHist.SetBounds(m, P(104), w - 2 * m, Math.Max(P(60), by - P(104) - P(8)));
            btnUndo.SetBounds(m, by, P(180), P(34));
            btnUndoAll.SetBounds(m + P(188), by, P(140), P(34));
            btnPurge.SetBounds(w - m - P(250), by, P(250), P(34));
        }

        static string StateText(Entry e)
        {
            if (e.State == History.Active) return "Se puede deshacer";
            if (e.State == History.Undone) return "Deshecho";
            if (e.State == History.Final) return "Definitivo (espacio liberado)";
            if (e.Type == "uninstall" || e.Type == "bin" || e.Type == "close") return "No se puede deshacer";
            return "No necesita deshacerse";
        }

        void RefreshHist()
        {
            List<Entry> all = History.All();
            lvHist.BeginUpdate();
            lvHist.Items.Clear();
            for (int i = all.Count - 1; i >= 0; i--)
            {
                Entry e = all[i];
                ListViewItem it = Item(e.State == History.Active ? cAccent : cSoft,
                    e.When.ToString("dd/MM/yyyy HH:mm"), e.Title, StateText(e), e.Detail);
                it.Tag = e;
                lvHist.Items.Add(it);
            }
            lvHist.EndUpdate();
            long q = History.QuarantinedBytes();
            lblQuar.Text = q > 0
                ? "Zona de seguridad: " + Util.Size(q) + " guardados por si quieres deshacer."
                : "Zona de seguridad vacía.";
        }

        void AfterUndo(string message)
        {
            RefreshHist();
            SetStatus(message);
        }

        void OnUndo(object sender, EventArgs e)
        {
            if (lvHist.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Elige primero una acción de la lista.", "Ligero");
                return;
            }
            Entry en = (Entry)lvHist.SelectedItems[0].Tag;
            if (en.State != History.Active)
            {
                MessageBox.Show(this, en.State == History.Info
                    ? (StateText(en) == "No se puede deshacer"
                        ? "Esta acción no se puede deshacer desde Ligero."
                        : "Esta acción no cambió nada permanente, así que no hay nada que deshacer.")
                    : "Esta acción ya está deshecha o es definitiva.", "Ligero");
                return;
            }
            string msg = "";
            Run("Deshaciendo…", delegate { msg = Undo.Run(en); }, delegate { AfterUndo(msg); });
        }

        void OnUndoAll(object sender, EventArgs e)
        {
            List<Entry> pending = new List<Entry>();
            List<Entry> all = History.All();
            for (int i = all.Count - 1; i >= 0; i--)
                if (all[i].State == History.Active) pending.Add(all[i]);
            if (pending.Count == 0)
            {
                MessageBox.Show(this, "No hay nada pendiente de deshacer.", "Ligero");
                return;
            }
            if (MessageBox.Show(this,
                "Se desharán " + pending.Count + " acciones: los ajustes vuelven a como estaban y los archivos guardados " +
                "regresan a su sitio.\n\n¿Continuar?", "Deshacer todo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            int ok = 0, bad = 0;
            Run("Deshaciendo todo…", delegate
            {
                foreach (Entry en in pending)
                {
                    try { Undo.Run(en); if (en.State == History.Undone) ok++; else bad++; }
                    catch { bad++; }
                }
            }, delegate
            {
                AfterUndo(ok + " acciones deshechas." + (bad > 0 ? " " + bad + " no se pudieron deshacer del todo (quizá piden administrador)." : ""));
            });
        }

        void OnPurge(object sender, EventArgs e)
        {
            long q = History.QuarantinedBytes();
            if (q <= 0)
            {
                MessageBox.Show(this, "La zona de seguridad está vacía: no hay espacio pendiente de liberar.", "Ligero");
                return;
            }
            if (MessageBox.Show(this,
                "Se borrarán para siempre " + Util.Size(q) + " guardados en la zona de seguridad, incluidos los archivos y restos " +
                "que hayas retirado o eliminado.\n\n" +
                "Ganarás ese espacio en disco, pero esas acciones YA NO se podrán deshacer.\n\n¿Borrar definitivamente?",
                "Liberar espacio", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            long freed = 0;
            Run("Liberando espacio…", delegate { freed = Undo.PurgeAll(); },
                delegate { AfterUndo("Espacio liberado: " + Util.Size(freed) + "."); });
        }
    }
}
