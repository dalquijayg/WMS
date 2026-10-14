using MySqlConnector;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ListPendientes : ContentPage
    {
        private List<HojaAsignada> todasLasHojas;
        private int idUsuario;

        public ListPendientes()
        {
            InitializeComponent();
            idUsuario = Preferences.Get("UserId", 0);
            CargarNombreUsuario();
            CargarHojasAsignadas();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            CargarHojasAsignadas();
        }

        private void CargarNombreUsuario()
        {
            string nombreUsuario = Preferences.Get("UserName", "Usuario");
            NombreUsuarioLabel.Text = $"👤 {nombreUsuario}";
        }

        private async void CargarHojasAsignadas()
        {
            try
            {
                LoadingOverlay.IsVisible = true;
                EmptyState.IsVisible = false;

                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    // Query para obtener las hojas asignadas al usuario
                    string query = @"SELECT
                                        PreparacionPedidos.Idpreparo,
                                        PreparacionPedidos.IdPedido,
                                        PreparacionPedidos.NoHoja,
                                        PreparacionPedidos.Sucursal,
                                        PreparacionPedidos.TotalSkus,
                                        PreparacionPedidos.TotalFardos,
                                        PreparacionPedidos.FechaHoraInicio,
                                        PreparacionPedidos.FechaHorafinalizo,
                                        COUNT(CASE WHEN detallepedidostienda_bodega.EstadoPreparacionproducto IN (1,2) THEN 1 END) as ProductosPreparados
                                    FROM
                                        PreparacionPedidos
                                        LEFT JOIN detallepedidostienda_bodega 
                                            ON PreparacionPedidos.IdPedido = detallepedidostienda_bodega.IdConsolidado 
                                            AND PreparacionPedidos.NoHoja = detallepedidostienda_bodega.NoHoja
                                    WHERE
                                        PreparacionPedidos.IdUsuario = @idUsuario
                                        AND PreparacionPedidos.FechaHorafinalizo IS NULL
                                    GROUP BY
                                        PreparacionPedidos.Idpreparo,
                                        PreparacionPedidos.IdPedido,
                                        PreparacionPedidos.NoHoja,
                                        PreparacionPedidos.Sucursal,
                                        PreparacionPedidos.TotalSkus,
                                        PreparacionPedidos.TotalFardos,
                                        PreparacionPedidos.FechaHoraInicio,
                                        PreparacionPedidos.FechaHorafinalizo
                                    ORDER BY
                                        PreparacionPedidos.FechaHoraInicio DESC,
                                        PreparacionPedidos.IdPedido ASC,
                                        PreparacionPedidos.NoHoja ASC";

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@idUsuario", idUsuario);

                        using (var reader = command.ExecuteReader())
                        {
                            var hojas = new List<HojaAsignada>();

                            while (reader.Read())
                            {
                                var hoja = new HojaAsignada
                                {
                                    IdPreparacion = reader.GetInt32("Idpreparo"),
                                    IdPedido = reader.GetInt32("IdPedido"),
                                    NoHoja = reader.GetInt32("NoHoja"),
                                    Sucursal = reader.GetString("Sucursal"),
                                    TotalSkus = reader.GetInt32("TotalSkus"),
                                    TotalFardos = reader.GetDouble("TotalFardos"),
                                    FechaHoraInicio = reader.IsDBNull(reader.GetOrdinal("FechaHoraInicio"))
                                        ? (DateTime?)null
                                        : reader.GetDateTime("FechaHoraInicio"),
                                    FechaHorafinalizo = reader.IsDBNull(reader.GetOrdinal("FechaHorafinalizo"))
                                        ? (DateTime?)null
                                        : reader.GetDateTime("FechaHorafinalizo"),
                                    ProductosPreparados = reader.GetInt32("ProductosPreparados")
                                };

                                hojas.Add(hoja);
                            }

                            todasLasHojas = hojas;
                            HojasCollection.ItemsSource = hojas;

                            // Mostrar mensaje si no hay hojas
                            EmptyState.IsVisible = hojas.Count == 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar hojas asignadas: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (todasLasHojas == null) return;

            if (string.IsNullOrWhiteSpace(e.NewTextValue))
            {
                HojasCollection.ItemsSource = todasLasHojas;
            }
            else
            {
                var filtrado = todasLasHojas.Where(h =>
                    h.IdPedido.ToString().Contains(e.NewTextValue) ||
                    h.Sucursal.ToLower().Contains(e.NewTextValue.ToLower()));
                HojasCollection.ItemsSource = filtrado;
            }
        }

        private async void OnHojaSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is HojaAsignada hoja)
            {
                try
                {
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        LoadingOverlay.IsVisible = true;
                    });

                    await Task.Delay(100);

                    // Si no ha iniciado, registrar el inicio
                    if (hoja.FechaHoraInicio == null)
                    {
                        string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

                        using (var connection = new MySqlConnection(connectionString))
                        {
                            connection.Open();

                            using (var command = new MySqlCommand(@"UPDATE PreparacionPedidos 
                                                           SET FechaHoraInicio = NOW() 
                                                           WHERE Idpreparo = @idPreparacion", connection))
                            {
                                command.Parameters.AddWithValue("@idPreparacion", hoja.IdPreparacion);
                                command.ExecuteNonQuery();
                            }
                        }
                    }

                    // CAMBIO: Navegar directo a DetallePedidoPendientes sin IdUbicacion
                    await Navigation.PushAsync(new DetallePedidoPendientes(hoja.IdPedido, hoja.NoHoja));
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", "Error al abrir la hoja: " + ex.Message, "OK");
                }
                finally
                {
                    HojasCollection.SelectedItem = null;
                    LoadingOverlay.IsVisible = false;
                }
            }
        }
    }

    public class HojaAsignada
    {
        public int IdPreparacion { get; set; }
        public int IdPedido { get; set; }
        public int NoHoja { get; set; }
        public string Sucursal { get; set; }
        public int TotalSkus { get; set; }
        public double TotalFardos { get; set; }
        public DateTime? FechaHoraInicio { get; set; }
        public DateTime? FechaHorafinalizo { get; set; }
        public int ProductosPreparados { get; set; }

        // Propiedades calculadas para el UI
        public string TituloHoja => $"Pedido #{IdPedido} - Hoja {NoHoja}";

        public string EstadoTexto
        {
            get
            {
                if (FechaHoraInicio == null) return "Pendiente";
                if (FechaHorafinalizo != null) return "Finalizado";
                return "En Proceso";
            }
        }

        public Color EstadoColor
        {
            get
            {
                if (FechaHoraInicio == null) return Color.FromHex("#6B7280");
                if (FechaHorafinalizo != null) return Color.FromHex("#10B981");
                return Color.FromHex("#F59E0B");
            }
        }

        public string ProgresoTexto => $"{ProductosPreparados}/{TotalSkus} SKUs";

        public int PorcentajeProgreso => TotalSkus > 0 ? (int)((ProductosPreparados * 100.0) / TotalSkus) : 0;

        public double ProgresoWidth
        {
            get
            {
                // Calcula el ancho proporcional para la barra de progreso
                // Asumiendo un ancho máximo de 300 (ajusta según tu UI)
                double anchoMaximo = 300;
                return (PorcentajeProgreso / 100.0) * anchoMaximo;
            }
        }

        public Color ProgresoColor
        {
            get
            {
                if (PorcentajeProgreso == 100) return Color.FromHex("#10B981");
                if (PorcentajeProgreso > 0) return Color.FromHex("#F59E0B");
                return Color.FromHex("#6B7280");
            }
        }
    }
}