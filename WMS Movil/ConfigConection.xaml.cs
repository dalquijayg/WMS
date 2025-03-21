using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ConfigConection : ContentPage
    {
        public ObservableCollection<string> Databases { get; set; }
        public ConfigConection()
        {
            InitializeComponent();
            Databases = new ObservableCollection<string>();
            LoadSavedConfig();
        }
        private void LoadSavedConfig()
        {
            IpEntry.Text = Preferences.Get("ip_address", string.Empty);
            UserEntry.Text = Preferences.Get("username", string.Empty);
            PasswordEntry.Text = Preferences.Get("password", string.Empty);
        }

        private async void OnEntryCompleted(object sender, EventArgs e)
        {
            var entry = (Entry)sender;
            if (entry == IpEntry)
            {
                UserEntry.Focus();
                Preferences.Set("ip_address", entry.Text);
            }
            else if (entry == UserEntry)
            {
                PasswordEntry.Focus();
                Preferences.Set("username", entry.Text);
            }
            else if (entry == PasswordEntry)
            {
                Preferences.Set("password", entry.Text);
                if (!string.IsNullOrEmpty(IpEntry.Text) &&
                    !string.IsNullOrEmpty(UserEntry.Text) &&
                    !string.IsNullOrEmpty(PasswordEntry.Text))
                {
                    await LoadDatabasesAsync();
                }
            }
        }

        private async Task<bool> TestConnectionAsync()
        {
            string connectionString = $"Server={IpEntry.Text};User ID={UserEntry.Text};Password={PasswordEntry.Text};";
            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task LoadDatabasesAsync()
        {
            string connectionString = $"Server={IpEntry.Text};User ID={UserEntry.Text};Password={PasswordEntry.Text};";

            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new MySqlCommand("SHOW DATABASES;", connection))
                    using (var reader = command.ExecuteReader())
                    {
                        Databases.Clear();
                        while (reader.Read())
                        {
                            string dbName = reader.GetString(0);
                            if (!dbName.Equals("information_schema") &&
                                !dbName.Equals("performance_schema") &&
                                !dbName.Equals("mysql"))
                            {
                                Databases.Add(dbName);
                            }
                        }
                    }
                }
                DatabasePicker.ItemsSource = Databases;

                // Cargar base de datos guardada
                string savedDb = Preferences.Get("selected_database", string.Empty);
                if (!string.IsNullOrEmpty(savedDb) && Databases.Contains(savedDb))
                {
                    DatabasePicker.SelectedItem = savedDb;
                }

                // Agregar evento de selección cambiada
                DatabasePicker.SelectedIndexChanged += (s, e) => {
                    if (DatabasePicker.SelectedItem != null)
                    {
                        Preferences.Set("selected_database", DatabasePicker.SelectedItem.ToString());
                    }
                };
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al obtener bases de datos: " + ex.Message, "OK");
            }
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(IpEntry.Text) ||
        string.IsNullOrEmpty(UserEntry.Text) ||
        string.IsNullOrEmpty(PasswordEntry.Text) ||
        DatabasePicker.SelectedItem == null)
            {
                await DisplayAlert("Error", "Por favor complete todos los campos y seleccione una base de datos", "OK");
                return;
            }

            saveButton.IsEnabled = false;

            try
            {
                if (!await TestConnectionAsync())
                {
                    throw new Exception("No se pudo establecer conexión con el servidor");
                }

                Preferences.Set("selected_database", DatabasePicker.SelectedItem.ToString());
                await DisplayAlert("Éxito", "Configuración guardada correctamente", "OK");

                // Reiniciar la aplicación
                System.Diagnostics.Process.GetCurrentProcess().CloseMainWindow();
                Application.Current.MainPage = new NavigationPage(new Ingreso());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al guardar la configuración: " + ex.Message, "OK");
            }
            finally
            {
                saveButton.IsEnabled = true;
            }
        }
    }
}