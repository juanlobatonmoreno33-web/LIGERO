using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Ligero
{
    /// <summary>¿Se está ejecutando Ligero instalado como paquete (Microsoft Store)?</summary>
    public static class PackageInfo
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetCurrentPackageFullName(ref int length, StringBuilder name);

        static bool? packaged;

        public static bool IsPackaged
        {
            get
            {
                if (!packaged.HasValue)
                {
                    try
                    {
                        int len = 0;
                        // 15700 = APPMODEL_ERROR_NO_PACKAGE: es una app normal, sin paquete.
                        packaged = GetCurrentPackageFullName(ref len, null) != 15700;
                    }
                    catch { packaged = false; }   // Windows antiguos sin esta función: nunca hay paquete
                    // Al reabrirse como administrador, Windows puede no darle al proceso su identidad de paquete.
                    // La carpeta de una app instalada desde un paquete siempre contiene AppxManifest.xml;
                    // la de un Ligero normal, nunca.
                    if (!packaged.Value)
                    {
                        try
                        {
                            Assembly entry = Assembly.GetEntryAssembly();
                            if (entry != null && !string.IsNullOrEmpty(entry.Location))
                                packaged = File.Exists(Path.Combine(Path.GetDirectoryName(entry.Location), "AppxManifest.xml"));
                        }
                        catch { }
                    }
                }
                return packaged.Value;
            }
        }
    }

    /// <summary>
    /// Funciones de Windows que solo existen cuando Ligero viene de la Store.
    /// Solo se llaman si PackageInfo.IsPackaged; sus firmas usan tipos normales para que
    /// la versión sin paquete no necesite cargar nada de esto.
    /// </summary>
    public static class StoreRuntime
    {
        /// <summary>El mismo identificador que en AppxManifest.xml (uap5:StartupTask TaskId).</summary>
        public const string TaskId = "LigeroInicio";

        static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            while (op.Status == Windows.Foundation.AsyncStatus.Started) Thread.Sleep(10);
            if (op.Status != Windows.Foundation.AsyncStatus.Completed)
                throw op.ErrorCode != null
                    ? (Exception)new COMException("Windows no completó la operación.", op.ErrorCode.HResult)
                    : new InvalidOperationException("Windows no completó la operación.");
            return op.GetResults();
        }

        static Windows.ApplicationModel.StartupTask Task()
        {
            return Wait(Windows.ApplicationModel.StartupTask.GetAsync(TaskId));
        }

        /// <summary>¿Lo ha abierto Windows al iniciar sesión (y no el usuario)?</summary>
        public static bool LaunchedAtStartup()
        {
            try
            {
                Windows.ApplicationModel.Activation.IActivatedEventArgs a = Windows.ApplicationModel.AppInstance.GetActivatedEventArgs();
                return a != null && a.Kind == Windows.ApplicationModel.Activation.ActivationKind.StartupTask;
            }
            catch { return false; }
        }

        public static bool IsEnabled()
        {
            try
            {
                Windows.ApplicationModel.StartupTaskState s = Task().State;
                return s == Windows.ApplicationModel.StartupTaskState.Enabled ||
                       s == Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
            }
            catch { return false; }
        }

        /// <summary>Activa o desactiva el arranque con Windows. Si Windows no lo permite, lanza una excepción con el motivo.</summary>
        public static void SetEnabled(bool on)
        {
            Windows.ApplicationModel.StartupTask t;
            try { t = Task(); }
            catch
            {
                throw new InvalidOperationException(Util.IsAdmin
                    ? "En modo administrador Windows no deja cambiar el inicio de una app de la Store. Abre LIGERO de forma " +
                      "normal (sin administrador) y actívalo ahí, o hazlo en Configuración > Aplicaciones > Inicio."
                    : "Windows no permitió cambiar el inicio de LIGERO. Puedes hacerlo en Configuración > Aplicaciones > Inicio.");
            }
            if (!on)
            {
                t.Disable();
                return;
            }
            Windows.ApplicationModel.StartupTaskState s = Wait(t.RequestEnableAsync());
            if (s == Windows.ApplicationModel.StartupTaskState.Enabled || s == Windows.ApplicationModel.StartupTaskState.EnabledByPolicy) return;
            if (s == Windows.ApplicationModel.StartupTaskState.DisabledByUser)
                throw new InvalidOperationException("Desactivaste el inicio de Ligero en la configuración de Windows, así que Windows no deja " +
                    "que la app lo vuelva a activar sola. Actívalo en Configuración > Aplicaciones > Inicio.");
            throw new InvalidOperationException("Una directiva de tu equipo no permite que Ligero arranque con Windows.");
        }
    }
}
