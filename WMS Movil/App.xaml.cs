using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WMS_Movil.Servicio;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    public partial class App : Application
    {
        public static int IdUsuarioActual { get; set; }
        public static string NombreUsuarioActual { get; set; }
        public static int NivelUsuarioActual { get; set; }
        public static string CurrentPageName { get; set; }

        // URL del archivo version.json en tu repositorio
        private const string UpdateCheckUrl = "http://192.168.1.250/wms-updates/version.json";

        public App()
        {
            InitializeComponent();
            AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskSchedulerOnUnobservedTaskException;
            
            if (ConfigExistente())
            {
                // Intentar restaurar estado de sesión si existe
                bool sesionActiva = RestoreSessionState();

                if (sesionActiva)
                {
                    // Si hay sesión activa, ir a Home
                    MainPage = new NavigationPage(new Home(NombreUsuarioActual));
                    IniciarMonitoreoPedidos();
                }
                else
                {
                    // Si no hay sesión, ir a login
                    MainPage = new NavigationPage(new Ingreso());
                }
            }
            else
            {
                MainPage = new ConfigConection();
            }
        }

        private bool ConfigExistente()
        {   
            //metodo para verificar si ya existe una configuración guardada en el movil
            return Preferences.ContainsKey("ip_address") &&
                   Preferences.ContainsKey("username") &&
                   Preferences.ContainsKey("password") &&
                   Preferences.ContainsKey("selected_database") &&
                   !string.IsNullOrEmpty(Preferences.Get("ip_address", "")) &&
                   !string.IsNullOrEmpty(Preferences.Get("username", "")) &&
                   !string.IsNullOrEmpty(Preferences.Get("password", "")) &&
                   !string.IsNullOrEmpty(Preferences.Get("selected_database", ""));
        }

        public static void SaveSessionState()
        {
            if (IdUsuarioActual > 0)
            {
                Preferences.Set("SessionActive", true);
                Preferences.Set("IdUsuarioActual", IdUsuarioActual);
                Preferences.Set("NombreUsuarioActual", NombreUsuarioActual ?? "");
                Preferences.Set("NivelUsuarioActual", NivelUsuarioActual);
                Preferences.Set("CurrentPageName", CurrentPageName ?? "");

                Debug.WriteLine($"Estado de sesión guardado: Usuario {IdUsuarioActual}, Página {CurrentPageName}");
            }
        }

        private bool RestoreSessionState()
        {
            bool sesionActiva = Preferences.Get("SessionActive", false);

            if (sesionActiva)
            {
                IdUsuarioActual = Preferences.Get("IdUsuarioActual", 0);
                NombreUsuarioActual = Preferences.Get("NombreUsuarioActual", "");
                NivelUsuarioActual = Preferences.Get("NivelUsuarioActual", 0);
                CurrentPageName = Preferences.Get("CurrentPageName", "");

                // Verificar que los datos sean válidos
                sesionActiva = IdUsuarioActual > 0 && !string.IsNullOrEmpty(NombreUsuarioActual);

                Debug.WriteLine($"Estado de sesión restaurado: {sesionActiva}, Usuario {IdUsuarioActual}");
            }

            return sesionActiva;
        }

        public static void ClearSessionState()
        {
            Preferences.Set("SessionActive", false);
            IdUsuarioActual = 0;
            NombreUsuarioActual = "";
            NivelUsuarioActual = 0;
            CurrentPageName = "";
            DetenerMonitoreoPedidos();

            Debug.WriteLine("Sesión cerrada");
        }

        protected override void OnStart()
        {
            RestoreSessionState();
            // Verificar actualizaciones al iniciar
            CheckForUpdatesAsync();
        }

        protected override void OnSleep()
        {
            SaveSessionState();
            // El monitoreo de pedidos NO se detiene aquí: debe seguir corriendo
            // (servicio en primer plano nativo) aunque la pantalla esté bloqueada.

            // Guardar el nombre de la página actual
            if (MainPage is NavigationPage navigationPage && navigationPage.CurrentPage != null)
            {
                CurrentPageName = navigationPage.CurrentPage.GetType().Name;
                Preferences.Set("CurrentPageName", CurrentPageName);
                Debug.WriteLine($"App dormida en página: {CurrentPageName}");
            }
        }

        protected override void OnResume()
        {
            bool sesionActiva = RestoreSessionState();

            if (sesionActiva)
            {
                IniciarMonitoreoPedidos();
            }

            // Si hay sesión activa pero estamos en la página de login, redirigir al Home
            if (sesionActiva && MainPage is NavigationPage navigationPage)
            {
                if (navigationPage.CurrentPage is Ingreso)
                {
                    Device.BeginInvokeOnMainThread(async () => {
                        await navigationPage.PopToRootAsync();
                        await navigationPage.PushAsync(new Home(NombreUsuarioActual));
                    });

                    Debug.WriteLine("Redirección a Home después de restaurar");
                }
            }

            // Verificar actualizaciones al resumir la app
            CheckForUpdatesAsync();
        }

        // Delega en un servicio nativo (foreground service en Android) que sondea
        // PreparacionPedidos y notifica (sonido + vibración + notificación del
        // sistema) cuando hay una hoja asignada sin iniciar. Corre aunque la app
        // esté en segundo plano o el teléfono bloqueado.
        public static void IniciarMonitoreoPedidos()
        {
            Debug.WriteLine($"Monitoreo de pedidos: solicitando inicio para usuario {IdUsuarioActual}.");
            DependencyService.Get<IPedidoBackgroundMonitorService>()?.Iniciar();
        }

        public static void DetenerMonitoreoPedidos()
        {
            Debug.WriteLine("Monitoreo de pedidos: solicitando detención.");
            DependencyService.Get<IPedidoBackgroundMonitorService>()?.Detener();
        }

        private async void CheckForUpdatesAsync()
        {
            try
            {
                Debug.WriteLine("Verificando actualizaciones...");

                using (HttpClient client = new HttpClient())
                {
                    // Descargar el archivo version.json
                    var response = await client.GetStringAsync(UpdateCheckUrl);
                    var json = JObject.Parse(response);

                    // Obtener la versión más reciente del JSON
                    string latestVersion = json["latestVersion"]?.ToString();
                    string updateUrl = json["updateUrl"]?.ToString();

                    if (string.IsNullOrEmpty(latestVersion) || string.IsNullOrEmpty(updateUrl))
                    {
                        Debug.WriteLine("Error: version.json mal formado");
                        return;
                    }

                    // Obtener la versión actual de la app
                    string currentVersionString = VersionTracking.CurrentVersion;
                    Version currentVersion = new Version(currentVersionString);
                    Version latestVersionParsed = new Version(latestVersion);

                    Debug.WriteLine($"Versión actual: {currentVersion}, Última versión: {latestVersionParsed}");

                    // Comparar las versiones
                    if (latestVersionParsed > currentVersion)
                    {
                        Debug.WriteLine("¡Nueva versión disponible!");

                        // Esperar a que MainPage esté disponible
                        if (MainPage == null)
                        {
                            await Task.Delay(1000);
                        }

                        // Mostrar alerta de actualización
                        Device.BeginInvokeOnMainThread(async () =>
                        {
                            bool update = await MainPage.DisplayAlert(
                                "Actualización Disponible",
                                $"Nueva versión {latestVersion} disponible.\n¿Desea actualizar ahora?",
                                "Actualizar",
                                "Más Tarde");

                            if (update)
                            {
                                // Abrir el enlace de descarga en el navegador
                                await Launcher.OpenAsync(new Uri(updateUrl));
                            }
                        });
                    }
                    else
                    {
                        Debug.WriteLine("La aplicación está actualizada");
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"Error de red al verificar actualizaciones: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al verificar actualizaciones: {ex.Message}");
            }
        }

        private void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            LogException(ex, "AppDomain Unhandled Exception");
        }

        private void TaskSchedulerOnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogException(e.Exception, "TaskScheduler Unobserved Exception");
            e.SetObserved();
        }

        private void LogException(Exception ex, string source)
        {
            try
            {
                Debug.WriteLine($"{source}: {ex?.Message}\n{ex?.StackTrace}");
            }
            catch
            {
                // No hacer nada si falla el logging
            }
        }
    }
}