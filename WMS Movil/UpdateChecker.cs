using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace WMS_Movil
{
    public class AppVersion
    {
        public string Version { get; set; }
        public string BuildNumber { get; set; }
        public string ReleaseNotes { get; set; }
        public string DownloadUrl { get; set; }
    }
    public class UpdateChecker
    {
        // URL de la API de GitHub para obtener la última release
        private const string VERSION_URL = "https://raw.githubusercontent.com/dalquijayg/WMS/refs/heads/master/version.json?token=GHSAT0AAAAAADAQW6I6ET4EAV6L2KVCQTBAZ7CYVTA";

        public async Task<bool> CheckForUpdate()
        {
            try
            {
                // Obtener la versión actual de la aplicación
                string currentVersion = VersionTracking.CurrentVersion;
                string currentBuild = VersionTracking.CurrentBuild;

                // Obtener información de versión desde GitHub
                var httpClient = new HttpClient();
                var response = await httpClient.GetStringAsync(VERSION_URL);

                var remoteVersion = JsonConvert.DeserializeObject<AppVersion>(response);

                // Comparar versiones (puedes usar lógica más compleja si es necesario)
                bool updateAvailable = false;

                // Primero compara por versión semántica
                if (CompareVersions(remoteVersion.Version, currentVersion) > 0)
                {
                    updateAvailable = true;
                }
                // Si las versiones son iguales, compara el número de compilación
                else if (remoteVersion.Version == currentVersion &&
                         CompareVersions(remoteVersion.BuildNumber, currentBuild) > 0)
                {
                    updateAvailable = true;
                }

                return updateAvailable;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error verificando actualizaciones: {ex.Message}");
                return false;
            }
        }

        private int CompareVersions(string version1, string version2)
        {
            var v1 = new Version(version1);
            var v2 = new Version(version2);

            return v1.CompareTo(v2);
        }

        public async Task<AppVersion> GetLatestVersion()
        {
            try
            {
                var httpClient = new HttpClient();
                var response = await httpClient.GetStringAsync(VERSION_URL);

                return JsonConvert.DeserializeObject<AppVersion>(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error obteniendo la última versión: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> DownloadAndInstallUpdate(string downloadUrl)
        {
            try
            {
                if (Device.RuntimePlatform == Device.Android)
                {
                    // Para Android, abrimos el navegador para descargar e instalar
                    await Browser.OpenAsync(downloadUrl, BrowserLaunchMode.External);
                    return true;
                }
                else
                {
                    // Para otras plataformas, puedes implementar lógicas específicas
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error descargando actualización: {ex.Message}");
                return false;
            }
        }
    }
}
