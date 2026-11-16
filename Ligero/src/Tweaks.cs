using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace Ligero
{
    // ------------------------------------------------------------------
    //  Ajustes reversibles de Windows.
    //  Cada ajuste sabe decir si está aplicado, aplicarse devolviendo cómo estaba
    //  antes, y volver exactamente a ese estado.
    // ------------------------------------------------------------------
    public abstract class Tweak
    {
        public string Id;
        public string Title;
        public string Description;
        public string DefaultData;
        public bool NeedsAdmin;
        public bool Recommended;
        public virtual bool Available() { return true; }
        public abstract bool IsApplied();
        /// <summary>Aplica el ajuste y devuelve cómo estaba antes (para poder deshacer).</summary>
        public abstract string Apply();
        public abstract void Restore(string previous);
    }

    static class Reg
    {
        static RegistryKey Root(string root)
        {
            if (root == "HKLM") return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
        }

        public static object GetRaw(string root, string sub, string name)
        {
            using (RegistryKey r = Root(root))
            using (RegistryKey k = r.OpenSubKey(sub))
                return k == null ? null : k.GetValue(name);
        }

        public static int? Get(string root, string sub, string name)
        {
            object v = GetRaw(root, sub, name);
            if (v is int) return (int)v;
            return null;
        }

        public static bool KeyExists(string root, string sub)
        {
            using (RegistryKey r = Root(root))
            using (RegistryKey k = r.OpenSubKey(sub))
                return k != null;
        }

        public static void Set(string root, string sub, string name, int? value)
        {
            using (RegistryKey r = Root(root))
            using (RegistryKey k = r.CreateSubKey(sub))
            {
                if (value.HasValue) k.SetValue(name, value.Value, RegistryValueKind.DWord);
                else k.DeleteValue(name, false);
            }
        }

        public static string Enc(int? v) { return v.HasValue ? v.Value.ToString() : "x"; }

        public static int? Dec(string s)
        {
            int v;
            if (s == "x" || !int.TryParse(s, out v)) return null;
            return v;
        }
    }

    public class RegEntry
    {
        public string Root, Key, Name;
        public int On;
        public int? Default;

        public RegEntry(string root, string key, string name, int on, int? def)
        {
            Root = root; Key = key; Name = name; On = on; Default = def;
        }
    }

    /// <summary>Ajuste formado por uno o varios valores del registro.</summary>
    public class RegTweak : Tweak
    {
        readonly RegEntry[] entries;
        readonly string broadcast;

        public RegTweak(string id, string title, string description, bool recommended, bool needsAdmin,
            string broadcast, params RegEntry[] entries)
        {
            Id = id; Title = title; Description = description;
            Recommended = recommended; NeedsAdmin = needsAdmin;
            this.entries = entries;
            this.broadcast = broadcast;
            List<string> d = new List<string>();
            foreach (RegEntry e in entries) d.Add(Reg.Enc(e.Default));
            DefaultData = string.Join(",", d.ToArray());
        }

        public override bool IsApplied()
        {
            foreach (RegEntry e in entries)
                if (Reg.Get(e.Root, e.Key, e.Name) != e.On) return false;
            return true;
        }

        public override string Apply()
        {
            List<string> prev = new List<string>();
            foreach (RegEntry e in entries) prev.Add(Reg.Enc(Reg.Get(e.Root, e.Key, e.Name)));
            foreach (RegEntry e in entries) Reg.Set(e.Root, e.Key, e.Name, e.On);
            if (broadcast != null) Native.BroadcastSetting(broadcast);
            return string.Join(",", prev.ToArray());
        }

        public override void Restore(string previous)
        {
            string[] p = (previous ?? "").Split(',');
            if (p.Length != entries.Length) p = DefaultData.Split(',');
            for (int i = 0; i < entries.Length; i++)
                Reg.Set(entries[i].Root, entries[i].Key, entries[i].Name, Reg.Dec(p[i]));
            if (broadcast != null) Native.BroadcastSetting(broadcast);
        }
    }

    public class AnimationTweak : Tweak
    {
        public AnimationTweak()
        {
            Id = "animaciones";
            Title = "Quitar animaciones de ventanas y menús";
            Description = "Las ventanas se abren, cierran y minimizan al instante, sin el efecto de movimiento. " +
                          "En equipos lentos todo se nota más ágil. Solo cambia el aspecto.";
            DefaultData = "1,1,1";
            Recommended = true;
        }

        // Si Windows rechaza una lectura o un cambio, se avisa en vez de dar el ajuste por hecho.
        static void Must(bool ok)
        {
            if (!ok) throw new InvalidOperationException("Windows no permitió leer o cambiar las animaciones.");
        }

        static int GetMin()
        {
            Native.ANIMATIONINFO ai = new Native.ANIMATIONINFO();
            ai.cbSize = 8;
            Must(Native.SpiAnim(0x0048, 8, ref ai, 0));
            return ai.iMinAnimate != 0 ? 1 : 0;
        }

        static void SetMin(int v)
        {
            Native.ANIMATIONINFO ai = new Native.ANIMATIONINFO();
            ai.cbSize = 8;
            ai.iMinAnimate = v;
            Must(Native.SpiAnim(0x0049, 8, ref ai, 3));
        }

        static int GetBool(uint action)
        {
            int v = 0;
            Must(Native.SpiGetBool(action, 0, ref v, 0));
            return v != 0 ? 1 : 0;
        }

        public override bool IsApplied() { return GetMin() == 0 && GetBool(0x1042) == 0; }

        public override string Apply()
        {
            string prev = GetMin() + "," + GetBool(0x1042) + "," + GetBool(0x1002);
            Restore("0,0,0");
            return prev;
        }

        public override void Restore(string previous)
        {
            string[] p = (previous ?? "").Split(',');
            if (p.Length < 3) p = new string[] { "1", "1", "1" };
            SetMin(p[0] == "0" ? 0 : 1);
            Must(Native.SpiSetBool(0x1043, 0, (IntPtr)(p[1] == "0" ? 0 : 1), 3));   // animaciones dentro de las ventanas
            Must(Native.SpiSetBool(0x1003, 0, (IntPtr)(p[2] == "0" ? 0 : 1), 3));   // animación de menús
        }
    }

    /// <summary>Apaga un servicio de Windows que consume recursos. Requiere administrador.</summary>
    public class ServiceTweak : Tweak
    {
        readonly string service;

        public ServiceTweak(string id, string service, string title, string description, string defaultData)
        {
            Id = id; Title = title; Description = description; DefaultData = defaultData;
            this.service = service;
            NeedsAdmin = true;
        }

        string Key { get { return @"SYSTEM\CurrentControlSet\Services\" + service; } }

        public override bool Available() { return Reg.Get("HKLM", Key, "Start") != null; }

        public override bool IsApplied() { return Reg.Get("HKLM", Key, "Start") == 4; }

        void Config(string mode)
        {
            string output;
            if (Util.Run("sc.exe", "config " + service + " start= " + mode, out output) != 0)
                throw new InvalidOperationException("Windows no permitió cambiar el servicio " + service + ". " + output.Trim());
        }

        public override string Apply()
        {
            if (!Util.IsAdmin) throw new UnauthorizedAccessException();
            int start = Reg.Get("HKLM", Key, "Start") ?? 3;
            int delayed = Reg.Get("HKLM", Key, "DelayedAutostart") ?? 0;
            string output;
            Util.Run("sc.exe", "query " + service, out output);
            bool running = output.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0;
            Config("disabled");
            Util.Run("sc.exe", "stop " + service, out output);
            return start + "," + delayed + "," + (running ? 1 : 0);
        }

        public override void Restore(string previous)
        {
            if (!Util.IsAdmin) throw new UnauthorizedAccessException();
            string[] p = (previous ?? "").Split(',');
            if (p.Length < 3) p = DefaultData.Split(',');
            string mode = p[0] == "2" ? (p[1] == "1" ? "delayed-auto" : "auto") : (p[0] == "4" ? "disabled" : "demand");
            Config(mode);
            string output;
            if (p[2] == "1") Util.Run("sc.exe", "start " + service, out output);
        }
    }

    public class PowerPlanTweak : Tweak
    {
        const string High = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        const string Schemes = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";

        public PowerPlanTweak()
        {
            Id = "energia";
            Title = "Plan de energía «Alto rendimiento»";
            Description = "El procesador deja de frenarse para ahorrar energía, así que el equipo responde más rápido. " +
                          "A cambio la batería dura menos y puede calentarse más: úsalo sobre todo con el cargador puesto.";
            DefaultData = "381b4222-f694-41f0-9685-ff5bb260df2e";   // Equilibrado
        }

        static string Active()
        {
            return Convert.ToString(Reg.GetRaw("HKLM", Schemes, "ActivePowerScheme") ?? "").ToLowerInvariant();
        }

        public override bool Available() { return Reg.KeyExists("HKLM", Schemes + "\\" + High); }

        public override bool IsApplied() { return Active() == High; }

        static void SetActive(string guid)
        {
            Guid g;
            if (!Guid.TryParse(guid, out g)) throw new InvalidOperationException("Plan de energía no válido.");
            string output;
            if (Util.Run("powercfg.exe", "/setactive " + g.ToString("D"), out output) != 0)
                throw new InvalidOperationException("Windows no permitió cambiar el plan de energía. " + output.Trim());
        }

        public override string Apply()
        {
            string prev = Active();
            SetActive(High);
            return string.IsNullOrEmpty(prev) ? DefaultData : prev;
        }

        public override void Restore(string previous) { SetActive(previous); }
    }

    public class HibernateTweak : Tweak
    {
        const string Key = @"SYSTEM\CurrentControlSet\Control\Power";

        public HibernateTweak()
        {
            Id = "hibernacion";
            Title = "Desactivar la hibernación (libera varios GB de disco)";
            Description = "Windows reserva en el disco un archivo casi tan grande como tu memoria RAM para poder hibernar. " +
                          "Al desactivarla ese espacio queda libre. Pierdes la opción «Hibernar» y el «Inicio rápido»: " +
                          "el equipo tardará unos segundos más en encender. «Suspender» sigue funcionando.";
            DefaultData = "1";
            NeedsAdmin = true;
        }

        public override bool IsApplied() { return Reg.Get("HKLM", Key, "HibernateEnabled") == 0; }

        static void Set(bool on)
        {
            if (!Util.IsAdmin) throw new UnauthorizedAccessException();
            string output;
            if (Util.Run("powercfg.exe", on ? "/hibernate on" : "/hibernate off", out output) != 0)
                throw new InvalidOperationException("Windows no permitió cambiar la hibernación. " + output.Trim());
        }

        public override string Apply()
        {
            string prev = Reg.Enc(Reg.Get("HKLM", Key, "HibernateEnabled"));
            Set(false);
            return prev;
        }

        public override void Restore(string previous)
        {
            if (previous != "0") Set(true);
        }
    }

    public static class Tweaks
    {
        static Tweak[] all;

        public static Tweak[] All()
        {
            if (all != null) return all;
            const string CV = @"Software\Microsoft\Windows\CurrentVersion\";
            const string CDM = CV + "ContentDeliveryManager";
            List<Tweak> l = new List<Tweak>();

            l.Add(new RegTweak("transparencia", "Quitar transparencias de Windows",
                "La barra de tareas y los menús dejan de ser translúcidos. Windows gasta menos memoria y tarjeta " +
                "gráfica al dibujarlos. Solo cambia el aspecto, no toca tus archivos.",
                true, false, "ImmersiveColorSet",
                new RegEntry("HKCU", CV + @"Themes\Personalize", "EnableTransparency", 0, 1)));

            l.Add(new AnimationTweak());

            l.Add(new RegTweak("efectos", "Quitar sombras y efectos del escritorio",
                "Elimina las animaciones de la barra de tareas y las sombras bajo los iconos y las selecciones. " +
                "Menos trabajo para la tarjeta gráfica. Se nota del todo al cerrar sesión y volver a entrar.",
                true, false, null,
                new RegEntry("HKCU", CV + @"Explorer\Advanced", "TaskbarAnimations", 0, 1),
                new RegEntry("HKCU", CV + @"Explorer\Advanced", "ListviewShadow", 0, 1),
                new RegEntry("HKCU", CV + @"Explorer\Advanced", "ListviewAlphaSelect", 0, 1)));

            l.Add(new RegTweak("grabacion", "Apagar la grabación de juegos en segundo plano",
                "Windows deja de estar preparado para grabar la pantalla mientras juegas (Xbox Game Bar). " +
                "Ahorra memoria y procesador. Si no grabas partidas, no notarás nada más.",
                true, false, null,
                new RegEntry("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 0, 1),
                new RegEntry("HKCU", CV + "GameDVR", "AppCaptureEnabled", 0, 1)));

            l.Add(new RegTweak("sugerencias", "Quitar sugerencias, anuncios y apps promocionadas",
                "Windows deja de mostrar consejos y publicidad en el menú Inicio y la pantalla de bloqueo, y deja de " +
                "instalar por su cuenta juegos y apps promocionadas. Menos actividad de fondo y menos descargas.",
                true, false, null,
                new RegEntry("HKCU", CDM, "SilentInstalledAppsEnabled", 0, 1),
                new RegEntry("HKCU", CDM, "SystemPaneSuggestionsEnabled", 0, 1),
                new RegEntry("HKCU", CDM, "SoftLandingEnabled", 0, 1),
                new RegEntry("HKCU", CDM, "SubscribedContent-338388Enabled", 0, 1),
                new RegEntry("HKCU", CDM, "SubscribedContent-338389Enabled", 0, 1),
                new RegEntry("HKCU", CDM, "SubscribedContent-310093Enabled", 0, 1)));

            l.Add(new RegTweak("segundoplano", "Impedir que las apps de la Tienda trabajen en segundo plano",
                "Las apps de Microsoft Store (Correo, Tiempo, Noticias…) dejan de actualizarse y gastar memoria cuando " +
                "no las tienes abiertas. Siguen funcionando con normalidad al abrirlas; solo dejarás de recibir sus avisos " +
                "mientras están cerradas. En Windows 11 el efecto es parcial.",
                true, false, null,
                new RegEntry("HKCU", CV + "BackgroundAccessApplications", "GlobalUserDisabled", 1, null),
                new RegEntry("HKCU", CV + "Search", "BackgroundAppGlobalToggle", 0, null)));

            // En la versión de la Microsoft Store este ajuste no se ofrece: cambiar directivas de los navegadores
            // desde otra app es motivo de rechazo. Allí se explica cómo hacerlo desde los propios navegadores.
            // Se sigue registrando (oculto) para poder deshacerlo si se aplicó con otra versión.
            browserTweak = new RegTweak("navegadores", "Impedir que Edge y Chrome sigan activos al cerrarlos",
                "Al cerrar el navegador, Edge y Chrome suelen quedarse en memoria para abrirse antes la próxima vez. " +
                "Con esto se cierran de verdad y devuelven toda su memoria. El navegador mostrará el aviso " +
                "«administrado por tu organización»: es normal, lo provoca este ajuste y desaparece al deshacerlo.",
                true, true, null,
                new RegEntry("HKCU", @"Software\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0, null),
                new RegEntry("HKCU", @"Software\Policies\Microsoft\Edge", "StartupBoostEnabled", 0, null),
                new RegEntry("HKCU", @"Software\Policies\Google\Chrome", "BackgroundModeEnabled", 0, null));
            if (!PackageInfo.IsPackaged) l.Add(browserTweak);

            l.Add(new RegTweak("busquedaweb", "Quitar los resultados de internet del buscador de Inicio",
                "Al escribir en el menú Inicio, Windows deja de consultar Bing y solo busca en tu equipo. " +
                "El buscador responde antes y no gasta conexión.",
                false, true, null,
                new RegEntry("HKCU", @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1, null)));

            l.Add(new PowerPlanTweak());

            l.Add(new ServiceTweak("sysmain", "SysMain", "Apagar la precarga de programas (SysMain)",
                "SysMain carga por adelantado en la memoria los programas que sueles usar. En equipos con poca RAM y disco " +
                "duro mecánico suele ser el culpable del «disco al 100 %». Con disco SSD apenas hay diferencia. " +
                "Los programas pueden tardar un poco más en abrirse la primera vez.", "2,0,1"));

            l.Add(new ServiceTweak("indexador", "WSearch", "Apagar el indexador de búsqueda de Windows",
                "Windows deja de revisar tus archivos en segundo plano para catalogarlos. Ahorra disco y procesador, " +
                "sobre todo con disco duro mecánico. A cambio, buscar archivos por nombre o contenido será bastante más lento.",
                "2,1,1"));

            l.Add(new ServiceTweak("telemetria", "DiagTrack", "Apagar el envío de datos de uso a Microsoft (telemetría)",
                "Windows deja de recopilar y enviar estadísticas de uso en segundo plano. Menos actividad de disco y red. " +
                "No afecta a las actualizaciones ni a la seguridad.", "2,0,1"));

            l.Add(new HibernateTweak());

            List<Tweak> ok = new List<Tweak>();
            foreach (Tweak t in l)
            {
                try { if (t.Available()) ok.Add(t); } catch { }
            }
            all = ok.ToArray();
            return all;
        }

        static Tweak[] hidden;
        static Tweak browserTweak;

        /// <summary>Ajustes que no salen en la lista de Acelerar pero también se apuntan y se pueden deshacer.</summary>
        static Tweak[] Hidden()
        {
            if (hidden == null)
            {
                All();   // crea browserTweak
                List<Tweak> h = new List<Tweak>();
                h.Add(new RegTweak("proxyoff", "Proxy desactivado", "El tráfico de internet deja de pasar por un servidor intermedio.",
                    false, false, null,
                    new RegEntry("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyEnable", 0, 0)));
                if (PackageInfo.IsPackaged) h.Add(browserTweak);
                hidden = h.ToArray();
            }
            return hidden;
        }

        public static Tweak ById(string id)
        {
            foreach (Tweak t in All()) if (t.Id == id) return t;
            foreach (Tweak t in Hidden()) if (t.Id == id) return t;
            return null;
        }
    }
}
