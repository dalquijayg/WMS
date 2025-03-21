using System;
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
        public App()
        {
            InitializeComponent();
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
            base.OnStart();

            // Iniciar la verificación de actualizaciones sin await
            Device.BeginInvokeOnMainThread(async () => {
                // Pequeño retraso para asegurar que la UI esté cargada
                await Task.Delay(1000);
                await UpdateChecker.CheckForUpdate();
            });
        }

        protected override void OnSleep()
        {
        }

        protected override void OnResume()
        {
        }
    }
}
