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
    // Contenido de una tarima: productos normales con ese NoTarima y la parte
    // de los productos repartidos en varias tarimas que quedó en ella.
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class DetalleMisPedidos : ContentPage
    {
        private readonly int idPedido;
        private readonly int noTarima;

        public class ProductoDetalle
        {
            public string UPC { get; set; }
            public string Descripcion { get; set; }
            public int Cantidad { get; set; }
            // En esta tarima (para un repartido, solo su parte)
            public int CantConfirmada { get; set; }
            public string NombreCompleto { get; set; }
            public DateTime? Fechahorapreparo { get; set; }
            public string DescripcionMotivo { get; set; }
            public bool EsRepartido { get; set; }

            // Hoja de origen y, si es repartido, en qué tarimas quedó
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

        public DetalleMisPedidos(int idPedido, int noTarima, int? idInventario)
        {
            InitializeComponent();
            this.idPedido = idPedido;
            this.noTarima = noTarima;

            TituloLabel.Text = "Detalle de Tarima";
            SubtituloLabel.Text = $"Pedido #{idPedido} - Tarima {noTarima}";
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
                            d.NoHoja,
                            d.Observaciones
                        FROM detallepedidostienda_bodega d
                        LEFT JOIN usuarios u ON d.IdUsuariopreparo = u.Id
                        LEFT JOIN TiposMotivosPendiente m ON d.EstadoPreparacionproducto = m.Idpreparacion
                        WHERE d.IdConsolidado = @IdPedido
                          AND (d.NoTarima = @NoTarima
                               OR (d.NoTarima = 0 AND d.CantConfirmada > 0
                                   AND d.Observaciones LIKE '[TARIMAS:%'))
                        ORDER BY d.Descripcion", connection))
                    {
                        command.Parameters.AddWithValue("@IdPedido", idPedido);
                        command.Parameters.AddWithValue("@NoTarima", noTarima);

                        var productos = new List<ProductoDetalle>();
                        var hojas = new SortedSet<int>();
                        int totalFardos = 0;

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int noHoja = Convert.ToInt32(reader["NoHoja"]);
                                var producto = new ProductoDetalle
                                {
                                    UPC = reader.GetString("UPC"),
                                    Descripcion = reader.GetString("Descripcion"),
                                    Cantidad = reader.GetInt32("Cantidad"),
                                    CantConfirmada = Convert.ToInt32(reader["CantConfirmada"]),
                                    NombreCompleto = reader.GetString("NombreCompleto"),
                                    Fechahorapreparo = reader.IsDBNull(reader.GetOrdinal("Fechahorapreparo"))
                                        ? (DateTime?)null : reader.GetDateTime("Fechahorapreparo"),
                                    DescripcionMotivo = reader.GetString("DescripcionMotivo"),
                                    TextoTarima = $"Hoja {noHoja}"
                                };

                                string observaciones = reader.IsDBNull(reader.GetOrdinal("Observaciones"))
                                    ? "" : reader.GetString("Observaciones");
                                if (Convert.ToInt32(reader["NoTarima"]) == 0)
                                {
                                    // Repartido: solo cuenta la parte que quedó en esta tarima
                                    var partes = RepartoTarimas.Leer(observaciones);
                                    int cantidadAqui = partes.Where(p => p.NoTarima == noTarima).Sum(p => p.Cantidad);
                                    if (cantidadAqui <= 0) continue;

                                    producto.EsRepartido = true;
                                    producto.CantConfirmada = cantidadAqui;
                                    producto.TextoTarima = $"Hoja {noHoja}  ·  Repartido: " +
                                        string.Join(", ", partes.Select(p => $"T{p.NoTarima} → {p.Cantidad}"));
                                }

                                productos.Add(producto);
                                hojas.Add(noHoja);
                                totalFardos += producto.CantConfirmada;
                            }
                        }

                        Device.BeginInvokeOnMainThread(() =>
                        {
                            ProductosCollection.ItemsSource = productos;
                            TotalLabel.Text = productos.Count.ToString();
                            PreparadosLabel.Text = totalFardos.ToString();
                            PendientesLabel.Text = hojas.Count.ToString();
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
