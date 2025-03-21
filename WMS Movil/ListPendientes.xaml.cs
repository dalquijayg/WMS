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
    public partial class ListPendientes : ContentPage
    {
        private List<PedidoPendiente> todosLosPedidos;
        public ListPendientes()
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
                                        pb.Estado = 4
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

                    string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};Allow User Variables=True;"; 
                    using (var connection = new MySqlConnection(connectionString))
                    {
                        connection.Open();

                        // Verificar si está paginado
                        using (var command = new MySqlCommand("SELECT Paginado FROM pedidostienda_bodega WHERE IdPedidos = @idPedido", connection))
                        {
                            command.Parameters.AddWithValue("@idPedido", pedido.IdPedidos);
                            var paginado = Convert.ToInt32(command.ExecuteScalar());

                            if (paginado == 0)
                            {
                                // Ejecutar la actualización para paginado
                                using (var updateCommand = new MySqlCommand(@"
                            UPDATE detallepedidostienda_bodega d
                            INNER JOIN (
                                SELECT 
                                    PedidosNumerados.*,
                                    CEILING(RowNum / 25.0) AS NumeroHoja
                                FROM (
                                    SELECT 
                                        PedidosDetallados.*,
                                        @row_number := IF(@current_pedido = PedidosDetallados.IdPedidos, @row_number + 1, 1) AS RowNum,
                                        @current_pedido := PedidosDetallados.IdPedidos
                                    FROM (
                                        SELECT
                                            pedidostienda_bodega.IdPedidos, 
                                            detallepedidostienda_bodega.UPC, 
                                            detallepedidostienda_bodega.Descripcion, 
                                            detallepedidostienda_bodega.Cantidad,
                                            detallepedidostienda_bodega.Id AS DetalleId,
                                            ubicacionesbodega.Id, 
                                            ubicacionesbodega.Rack, 
                                            ubicacionesbodega.Nivel, 
                                            ubicacionesbodega.Descripcion AS Ubicacion,
                                            ubicacionesbodega.Id as IdUbicacionBodega
                                        FROM
                                            pedidostienda_bodega
                                            INNER JOIN detallepedidostienda_bodega ON pedidostienda_bodega.IdPedidos = detallepedidostienda_bodega.IdConsolidado
                                            INNER JOIN productospaquetes ON detallepedidostienda_bodega.UPC = productospaquetes.UPCPaquete
                                            INNER JOIN productos ON productospaquetes.Upc = productos.Upc
                                            INNER JOIN ubicacionesbodega ON productos.IdUbicacionBodega = ubicacionesbodega.Id
                                        WHERE
                                            pedidostienda_bodega.IdPedidos = @idPedido
                                        ORDER BY 
                                            Nivel ASC,
                                            Id ASC
                                    ) AS PedidosDetallados
                                    CROSS JOIN (SELECT @current_pedido := 0, @row_number := 0) AS vars
                                ) AS PedidosNumerados
                            ) AS t ON d.Id = t.DetalleId
                            SET 
                                d.NoHoja = t.NumeroHoja,
                                d.IdUbicacionBodega = t.IdUbicacionBodega
                            WHERE d.IdConsolidado = @idPedido", connection))
                                {
                                    updateCommand.Parameters.AddWithValue("@idPedido", pedido.IdPedidos);
                                    updateCommand.ExecuteNonQuery();
                                }

                                // Insertar en HistorialPreparacionPedidos
                                using (var insertCommand = new MySqlCommand(@"
                            INSERT INTO HistorialPreparacionPedidos (IdPedido, NoHoja, Sucursal, TotalSKUs, TotalFardos)
                                    SELECT 
                                        d.IdConsolidado,
                                        d.NoHoja,
                                        p.NombreEmpresa,
                                        COUNT(*) AS TotalSKUs,
                                        SUM(d.Cantidad) AS TotalFardos
                                    FROM detallepedidostienda_bodega d
                                    INNER JOIN pedidostienda_bodega p ON p.IdPedidos = d.IdConsolidado
                                    WHERE d.IdConsolidado = @idPedido
                                    GROUP BY d.IdConsolidado, d.NoHoja, p.NombreEmpresa", connection))
                                {
                                    insertCommand.Parameters.AddWithValue("@idPedido", pedido.IdPedidos);
                                    insertCommand.ExecuteNonQuery();
                                }

                                // Actualizar pedidostienda_bodega
                                using (var finalUpdateCommand = new MySqlCommand(@"
                                    UPDATE pedidostienda_bodega p
                                    SET p.Paginado = 1,
                                        p.Nohojas = (
                                            SELECT MAX(NoHoja)
                                            FROM detallepedidostienda_bodega
                                            WHERE IdConsolidado = @idPedido
                                        )
                                    WHERE p.IdPedidos = @idPedido", connection))
                                {
                                    finalUpdateCommand.Parameters.AddWithValue("@idPedido", pedido.IdPedidos);
                                    finalUpdateCommand.ExecuteNonQuery();
                                }
                            }
                        }
                    }

                    // Navegar a DetalleHojaUbicaciones
                    await Navigation.PushAsync(new DetalleHojaunbicaciones(pedido.IdPedidos, null));
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