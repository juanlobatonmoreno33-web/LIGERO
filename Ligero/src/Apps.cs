using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ligero
{
    // ------------------------------------------------------------------
    //  Análisis de riesgo: qué pasa si se elimina un archivo, carpeta o programa
    // ------------------------------------------------------------------
    public class Verdict
    {
        /// <summary>0 = seguro, 1 = precaución, 2 = peligro, 3 = crítico.</summary>
        public int Level;
        public string Title;
        public string Text;
        /// <summary>Ligero se niega a eliminarlo: dejaría el equipo inservible.</summary>
        public bool Refuse;

        public Verdict(int level, string title, string text) { Level = level; Title = title; Text = text; }

        public static Verdict No(string text)
        {
            Verdict v = new Verdict(3, "Ligero no elimina esto", text);
            v.Refuse = true;
            return v;
        }
    }

    public static class Risk
    {
        static readonly string[] SecurityWords = {
            "antivirus", "anti-virus", "kaspersky", "avast", "avg", "norton", "mcafee", "eset", "bitdefender",
            "malwarebytes", "defender", "firewall", "cortafuegos", "panda security", "trend micro", "sophos",
            "avira", "f-secure", "webroot", "internet security", "endpoint protection", "windows security"
        };
        static readonly string[] HardwareWords = {
            "driver", "controlador", "chipset", "graphics", "gráficos", "audio", "bluetooth", "wireless", "wi-fi",
            "wifi", "wlan", "touchpad", "firmware", "realtek", "synaptics", "nvidia", "radeon", "geforce",
            "management engine", "rapid storage", "thunderbolt", "card reader", "serial io", "elan"
        };
        static readonly string[] HardwareMakers = {
            "intel", "advanced micro devices", "amd", "nvidia", "realtek", "synaptics", "qualcomm", "mediatek",
            "elan", "broadcom", "conexant", "dolby", "dts"
        };
        static readonly string[] RuntimeWords = {
            "visual c++", "redistributable", ".net", "directx", "webview2", "windows app runtime", "desktop runtime",
            "asp.net", "vulkan", "openal", "physx", "java ", "jre", "msxml", "visual studio tools for office"
        };
        static readonly string[] MakerWords = {
            "acer", "predator", "hp ", "hewlett", "lenovo", "dell", "asus", "armoury", "msi ", "samsung", "toshiba",
            "huawei", "vaio", "quick access", "hotkey", "care center"
        };

        static bool HasAny(string text, string[] words)
        {
            foreach (string w in words) if (text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static bool LooksLikeSecurity(string text) { return HasAny(text ?? "", SecurityWords); }

        public static string Norm(string p)
        {
            return Path.GetFullPath(p).TrimEnd('\\');
        }

        static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }

        /// <summary>¿"path" está dentro de "dir" (sin ser la propia carpeta)?</summary>
        public static bool Inside(string path, string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            return path.Length > dir.Length && path.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Primera carpeta por debajo de "dir" en la ruta (el "dueño" del archivo).</summary>
        static string TopFolder(string path, string dir)
        {
            string rest = path.Substring(dir.Length + 1);
            int i = rest.IndexOf('\\');
            return i < 0 ? rest : rest.Substring(0, i);
        }

        static string Special(Environment.SpecialFolder f)
        {
            string s = Environment.GetFolderPath(f);
            return string.IsNullOrEmpty(s) ? "" : Norm(s);
        }

        /// <summary>Evalúa qué supondría eliminar un archivo o carpeta.</summary>
        public static Verdict ForPath(string rawPath)
        {
            string path;
            try { path = Norm(rawPath); }
            catch { return Verdict.No("La ruta no es válida."); }

            string win = Special(Environment.SpecialFolder.Windows);
            string pf = Special(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Special(Environment.SpecialFolder.ProgramFilesX86);
            string pd = Special(Environment.SpecialFolder.CommonApplicationData);
            string profile = Special(Environment.SpecialFolder.UserProfile);
            string local = Special(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Special(Environment.SpecialFolder.ApplicationData);
            string users = "";
            try { users = Norm(Directory.GetParent(profile).FullName); } catch { }
            string appDir = Norm(Util.AppDir);
            string root = (Path.GetPathRoot(path) ?? "").TrimEnd('\\');
            string name = Path.GetFileName(path);
            string parent = (Path.GetDirectoryName(path) ?? "").TrimEnd('\\');

            // ---- Cosas que Ligero no elimina nunca ----
            if (Same(path, root))
                return Verdict.No("Es una unidad de disco entera. Borrarla eliminaría todo lo que contiene.");

            string[] vital = { win, pf, pf86, pd, users, profile, local, roaming };
            string[] vitalWhy = {
                "Es la carpeta de Windows. Sin ella el equipo no arranca.",
                "Es la carpeta donde están instalados todos tus programas.",
                "Es la carpeta donde están instalados todos tus programas.",
                "Es la carpeta donde todos los programas guardan sus datos compartidos.",
                "Es la carpeta que contiene a todos los usuarios del equipo.",
                "Es tu carpeta de usuario entera: documentos, escritorio, configuración… todo.",
                "Es la carpeta donde todos tus programas guardan su configuración.",
                "Es la carpeta donde todos tus programas guardan su configuración."
            };
            for (int i = 0; i < vital.Length; i++)
            {
                if (vital[i].Length == 0) continue;
                if (Same(path, vital[i])) return Verdict.No(vitalWhy[i]);
                if (Inside(vital[i], path))
                    return Verdict.No("Esta carpeta contiene dentro partes esenciales del sistema (" + vital[i] + ").");
            }

            if (Same(path, appDir) || Inside(path, appDir) ||
                (path + "\\").IndexOf("\\" + Quarantine.OtherDriveFolder + "\\", StringComparison.OrdinalIgnoreCase) >= 0)
                return Verdict.No("Es la zona de seguridad del propio Ligero. Para vaciarla usa «Liberar espacio definitivamente» en la pestaña Deshacer.");

            if (Same(parent, root))
            {
                string[] rootVital = { "pagefile.sys", "hiberfil.sys", "swapfile.sys", "bootmgr", "bootnxt",
                    "System Volume Information", "$Recycle.Bin", "Recovery", "EFI", "Boot", "$WinREAgent" };
                foreach (string r in rootVital)
                    if (Same(name, r))
                        return Verdict.No("«" + name + "» es una pieza que Windows usa para arrancar, para la memoria o para recuperarse de fallos.");
            }

            if (win.Length > 0)
            {
                string[] winVital = { "System32", "SysWOW64", "WinSxS", "Boot", "servicing", "assembly", "Microsoft.NET",
                    @"System32\drivers", @"System32\config", "Fonts", "INF", "SystemApps", "Installer" };
                foreach (string w in winVital)
                    if (Same(path, win + "\\" + w))
                        return Verdict.No("«" + w + "» es una carpeta central de Windows. Sin ella el equipo no arranca o deja de poder actualizarse.");
                if (Inside(path, win + @"\System32\config"))
                    return Verdict.No("Forma parte del registro de Windows: ahí se guarda toda la configuración del equipo y las cuentas de usuario.");
            }

            if (profile.Length > 0 && Same(parent, profile) && name.StartsWith("ntuser.", StringComparison.OrdinalIgnoreCase))
                return Verdict.No("Es el archivo donde Windows guarda toda la configuración de tu usuario. Sin él no podrías iniciar sesión.");

            foreach (string p in new string[] { pf, pf86 })
            {
                if (p.Length == 0) continue;
                if (Same(path, p + @"\WindowsApps") || Same(path, p + @"\Common Files"))
                    return Verdict.No("Es una carpeta compartida por muchos programas y por las apps de Windows.");
            }
            if (pd.Length > 0 && Same(path, pd + @"\Microsoft"))
                return Verdict.No("Contiene los datos de Windows y de sus componentes de seguridad.");

            // ---- Crítico: dentro de Windows ----
            if (win.Length > 0 && Inside(path, win))
            {
                string extra = "";
                if (Same(path, win + @"\Temp") || Inside(path, win + @"\Temp") || Inside(path, win + @"\SoftwareDistribution\Download") ||
                    Inside(path, win + @"\Minidump"))
                    return new Verdict(1, "Precaución: zona temporal de Windows",
                        "Está dentro de Windows, pero en una zona de archivos temporales o descargas que se pueden regenerar. " +
                        "Lo normal es limpiarla desde la pestaña Disco.");
                if (Inside(path, win + @"\System32\drivers") || path.EndsWith(".sys", StringComparison.OrdinalIgnoreCase))
                    extra = " Es un controlador: algún dispositivo (pantalla, disco, red, teclado…) puede dejar de funcionar o el equipo puede no arrancar.";
                else if (Inside(path, win + @"\WinSxS") || Inside(path, win + @"\servicing"))
                    extra = " Windows lo usa para repararse y actualizarse: sin él las actualizaciones pueden fallar para siempre.";
                return new Verdict(3, "CRÍTICO: forma parte de Windows",
                    "Este elemento está dentro de la carpeta de Windows. Eliminarlo puede hacer que Windows falle, que deje de " +
                    "actualizarse o que no arranque." + extra + " Windows protege muchos de estos archivos y no dejará moverlos; " +
                    "Ligero no fuerza esa protección.");
            }

            // ---- Seguridad de Windows ----
            foreach (string p in new string[] { pf, pf86, pd.Length > 0 ? pd + @"\Microsoft" : "" })
            {
                if (p.Length == 0 || !Inside(path, p)) continue;
                string top = TopFolder(path, p);
                if (top.StartsWith("Windows Defender", StringComparison.OrdinalIgnoreCase) ||
                    top.StartsWith("Windows Security", StringComparison.OrdinalIgnoreCase))
                    return new Verdict(3, "CRÍTICO: es el antivirus de Windows",
                        "Pertenece a Microsoft Defender, la protección contra virus incluida en Windows. Sin él el equipo queda " +
                        "expuesto a virus y programas maliciosos. Windows lo protege y no dejará moverlo.");
            }

            // ---- Dentro de programas instalados ----
            foreach (string p in new string[] { pf, pf86 })
            {
                if (p.Length == 0 || !Inside(path, p)) continue;
                string top = TopFolder(path, p);
                if (LooksLikeSecurity(top))
                    return new Verdict(2, "PELIGRO: compromete la seguridad del equipo",
                        "Pertenece a «" + top + "», que parece un programa de protección (antivirus o cortafuegos). Si lo dañas, " +
                        "el equipo puede quedarse sin protección sin que te avise. Si quieres quitarlo, hazlo desde la pestaña Programas.");
                if (top.StartsWith("Windows", StringComparison.OrdinalIgnoreCase) || Same(top, "Microsoft") || Same(top, "Common Files") ||
                    Same(top, "Internet Explorer") || Same(top, "ModifiableWindowsApps"))
                    return new Verdict(3, "CRÍTICO: componente incluido en Windows",
                        "Está en «" + top + "», una carpeta de componentes que vienen con Windows o que comparten muchos programas. " +
                        "Eliminarlo puede romper funciones del sistema o varias aplicaciones a la vez.");
                return new Verdict(2, "PELIGRO: pertenece a un programa instalado",
                    "Forma parte del programa «" + top + "». Si borras piezas sueltas, el programa puede dejar de abrirse o fallar, " +
                    "y además seguirá apareciendo como instalado. Lo correcto es desinstalarlo desde la pestaña Programas, " +
                    "que después limpia sus restos.");
            }

            if (pd.Length > 0 && Inside(path, pd))
            {
                string top = TopFolder(path, pd);
                if (Same(top, "Microsoft"))
                    return new Verdict(3, "CRÍTICO: datos de Windows",
                        "Aquí Windows guarda datos de sus propios componentes (inicio, actualizaciones, seguridad, apps). " +
                        "Eliminarlo puede provocar fallos difíciles de reparar.");
                if (LooksLikeSecurity(top))
                    return new Verdict(2, "PELIGRO: compromete la seguridad del equipo",
                        "Son datos de «" + top + "», que parece un programa de protección. Sin ellos puede dejar de protegerte.");
                return new Verdict(2, "PELIGRO: datos compartidos de un programa",
                    "Son datos del programa «" + top + "» que usan todos los usuarios del equipo (licencias, bases de datos, " +
                    "configuración). El programa puede fallar o pedirte activarlo de nuevo.");
            }

            // ---- Configuración de programas en tu usuario ----
            foreach (string p in new string[] { local, roaming })
            {
                if (p.Length == 0 || !Inside(path, p)) continue;
                string top = TopFolder(path, p);
                if (Same(top, "Temp"))
                    return new Verdict(0, "Seguro: archivo temporal",
                        "Está en la carpeta de temporales. Los programas la usan como borrador y no deberían necesitarlo después.");
                if (Same(top, "Microsoft") || Same(top, "Packages"))
                    return new Verdict(2, "PELIGRO: datos de Windows y de sus apps",
                        "Aquí se guardan tu menú Inicio, tus credenciales guardadas, el navegador Edge y las apps de la Tienda. " +
                        "Puedes perder sesiones iniciadas, contraseñas guardadas o configuración del sistema.");
                if (LooksLikeSecurity(top))
                    return new Verdict(2, "PELIGRO: compromete la seguridad del equipo",
                        "Son datos de «" + top + "», que parece un programa de protección.");
                return new Verdict(1, "Precaución: configuración de un programa",
                    "Son datos que «" + top + "» guarda para tu usuario: ajustes, sesiones iniciadas, partidas guardadas o caché. " +
                    "El programa seguirá instalado, pero puede arrancar como recién instalado y habrás perdido lo que guardaba ahí.");
            }

            // ---- Resto ----
            try
            {
                FileAttributes at = File.GetAttributes(path);
                if ((at & FileAttributes.System) != 0)
                    return new Verdict(2, "PELIGRO: marcado como archivo de sistema",
                        "Windows o un programa lo marcó como archivo de sistema, lo que indica que lo necesita para funcionar. " +
                        "No lo elimines si no sabes exactamente qué es.");
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if ((at & FileAttributes.Directory) == 0 && (ext == ".sys" || ext == ".dll" || ext == ".exe" || ext == ".drv"))
                {
                    string company = "";
                    try { company = FileVersionInfo.GetVersionInfo(path).CompanyName ?? ""; } catch { }
                    if (ext == ".sys" || ext == ".drv")
                        return new Verdict(2, "PELIGRO: es un controlador",
                            "Los archivos de este tipo hacen funcionar dispositivos del equipo. Si alguno lo está usando, puede dejar de funcionar.");
                    return new Verdict(1, "Precaución: es un programa o una pieza de programa",
                        "Es un archivo ejecutable" + (company.Length > 0 ? " de «" + company + "»" : "") +
                        ". Si pertenece a un programa que usas, ese programa dejará de funcionar. Si es un instalador que descargaste, puedes borrarlo sin problema.");
                }
                if ((at & FileAttributes.Hidden) != 0)
                    return new Verdict(1, "Precaución: está oculto",
                        "Windows oculta archivos que normalmente no hay que tocar. Puede ser configuración de alguna carpeta o programa.");
            }
            catch { }

            if (profile.Length > 0 && Inside(path, profile))
                return new Verdict(0, "Seguro para el equipo: es un archivo tuyo",
                    "Está en tus carpetas personales. Eliminarlo no afecta a Windows ni a la seguridad: lo único que se pierde es el propio contenido.");

            return new Verdict(0, "Seguro para el equipo",
                "No forma parte de Windows ni de la carpeta de un programa instalado. Eliminarlo no afecta al sistema ni a la seguridad; " +
                "solo se pierde su contenido.");
        }

        /// <summary>Evalúa qué supondría desinstalar un programa.</summary>
        public static Verdict ForApp(AppInfo a)
        {
            string text = (a.Name + " " + a.Publisher).ToLowerInvariant() + " ";
            string pub = (a.Publisher ?? "").ToLowerInvariant();
            string maker = pub.Replace(" corporation", "").Replace(", inc.", "").Replace(" inc.", "").Replace(" inc", "").Trim();

            if (HasAny(text, SecurityWords))
                return new Verdict(2, "PELIGRO: compromete la seguridad",
                    "Parece un programa de protección (antivirus, cortafuegos o similar). Al quitarlo el equipo queda sin esa defensa. " +
                    "Si es un antivirus de terceros, Windows activará su propio antivirus (Microsoft Defender) poco después; " +
                    "comprueba luego en «Seguridad de Windows» que todo aparece en verde.");
            if (HasAny(text, RuntimeWords))
                return new Verdict(2, "PELIGRO: otros programas dependen de él",
                    "Es una pieza compartida que muchos programas y juegos necesitan para abrirse. No ocupa casi nada. " +
                    "Si la quitas, programas que ahora funcionan pueden mostrar errores del tipo «falta un archivo .dll».");
            if (HasAny(text, HardwareWords) || Array.IndexOf(HardwareMakers, maker) >= 0)
                return new Verdict(2, "PELIGRO: controla una parte del equipo",
                    "Parece un controlador o un programa del fabricante de una pieza (gráficos, sonido, wifi, bluetooth, panel táctil…). " +
                    "Al quitarlo esa parte puede dejar de funcionar o funcionar peor hasta que se reinstale.");
            if (a.SystemComponent)
                return new Verdict(2, "PELIGRO: componente del sistema",
                    "Windows o su instalador lo marcan como componente interno y por eso no aparece en la lista normal de programas. " +
                    "Suele ser una pieza de otro programa o del propio sistema; quitarlo por separado puede romper algo.");
            if (pub.Contains("microsoft") && (text.Contains("edge") || text.Contains("onedrive") || text.Contains("update") || text.Contains("windows")))
                return new Verdict(1, "Precaución: viene con Windows",
                    "Es un programa de Microsoft incluido con Windows. Se puede quitar, pero algunas funciones dejarán de estar " +
                    "disponibles (por ejemplo la copia en la nube o abrir enlaces) y Windows puede volver a instalarlo al actualizarse.");
            if (HasAny(text, MakerWords))
                return new Verdict(1, "Precaución: utilidad del fabricante del equipo",
                    "Parece una herramienta del fabricante de tu portátil. Algunas solo son publicidad, pero otras controlan las teclas " +
                    "especiales, los ventiladores, la batería o las actualizaciones de la BIOS. Quítala solo si sabes que no la usas.");
            return new Verdict(0, "Seguro de quitar",
                "Es un programa normal: desinstalarlo no afecta a Windows ni a la seguridad del equipo. Solo perderás el propio programa " +
                "y, si luego limpias sus restos, también su configuración.");
        }

        static readonly string[] StoreVital = {
            "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller", "Microsoft.SecHealthUI",
            "Microsoft.AAD.BrokerPlugin", "Microsoft.AccountsControl", "Microsoft.LockApp", "MicrosoftWindows.",
            "windows.immersivecontrolpanel", "Microsoft.WindowsAppRuntime", "Microsoft.VCLibs", "Microsoft.UI.Xaml",
            "Microsoft.NET.", "Microsoft.Services.Store.Engagement", "Microsoft.Winget", "Microsoft.Windows.",
            "Microsoft.Win32WebViewHost", "Microsoft.CredDialogHost", "Microsoft.ECApp", "Microsoft.BioEnrollment"
        };

        public static Verdict ForStoreApp(AppInfo a)
        {
            if (a.Locked)
                return new Verdict(3, "CRÍTICO: parte de Windows",
                    "Es una pieza interna de Windows (menú Inicio, pantalla de bloqueo, Configuración…). Windows no permite quitarla " +
                    "y Ligero no lo fuerza: hacerlo dejaría el sistema inestable.");
            foreach (string v in StoreVital)
                if (a.Name.StartsWith(v, StringComparison.OrdinalIgnoreCase) && !a.Name.StartsWith("Microsoft.Windows.Photos", StringComparison.OrdinalIgnoreCase))
                    return new Verdict(3, "CRÍTICO: Windows u otras apps la necesitan",
                        "Es la Tienda, el instalador de apps, la seguridad de Windows o una pieza que otras apps necesitan para abrirse. " +
                        "Sin ella dejarían de funcionar las apps de la Tienda o partes del sistema, y recuperarla es complicado.");
            if (LooksLikeSecurity(a.Name))
                return new Verdict(2, "PELIGRO: compromete la seguridad", "Parece una app de protección del equipo.");
            if (a.Name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase))
                return new Verdict(1, "Precaución: app incluida con Windows",
                    "Es una app que viene con Windows. Quitarla no daña el sistema y libera espacio; si la echas de menos se puede " +
                    "volver a instalar gratis desde Microsoft Store. Desinstalarla no se puede deshacer desde Ligero.");
            return new Verdict(0, "Seguro de quitar",
                "Es una app de la Tienda. Quitarla no afecta a Windows; se puede volver a instalar desde Microsoft Store. " +
                "Desinstalarla no se puede deshacer desde Ligero.");
        }
    }

    // ------------------------------------------------------------------
    //  Programas instalados
    // ------------------------------------------------------------------
    public class AppInfo
    {
        public string Name = "", Publisher = "", Version = "", InstallLocation = "", UninstallString = "";
        public long SizeBytes;
        public string RegRoot;      // HKLM o HKCU
        public bool View32;
        public string KeyName;
        public bool IsMsi;
        public bool SystemComponent;
        public bool IsStore;
        public string PackageFullName;
        public bool Locked;
        public Verdict Risk;

        public string RegPath
        {
            get
            {
                return RegRoot + @"\Software\" + (View32 && RegRoot == "HKLM" ? @"WOW6432Node\" : "") +
                       @"Microsoft\Windows\CurrentVersion\Uninstall\" + KeyName;
            }
        }

        public string Kind
        {
            get { return IsStore ? "App de la Tienda" : (SystemComponent ? "Componente del sistema" : "Programa"); }
        }
    }

    public class Leftover
    {
        public string Kind;     // Carpeta o Registro
        public string Path;
        public long Size;
        public long Count;
        public string Why;
    }

    public static class Apps
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

        static void ReadHive(List<AppInfo> list, HashSet<string> seen, RegistryHive hive, RegistryView view, bool includeSystem)
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey un = root.OpenSubKey(UninstallKey))
                {
                    if (un == null) return;
                    foreach (string keyName in un.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey k = un.OpenSubKey(keyName))
                            {
                                if (k == null) continue;
                                string name = Convert.ToString(k.GetValue("DisplayName") ?? "").Trim();
                                if (name.Length == 0) continue;
                                if (k.GetValue("ParentKeyName") != null) continue;   // parches de otro programa
                                string release = Convert.ToString(k.GetValue("ReleaseType") ?? "");
                                if (release.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    release.IndexOf("Hotfix", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                bool system = (k.GetValue("SystemComponent") as int? ?? 0) == 1;
                                if (system && !includeSystem) continue;

                                AppInfo a = new AppInfo();
                                a.Name = name;
                                a.Publisher = Convert.ToString(k.GetValue("Publisher") ?? "").Trim();
                                a.Version = Convert.ToString(k.GetValue("DisplayVersion") ?? "").Trim();
                                a.InstallLocation = Convert.ToString(k.GetValue("InstallLocation") ?? "").Trim().Trim('"').TrimEnd('\\');
                                a.UninstallString = Convert.ToString(k.GetValue("UninstallString") ?? "").Trim();
                                a.SizeBytes = (long)(k.GetValue("EstimatedSize") as int? ?? 0) * 1024;
                                a.RegRoot = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
                                a.View32 = view == RegistryView.Registry32;
                                a.KeyName = keyName;
                                Guid g;
                                a.IsMsi = (k.GetValue("WindowsInstaller") as int? ?? 0) == 1 && Guid.TryParse(keyName, out g);
                                a.SystemComponent = system;
                                if (!seen.Add(a.Name + "|" + a.Version)) continue;
                                a.Risk = Risk.ForApp(a);
                                list.Add(a);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        static string PublisherName(string dn)
        {
            Match m = Regex.Match(dn ?? "", @"O=""?([^,""]+)");
            if (!m.Success) m = Regex.Match(dn ?? "", @"CN=""?([^,""]+)");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        static void ReadStore(List<AppInfo> list)
        {
            string output;
            int code = Util.RunPS(
                "Get-AppxPackage | ForEach-Object { ($_.Name, $_.PackageFullName, $_.Publisher, $_.NonRemovable, $_.IsFramework, $_.SignatureKind, $_.InstallLocation) -join [char]9 }",
                60000, out output);
            if (code != 0) return;
            foreach (string line in output.Split('\n'))
            {
                string[] p = line.Trim('\r').Split('\t');
                if (p.Length < 7 || p[0].Length == 0) continue;
                if (string.Equals(p[4], "True", StringComparison.OrdinalIgnoreCase)) continue;   // piezas compartidas
                AppInfo a = new AppInfo();
                a.IsStore = true;
                a.Name = p[0];
                a.PackageFullName = p[1];
                a.Publisher = PublisherName(p[2]);
                a.Locked = string.Equals(p[3], "True", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(p[5], "System", StringComparison.OrdinalIgnoreCase);
                a.InstallLocation = p[6];
                a.Risk = Risk.ForStoreApp(a);
                list.Add(a);
            }
        }

        public static List<AppInfo> List(bool includeSystem)
        {
            List<AppInfo> list = new List<AppInfo>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ReadHive(list, seen, RegistryHive.LocalMachine, RegistryView.Registry64, includeSystem);
            if (Environment.Is64BitOperatingSystem)
                ReadHive(list, seen, RegistryHive.LocalMachine, RegistryView.Registry32, includeSystem);
            ReadHive(list, seen, RegistryHive.CurrentUser, RegistryView.Default, includeSystem);
            if (includeSystem) ReadStore(list);
            list.Sort(delegate(AppInfo a, AppInfo b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            return list;
        }

        public static bool StillInstalled(AppInfo a)
        {
            try
            {
                if (a.IsStore)
                {
                    string output;
                    Util.RunPS("if (Get-AppxPackage | Where-Object { $_.PackageFullName -eq '" + a.PackageFullName.Replace("'", "''") + "' }) { 'SI' }", 60000, out output);
                    return output.Contains("SI");
                }
                using (RegistryKey root = RegistryKey.OpenBaseKey(
                    a.RegRoot == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
                    a.RegRoot == "HKLM" ? (a.View32 ? RegistryView.Registry32 : RegistryView.Registry64) : RegistryView.Default))
                using (RegistryKey k = root.OpenSubKey(UninstallKey + "\\" + a.KeyName))
                    return k != null && k.GetValue("DisplayName") != null;
            }
            catch { return true; }
        }

        /// <summary>Punto de restauración de Windows antes de desinstalar (solo como administrador).</summary>
        public static string CreateRestorePoint(string appName)
        {
            if (!Util.IsAdmin) return "Sin punto de restauración (hace falta abrir Ligero como administrador).";
            string clean = Regex.Replace(appName, @"[^\w \.\-]", "");
            if (clean.Length > 40) clean = clean.Substring(0, 40);
            int code = -1;
            try
            {
                // Directamente a Restaurar sistema de Windows: 1 = desinstalación de aplicación, 100 = inicio de un cambio.
                using (System.Management.ManagementClass sr = new System.Management.ManagementClass(@"\\.\root\default:SystemRestore"))
                using (System.Management.ManagementBaseObject args = sr.GetMethodParameters("CreateRestorePoint"))
                {
                    args["Description"] = "Ligero antes de quitar " + clean;
                    args["RestorePointType"] = 1;
                    args["EventType"] = 100;
                    using (System.Management.ManagementBaseObject result = sr.InvokeMethod("CreateRestorePoint", args, null))
                        code = Convert.ToInt32(result["ReturnValue"]);
                }
            }
            catch { }
            return code == 0
                ? "Punto de restauración de Windows creado (o ya había uno de las últimas 24 horas)."
                : "No se pudo crear el punto de restauración (la Protección del sistema puede estar desactivada).";
        }

        /// <summary>
        /// Lanza el desinstalador propio del programa. Devuelve true si ya terminó (apps de la Tienda);
        /// false si se abrió el asistente del programa y hay que esperar a que el usuario lo complete.
        /// </summary>
        public static bool Uninstall(AppInfo a, out string message)
        {
            if (a.IsStore)
            {
                if (a.Locked) throw new InvalidOperationException("Windows no permite quitar esta app y Ligero no lo fuerza.");
                string output;
                int code = Util.RunPS(
                    "try { Remove-AppxPackage -Package '" + a.PackageFullName.Replace("'", "''") + "' -ErrorAction Stop; exit 0 } " +
                    "catch { Write-Output $_.Exception.Message; exit 1 }", 300000, out output);
                if (code != 0) throw new InvalidOperationException("Windows no pudo quitar la app. " + output.Trim());
                message = "App quitada. Si la echas de menos, se reinstala desde Microsoft Store.";
                return true;
            }

            ProcessStartInfo psi;
            string cmd = a.UninstallString;
            if (a.IsMsi)
            {
                psi = new ProcessStartInfo("msiexec.exe", "/x " + a.KeyName);
            }
            else
            {
                if (cmd.Length == 0)
                    throw new InvalidOperationException("Este programa no trae desinstalador propio. Puedes usar «Buscar restos» para retirar sus archivos.");
                if (Regex.IsMatch(cmd, @"^\s*""?msiexec", RegexOptions.IgnoreCase))
                    cmd = Regex.Replace(cmd, @"/I\s*\{", "/X{", RegexOptions.IgnoreCase);
                psi = new ProcessStartInfo("cmd.exe", "/s /c \"" + cmd + "\"");
                psi.CreateNoWindow = true;
            }
            psi.UseShellExecute = false;
            Process.Start(psi).Dispose();
            message = "Se abrió el desinstalador de «" + a.Name + "». Sigue sus pasos; cuando termine, pulsa «Buscar restos».";
            return false;
        }

        // ---------------- restos ----------------

        static readonly string[] Generic = {
            "microsoft", "windows", "common files", "google", "intel", "amd", "nvidia", "adobe", "programs", "temp",
            "packages", "apps", "system", "system32", "users", "default", "public", "program files", "windows nt",
            "internet explorer", "microsoft office", "windowsapps", "java", "python", "classes", "policies",
            "wow6432node", "clients", "registeredapplications", "software", "data", "app", "setup", "update", "tools"
        };

        public static string CoreName(string name)
        {
            string s = Regex.Replace(name ?? "", @"\s*\([^)]*\)", "");
            s = Regex.Replace(s, @"\s+(v|version\s+|versión\s+)?\d+(\.\d+)*[a-z]?(\s.*)?$", "", RegexOptions.IgnoreCase);
            s = s.Trim(' ', '-', '_', '.');
            foreach (char c in Path.GetInvalidFileNameChars()) if (s.IndexOf(c) >= 0) return "";
            return s;
        }

        static bool Usable(string name)
        {
            return name != null && name.Length >= 4 && Array.IndexOf(Generic, name.ToLowerInvariant()) < 0;
        }

        static void MeasureDir(string dir, out long size, out long count)
        {
            long s = 0, c = 0;
            Walker.Walk(dir, delegate(FileInfo f) { s += f.Length; c++; });
            size = s;
            count = c;
        }

        static bool KeyExists(string regPath)
        {
            try
            {
                int i = regPath.IndexOf('\\');
                using (RegistryKey root = RegistryKey.OpenBaseKey(
                    regPath.StartsWith("HKLM") ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
                    regPath.StartsWith("HKLM") ? RegistryView.Registry64 : RegistryView.Default))
                using (RegistryKey k = root.OpenSubKey(regPath.Substring(i + 1)))
                    return k != null;
            }
            catch { return false; }
        }

        /// <summary>Busca lo que un programa deja atrás tras desinstalarse: carpetas y claves del registro.</summary>
        public static List<Leftover> FindLeftovers(AppInfo a, List<AppInfo> installed)
        {
            List<Leftover> found = new List<Leftover>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string core = CoreName(a.Name);
            string pub = CoreName(a.Publisher);

            // Carpetas de otros programas: nunca se proponen ni las que los contienen.
            List<string> others = new List<string>();
            if (installed != null)
                foreach (AppInfo o in installed)
                    if (o != a && !string.IsNullOrEmpty(o.InstallLocation) && !o.IsStore &&
                        !(o.Name == a.Name && o.Version == a.Version))
                    {
                        try { others.Add(Risk.Norm(o.InstallLocation)); } catch { }
                    }

            Action<string, string> addDir = delegate(string dir, string why)
            {
                try
                {
                    if (string.IsNullOrEmpty(dir)) return;
                    string full = Risk.Norm(dir);
                    DirectoryInfo di = new DirectoryInfo(full);
                    if (!di.Exists || (di.Attributes & FileAttributes.ReparsePoint) != 0 || !seen.Add(full)) return;
                    Verdict v = Risk.ForPath(full);
                    if (v.Refuse || v.Level >= 3) return;
                    foreach (string o in others)
                        if (string.Equals(o, full, StringComparison.OrdinalIgnoreCase) || Risk.Inside(o, full)) return;
                    Leftover l = new Leftover();
                    l.Kind = "Carpeta";
                    l.Path = full;
                    l.Why = why;
                    MeasureDir(full, out l.Size, out l.Count);
                    found.Add(l);
                }
                catch { }
            };

            if (!a.IsStore) addDir(a.InstallLocation, "Carpeta donde estaba instalado");

            if (Usable(core))
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string[] bases = {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    local,
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Path.Combine(local, "Programs")
                };
                string[] labels = {
                    "Archivos del programa", "Archivos del programa", "Datos compartidos del programa",
                    "Configuración y caché en tu usuario", "Configuración en tu usuario", "Archivos del programa"
                };
                for (int i = 0; i < bases.Length; i++)
                {
                    if (string.IsNullOrEmpty(bases[i])) continue;
                    addDir(Path.Combine(bases[i], core), labels[i]);
                    if (Usable(pub) && !string.Equals(pub, core, StringComparison.OrdinalIgnoreCase))
                        addDir(Path.Combine(Path.Combine(bases[i], pub), core), labels[i]);
                }
                addDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), core), "Accesos directos del menú Inicio");
                addDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), core), "Accesos directos del menú Inicio");

                List<string> keys = new List<string>();
                foreach (string root in new string[] { @"HKCU\Software\", @"HKLM\Software\", @"HKLM\Software\WOW6432Node\" })
                {
                    keys.Add(root + core);
                    if (Usable(pub) && !string.Equals(pub, core, StringComparison.OrdinalIgnoreCase)) keys.Add(root + pub + "\\" + core);
                }
                foreach (string key in keys)
                {
                    if (!seen.Add(key) || !KeyExists(key)) continue;
                    Leftover l = new Leftover();
                    l.Kind = "Registro";
                    l.Path = key;
                    l.Why = "Configuración del programa en el registro";
                    found.Add(l);
                }
            }

            if (!a.IsStore && !string.IsNullOrEmpty(a.KeyName) && KeyExists(a.RegPath))
            {
                Leftover l = new Leftover();
                l.Kind = "Registro";
                l.Path = a.RegPath;
                l.Why = "Su entrada en la lista de programas instalados";
                found.Add(l);
            }
            return found;
        }

        public static void RemoveEmptyDirs(string dir)
        {
            try
            {
                DirectoryInfo di = new DirectoryInfo(dir);
                if (!di.Exists || (di.Attributes & FileAttributes.ReparsePoint) != 0) return;
                foreach (DirectoryInfo sub in di.GetDirectories()) RemoveEmptyDirs(sub.FullName);
                Directory.Delete(dir, false);   // solo si quedó vacía
            }
            catch { }
        }

        /// <summary>
        /// Retira los restos elegidos. Las carpetas van a la zona de seguridad y de cada clave del
        /// registro se guarda una copia antes de borrarla, así que todo se puede deshacer.
        /// </summary>
        public static string RemoveLeftovers(string appName, List<Leftover> items)
        {
            string id = History.NewId();
            int keys = 0, keysFailed = 0;
            long bytes = 0;
            int moved = 0, failed = 0;

            using (Quarantine.Session s = new Quarantine.Session(id))
            {
                foreach (Leftover l in items)
                {
                    if (l.Kind != "Carpeta") continue;
                    Verdict v = Risk.ForPath(l.Path);
                    if (v.Refuse || v.Level >= 3) continue;
                    Walker.Walk(l.Path, delegate(FileInfo f) { s.Move(f); });
                    s.Flush();
                    RemoveEmptyDirs(l.Path);
                }
                s.Flush();
                moved = s.Moved; failed = s.Failed; bytes = s.Bytes;
                if (s.Moved > 0)
                    History.Add("files", "Restos de «" + appName + "» retirados",
                        Util.Size(s.Bytes) + " en " + s.Moved + " archivos", History.Active, s.Dirs, s.Bytes, id);
            }

            string backupDir = Path.Combine(Path.Combine(Util.AppDir, "Registro"), id);
            foreach (Leftover l in items)
            {
                if (l.Kind != "Registro") continue;
                if (!Regex.IsMatch(l.Path, @"^HK(LM|CU)\\Software\\[^""]+$", RegexOptions.IgnoreCase)) continue;
                Directory.CreateDirectory(backupDir);
                string file = Path.Combine(backupDir, (keys + keysFailed + 1).ToString("0000") + ".reg");
                string output;
                // Sin copia de seguridad no se borra nada.
                if (Util.Run("reg.exe", "export \"" + l.Path + "\" \"" + file + "\" /y", out output) != 0 || !File.Exists(file))
                {
                    keysFailed++;
                    continue;
                }
                if (Util.Run("reg.exe", "delete \"" + l.Path + "\" /f", out output) == 0) keys++;
                else { keysFailed++; try { File.Delete(file); } catch { } }
            }
            if (keys > 0)
                History.Add("reg", "Registro de «" + appName + "» limpiado", keys + " claves borradas (hay copia para deshacer)",
                    History.Active, backupDir, 0, null);

            string msg = "";
            if (moved > 0) msg += Util.Size(bytes) + " en " + moved + " archivos retirados a la zona de seguridad. ";
            if (failed > 0) msg += failed + " archivos estaban en uso y se quedaron. ";
            if (keys > 0) msg += keys + " claves del registro borradas (con copia). ";
            if (keysFailed > 0) msg += keysFailed + " claves no se pudieron borrar (suelen pedir abrir Ligero como administrador). ";
            if (msg.Length == 0) msg = "No había nada que retirar.";
            return msg.Trim();
        }
    }

    // ------------------------------------------------------------------
    //  Eliminar un archivo o carpeta cualquiera (pasando por la zona de seguridad)
    // ------------------------------------------------------------------
    public static class Deleter
    {
        /// <summary>Tamaño y número de archivos de lo que se va a eliminar.</summary>
        public static void Measure(string path, out long size, out long count)
        {
            long s = 0, c = 0;
            try
            {
                if (File.Exists(path)) { s = new FileInfo(path).Length; c = 1; }
                else Walker.Walk(path, delegate(FileInfo f) { s += f.Length; c++; });
            }
            catch { }
            size = s;
            count = c;
        }

        public static string Delete(string path, Verdict verdict, out List<string> failedPaths)
        {
            failedPaths = new List<string>();
            if (verdict == null || verdict.Refuse) throw new InvalidOperationException("Ligero no elimina este elemento.");
            string id = History.NewId();
            using (Quarantine.Session s = new Quarantine.Session(id))
            {
                if (File.Exists(path)) s.Move(new FileInfo(path));
                else if (Directory.Exists(path))
                    Walker.Walk(path, delegate(FileInfo f) { s.Move(f); });
                else throw new FileNotFoundException("Ya no existe: " + path);
                s.Flush();
                if (Directory.Exists(path)) Apps.RemoveEmptyDirs(path);
                failedPaths.AddRange(s.FailedPaths);
                if (s.Moved > 0)
                    History.Add("files", "Eliminado: " + Path.GetFileName(path.TrimEnd('\\')),
                        Util.Size(s.Bytes) + " en " + s.Moved + " archivos · " + path + " · " + verdict.Title,
                        History.Active, s.Dirs, s.Bytes, id);
                string msg = s.Moved > 0
                    ? "Eliminado: " + Util.Size(s.Bytes) + " en " + s.Moved + " archivos. Está en la zona de seguridad: puedes recuperarlo en Deshacer."
                    : "No se pudo eliminar nada.";
                if (s.Failed > 0)
                    msg += " " + s.Failed + " archivos no se pudieron mover: están en uso o Windows los protege.";
                return msg;
            }
        }

        /// <summary>Programa la retirada de archivos en uso para el próximo reinicio. Requiere administrador.</summary>
        public static int ScheduleOnReboot(List<string> paths, string label)
        {
            string id = History.NewId();
            using (Quarantine.Session s = new Quarantine.Session(id))
            {
                foreach (string p in paths)
                {
                    try
                    {
                        FileInfo f = new FileInfo(p);
                        if (f.Exists) s.MoveOnReboot(f);
                    }
                    catch { }
                }
                if (s.Scheduled > 0)
                    History.Add("files", "Se retirará al reiniciar: " + label,
                        s.Scheduled + " archivos en uso. Windows los moverá a la zona de seguridad al reiniciar; después podrás deshacerlo aquí.",
                        History.Active, s.Dirs, s.Bytes, id);
                return s.Scheduled;
            }
        }
    }

    // ------------------------------------------------------------------
    //  Optimización en un clic
    // ------------------------------------------------------------------
    public static class Booster
    {
        /// <summary>Lo que se limpia en un clic: solo lo más seguro (temporales antiguos e informes de fallos).</summary>
        static List<Category> Quick()
        {
            List<Category> l = new List<Category>();
            foreach (Category c in Cleaner.Build())
                if (c.Id == "temp" || c.Id == "errores") l.Add(c);
            return l;
        }

        public static long JunkSize()
        {
            long total = 0;
            foreach (Category c in Quick())
            {
                Cleaner.Scan(c);
                total += Math.Max(0, c.Size);
            }
            return total;
        }

        /// <summary>Libera memoria y retira los temporales antiguos a la zona de seguridad.</summary>
        public static string Run()
        {
            long total, before, after;
            Memory.Read(out total, out before);
            Memory.Trim();
            string clean = Cleaner.Clean(Quick());
            System.Threading.Thread.Sleep(400);
            Memory.Read(out total, out after);
            long gain = Math.Max(0, after - before);
            bool freed = gain > (4L << 20);
            History.Add("ram", "Optimización en un clic",
                freed ? Util.Size(gain) + " de memoria liberados" : "La memoria ya estaba ajustada", History.Info, "", 0, null);
            return (freed ? "Memoria: " + Util.Size(gain) + " liberados. " : "Memoria: ya estaba ajustada. ") + "Temporales: " + clean;
        }
    }

    // ------------------------------------------------------------------
    //  Estado de la protección del equipo (solo lectura)
    // ------------------------------------------------------------------
    public class SecurityInfo
    {
        public bool Known;
        public string Antivirus = "";
        public bool AvOn, AvUpdated, FirewallOn, RebootPending;
        public string Firewall = "";
    }

    public static class Security
    {
        public static SecurityInfo Read()
        {
            SecurityInfo s = new SecurityInfo();
            List<string> on = new List<string>(), all = new List<string>();
            // Se pregunta directamente al Centro de seguridad de Windows (sin abrir PowerShell).
            foreach (string[] row in Wmi.Query(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct", "displayName", "productState"))
            {
                int state;
                if (!int.TryParse(row[1], out state)) continue;
                s.Known = true;
                all.Add(row[0]);
                if ((state & 0x1000) == 0) continue;
                on.Add(row[0]);
                s.AvOn = true;
                if ((state & 0x10) == 0) s.AvUpdated = true;
            }
            foreach (string[] row in Wmi.Query(@"root\SecurityCenter2", "SELECT displayName, productState FROM FirewallProduct", "displayName", "productState"))
            {
                int state;
                if (!int.TryParse(row[1], out state) || (state & 0x1000) == 0) continue;
                s.FirewallOn = true;
                s.Firewall = row[0];
            }
            s.Antivirus = string.Join(", ", (on.Count > 0 ? on : all).ToArray());

            try
            {
                const string fw = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\";
                if (Reg.Get("HKLM", fw + "StandardProfile", "EnableFirewall") == 1 && Reg.Get("HKLM", fw + "PublicProfile", "EnableFirewall") == 1)
                {
                    s.FirewallOn = true;
                    if (s.Firewall.Length == 0) s.Firewall = "Firewall de Windows";
                }
                s.RebootPending =
                    Reg.KeyExists("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") ||
                    Reg.KeyExists("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            }
            catch { }
            return s;
        }
    }

    // ------------------------------------------------------------------
    //  Chequeo de salud: revisa todo de una vez y dice adónde ir a arreglarlo
    // ------------------------------------------------------------------
    public class HealthItem
    {
        public string Title, Detail, Target;
        /// <summary>0 = bien, 1 = mejorable, 2 = problema.</summary>
        public int Level;

        public HealthItem(string title, string detail, int level, string target)
        {
            Title = title; Detail = detail; Level = level; Target = target;
        }
    }

    public static class Health
    {
        public static List<HealthItem> Check(out int score)
        {
            List<HealthItem> l = new List<HealthItem>();

            long total, avail;
            Memory.Read(out total, out avail);
            int pct = total > 0 ? (int)((total - avail) * 100 / total) : 0;
            l.Add(new HealthItem("Memoria", pct + " % en uso (" + Util.Size(avail) + " libres)" +
                (pct >= 85 ? ". Va justa: cierra programas que no uses o desactiva los que arrancan solos." : "."),
                pct >= 93 ? 2 : (pct >= 85 ? 1 : 0), "memoria"));

            long junk = 0;
            try { junk = Booster.JunkSize(); } catch { }
            l.Add(new HealthItem("Archivos temporales", Util.Size(junk) + " que se pueden retirar con seguridad.",
                junk >= (3L << 30) ? 2 : (junk >= (500L << 20) ? 1 : 0), "disco"));

            try
            {
                DriveInfo d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                int free = (int)(d.AvailableFreeSpace * 100 / Math.Max(1, d.TotalSize));
                l.Add(new HealthItem("Espacio en disco", Util.Size(d.AvailableFreeSpace) + " libres (" + free + " %)" +
                    (free < 15 ? ". Con poco espacio Windows se vuelve lento y no puede actualizarse." : "."),
                    free < 7 ? 2 : (free < 15 ? 1 : 0), "disco"));
            }
            catch { }

            int starting = 0;
            try { foreach (StartupItem s in Startup.List()) if (s.Enabled) starting++; } catch { }
            l.Add(new HealthItem("Programas de arranque", starting + " se abren solos al encender" +
                (starting > 6 ? ". Son bastantes: cada uno ocupa memoria aunque no lo uses." : "."),
                starting > 12 ? 2 : (starting > 6 ? 1 : 0), "arranque"));

            int pending = 0;
            foreach (Tweak t in Tweaks.All())
            {
                try { if (t.Recommended && !t.IsApplied() && (!t.NeedsAdmin || Util.IsAdmin)) pending++; } catch { }
            }
            l.Add(new HealthItem("Ajustes recomendados", pending == 0 ? "Todos aplicados." : pending + " sin aplicar. Alivian al equipo sin ninguna pega práctica.",
                pending > 0 ? 1 : 0, "acelerar"));

            SecurityInfo sec = Security.Read();
            if (sec.Known)
                l.Add(new HealthItem("Antivirus", sec.AvOn
                    ? sec.Antivirus + " está activo" + (sec.AvUpdated ? " y al día." : ", pero sus definiciones están desactualizadas.")
                    : "No hay ningún antivirus activo. El equipo está expuesto.",
                    sec.AvOn ? (sec.AvUpdated ? 0 : 1) : 2, "proteccion"));
            l.Add(new HealthItem("Cortafuegos", sec.FirewallOn ? sec.Firewall + " activo." : "Desactivado: cualquier programa puede recibir conexiones de la red.",
                sec.FirewallOn ? 0 : 2, "proteccion"));
            if (sec.RebootPending)
                l.Add(new HealthItem("Actualizaciones", "Windows tiene actualizaciones instaladas a medias: reinicia el equipo para terminarlas.", 1, "proteccion"));

            long q = History.QuarantinedBytes();
            if (q > 0)
                l.Add(new HealthItem("Zona de seguridad", Util.Size(q) + " guardados por si quieres deshacer. Si todo va bien, libéralos para recuperar el espacio.", 1, "deshacer"));

            int penalty = 0;
            foreach (HealthItem h in l) penalty += h.Level == 2 ? 18 : (h.Level == 1 ? 7 : 0);
            score = Math.Max(5, 100 - penalty);
            return l;
        }
    }

    // ------------------------------------------------------------------
    //  Diagnóstico y reparación de la conexión
    // ------------------------------------------------------------------
    public class NetItem
    {
        public string Title, Detail, Fix = "", FixLabel = "";
        public int Level;

        public NetItem(string title, string detail, int level) { Title = title; Detail = detail; Level = level; }
    }

    public static class NetDoctor
    {
        const string InternetSettings = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        static long PingMs(string host, int timeout)
        {
            try
            {
                using (System.Net.NetworkInformation.Ping p = new System.Net.NetworkInformation.Ping())
                {
                    System.Net.NetworkInformation.PingReply r = p.Send(host, timeout);
                    return r.Status == System.Net.NetworkInformation.IPStatus.Success ? Math.Max(1, r.RoundtripTime) : -1;
                }
            }
            catch { return -1; }
        }

        public static List<NetItem> Check()
        {
            List<NetItem> l = new List<NetItem>();
            string adapter = null, gateway = null, ip = "";
            bool dhcp = true, wifi = false;
            try
            {
                foreach (System.Net.NetworkInformation.NetworkInterface ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;
                    System.Net.NetworkInformation.IPInterfaceProperties props = ni.GetIPProperties();
                    foreach (System.Net.NetworkInformation.GatewayIPAddressInformation g in props.GatewayAddresses)
                    {
                        if (g.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || g.Address.Equals(System.Net.IPAddress.Any)) continue;
                        adapter = ni.Name;
                        gateway = g.Address.ToString();
                        wifi = ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211;
                        foreach (System.Net.NetworkInformation.UnicastIPAddressInformation u in props.UnicastAddresses)
                            if (u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) ip = u.Address.ToString();
                        try { dhcp = props.GetIPv4Properties().IsDhcpEnabled; } catch { }
                    }
                    if (adapter != null) break;
                }
            }
            catch { }

            if (adapter == null)
            {
                l.Add(new NetItem("Conexión de red", "No hay ninguna conexión activa. Comprueba que el wifi esté encendido y conectado, " +
                    "o que el cable de red esté bien puesto. Esto no se puede arreglar desde aquí.", 2));
            }
            else
            {
                l.Add(new NetItem("Conexión de red", (wifi ? "Wifi" : "Cable") + " «" + adapter + "», dirección " + ip + ".", 0));

                long gw = PingMs(gateway, 1500);
                NetItem router = new NetItem("Router", gw > 0 ? "Responde en " + gw + " ms (" + gateway + ")."
                    : "El router (" + gateway + ") no responde. Algunos routers no contestan a esta prueba; si internet funciona, ignóralo.", gw > 0 ? 0 : 1);
                if (gw <= 0) { router.Fix = "renew"; router.FixLabel = "Renovar la dirección"; }
                l.Add(router);

                long net = PingMs("1.1.1.1", 2500);
                if (net <= 0) net = PingMs("8.8.8.8", 2500);
                NetItem inet = new NetItem("Salida a internet", net > 0
                    ? "Funciona. Tiempo de respuesta: " + net + " ms" + (net > 150 ? " (lento: notarás retraso en videollamadas y juegos)." : ".")
                    : "No hay salida a internet. Si el router responde, el fallo está en el router o en tu proveedor: apaga el router 30 segundos y vuelve a encenderlo.",
                    net > 0 ? (net > 150 ? 1 : 0) : 2);
                if (net <= 0) { inet.Fix = "renew"; inet.FixLabel = "Renovar la dirección"; }
                l.Add(inet);

                bool dns = false;
                try
                {
                    IAsyncResult ar = System.Net.Dns.BeginGetHostAddresses("www.microsoft.com", null, null);
                    if (ar.AsyncWaitHandle.WaitOne(4000)) dns = System.Net.Dns.EndGetHostAddresses(ar).Length > 0;
                }
                catch { }
                NetItem d = new NetItem("Nombres de internet (DNS)", dns ? "Funciona: los nombres de las webs se resuelven bien."
                    : "Falla: hay conexión pero no se encuentran las webs por su nombre. Suele arreglarse vaciando la caché de DNS.", dns ? 0 : 2);
                d.Fix = "flushdns";
                d.FixLabel = "Vaciar caché de DNS";
                l.Add(d);

                l.Add(new NetItem("Dirección del equipo", dhcp ? "Automática: el router se la asigna (lo normal)."
                    : "Fija, puesta a mano. Si la conexión falla al cambiar de red, ponla en automático en la configuración de red.", dhcp ? 0 : 1));
            }

            int proxy = 0;
            string server = "";
            try
            {
                proxy = Reg.Get("HKCU", InternetSettings, "ProxyEnable") ?? 0;
                server = Convert.ToString(Reg.GetRaw("HKCU", InternetSettings, "ProxyServer") ?? "");
            }
            catch { }
            NetItem px = new NetItem("Proxy", proxy == 1
                ? "Hay un proxy configurado (" + server + "): todo tu tráfico pasa por él. Si no lo pusiste tú, puede ser la causa de fallos o un programa no deseado."
                : "Sin proxy (lo normal).", proxy == 1 ? 1 : 0);
            if (proxy == 1) { px.Fix = "proxyoff"; px.FixLabel = "Desactivar el proxy"; }
            l.Add(px);
            return l;
        }

        public static string Fix(string id)
        {
            string output;
            if (id == "flushdns")
            {
                if (Util.Run("ipconfig.exe", "/flushdns", out output) != 0) throw new InvalidOperationException("Windows no pudo vaciar la caché de DNS.");
                History.Add("net", "Caché de DNS vaciada", "Windows la vuelve a llenar sola", History.Info, "", 0, null);
                return "Caché de DNS vaciada. Windows volverá a aprender las direcciones al navegar.";
            }
            if (id == "renew")
            {
                Util.Run("ipconfig.exe", "/renew", 90000, out output);
                History.Add("net", "Dirección de red renovada", "", History.Info, "", 0, null);
                return "Se pidió al router una dirección nueva. Vuelve a diagnosticar para ver si ya funciona.";
            }
            if (id == "proxyoff")
            {
                Tweak t = Tweaks.ById("proxyoff");
                string prev = t.Apply();
                History.Add("tweak", t.Title, "Se puede deshacer", History.Active, t.Id + "|" + prev, 0, null);
                return "Proxy desactivado (puedes deshacerlo en la pestaña Deshacer). Cierra y abre el navegador para que lo note.";
            }
            if (id == "winsock")
            {
                if (!Util.IsAdmin) throw new UnauthorizedAccessException();
                if (Util.Run("netsh.exe", "winsock reset", out output) != 0) throw new InvalidOperationException("Windows no pudo reiniciar los componentes de red. " + output.Trim());
                Util.Run("netsh.exe", "int ip reset", out output);
                Util.Run("ipconfig.exe", "/flushdns", out output);
                History.Add("net", "Reparación de red a fondo", "Componentes de red reiniciados; hay que reiniciar el equipo", History.Info, "", 0, null);
                return "Componentes de red reiniciados. Reinicia el equipo para que surta efecto.";
            }
            return "";
        }
    }

    // ------------------------------------------------------------------
    //  Archivos duplicados y carpeta de descargas
    // ------------------------------------------------------------------
    public class DupFile
    {
        public string Path;
        public long Size;
        public int Group;
        public DateTime Modified;
        /// <summary>La copia que se propone conservar (la más antigua de cada grupo).</summary>
        public bool Keep;
    }

    public static class Duplicates
    {
        const int Edge = 512 * 1024;

        /// <summary>
        /// Huella del contenido. Los archivos de hasta 64 MB se leen enteros; de los mayores se leen
        /// el principio y el final (junto con el tamaño exacto), para no tardar minutos en discos lentos.
        /// </summary>
        static string Signature(string path, long size)
        {
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan))
            {
                byte[] hash;
                if (size <= (64L << 20)) hash = md5.ComputeHash(fs);
                else
                {
                    byte[] buf = new byte[Edge * 2];
                    int a = fs.Read(buf, 0, Edge);
                    fs.Seek(-Edge, SeekOrigin.End);
                    int b = fs.Read(buf, a, Edge);
                    hash = md5.ComputeHash(buf, 0, a + b);
                }
                return BitConverter.ToString(hash);
            }
        }

        public static List<DupFile> Find(IEnumerable<string> folders, int minMB)
        {
            long min = (long)minMB << 20;
            Dictionary<long, List<FileInfo>> bySize = new Dictionary<long, List<FileInfo>>();
            foreach (string folder in folders)
                Walker.Walk(folder, delegate(FileInfo f)
                {
                    if (f.Length < min || Walker.IsCloudOnly(f)) return;
                    if ((f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) return;
                    List<FileInfo> same;
                    if (!bySize.TryGetValue(f.Length, out same)) { same = new List<FileInfo>(); bySize[f.Length] = same; }
                    same.Add(f);
                });

            List<List<FileInfo>> groups = new List<List<FileInfo>>();
            foreach (List<FileInfo> same in bySize.Values)
            {
                if (same.Count < 2) continue;
                Dictionary<string, List<FileInfo>> bySig = new Dictionary<string, List<FileInfo>>();
                foreach (FileInfo f in same)
                {
                    string sig;
                    try { sig = Signature(f.FullName, f.Length); } catch { continue; }   // en uso o sin permiso
                    List<FileInfo> g;
                    if (!bySig.TryGetValue(sig, out g)) { g = new List<FileInfo>(); bySig[sig] = g; }
                    g.Add(f);
                }
                foreach (List<FileInfo> g in bySig.Values) if (g.Count > 1) groups.Add(g);
            }
            groups.Sort(delegate(List<FileInfo> a, List<FileInfo> b)
            {
                return (b[0].Length * (b.Count - 1)).CompareTo(a[0].Length * (a.Count - 1));
            });
            if (groups.Count > 200) groups.RemoveRange(200, groups.Count - 200);

            List<DupFile> result = new List<DupFile>();
            int n = 0;
            foreach (List<FileInfo> g in groups)
            {
                n++;
                g.Sort(delegate(FileInfo a, FileInfo b) { return a.CreationTimeUtc.CompareTo(b.CreationTimeUtc); });
                for (int i = 0; i < g.Count; i++)
                {
                    DupFile d = new DupFile();
                    d.Path = g[i].FullName;
                    d.Size = g[i].Length;
                    d.Group = n;
                    d.Modified = g[i].LastWriteTime;
                    d.Keep = i == 0;
                    result.Add(d);
                }
            }
            return result;
        }

        /// <summary>Lo que hay en la carpeta Descargas, de mayor a menor.</summary>
        public static List<OldFile> Downloads()
        {
            List<OldFile> list = new List<OldFile>();
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Walker.Walk(dir, delegate(FileInfo f)
            {
                if (f.Length < (1L << 20) || Walker.IsCloudOnly(f)) return;
                if ((f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) return;
                OldFile o = new OldFile();
                o.Path = f.FullName;
                o.Size = f.Length;
                o.LastUse = f.CreationTime > f.LastWriteTime ? f.CreationTime : f.LastWriteTime;
                list.Add(o);
            });
            list.Sort(delegate(OldFile a, OldFile b) { return b.Size.CompareTo(a.Size); });
            if (list.Count > 500) list.RemoveRange(500, list.Count - 500);
            return list;
        }
    }

    // ------------------------------------------------------------------
    //  Reconocimiento del equipo: qué tiene y de qué gama es
    // ------------------------------------------------------------------
    public class SystemProfile
    {
        public string Cpu = "", Gpu = "", Disk = "desconocido";
        public int Threads;
        public long RamBytes, GpuBytes;
        public bool Laptop;
        /// <summary>baja, media o alta.</summary>
        public string Tier = "media";
        public List<string> Reasons = new List<string>();

        public string Summary
        {
            get
            {
                return Cpu + "  ·  " + Util.Size(RamBytes) + " de memoria  ·  disco " + Disk +
                       (Gpu.Length > 0 ? "  ·  " + Gpu + (GpuBytes >= (1L << 30) ? " (" + Util.Size(GpuBytes) + ")" : "") : "");
            }
        }
    }

    public static class Recon
    {
        /// <summary>Lee el procesador, la memoria, el tipo de disco y la gráfica. No cambia nada.</summary>
        public static SystemProfile Scan()
        {
            SystemProfile p = new SystemProfile();
            p.Threads = Environment.ProcessorCount;
            try
            {
                p.Cpu = System.Text.RegularExpressions.Regex.Replace(
                    Convert.ToString(Reg.GetRaw("HKLM", @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") ?? ""), @"\s+", " ").Trim();
            }
            catch { }
            if (p.Cpu.Length == 0) p.Cpu = "Procesador de " + p.Threads + " hilos";

            long total, avail;
            Memory.Read(out total, out avail);
            p.RamBytes = total;

            // Gráfica: la de más memoria propia entre las instaladas.
            try
            {
                using (RegistryKey lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey cls = lm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls != null)
                        foreach (string sub in cls.GetSubKeyNames())
                        {
                            try
                            {
                                using (RegistryKey k = cls.OpenSubKey(sub))
                                {
                                    if (k == null) continue;
                                    string name = Convert.ToString(k.GetValue("DriverDesc") ?? "");
                                    if (name.Length == 0 || name.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                    long mem = 0;
                                    object q = k.GetValue("HardwareInformation.qwMemorySize");
                                    if (q is long) mem = (long)q;
                                    else
                                    {
                                        object m = k.GetValue("HardwareInformation.MemorySize");
                                        if (m is int) mem = (uint)(int)m;
                                        else if (m is byte[] && ((byte[])m).Length >= 4) mem = BitConverter.ToUInt32((byte[])m, 0);
                                    }
                                    if (p.Gpu.Length == 0 || mem > p.GpuBytes) { p.Gpu = name; p.GpuBytes = mem; }
                                }
                            }
                            catch { }
                        }
                }
            }
            catch { }

            // Tipo de disco donde está Windows.
            // (Se pregunta directamente al servicio de almacenamiento de Windows, sin abrir PowerShell.)
            try
            {
                char letter = char.ToUpperInvariant(Path.GetPathRoot(Environment.SystemDirectory)[0]);
                string disk = null;
                foreach (string[] row in Wmi.Query(@"root\Microsoft\Windows\Storage", "SELECT DriveLetter, DiskNumber FROM MSFT_Partition", "DriveLetter", "DiskNumber"))
                {
                    int code;
                    if (int.TryParse(row[0], out code) && code > 0 && char.ToUpperInvariant((char)code) == letter) { disk = row[1]; break; }
                    if (row[0].Length == 1 && char.ToUpperInvariant(row[0][0]) == letter) { disk = row[1]; break; }
                }
                if (disk != null)
                    foreach (string[] row in Wmi.Query(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, MediaType, BusType FROM MSFT_PhysicalDisk", "DeviceId", "MediaType", "BusType"))
                    {
                        if (row[0] != disk) continue;
                        // MediaType: 3 = HDD, 4 = SSD.  BusType: 17 = NVMe.
                        if (row[1] == "4") p.Disk = row[2] == "17" ? "SSD rápido (NVMe)" : "SSD";
                        else if (row[1] == "3") p.Disk = "duro mecánico (HDD)";
                        break;
                    }
            }
            catch { }

            try { p.Laptop = System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery; }
            catch { }

            Classify(p);
            return p;
        }

        /// <summary>Decide la gama a partir de las piezas. Separado para poder probarlo con equipos inventados.</summary>
        public static void Classify(SystemProfile p)
        {
            double ram = p.RamBytes / (double)(1L << 30);
            bool hdd = p.Disk.IndexOf("HDD", StringComparison.OrdinalIgnoreCase) >= 0;
            bool ssd = p.Disk.IndexOf("SSD", StringComparison.OrdinalIgnoreCase) >= 0;
            bool dedicated = p.GpuBytes >= (3L << 30);
            p.Reasons.Clear();

            if (ram < 6 || p.Threads <= 2 || (hdd && ram < 12))
            {
                p.Tier = "baja";
                if (ram < 6) p.Reasons.Add("tiene poca memoria (" + Util.Size(p.RamBytes) + ")");
                if (p.Threads <= 2) p.Reasons.Add("el procesador solo puede hacer " + p.Threads + " cosas a la vez");
                if (hdd) p.Reasons.Add("usa un disco duro mecánico, que es lento");
            }
            else if (ram >= 15 && p.Threads >= 8 && ssd && (dedicated || ram >= 30 || p.Threads >= 16))
            {
                p.Tier = "alta";
                p.Reasons.Add("tiene memoria de sobra (" + Util.Size(p.RamBytes) + ")");
                p.Reasons.Add("el procesador puede hacer " + p.Threads + " cosas a la vez");
                p.Reasons.Add("el disco es un " + p.Disk);
                if (dedicated) p.Reasons.Add("tiene tarjeta gráfica propia");
            }
            else
            {
                p.Tier = "media";
                p.Reasons.Add(Util.Size(p.RamBytes) + " de memoria");
                p.Reasons.Add(p.Threads + " hilos de procesador");
                p.Reasons.Add(hdd ? "disco duro mecánico" : (ssd ? "disco SSD" : "disco de tipo desconocido"));
                if (!dedicated) p.Reasons.Add("gráfica integrada");
            }
        }
    }
}
