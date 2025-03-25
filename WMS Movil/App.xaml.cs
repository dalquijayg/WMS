using System;
using System.Diagnostics;
using System.Threading.Tasks;
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

        private readonly UpdateChecker _updateService;
        public App()
        {
            InitializeComponent();
            _updateService = new UpdateChecker();
            if (ConfigExistente())
            {
                MainPage = new NavigationPage(new Ingreso());
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

        protected override void OnStart()
        {
            CheckForUpdatesAsync();
        }

        protected override void OnSleep()
        {
        }

        protected override void OnResume()
        {
        }
        private async void CheckForUpdatesAsync()
        {
            try
            {
                bool updateAvailable = await _updateService.CheckForUpdate();

                if (updateAvailable)
                {
                    var appVersion = await _updateService.GetLatestVersion();

                    if (appVersion != null)
                    {
                        // Mostrar diálogo de actualización
                        bool userAccepted = await Current.MainPage.DisplayAlert(
                            "Actualización disponible",
                            $"Hay una nueva versión disponible ({appVersion.Version}).\n\nNotas de la versión:\n{appVersion.ReleaseNotes}\n\n¿Desea actualizar ahora?",
                            "Actualizar", "Más tarde");

                        if (userAccepted)
                        {
                            await _updateService.DownloadAndInstallUpdate(appVersion.DownloadUrl);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error en verificación de actualizaciones: {ex.Message}");
            }
        }
    }
}
