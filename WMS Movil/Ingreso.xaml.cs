using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Ingreso : ContentPage
    {
        private bool isPasswordHidden = true;
        private bool isPasswordDialogOpen = false;
        public bool IsPasswordHidden
        {
            get => isPasswordHidden;
            set
            {
                isPasswordHidden = value;
                OnPropertyChanged();
            }
        }
        public static int IdUsuarioActual { get; set; }
        public Ingreso()
        {
            InitializeComponent();
            BindingContext = this;
        }
        private void OnTogglePassword(object sender, EventArgs e)
        {
            IsPasswordHidden = !IsPasswordHidden;
        }
        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Obtener la versión actual de la aplicación
            string version = Xamarin.Essentials.VersionTracking.CurrentVersion;

            // Actualizar la etiqueta de versión
            VersionLabel.Text = $"Versión: {version}";
        }
        private async void OnLoginClicked(object sender, EventArgs e)
        {

            if (string.IsNullOrEmpty(UserEntry.Text) || string.IsNullOrEmpty(PasswordEntry.Text))
            {
                await DisplayAlert("Error", "Por favor ingrese usuario y contraseña", "OK");
                return;
            }
            
            try
            {
                LoadingOverlay.IsVisible = true;
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    string query = @"SELECT 
                            Id, 
                            Nombres, 
                            Apellidos, 
                            CAST(Activo AS SIGNED) AS Activo, 
                            CAST(IdNivel AS SIGNED) AS IdNivel 
                           FROM usuarios 
                           WHERE Usuario = @usuario AND Password = @password";

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@usuario", UserEntry.Text);
                        command.Parameters.AddWithValue("@password", PasswordEntry.Text);

                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                try
                                {
                                    int activo = Convert.ToInt32(reader["Activo"]);
                                    int idNivel = Convert.ToInt32(reader["IdNivel"]);

                                    if (activo == 0)
                                    {
                                        await DisplayAlert("Error", "Usuario desactivado", "OK");
                                        return;
                                    }

                                    if (idNivel != 3 && idNivel != 4)
                                    {
                                        await DisplayAlert("Error", "No tiene permisos suficientes", "OK");
                                        return;
                                    }

                                    App.IdUsuarioActual = reader.GetInt32("Id");
                                    string nombres = reader.GetString("Nombres");
                                    string apellidos = reader.GetString("Apellidos");
                                    string nombreCompleto = $"{nombres} {apellidos}";
                                    App.NombreUsuarioActual = nombreCompleto;
                                    App.NivelUsuarioActual = idNivel;

                                    Application.Current.MainPage = new NavigationPage(new Home(nombreCompleto));
                                }
                                catch (Exception ex)
                                {
                                    await DisplayAlert("Error", "Error al procesar datos del usuario: " + ex.Message, "OK");
                                }
                            }
                            else
                            {
                                await DisplayAlert("Error", "Usuario o contraseña incorrectos", "OK");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al iniciar sesión: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }
        private async void OnLogoTapped(object sender, EventArgs e)
        {
            if (isPasswordDialogOpen) return;
            isPasswordDialogOpen = true;

            try
            {
                string password = await DisplayPromptAsync("Configuración", "Ingrese la contraseña:",
                    cancel: "Cancelar", maxLength: 20, keyboard: Keyboard.Default);

                if (password == "creasys2445")
                {
                    await Navigation.PushAsync(new ConfigConection());
                }
                else if (!string.IsNullOrEmpty(password))
                {
                    await DisplayAlert("Error", "Contraseña incorrecta", "OK");
                }
            }
            finally
            {
                isPasswordDialogOpen = false;
            }
        }
    }
}