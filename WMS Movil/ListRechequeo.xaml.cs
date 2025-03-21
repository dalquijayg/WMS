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
    public partial class ListRechequeo : ContentPage
    {
        private List<PedidoPendiente> todosLosPedidos;
        public ListRechequeo()
        {
            InitializeComponent();
            CargarPedidos();
        }
        protected override void OnAppearing()
        {
            base.OnAppearing();
            CargarPedidos();
        }

        private async void CargarPedidos()
        {
            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    string query = @"SELECT
                                        pb.IdPedidos, 
                                        pb.Fecha, 
                                        CONCAT(pb.NombreEmpresa, ' - ', pb.Departamento) as NombreEmpresa, 
                                        pb.TotalCantidad,
                                        COUNT(dpb.Id) AS CantidadLineas
                                    FROM
                                        pedidostienda_bodega pb
                                    INNER JOIN
                                        detallepedidostienda_bodega dpb ON pb.IdPedidos = dpb.IdConsolidado
                                    WHERE
                                        pb.Estado = 6
                                    GROUP BY
                                        pb.IdPedidos, pb.Fecha, pb.NombreEmpresa, pb.Departamento, pb.TotalCantidad";

                    using (var command = new MySqlCommand(query, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        var pedidos = new List<PedidoPendiente>();
                        while (reader.Read())
                        {
                            pedidos.Add(new PedidoPendiente
                            {
                                IdPedidos = reader.GetInt32("IdPedidos"),
                                Fecha = reader.GetDateTime("Fecha"),
                                NombreEmpresa = reader.GetString("NombreEmpresa"),
                                TotalCantidad = reader.GetDouble("TotalCantidad"),
                                CantidadLineas = reader.GetInt32("CantidadLineas")
                            });
                        }
                        todosLosPedidos = pedidos;
                        PedidosCollection.ItemsSource = pedidos;
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar pedidos: " + ex.Message, "OK");
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.NewTextValue))
                PedidosCollection.ItemsSource = todosLosPedidos;
            else
            {
                var filtrado = todosLosPedidos.Where(p =>
                    p.IdPedidos.ToString().Contains(e.NewTextValue));
                PedidosCollection.ItemsSource = filtrado;
            }
        }

        private async void OnPedidoSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is PedidoPendiente pedido)
            {
                try
                {
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        LoadingOverlay.IsVisible = true;
                    });

                    // Esperar un momento para asegurar que el LoadingOverlay sea visible
                    await Task.Delay(100);

                    // Navegar a DetalleHojaUbicaciones
                    await Navigation.PushAsync(new DetalleHojarechequeo(pedido.IdPedidos, null));
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", "Error al procesar el pedido: " + ex.Message, "OK");
                }
                finally
                {
                    PedidosCollection.SelectedItem = null;
                }
                Device.BeginInvokeOnMainThread(() =>
                {
                    LoadingOverlay.IsVisible = false;
                });
            }
        }
    public class PedidoPendiente
    {
        public int IdPedidos { get; set; }
        public DateTime Fecha { get; set; }
        public string NombreEmpresa { get; set; }
        public double TotalCantidad { get; set; }
        public int CantidadLineas { get; set; }
    }
}
}