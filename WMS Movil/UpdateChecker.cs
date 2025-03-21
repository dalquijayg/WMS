using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace WMS_Movil
{
    public class UpdateChecker
    {
        // URL de la API de GitHub para obtener la última release
        private const string GITHUB_API_URL = "https://api.github.com/repos/dalquijayg/WMS/releases/latest";

        public class GitHubRelease
        {
            [JsonProperty("tag_name")]
            public string TagName { get; set; }

            [JsonProperty("html_url")]
            public string HtmlUrl { get; set; }

            [JsonProperty("body")]
            public string ReleaseNotes { get; set; }
        }

        public class AppVersion
        {
            public int VersionCode { get; set; }
            public string VersionName { get; set; }
        }

        public static async Task<bool> CheckForUpdate()
        {
            try
            {
                // Obtener información de la versión actual
                AppVersion currentVersion = GetCurrentVersion();

                // Obtener información de la última versión en GitHub
                GitHubRelease latestRelease = await GetLatestRelease();

                if (latestRelease != null)
                {
                    // Convertir el tag_name (por ejemplo v1.2) a un versionCode (por ejemplo 102)
                    string versionName = latestRelease.TagName.TrimStart('v');
                    int latestVersionCode = ConvertVersionNameToCode(versionName);

                    // Comparar versiones
                    if (latestVersionCode > currentVersion.VersionCode)
                    {
                        // Mostrar diálogo de actualización
                        bool update = await Application.Current.MainPage.DisplayAlert(
                            "Actualización disponible",
                            $"Hay una nueva versión disponible ({versionName}). Es recomendable actualizar para obtener las últimas funcionalidades y correcciones.\n\nNotas de la versión:\n{latestRelease.ReleaseNotes}",
                            "Actualizar",
                            "Más tarde");

                        if (update)
                        {
                            // Abrir la URL de la release
                            await Browser.OpenAsync(latestRelease.HtmlUrl, BrowserLaunchMode.SystemPreferred);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al verificar actualizaciones: {ex.Message}");
            }

            return false;
        }

        private static AppVersion GetCurrentVersion()
        {
            var version = VersionTracking.CurrentVersion;
            var build = VersionTracking.CurrentBuild;

            return new AppVersion
            {
                VersionName = version,
                VersionCode = ConvertVersionNameToCode(version)
            };
        }

        private static async Task<GitHubRelease> GetLatestRelease()
        {
            using (var client = new HttpClient())
            {
                // Agregar User-Agent header (requerido por GitHub API)
                client.DefaultRequestHeaders.Add("User-Agent", "WMS-App");

                var response = await client.GetStringAsync(GITHUB_API_URL);
                return JsonConvert.DeserializeObject<GitHubRelease>(response);
            }
        }

        private static int ConvertVersionNameToCode(string versionName)
        {
            // Convertir "1.2.3" a 1023 (sin puntos, cada parte como 2 dígitos)
            string[] parts = versionName.Split('.');
            int versionCode = 0;

            for (int i = 0; i < Math.Min(parts.Length, 3); i++)
            {
                if (int.TryParse(parts[i], out int part))
                {
                    versionCode = versionCode * 100 + part;
                }
            }

            // Asegurar que tenga al menos 3 componentes
            while (parts.Length < 3)
            {
                versionCode = versionCode * 100;
                parts = new string[parts.Length + 1];
            }

            return versionCode;
        }
    }
}
