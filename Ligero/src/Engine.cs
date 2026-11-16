using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Ligero
{
    // ------------------------------------------------------------------
    //  Utilidades
    // ------------------------------------------------------------------
    public static class Util
    {
        /// <summary>Solo para pruebas: carpeta de datos alternativa.</summary>
        public static string AppDirOverride;

        public static string AppDir
        {
            get
            {
                string d = AppDirOverride;
                if (string.IsNullOrEmpty(d))
                    d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ligero");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        static bool? admin;

        /// <summary>
        /// Devuelve a Windows la memoria que Ligero ya no necesita. Tras una tarea pesada quedan
        /// restos en memoria que el sistema no reclama solo; aquí se descartan. Con trim = true además
        /// se suelta la memoria residente (para cuando la ventana está minimizada o en segundo plano).
        /// </summary>
        public static void ReleaseMemory(bool trim)
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch { }
            if (!trim) return;
            try
            {
                using (Process p = Process.GetCurrentProcess()) Native.EmptyWorkingSet(p.Handle);
            }
            catch { }
        }

        /// <summary>¿Se abrió Ligero como administrador?</summary>
        public static bool IsAdmin
        {
            get
            {
                if (!admin.HasValue)
                {
                    try
                    {
                        using (System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent())
                            admin = new System.Security.Principal.WindowsPrincipal(id)
                                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                    }
                    catch { admin = false; }
                }
                return admin.Value;
            }
        }

        /// <summary>Ejecuta una herramienta de Windows sin mostrar ventana y devuelve su código de salida.</summary>
        public static int Run(string exe, string args, out string output)
        {
            return Run(exe, args, 120000, out output);
        }

        public static int Run(string exe, string args, int timeoutMs, out string output)
        {
            output = "";
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                StringBuilder sb = new StringBuilder();
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    output = "La operación tardó demasiado.";
                    return -1;
                }
                p.WaitForExit();
                lock (sb) output = sb.ToString();
                return p.ExitCode;
            }
        }

        /// <summary>Ejecuta una orden de PowerShell (para funciones de Windows que solo existen ahí).</summary>
        public static int RunPS(string script, int timeoutMs, out string output)
        {
            string b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(
                "[Console]::OutputEncoding=[Text.Encoding]::UTF8;$ProgressPreference='SilentlyContinue';" + script));
            ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + b64);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            output = "";
            using (Process p = Process.Start(psi))
            {
                string text = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return -1; }
                output = text;
                return p.ExitCode;
            }
        }

        public static string Size(long b)
        {
            if (b < 0) b = 0;
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.0") + " GB";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("0") + " MB";
            if (b >= 1024) return (b / 1024.0).ToString("0") + " KB";
            return b + " bytes";
        }

        public static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "").Replace("\n", "\\n");
        }

        public static string Unesc(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    if (n == 't') sb.Append('\t');
                    else if (n == 'n') sb.Append('\n');
                    else sb.Append(n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------
    //  Historial: todo lo que hace la app queda apuntado aquí
    // ------------------------------------------------------------------
    public class Entry
    {
        public string Id;
        public DateTime When;
        public string Type;    // ram, close, bin, tweak, startup, clean, files
        public string State;   // activo, deshecho, definitivo, info
        public long Bytes;
        public string Title;
        public string Detail;
        public string Data;
    }

    public static class History
    {
        public const string Active = "activo";
        public const string Undone = "deshecho";
        public const string Final = "definitivo";
        public const string Info = "info";

        static List<Entry> items;
        static int counter;
        static readonly object gate = new object();

        static string FilePath { get { return Path.Combine(Util.AppDir, "historial.txt"); } }

        public static string NewId()
        {
            lock (gate)
            {
                counter++;
                return DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + counter;
            }
        }

        /// <summary>Solo para pruebas: olvida lo cargado en memoria.</summary>
        public static void Reset() { lock (gate) { items = null; } }

        static void EnsureLoaded()
        {
            if (items != null) return;
            items = new List<Entry>();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string[] p = line.Split('\t');
                    if (p.Length < 8) continue;
                    Entry e = new Entry();
                    e.Id = Util.Unesc(p[0]);
                    DateTime.TryParse(p[1], null, System.Globalization.DateTimeStyles.RoundtripKind, out e.When);
                    e.Type = Util.Unesc(p[2]);
                    e.State = Util.Unesc(p[3]);
                    long.TryParse(p[4], out e.Bytes);
                    e.Title = Util.Unesc(p[5]);
                    e.Detail = Util.Unesc(p[6]);
                    e.Data = Util.Unesc(p[7]);
                    items.Add(e);
                }
            }
            catch { }
        }

        public static List<Entry> All()
        {
            lock (gate) { EnsureLoaded(); return new List<Entry>(items); }
        }

        public static Entry Add(string type, string title, string detail, string state, string data, long bytes, string id)
        {
            lock (gate)
            {
                EnsureLoaded();
                Entry e = new Entry();
                e.Id = id ?? NewId();
                e.When = DateTime.Now;
                e.Type = type;
                e.Title = title;
                e.Detail = detail ?? "";
                e.State = state;
                e.Data = data ?? "";
                e.Bytes = bytes;
                items.Add(e);
                // El historial no crece sin límite: se conservan las 300 últimas entradas,
                // pero nunca se descarta una que todavía se pueda deshacer.
                for (int i = 0; items.Count > 300 && i < items.Count; )
                {
                    if (items[i].State != Active) items.RemoveAt(i); else i++;
                }
                Save();
                return e;
            }
        }

        public static void Save()
        {
            lock (gate)
            {
                EnsureLoaded();
                StringBuilder sb = new StringBuilder();
                foreach (Entry e in items)
                {
                    sb.Append(Util.Esc(e.Id)).Append('\t')
                      .Append(e.When.ToString("o")).Append('\t')
                      .Append(Util.Esc(e.Type)).Append('\t')
                      .Append(Util.Esc(e.State)).Append('\t')
                      .Append(e.Bytes).Append('\t')
                      .Append(Util.Esc(e.Title)).Append('\t')
                      .Append(Util.Esc(e.Detail)).Append('\t')
                      .Append(Util.Esc(e.Data)).Append("\r\n");
                }
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                try
                {
                    if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                    else File.Move(tmp, FilePath);
                }
                catch
                {
                    File.Copy(tmp, FilePath, true);   // discos donde Replace no está disponible
                    File.Delete(tmp);
                }
            }
        }

        /// <summary>Última acción que aún se puede deshacer y cumple la condición.</summary>
        public static Entry LastActive(string type, Predicate<Entry> match)
        {
            List<Entry> all = All();
            for (int i = all.Count - 1; i >= 0; i--)
            {
                Entry e = all[i];
                if (e.Type == type && e.State == Active && (match == null || match(e))) return e;
            }
            return null;
        }

        /// <summary>Espacio que ocupa ahora la zona de seguridad (lo que aún se puede recuperar).</summary>
        public static long QuarantinedBytes()
        {
            long total = 0;
            foreach (Entry e in All())
                if (e.State == Active && (e.Type == "clean" || e.Type == "files")) total += e.Bytes;
            return total;
        }

        /// <summary>Borra de verdad la basura guardada hace más de N días (nunca archivos personales).</summary>
        public static long AutoPurge(int days)
        {
            long freed = 0;
            foreach (Entry e in All())
            {
                if (e.Type != "clean" || e.When >= DateTime.Now.AddDays(-days)) continue;
                lock (Quarantine.Gate)
                {
                    if (e.State != Active) continue;   // el usuario pudo deshacerla mientras tanto
                    freed += Quarantine.Purge(e.Data);
                    e.State = Final;
                    e.Detail = e.Detail + " · Espacio liberado automáticamente tras " + days + " días";
                }
            }
            if (freed > 0) Save();
            return freed;
        }

        /// <summary>Si la app se cerró a mitad de una limpieza, recupera esas carpetas para poder deshacerlas.</summary>
        public static void AdoptOrphans()
        {
            HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry e in All())
                foreach (string d in e.Data.Split('|')) known.Add(d);

            foreach (string baseDir in Quarantine.AllBases())
            {
                try
                {
                    if (!Directory.Exists(baseDir)) continue;
                    foreach (string dir in Directory.GetDirectories(baseDir))
                    {
                        if (known.Contains(dir) || !Quarantine.IsQuarantineDir(dir)) continue;
                        long bytes = Quarantine.SizeOf(dir);
                        Add("files", "Limpieza interrumpida (recuperada)",
                            "La app se cerró antes de terminar. Puedes deshacerla igualmente.",
                            Active, dir, bytes, null);
                    }
                }
                catch { }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Memoria RAM
    // ------------------------------------------------------------------
    public class ProcGroup
    {
        public string Name;
        public long Bytes;
        public int Count;
    }

    public static class Memory
    {
        static readonly string[] ProtectedNames = {
            "explorer", "dwm", "csrss", "winlogon", "services", "lsass", "svchost", "system", "idle",
            "smss", "wininit", "fontdrvhost", "memory compression", "registry", "sihost", "ctfmon",
            "taskhostw", "ligero", "secure system", "searchhost", "startmenuexperiencehost",
            "shellexperiencehost", "runtimebroker", "msmpeng", "securityhealthservice"
        };

        public static bool IsProtected(string name)
        {
            return Array.IndexOf(ProtectedNames, (name ?? "").ToLowerInvariant()) >= 0;
        }

        public static void Read(out long total, out long avail)
        {
            Native.MEMORYSTATUSEX m = new Native.MEMORYSTATUSEX();
            m.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            Native.GlobalMemoryStatusEx(ref m);
            total = (long)m.ullTotalPhys;
            avail = (long)m.ullAvailPhys;
        }

        /// <summary>
        /// Pide a Windows que saque de la RAM las páginas que cada programa no está usando ahora.
        /// No cierra ni modifica ningún programa. Devuelve cuántos programas se pudieron ajustar.
        /// </summary>
        public static int Trim()
        {
            int ok = 0;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    // PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_QUOTA
                    IntPtr h = Native.OpenProcess(0x1000 | 0x0100, false, p.Id);
                    if (h != IntPtr.Zero)
                    {
                        if (Native.EmptyWorkingSet(h)) ok++;
                        Native.CloseHandle(h);
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            return ok;
        }

        /// <summary>Programas que más memoria ocupan, agrupando sus procesos por nombre.</summary>
        public static List<ProcGroup> Top(int n)
        {
            Dictionary<string, ProcGroup> map = new Dictionary<string, ProcGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    string name = p.ProcessName;
                    ProcGroup g;
                    if (!map.TryGetValue(name, out g))
                    {
                        g = new ProcGroup();
                        g.Name = name;
                        map[name] = g;
                    }
                    g.Bytes += p.WorkingSet64;
                    g.Count++;
                }
                catch { }
                finally { p.Dispose(); }
            }
            List<ProcGroup> list = new List<ProcGroup>(map.Values);
            list.Sort(delegate(ProcGroup a, ProcGroup b) { return b.Bytes.CompareTo(a.Bytes); });
            if (list.Count > n) list.RemoveRange(n, list.Count - n);
            return list;
        }

        /// <summary>Termina a la fuerza todos los procesos de un programa. Lo que no esté guardado se pierde.</summary>
        public static int KillGroup(string name)
        {
            if (IsProtected(name)) return 0;
            int killed = 0;
            foreach (Process p in Process.GetProcessesByName(name))
            {
                try { p.Kill(); killed++; }
                catch { }
                finally { p.Dispose(); }
            }
            return killed;
        }

        /// <summary>Pide a un programa que se cierre con normalidad (como pulsar su X). No fuerza nada.</summary>
        public static int CloseGroup(string name)
        {
            if (IsProtected(name)) return 0;
            int asked = 0;
            foreach (Process p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow()) asked++;
                }
                catch { }
                finally { p.Dispose(); }
            }
            return asked;
        }
    }

    // ------------------------------------------------------------------
    //  Programas que arrancan con Windows
    //  Se usa el mismo mecanismo que el Administrador de tareas: no se borra nada,
    //  solo se marca la entrada como desactivada.
    // ------------------------------------------------------------------
    public class StartupItem
    {
        public string Name;
        public string Command;
        public string Source;   // HKCU, HKLM, HKLM32, CarpetaUsuario, CarpetaTodos
        public bool Enabled;

        public bool NeedsAdmin { get { return Source != "HKCU" && Source != "CarpetaUsuario"; } }

        public string SourceText
        {
            get { return NeedsAdmin ? "Todos los usuarios (pide administrador)" : "Tu usuario"; }
        }
    }

    public static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        static RegistryKey Machine(bool view32)
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view32 ? RegistryView.Registry32 : RegistryView.Registry64);
        }

        static RegistryKey OpenApproved(string source, bool writable)
        {
            string sub = ApprovedBase + (source == "HKLM32" ? "Run32" : (source.StartsWith("Carpeta") ? "StartupFolder" : "Run"));
            if (source == "HKCU" || source == "CarpetaUsuario")
                return writable ? Registry.CurrentUser.CreateSubKey(sub) : Registry.CurrentUser.OpenSubKey(sub);
            using (RegistryKey lm = Machine(false))
                return writable ? lm.CreateSubKey(sub) : lm.OpenSubKey(sub);
        }

        static bool ReadEnabled(string source, string name)
        {
            try
            {
                using (RegistryKey k = OpenApproved(source, false))
                {
                    if (k == null) return true;
                    byte[] b = k.GetValue(name) as byte[];
                    return b == null || b.Length == 0 || (b[0] & 1) == 0;
                }
            }
            catch { return true; }
        }

        static void AddRun(List<StartupItem> list, RegistryKey root, string source)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(RunKey))
                {
                    if (k == null) return;
                    foreach (string name in k.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        StartupItem it = new StartupItem();
                        it.Name = name;
                        it.Command = Convert.ToString(k.GetValue(name));
                        it.Source = source;
                        it.Enabled = ReadEnabled(source, name);
                        list.Add(it);
                    }
                }
            }
            catch { }
        }

        static void AddFolder(List<StartupItem> list, Environment.SpecialFolder folder, string source)
        {
            try
            {
                string dir = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                foreach (string f in Directory.GetFiles(dir))
                {
                    string name = Path.GetFileName(f);
                    if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    StartupItem it = new StartupItem();
                    it.Name = name;
                    it.Command = f;
                    it.Source = source;
                    it.Enabled = ReadEnabled(source, name);
                    list.Add(it);
                }
            }
            catch { }
        }

        public static List<StartupItem> List()
        {
            List<StartupItem> list = new List<StartupItem>();
            AddRun(list, Registry.CurrentUser, "HKCU");
            AddFolder(list, Environment.SpecialFolder.Startup, "CarpetaUsuario");
            using (RegistryKey lm = Machine(false)) AddRun(list, lm, "HKLM");
            if (Environment.Is64BitOperatingSystem)
                using (RegistryKey lm32 = Machine(true)) AddRun(list, lm32, "HKLM32");
            AddFolder(list, Environment.SpecialFolder.CommonStartup, "CarpetaTodos");
            return list;
        }

        static string Hex(byte[] b)
        {
            if (b == null) return "x";
            StringBuilder sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        static byte[] Unhex(string s)
        {
            byte[] b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return b;
        }

        /// <summary>Activa o desactiva el arranque. Devuelve el valor anterior para poder deshacer.</summary>
        public static string SetEnabled(string source, string name, bool enabled)
        {
            using (RegistryKey k = OpenApproved(source, true))
            {
                string prev = Hex(k.GetValue(name) as byte[]);
                byte[] v = new byte[12];
                v[0] = (byte)(enabled ? 2 : 3);
                if (!enabled) BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(v, 4);
                k.SetValue(name, v, RegistryValueKind.Binary);
                return prev;
            }
        }

        public static void Restore(string source, string name, string previousHex)
        {
            using (RegistryKey k = OpenApproved(source, true))
            {
                if (previousHex == "x" || string.IsNullOrEmpty(previousHex)) k.DeleteValue(name, false);
                else k.SetValue(name, Unhex(previousHex), RegistryValueKind.Binary);
            }
        }
    }

    // ------------------------------------------------------------------
    //  Zona de seguridad (cuarentena): nada se borra directamente.
    //  Los archivos se mueven aquí y se pueden devolver a su sitio.
    // ------------------------------------------------------------------
    public static class Quarantine
    {
        public const string OtherDriveFolder = "Ligero-Cuarentena";
        const string Manifest = "manifest.txt";

        /// <summary>Evita que deshacer y liberar espacio actúen a la vez sobre lo mismo.</summary>
        public static readonly object Gate = new object();

        public static string LocalBase { get { return Path.Combine(Util.AppDir, "Cuarentena"); } }

        public static List<string> AllBases()
        {
            List<string> l = new List<string>();
            l.Add(LocalBase);
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                    if (d.DriveType == DriveType.Fixed && d.IsReady) l.Add(Path.Combine(d.Name, OtherDriveFolder));
            }
            catch { }
            return l;
        }

        /// <summary>Carpeta de seguridad en el mismo disco que el archivo (mover es instantáneo y no gasta espacio).</summary>
        public static string BaseFor(string filePath)
        {
            if (filePath.StartsWith(@"\\")) return null;   // nunca en unidades de red
            string local = LocalBase;
            string root = Path.GetPathRoot(filePath);
            if (string.Equals(root, Path.GetPathRoot(local), StringComparison.OrdinalIgnoreCase)) return local;
            try
            {
                string other = Path.Combine(root, OtherDriveFolder);
                if (!Directory.Exists(other))
                {
                    DirectoryInfo di = Directory.CreateDirectory(other);
                    di.Attributes |= FileAttributes.Hidden;
                }
                return other;
            }
            catch { return null; }
        }

        static bool ValidStoredName(string s)
        {
            if (s.Length != 8 || !s.EndsWith(".q")) return false;
            for (int i = 0; i < 6; i++) if (!char.IsDigit(s[i])) return false;
            return true;
        }

        public static bool IsQuarantineDir(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return false;
                DirectoryInfo di = new DirectoryInfo(dir);
                if (!di.Exists || di.Parent == null) return false;
                if ((di.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                bool okParent = string.Equals(di.Parent.FullName.TrimEnd('\\'), LocalBase.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    || (string.Equals(di.Parent.Name, OtherDriveFolder, StringComparison.OrdinalIgnoreCase)
                        && di.Parent.Parent != null && di.Parent.Parent.Parent == null);
                return okParent && File.Exists(Path.Combine(dir, Manifest));
            }
            catch { return false; }
        }

        /// <summary>Una operación de limpieza. Puede repartirse en varios discos.</summary>
        public class Session : IDisposable
        {
            class Slot
            {
                public string Dir;
                public FileStream Stream;
                public StreamWriter Writer;
                public int N;
            }

            struct Plan
            {
                public string From;
                public string To;
                public long Length;
            }

            const int BatchSize = 64;
            readonly string id;
            readonly Dictionary<string, Slot> slots = new Dictionary<string, Slot>(StringComparer.OrdinalIgnoreCase);
            readonly List<FileInfo> pending = new List<FileInfo>();
            public long Bytes;
            public int Moved;
            public int Failed;
            public int Scheduled;
            /// <summary>Archivos que no se pudieron mover (en uso o protegidos). Como mucho 5000.</summary>
            public readonly List<string> FailedPaths = new List<string>();

            public Session(string id) { this.id = id; }

            /// <summary>
            /// Para archivos en uso: Windows los llevará a la zona de seguridad en el próximo reinicio.
            /// Se anota igual que un movimiento normal, así que también se puede deshacer después.
            /// </summary>
            public bool MoveOnReboot(FileInfo f)
            {
                try
                {
                    string baseDir = BaseFor(f.FullName);
                    if (baseDir == null) return false;
                    Slot s;
                    if (!slots.TryGetValue(baseDir, out s))
                    {
                        s = new Slot();
                        s.Dir = Path.Combine(baseDir, id);
                        Directory.CreateDirectory(s.Dir);
                        s.Stream = new FileStream(Path.Combine(s.Dir, Manifest), FileMode.Append, FileAccess.Write, FileShare.Read);
                        s.Writer = new StreamWriter(s.Stream, Encoding.UTF8);
                        slots[baseDir] = s;
                    }
                    if (s.N >= 999999) return false;
                    long len = f.Length;
                    string stored = (++s.N).ToString("000000") + ".q";
                    s.Writer.WriteLine(stored + "\t" + len + "\t" + f.FullName);
                    s.Writer.Flush();
                    s.Stream.Flush(true);
                    // MOVEFILE_DELAY_UNTIL_REBOOT
                    if (!Native.MoveFileEx(f.FullName, Path.Combine(s.Dir, stored), 4)) return false;
                    Bytes += len;
                    Scheduled++;
                    return true;
                }
                catch { return false; }
            }

            public string Dirs
            {
                get
                {
                    List<string> l = new List<string>();
                    foreach (Slot s in slots.Values) l.Add(s.Dir);
                    return string.Join("|", l.ToArray());
                }
            }

            /// <summary>Encola un archivo. Se mueve por lotes; llama a Flush antes de leer los contadores.</summary>
            public void Move(FileInfo f)
            {
                pending.Add(f);
                if (pending.Count >= BatchSize) Flush();
            }

            /// <summary>
            /// Apunta el lote en el registro, obliga a Windows a grabarlo físicamente en el disco y
            /// solo entonces mueve los archivos. Así, ni con un corte de luz queda un archivo
            /// guardado sin que conste de dónde vino.
            /// </summary>
            public void Flush()
            {
                if (pending.Count == 0) return;
                List<Plan> plans = new List<Plan>();
                List<Slot> touched = new List<Slot>();
                foreach (FileInfo f in pending)
                {
                    try
                    {
                        string baseDir = BaseFor(f.FullName);
                        if (baseDir == null) { Failed++; continue; }
                        Slot s;
                        if (!slots.TryGetValue(baseDir, out s))
                        {
                            s = new Slot();
                            s.Dir = Path.Combine(baseDir, id);
                            Directory.CreateDirectory(s.Dir);
                            s.Stream = new FileStream(Path.Combine(s.Dir, Manifest), FileMode.Append, FileAccess.Write, FileShare.Read);
                            s.Writer = new StreamWriter(s.Stream, Encoding.UTF8);
                            slots[baseDir] = s;
                        }
                        if (s.N >= 999999) { Failed++; continue; }
                        Plan p = new Plan();
                        p.From = f.FullName;
                        p.Length = f.Length;
                        string stored = (++s.N).ToString("000000") + ".q";
                        p.To = Path.Combine(s.Dir, stored);
                        s.Writer.WriteLine(stored + "\t" + p.Length + "\t" + p.From);
                        plans.Add(p);
                        if (!touched.Contains(s)) touched.Add(s);
                    }
                    catch { Failed++; }
                }
                pending.Clear();

                foreach (Slot s in touched)
                {
                    try
                    {
                        s.Writer.Flush();
                        s.Stream.Flush(true);
                    }
                    catch
                    {
                        Failed += plans.Count;   // sin registro seguro no se mueve nada
                        return;
                    }
                }

                foreach (Plan p in plans)
                {
                    try
                    {
                        File.Move(p.From, p.To);
                        Bytes += p.Length;
                        Moved++;
                    }
                    catch
                    {
                        Failed++;   // archivo en uso o sin permiso: se deja donde está
                        if (FailedPaths.Count < 5000) FailedPaths.Add(p.From);
                    }
                }
            }

            public void Dispose()
            {
                try { Flush(); } catch { }
                foreach (Slot s in slots.Values)
                {
                    try { s.Writer.Dispose(); } catch { }
                }
            }
        }

        static string AltName(string target)
        {
            string dir = Path.GetDirectoryName(target);
            string name = Path.GetFileNameWithoutExtension(target);
            string ext = Path.GetExtension(target);
            for (int i = 1; i < 1000; i++)
            {
                string cand = Path.Combine(dir, name + " (recuperado" + (i > 1 ? " " + i : "") + ")" + ext);
                if (!File.Exists(cand)) return cand;
            }
            throw new IOException("No hay nombre libre para restaurar " + target);
        }

        static IEnumerable<string[]> ReadManifest(string dir)
        {
            foreach (string line in File.ReadAllLines(Path.Combine(dir, Manifest), Encoding.UTF8))
            {
                string[] p = line.Split(new char[] { '\t' }, 3);
                if (p.Length == 3 && ValidStoredName(p[0])) yield return p;
            }
        }

        /// <summary>Devuelve cada archivo a su sitio original.</summary>
        public static void Restore(string dirs, out int restored, out int failed)
        {
            restored = 0;
            failed = 0;
            foreach (string dir in (dirs ?? "").Split('|'))
            {
                if (!IsQuarantineDir(dir)) continue;
                int failedHere = 0;
                foreach (string[] p in ReadManifest(dir))
                {
                    string stored = Path.Combine(dir, p[0]);
                    if (!File.Exists(stored)) continue;
                    try
                    {
                        // Ligero solo guarda archivos normales: un enlace aquí dentro no es suyo.
                        if ((File.GetAttributes(stored) & FileAttributes.ReparsePoint) != 0) { failedHere++; continue; }
                        string target = p[2];
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        if (File.Exists(target)) target = AltName(target);
                        File.Move(stored, target);
                        restored++;
                    }
                    catch { failedHere++; }
                }
                failed += failedHere;
                if (failedHere == 0)
                {
                    try
                    {
                        File.Delete(Path.Combine(dir, Manifest));
                        Directory.Delete(dir, false);
                    }
                    catch { }
                }
            }
        }

        /// <summary>Borra de verdad lo guardado (ya no se podrá deshacer). Devuelve los bytes liberados.</summary>
        public static long Purge(string dirs)
        {
            long freed = 0;
            foreach (string dir in (dirs ?? "").Split('|'))
            {
                if (!IsQuarantineDir(dir)) continue;
                bool allGone = true;
                foreach (string[] p in ReadManifest(dir))
                {
                    string stored = Path.Combine(dir, p[0]);
                    try
                    {
                        FileInfo fi = new FileInfo(stored);
                        if (!fi.Exists) continue;
                        if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) { allGone = false; continue; }
                        long len = fi.Length;
                        fi.Attributes = FileAttributes.Normal;
                        fi.Delete();
                        freed += len;
                    }
                    catch { allGone = false; }
                }
                if (allGone)
                {
                    try
                    {
                        File.Delete(Path.Combine(dir, Manifest));
                        Directory.Delete(dir, false);
                    }
                    catch { }
                }
            }
            return freed;
        }

        public static long SizeOf(string dir)
        {
            long total = 0;
            try
            {
                foreach (string[] p in ReadManifest(dir))
                {
                    FileInfo fi = new FileInfo(Path.Combine(dir, p[0]));
                    if (fi.Exists) total += fi.Length;
                }
            }
            catch { }
            return total;
        }
    }

    // ------------------------------------------------------------------
    //  Recorrido seguro de carpetas
    // ------------------------------------------------------------------
    public static class Walker
    {
        // Archivos que viven en la nube (OneDrive): tocarlos obligaría a descargarlos.
        const int CloudMask = 0x1000 | 0x40000 | 0x400000;

        public static bool IsCloudOnly(FileInfo f) { return ((int)f.Attributes & CloudMask) != 0; }

        /// <summary>Recorre una carpeta sin seguir accesos directos/enlaces y sin fallar por permisos.</summary>
        public static void Walk(string root, Action<FileInfo> onFile)
        {
            Stack<string> stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                DirectoryInfo di;
                try
                {
                    di = new DirectoryInfo(dir);
                    if (!di.Exists || (di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch { continue; }

                // Uno a uno, sin cargar la carpeta entera en memoria (hay cachés con decenas de miles de archivos).
                try
                {
                    foreach (FileInfo f in di.EnumerateFiles())
                    {
                        try { onFile(f); } catch { }
                    }
                }
                catch { }

                try
                {
                    foreach (DirectoryInfo d in di.GetDirectories())
                    {
                        if ((d.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if (string.Equals(d.Name, Quarantine.OtherDriveFolder, StringComparison.OrdinalIgnoreCase)) continue;
                        stack.Push(d.FullName);
                    }
                }
                catch { }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Limpieza de archivos basura
    // ------------------------------------------------------------------
    public class Category
    {
        public string Id;
        public string Title;
        public string Description;
        public bool Undoable = true;
        public bool DefaultChecked = true;
        public int MinAgeHours;
        public List<string> Roots = new List<string>();
        public List<string> Files = new List<string>();   // archivos sueltos, además de las carpetas
        public long Size = -1;
        public long Count;
    }

    public static class Cleaner
    {
        static void AddChromium(Category c, string userData)
        {
            try
            {
                if (!Directory.Exists(userData)) return;
                foreach (string profile in Directory.GetDirectories(userData))
                {
                    string n = Path.GetFileName(profile);
                    if (n != "Default" && !n.StartsWith("Profile ")) continue;
                    foreach (string sub in new string[] { @"Cache", @"Code Cache", @"GPUCache" })
                    {
                        string p = Path.Combine(profile, sub);
                        if (Directory.Exists(p)) c.Roots.Add(p);
                    }
                }
            }
            catch { }
        }

        public static List<Category> Build()
        {
            List<Category> list = new List<Category>();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            Category temp = new Category();
            temp.Id = "temp";
            temp.Title = "Archivos temporales de tus programas";
            temp.Description = "Restos que los programas e instaladores dejan mientras trabajan y luego olvidan borrar. " +
                "Solo se tocan los que llevan más de un día sin cambios, para no molestar a nada que esté abierto ahora. " +
                "Los archivos que estén en uso se dejan como están.";
            temp.MinAgeHours = 24;
            string tp = Path.GetTempPath().TrimEnd('\\');
            string last = Path.GetFileName(tp).ToLowerInvariant();
            if (last != "temp" && last != "tmp") tp = Path.Combine(local, "Temp");   // por si TEMP apunta a un sitio raro
            temp.Roots.Add(tp);
            list.Add(temp);

            Category wtemp = new Category();
            wtemp.Id = "wintemp";
            wtemp.Title = "Archivos temporales de Windows";
            wtemp.Description = "Lo mismo, pero de la carpeta temporal del propio Windows. Para limpiarla entera hay que " +
                "abrir Ligero con clic derecho > Ejecutar como administrador; si no, se limpia solo lo que Windows permita.";
            wtemp.MinAgeHours = 24;
            wtemp.Roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"));
            list.Add(wtemp);

            Category web = new Category();
            web.Id = "cache";
            web.Title = "Caché de navegadores (Chrome, Edge, Brave, Firefox)";
            web.Description = "Copias de imágenes y páginas que el navegador guarda para cargar más rápido. Borrarla NO " +
                "elimina contraseñas, favoritos, historial ni sesiones abiertas; las webs tardarán un poco más la primera " +
                "vez. Mejor con el navegador cerrado: lo que esté en uso o sea de la última hora se deja como está.";
            web.MinAgeHours = 1;
            AddChromium(web, Path.Combine(local, @"Google\Chrome\User Data"));
            AddChromium(web, Path.Combine(local, @"Microsoft\Edge\User Data"));
            AddChromium(web, Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data"));
            try
            {
                string ff = Path.Combine(local, @"Mozilla\Firefox\Profiles");
                if (Directory.Exists(ff))
                    foreach (string prof in Directory.GetDirectories(ff))
                    {
                        string p = Path.Combine(prof, "cache2");
                        if (Directory.Exists(p)) web.Roots.Add(p);
                    }
            }
            catch { }
            list.Add(web);

            Category dumps = new Category();
            dumps.Id = "errores";
            dumps.Title = "Informes de errores y volcados de fallos";
            dumps.Description = "Cuando un programa se cuelga, Windows guarda un informe (a veces enorme) por si quieres " +
                "enviarlo al fabricante. Si no estás investigando un fallo, no sirven para nada.";
            dumps.Roots.Add(Path.Combine(local, "CrashDumps"));
            dumps.Roots.Add(Path.Combine(local, @"Microsoft\Windows\WER\ReportArchive"));
            dumps.Roots.Add(Path.Combine(local, @"Microsoft\Windows\WER\ReportQueue"));
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            dumps.Roots.Add(Path.Combine(win, "Minidump"));
            dumps.Files.Add(Path.Combine(win, "MEMORY.DMP"));
            list.Add(dumps);

            Category wu = new Category();
            wu.Id = "wupdate";
            wu.Title = "Descargas antiguas de Windows Update";
            wu.Description = "Instaladores de actualizaciones que Windows ya descargó hace más de una semana y normalmente " +
                "ya instaló. Si Windows necesitara alguno, lo vuelve a descargar solo. Hace falta abrir Ligero como " +
                "administrador; si no, esta fila saldrá vacía.";
            wu.MinAgeHours = 24 * 7;
            wu.Roots.Add(Path.Combine(win, @"SoftwareDistribution\Download"));
            list.Add(wu);

            Category dopt = new Category();
            dopt.Id = "entrega";
            dopt.Title = "Caché de reparto de actualizaciones";
            dopt.Description = "Copias de actualizaciones que Windows guarda para compartirlas con otros equipos de tu red o de " +
                "internet. No las necesitas para nada. Hace falta abrir Ligero como administrador.";
            dopt.MinAgeHours = 24;
            dopt.Roots.Add(Path.Combine(win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"));
            list.Add(dopt);

            Category gfx = new Category();
            gfx.Id = "graficos";
            gfx.Title = "Cachés de gráficos de juegos y programas";
            gfx.Description = "Cálculos que la tarjeta gráfica guarda para no repetirlos (sombreadores). Con el tiempo se acumulan " +
                "los de juegos y programas que ya no usas. Se regeneran solos: la primera vez que abras cada juego puede tardar un poco más.";
            gfx.MinAgeHours = 24 * 3;
            gfx.Roots.Add(Path.Combine(local, "D3DSCache"));
            gfx.Roots.Add(Path.Combine(local, @"NVIDIA\DXCache"));
            gfx.Roots.Add(Path.Combine(local, @"NVIDIA\GLCache"));
            gfx.Roots.Add(Path.Combine(local, @"AMD\DxCache"));
            gfx.Roots.Add(Path.Combine(local, @"AMD\DxcCache"));
            list.Add(gfx);

            Category logs = new Category();
            logs.Id = "registros";
            logs.Title = "Registros antiguos de Windows";
            logs.Description = "Diarios de texto donde Windows apunta lo que hace al instalar y actualizar. Solo sirven para investigar " +
                "fallos recientes: se retiran los de más de un mes. Hace falta abrir Ligero como administrador.";
            logs.MinAgeHours = 24 * 30;
            logs.Roots.Add(Path.Combine(win, "Logs"));
            list.Add(logs);

            Category bin = new Category();
            bin.Id = "bin";
            bin.Title = "Papelera de reciclaje";
            bin.Description = "Vacía la papelera de Windows. ATENCIÓN: es lo único de esta lista que NO se puede deshacer, " +
                "por eso viene sin marcar. Revisa antes la papelera por si hay algo que quieras conservar.";
            bin.Undoable = false;
            bin.DefaultChecked = false;
            list.Add(bin);

            return list;
        }

        static bool Eligible(FileInfo f, Category c)
        {
            if (Walker.IsCloudOnly(f)) return false;
            if (c.MinAgeHours > 0 && f.LastWriteTimeUtc > DateTime.UtcNow.AddHours(-c.MinAgeHours)) return false;
            return true;
        }

        public static void Scan(Category c)
        {
            long size = 0, count = 0;
            if (c.Id == "bin")
            {
                Native.QueryRecycleBin(out size, out count);
            }
            else
            {
                foreach (string root in c.Roots)
                    Walker.Walk(root, delegate(FileInfo f)
                    {
                        if (!Eligible(f, c)) return;
                        size += f.Length;
                        count++;
                    });
                foreach (string path in c.Files)
                {
                    try
                    {
                        FileInfo f = new FileInfo(path);
                        if (f.Exists && Eligible(f, c)) { size += f.Length; count++; }
                    }
                    catch { }
                }
            }
            c.Size = size;
            c.Count = count;
        }

        /// <summary>Mueve la basura a la zona de seguridad. Devuelve un resumen para mostrar.</summary>
        public static string Clean(List<Category> selected)
        {
            StringBuilder summary = new StringBuilder();
            string id = History.NewId();
            List<string> names = new List<string>();
            bool emptyBin = false;

            using (Quarantine.Session s = new Quarantine.Session(id))
            {
                foreach (Category c in selected)
                {
                    if (c.Id == "bin") { emptyBin = true; continue; }
                    names.Add(c.Title);
                    Category cat = c;
                    foreach (string root in c.Roots)
                        Walker.Walk(root, delegate(FileInfo f)
                        {
                            if (Eligible(f, cat)) s.Move(f);
                        });
                    foreach (string path in c.Files)
                    {
                        try
                        {
                            FileInfo f = new FileInfo(path);
                            if (f.Exists && Eligible(f, c)) s.Move(f);
                        }
                        catch { }
                    }
                }
                s.Flush();

                if (s.Moved > 0)
                {
                    string detail = Util.Size(s.Bytes) + " en " + s.Moved + " archivos · " + string.Join(", ", names.ToArray());
                    History.Add("clean", "Limpieza de disco", detail, History.Active, s.Dirs, s.Bytes, id);
                    summary.Append("Se movieron " + Util.Size(s.Bytes) + " (" + s.Moved + " archivos) a la zona de seguridad.");
                    if (s.Failed > 0) summary.Append(" " + s.Failed + " estaban en uso y se dejaron como estaban.");
                }
                else if (names.Count > 0)
                {
                    summary.Append("No había nada que limpiar (o todo estaba en uso).");
                }
            }

            if (emptyBin)
            {
                long size, count;
                Native.QueryRecycleBin(out size, out count);
                if (count > 0)
                {
                    // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND
                    Native.SHEmptyRecycleBin(IntPtr.Zero, null, 1 | 2 | 4);
                    History.Add("bin", "Papelera vaciada", Util.Size(size) + " en " + count + " elementos",
                        History.Info, "", size, null);
                    if (summary.Length > 0) summary.Append(" ");
                    summary.Append("Papelera vaciada: " + Util.Size(size) + " liberados.");
                }
            }
            return summary.ToString();
        }
    }

    // ------------------------------------------------------------------
    //  Archivos grandes que no usas
    // ------------------------------------------------------------------
    public class OldFile
    {
        public string Path;
        public long Size;
        public DateTime LastUse;
    }

    public static class Finder
    {
        public static List<string> UserFolders()
        {
            List<string> l = new List<string>();
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] cands = {
                Path.Combine(profile, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };
            foreach (string c in cands)
            {
                if (string.IsNullOrEmpty(c) || !Directory.Exists(c)) continue;
                string full = Path.GetFullPath(c).TrimEnd('\\') + "\\";
                bool nested = false;
                foreach (string e in l)
                    if (full.StartsWith(e, StringComparison.OrdinalIgnoreCase)) nested = true;
                if (!nested) l.Add(full);
            }
            return l;
        }

        /// <summary>Archivos de al menos minMB que nadie ha abierto ni cambiado en "days" días.</summary>
        public static List<OldFile> FindUnused(IEnumerable<string> folders, int minMB, int days)
        {
            List<OldFile> list = new List<OldFile>();
            long minBytes = (long)minMB << 20;
            DateTime limit = DateTime.Now.AddDays(-days);
            foreach (string folder in folders)
            {
                Walker.Walk(folder, delegate(FileInfo f)
                {
                    if (f.Length < minBytes) return;
                    if ((f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) return;
                    if (Walker.IsCloudOnly(f)) return;
                    DateTime last = f.LastWriteTime;
                    if (f.LastAccessTime > last) last = f.LastAccessTime;
                    if (f.CreationTime > last) last = f.CreationTime;
                    if (last > limit) return;
                    OldFile o = new OldFile();
                    o.Path = f.FullName;
                    o.Size = f.Length;
                    o.LastUse = last;
                    list.Add(o);
                });
            }
            list.Sort(delegate(OldFile a, OldFile b) { return b.Size.CompareTo(a.Size); });
            if (list.Count > 500) list.RemoveRange(500, list.Count - 500);
            return list;
        }

        /// <summary>Mueve los archivos elegidos a la zona de seguridad. Devuelve cuántos se movieron.</summary>
        public static int MoveFiles(List<string> paths, out long bytes, out int failed)
        {
            string id = History.NewId();
            using (Quarantine.Session s = new Quarantine.Session(id))
            {
                foreach (string p in paths)
                {
                    FileInfo f = new FileInfo(p);
                    if (f.Exists) s.Move(f); else s.Failed++;
                }
                s.Flush();
                if (s.Moved > 0)
                    History.Add("files", "Archivos sin usar retirados",
                        Util.Size(s.Bytes) + " en " + s.Moved + " archivos", History.Active, s.Dirs, s.Bytes, id);
                bytes = s.Bytes;
                failed = s.Failed;
                return s.Moved;
            }
        }
    }

    // ------------------------------------------------------------------
    //  Deshacer
    // ------------------------------------------------------------------
    public static class Undo
    {
        /// <summary>Deshace una acción del historial. Devuelve un mensaje para el usuario.</summary>
        public static string Run(Entry e)
        {
            if (e.State != History.Active) return "Esta acción ya no se puede deshacer.";

            if (e.Type == "tweak")
            {
                int i = e.Data.IndexOf('|');
                Tweak t = i > 0 ? Tweaks.ById(e.Data.Substring(0, i)) : null;
                if (t == null) return "No se reconoce el ajuste.";
                t.Restore(e.Data.Substring(i + 1));
                e.State = History.Undone;
                History.Save();
                return "Ajuste devuelto a como estaba: " + t.Title + ".";
            }

            if (e.Type == "startup")
            {
                string[] p = e.Data.Split(new char[] { '|' }, 3);
                if (p.Length < 3) return "Datos incompletos.";
                Startup.Restore(p[1], p[2], p[0]);
                e.State = History.Undone;
                History.Save();
                return "Arranque de «" + p[2] + "» devuelto a como estaba.";
            }

            if (e.Type == "clean" || e.Type == "files")
            {
                int restored, failed;
                lock (Quarantine.Gate)
                {
                    if (e.State != History.Active) return "Esta acción ya no se puede deshacer.";
                    Quarantine.Restore(e.Data, out restored, out failed);
                    if (failed == 0)
                    {
                        e.State = History.Undone;
                        e.Bytes = 0;
                    }
                    else
                    {
                        e.Detail = e.Detail + " · " + failed + " archivos no se pudieron devolver (inténtalo de nuevo)";
                    }
                }
                History.Save();
                return restored + " archivos devueltos a su sitio." +
                       (failed > 0 ? " " + failed + " no se pudieron devolver; siguen guardados." : "");
            }

            if (e.Type == "reg")
            {
                int ok = 0, bad = 0;
                if (Directory.Exists(e.Data))
                {
                    foreach (string file in Directory.GetFiles(e.Data, "*.reg"))
                    {
                        string output;
                        if (Util.Run("reg.exe", "import \"" + file + "\"", out output) == 0) ok++; else bad++;
                    }
                }
                if (bad == 0)
                {
                    e.State = History.Undone;
                    History.Save();
                    return ok + " claves del registro restauradas.";
                }
                return bad + " claves no se pudieron restaurar (abre Ligero como administrador e inténtalo de nuevo).";
            }

            return "Esta acción no necesita deshacerse.";
        }

        /// <summary>Borra de verdad todo lo guardado en la zona de seguridad.</summary>
        public static long PurgeAll()
        {
            long freed = 0;
            foreach (Entry e in History.All())
            {
                if (e.Type != "clean" && e.Type != "files") continue;
                lock (Quarantine.Gate)
                {
                    if (e.State != History.Active) continue;
                    freed += Quarantine.Purge(e.Data);
                    e.State = History.Final;
                    e.Bytes = 0;
                }
            }
            History.Save();
            return freed;
        }
    }
}
