using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using WMS_Movil.Servicio;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil.Reportes
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class DetalleMisChequeos : ContentPage
    {
        private readonly int idPedido;
        private readonly int noTarima;
        private readonly int idInventario;

        public class ProductoChequeo
        {
            public string UPC { get; set; }
            public string Descripcion { get; set; }
            public int CantidadSolicitada { get; set; }
            public int CantidadPreparada { get; set; }
            public int CantidadInventario { get; set; }
            public int UnidadesFardo { get; set; }

            // Propiedad calculada: Fardos = CantidadInventario / UnidadesFardo
            public decimal CantidadFardos
            {
                get
                {
                    if (UnidadesFardo > 0)
                        return (decimal)CantidadInventario / UnidadesFardo;
                    return 0;
                }
            }
        }

        public DetalleMisChequeos(int idPedido, int noTarima, int idInventario)
        {
            InitializeComponent();
            this.idPedido = idPedido;
            this.noTarima = noTarima;
            this.idInventario = idInventario;

            SubtituloLabel.Text = $"Pedido #{idPedido} - Tarima {noTarima}";
            InventarioLabel.Text = $"Inventario #{idInventario}";
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            CargarDetalle();
        }

        private async void CargarDetalle()
        {
            LoadingOverlay.IsVisible = true;

            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    // Datos anteriores: un inventario por hoja compartido entre varias tarimas.
                    // Ahora cada tarima tiene su propio inventario con solo lo que lleva.
                    bool inventarioCompartido;
                    using (var cmd = new MySqlCommand(
                        "SELECT COUNT(*) FROM TarimasInventario WHERE IdInventario = @IdInventario", connection))
                    {
                        cmd.Parameters.AddWithValue("@IdInventario", idInventario);
                        inventarioCompartido = Convert.ToInt32(cmd.ExecuteScalar()) > 1;
                    }

                    using (var command = new MySqlCommand(@"
                        SELECT DISTINCT
                            detallepedidostienda_bodega.UPC, 
                            detallepedidostienda_bodega.Descripcion, 
                            detallepedidostienda_bodega.Cantidad AS CantidadSolicitada, 
                            detallepedidostienda_bodega.CantConfirmada AS CantidadPreparada, 
                            COALESCE(detalleinventarios.Cantidad, 0) AS CantidadInventario,
                            COALESCE(detalleinventarios.UnidadesFardo, 1) AS UnidadesFardo,
                            detallepedidostienda_bodega.NoTarima,
                            detallepedidostienda_bodega.Observaciones
                        FROM detallepedidostienda_bodega
                        LEFT JOIN detalleinventarios
                            ON detalleinventarios.IdInventarios = @IdInventario
                            AND (detalleinventarios.Upc = detallepedidostienda_bodega.UPC
                                 OR detalleinventarios.DUN14 = detallepedidostienda_bodega.UPC)
                        WHERE detallepedidostienda_bodega.IdConsolidado = @IdPedido
                        AND (detallepedidostienda_bodega.NoTarima = @NoTarima
                             OR (detallepedidostienda_bodega.NoTarima = 0
                                 AND detallepedidostienda_bodega.CantConfirmada > 0
                                 AND detallepedidostienda_bodega.Observaciones LIKE '[TARIMAS:%'))
                        ORDER BY detallepedidostienda_bodega.Descripcion", connection))
                    {
                        command.Parameters.AddWithValue("@IdPedido", idPedido);
                        command.Parameters.AddWithValue("@NoTarima", noTarima);
                        command.Parameters.AddWithValue("@IdInventario", idInventario);

                        var productos = new List<ProductoChequeo>();
                        int totalProductos = 0;
                        decimal totalFardos = 0;
                        var skusUnicos = new HashSet<string>();

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var producto = new ProductoChequeo
                                {
                                    UPC = reader.GetString("UPC"),
                                    Descripcion = reader.GetString("Descripcion"),
                                    CantidadSolicitada = reader.GetInt32("CantidadSolicitada"),
                                    CantidadPreparada = reader.GetInt32("CantidadPreparada"),
                                    CantidadInventario = reader.GetInt32("CantidadInventario"),
                                    UnidadesFardo = reader.GetInt32("UnidadesFardo")
                                };

                                // Producto repartido en varias tarimas: solo cuenta su parte en esta tarima
                                string observaciones = reader.IsDBNull(reader.GetOrdinal("Observaciones"))
                                    ? "" : reader.GetString("Observaciones");
                                bool esRepartido = !reader.IsDBNull(reader.GetOrdinal("NoTarima"))
                                    && Convert.ToInt32(reader["NoTarima"]) == 0
                                    && RepartoTarimas.TieneReparto(observaciones);
                                if (esRepartido)
                                {
                                    int cantidadAqui = RepartoTarimas.Leer(observaciones)
                                        .Where(p => p.NoTarima == noTarima).Sum(p => p.Cantidad);
                                    if (cantidadAqui <= 0) continue;

                                    // Inventario compartido: registra el total del producto; se toma la proporción de esta tarima
                                    if (inventarioCompartido && producto.CantidadPreparada > 0)
                                        producto.CantidadInventario = producto.CantidadInventario * cantidadAqui / producto.CantidadPreparada;
                                    // Solicitada queda con lo que pidieron; preparada es lo de esta tarima
                                    producto.CantidadPreparada = cantidadAqui;
                                }

                                productos.Add(producto);
                                totalProductos++;
                                totalFardos += producto.CantidadFardos;
                                skusUnicos.Add(producto.UPC);
                            }
                        }

                        Device.BeginInvokeOnMainThread(() =>
                        {
                            ProductosCollection.ItemsSource = productos;
                            TotalLabel.Text = totalProductos.ToString();
                            SkusLabel.Text = skusUnicos.Count.ToString();
                            FardosLabel.Text = totalFardos.ToString("F2");
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar detalle: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        private async void OnBackClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}