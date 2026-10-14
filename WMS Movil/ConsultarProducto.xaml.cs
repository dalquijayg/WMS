using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    public class UbicacionConExistencia
    {
        public string Codigo { get; set; }
        public string Detalle { get; set; }
        public decimal ExistenciaFardo { get; set; }
        public decimal ExistenciaUnidad { get; set; }
    }

    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ConsultarProducto : ContentPage
    {
        private readonly string _connectionString;
        private readonly ObservableCollection<UbicacionConExistencia> _ubicaciones;

        public ConsultarProducto()
        {
            InitializeComponent();
            _connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
            _ubicaciones = new ObservableCollection<UbicacionConExistencia>();
            UbicacionesCollection.ItemsSource = _ubicaciones;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Device.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(200);
                UpcEntry.Focus();
            });
        }

        private async void OnUpcCompleted(object sender, EventArgs e)
        {
            string upc = UpcEntry.Text?.Trim();
            if (string.IsNullOrEmpty(upc)) return;
            await BuscarProducto(upc);
        }

        private async Task BuscarProducto(string upcEscaneado)
        {
            if (upcEscaneado.Length < 13)
                upcEscaneado = upcEscaneado.PadLeft(13, '0');

            LoadingOverlay.IsVisible = true;

            string upcFinal = upcEscaneado;
            string descripcion = string.Empty;
            string upcPaquete = string.Empty;
            int cantPorPaquete = 0;
            var ubicaciones = new List<UbicacionConExistencia>();

            try
            {
                await Task.Run(() =>
                {
                    using (var conn = new MySqlConnection(_connectionString))
                    {
                        conn.Open();

                        // Paso 1: Intentar como UPCPaquete
                        using (var cmd = new MySqlCommand(@"
                            SELECT pp.Upc, COALESCE(p.DescLarga, pp.Upc) AS Descripcion, pp.Cantidad
                            FROM productospaquetes pp
                            LEFT JOIN productos p ON pp.Upc = p.Upc
                            WHERE pp.UPCPaquete = @Upc LIMIT 1", conn))
                        {
                            cmd.Parameters.AddWithValue("@Upc", upcEscaneado);
                            using (var reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    upcPaquete = upcEscaneado;
                                    upcFinal = reader.GetString("Upc");
                                    descripcion = reader.GetString("Descripcion");
                                    cantPorPaquete = reader.IsDBNull(reader.GetOrdinal("Cantidad"))
                                        ? 0 : reader.GetInt32("Cantidad");
                                }
                            }
                        }

                        // Paso 2: Buscar como UPC unitario
                        if (string.IsNullOrEmpty(descripcion))
                        {
                            using (var cmd = new MySqlCommand(
                                "SELECT COALESCE(DescLarga, Upc) AS Descripcion FROM productos WHERE Upc = @Upc LIMIT 1", conn))
                            {
                                cmd.Parameters.AddWithValue("@Upc", upcFinal);
                                using (var reader = cmd.ExecuteReader())
                                {
                                    if (reader.Read())
                                        descripcion = reader.GetString("Descripcion");
                                }
                            }

                            // Paso 3: Reverse-lookup paquete
                            if (!string.IsNullOrEmpty(descripcion))
                            {
                                using (var cmd = new MySqlCommand(
                                    "SELECT UPCPaquete, Cantidad FROM productospaquetes WHERE Upc = @Upc LIMIT 1", conn))
                                {
                                    cmd.Parameters.AddWithValue("@Upc", upcFinal);
                                    using (var reader = cmd.ExecuteReader())
                                    {
                                        if (reader.Read())
                                        {
                                            upcPaquete = reader.IsDBNull(reader.GetOrdinal("UPCPaquete"))
                                                ? string.Empty : reader.GetString("UPCPaquete");
                                            cantPorPaquete = reader.IsDBNull(reader.GetOrdinal("Cantidad"))
                                                ? 0 : reader.GetInt32("Cantidad");
                                        }
                                    }
                                }
                            }
                        }

                        if (string.IsNullOrEmpty(descripcion)) return;

                        // Paso 4: Ubicaciones con existencia
                        using (var cmd = new MySqlCommand(@"
                            SELECT ub.Rack, ub.Bloque, ub.Nivel,
                                   iu.ExistenciaFardo, iu.ExistenciaUnidad
                            FROM InventarioUbicaciones iu
                            JOIN ubicacionesbodega ub ON iu.IdUbicacion = ub.Id
                            WHERE iu.Upc = @Upc
                            ORDER BY ub.Rack, ub.Bloque, ub.Nivel", conn))
                        {
                            cmd.Parameters.AddWithValue("@Upc", upcFinal);
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    string rack = reader["Rack"]?.ToString() ?? "";
                                    string bloque = reader["Bloque"]?.ToString() ?? "";
                                    string nivel = reader["Nivel"]?.ToString() ?? "";
                                    ubicaciones.Add(new UbicacionConExistencia
                                    {
                                        Codigo = $"R{rack}-B{bloque}-N{nivel}",
                                        Detalle = $"Rack {rack} · Bloque {bloque} · Nivel {nivel}",
                                        ExistenciaFardo = reader.GetDecimal("ExistenciaFardo"),
                                        ExistenciaUnidad = reader.GetDecimal("ExistenciaUnidad")
                                    });
                                }
                            }
                        }
                    }
                });

                if (string.IsNullOrEmpty(descripcion))
                {
                    await DisplayAlert("No encontrado", "No se encontró ningún producto con ese UPC.", "OK");
                    UpcEntry.Text = string.Empty;
                    UpcEntry.Focus();
                    return;
                }

                Device.BeginInvokeOnMainThread(() =>
                {
                    DescLabel.Text = descripcion;
                    UpcLabel.Text = upcFinal;
                    UpcPaqueteLabel.Text = string.IsNullOrEmpty(upcPaquete) ? "—" : upcPaquete;
                    CantPaqueteLabel.Text = cantPorPaquete > 0 ? cantPorPaquete.ToString() : "—";
                    PanelProducto.IsVisible = true;

                    _ubicaciones.Clear();
                    foreach (var u in ubicaciones)
                        _ubicaciones.Add(u);

                    PanelUbicaciones.IsVisible = true;
                    SinUbicacionesPanel.IsVisible = ubicaciones.Count == 0;
                    UpcEntry.Text = string.Empty;
                    UpcEntry.Focus();
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Error al buscar producto: {ex.Message}", "OK");
                UpcEntry.Text = string.Empty;
                UpcEntry.Focus();
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        private async void OnVolverClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}