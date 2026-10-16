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
    // Contenido de una hoja: sus productos con el inventario de la hoja y la
    // tarima (o tarimas, si es repartido) en que quedó cada uno.
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class DetalleMisPedidos : ContentPage
    {
        private readonly int idPedido;
        private readonly int noHoja;

        public class ProductoDetalle
        {
            public string UPC { get; set; }
            public string Descripcion { get; set; }
            public int Cantidad { get; set; }
            public int CantConfirmada { get; set; }
            public string NombreCompleto { get; set; }
            public DateTime? Fechahorapreparo { get; set; }
            public string DescripcionMotivo { get; set; }
            public bool EsRepartido { get; set; }

            // Tarima donde quedó y, si es repartido, el reparto
            public string TextoTarima { get; set; }
            public bool TieneTarima => !string.IsNullOrEmpty(TextoTarima);

            // Propiedades calculadas
            public string FechahorapreparoFormateada => Fechahorapreparo?.ToString("dd/MM/yyyy HH:mm") ?? "--";

            public bool EstaCompleto => CantConfirmada == Cantidad && Cantidad > 0;
            public bool SinPreparar => CantConfirmada == 0;

            public string TextoEstado
            {
                get
                {
                    if (EsRepartido) return "Repartido";
                    if (SinPreparar) return "Sin preparar";
                    if (EstaCompleto) return "Completo";
                    return "Diferencia";
                }
            }

            public Color ColorEstado
            {
                get
                {
                    if (EsRepartido) return Color.FromHex("#0891B2");
                    if (SinPreparar) return Color.FromHex("#E53E3E");
                    if (EstaCompleto) return Color.FromHex("#48BB78");
                    return Color.FromHex("#ECC94B");
                }
            }

            public Color ColorBorde => ColorEstado;
        }

        public DetalleMisPedidos(int idPedido, int noHoja, int? idInventario)
        {
            InitializeComponent();
            this.idPedido = idPedido;
            this.noHoja = noHoja;

            TituloLabel.Text = "Detalle de Hoja";
            SubtituloLabel.Text = $"Pedido #{idPedido} - Hoja {noHoja}";
            TarimasResumenLabel.Text = idInventario.HasValue ? $"📦 Inventario #{idInventario.Value}" : "📦 Sin inventario";
            TarimasResumenLabel.IsVisible = true;
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

                    using (var command = new MySqlCommand(@"
                        SELECT
                            d.UPC,
                            d.Descripcion,
                            d.Cantidad,
                            COALESCE(d.CantConfirmada, 0) AS CantConfirmada,
                            COALESCE(u.NombreCompleto, '') AS NombreCompleto,
                            d.Fechahorapreparo,
                            COALESCE(m.DescripcionMotivo, '') AS DescripcionMotivo,
                            COALESCE(d.NoTarima, 0) AS NoTarima,
                            d.Observaciones
                        FROM detallepedidostienda_bodega d
                        LEFT JOIN usuarios u ON d.IdUsuariopreparo = u.Id
                        LEFT JOIN TiposMotivosPendiente m ON d.EstadoPreparacionproducto = m.Idpreparacion
                        WHERE d.IdConsolidado = @IdPedido
                          AND d.NoHoja = @NoHoja
                        ORDER BY d.Descripcion", connection))
                    {
                        command.Parameters.AddWithValue("@IdPedido", idPedido);
                        command.Parameters.AddWithValue("@NoHoja", noHoja);

                        var productos = new List<ProductoDetalle>();
                        var tarimas = new SortedSet<int>();
                        int totalFardos = 0;

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var producto = new ProductoDetalle
                                {
                                    UPC = reader.GetString("UPC"),
                                    Descripcion = reader.GetString("Descripcion"),
                                    Cantidad = reader.GetInt32("Cantidad"),
                                    CantConfirmada = Convert.ToInt32(reader["CantConfirmada"]),
                                    NombreCompleto = reader.GetString("NombreCompleto"),
                                    Fechahorapreparo = reader.IsDBNull(reader.GetOrdinal("Fechahorapreparo"))
                                        ? (DateTime?)null : reader.GetDateTime("Fechahorapreparo"),
                                    DescripcionMotivo = reader.GetString("DescripcionMotivo")
                                };

                                int noTarima = Convert.ToInt32(reader["NoTarima"]);
                                string observaciones = reader.IsDBNull(reader.GetOrdinal("Observaciones"))
                                    ? "" : reader.GetString("Observaciones");
                                var partes = noTarima == 0 ? RepartoTarimas.Leer(observaciones) : null;

                                if (partes != null && partes.Any())
                                {
                                    producto.EsRepartido = true;
                                    producto.TextoTarima = "Repartido: " +
                                        string.Join(", ", partes.Select(p => $"T{p.NoTarima} → {p.Cantidad}"));
                                    foreach (var p in partes) tarimas.Add(p.NoTarima);
                                }
                                else if (noTarima > 0 && producto.CantConfirmada > 0)
                                {
                                    producto.TextoTarima = $"Tarima {noTarima}";
                                    tarimas.Add(noTarima);
                                }

                                productos.Add(producto);
                                totalFardos += producto.CantConfirmada;
                            }
                        }

                        Device.BeginInvokeOnMainThread(() =>
                        {
                            ProductosCollection.ItemsSource = productos;
                            TotalLabel.Text = productos.Count.ToString();
                            PreparadosLabel.Text = totalFardos.ToString();
                            PendientesLabel.Text = tarimas.Count.ToString();
                            if (tarimas.Any())
                                TarimasResumenLabel.Text += $"  ·  Tarimas: {string.Join(", ", tarimas)}";
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
