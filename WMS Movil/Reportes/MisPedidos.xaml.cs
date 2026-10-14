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
    // Historial del usuario por tarima: cada tarima tiene su propio inventario
    // y puede llevar productos de varias hojas del mismo pedido.
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MisPedidos : ContentPage
    {
        public class TarimaRealizada
        {
            public int IdTarima { get; set; }
            public int IdPedido { get; set; }
            public int NoTarima { get; set; }
            public string NombreEmpresa { get; set; }
            public DateTime FechaHoraInicio { get; set; }
            public DateTime? FechaHoraFinalizo { get; set; }
            public int TotalSkus { get; set; }
            public int TotalFardos { get; set; }
            public int? IdInventario { get; set; }
            public string Hojas { get; set; }

            // Propiedades calculadas para UI
            public bool EstaEntregada => FechaHoraFinalizo.HasValue;
            public string TextoEstado => EstaEntregada ? "✓ Entregada" : "En uso";
            public Color ColorEstado => Color.FromHex(EstaEntregada ? "#48BB78" : "#D97706");
            public string TextoInventario => IdInventario.HasValue ? $"Inventario #{IdInventario.Value}" : "Sin inventario";
            public string TextoHojas => string.IsNullOrEmpty(Hojas) ? "Sin productos" : $"Hojas: {Hojas}";

            public string TiempoTranscurrido
            {
                get
                {
                    if (FechaHoraFinalizo.HasValue)
                    {
                        var duracion = FechaHoraFinalizo.Value - FechaHoraInicio;
                        if (duracion.TotalHours >= 1)
                            return $"{(int)duracion.TotalHours}h {duracion.Minutes}m";
                        else
                            return $"{duracion.Minutes}m";
                    }
                    return "En proceso";
                }
            }

            public string HoraInicio => FechaHoraInicio.ToString("HH:mm");
            public string HoraFin => FechaHoraFinalizo?.ToString("HH:mm") ?? "--:--";
        }

        public MisPedidos()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            CargarTarimas();
        }

        private async void CargarTarimas()
        {
            LoadingOverlay.IsVisible = true;

            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

                DateTime hoy = DateTime.Today;
                DateTime inicioBusqueda = hoy.AddDays(-2);
                DateTime finDia = hoy.AddDays(1);
                var tarimas = new List<TarimaRealizada>();

                await System.Threading.Tasks.Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(connectionString))
                    {
                        connection.Open();

                        using (var command = new MySqlCommand(@"
                            SELECT
                                ti.IdTarima,
                                ti.IdPedido,
                                ti.NoTarima,
                                ti.FechaCreacion,
                                ti.FechaFinalizacion,
                                COALESCE(ti.CantidadSkus, 0) AS CantidadSkus,
                                COALESCE(ti.CantidadFardos, 0) AS CantidadFardos,
                                ti.IdInventario,
                                COALESCE(p.NombreEmpresa, '') AS NombreEmpresa,
                                COALESCE((
                                    SELECT GROUP_CONCAT(DISTINCT CAST(d.NoHoja AS CHAR) ORDER BY d.NoHoja SEPARATOR ', ')
                                    FROM detallepedidostienda_bodega d
                                    WHERE d.IdConsolidado = ti.IdPedido
                                      AND (d.NoTarima = ti.NoTarima
                                           OR (d.NoTarima = 0 AND d.Observaciones REGEXP
                                               CONCAT('^\\[TARIMAS:([0-9]+=[0-9]+;)*', ti.NoTarima, '=')))
                                ), '') AS Hojas
                            FROM TarimasInventario ti
                            LEFT JOIN pedidostienda_bodega p ON ti.IdPedido = p.IdPedidos
                            WHERE ti.IdUsuario = @IdUsuario
                              AND ti.FechaCreacion >= @InicioBusqueda
                              AND ti.FechaCreacion < @FinDia
                            ORDER BY ti.FechaCreacion DESC, ti.NoTarima DESC", connection))
                        {
                            command.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                            command.Parameters.AddWithValue("@InicioBusqueda", inicioBusqueda);
                            command.Parameters.AddWithValue("@FinDia", finDia);

                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    tarimas.Add(new TarimaRealizada
                                    {
                                        IdTarima = reader.GetInt32("IdTarima"),
                                        IdPedido = reader.GetInt32("IdPedido"),
                                        NoTarima = reader.GetInt32("NoTarima"),
                                        NombreEmpresa = reader.GetString("NombreEmpresa"),
                                        FechaHoraInicio = reader.GetDateTime("FechaCreacion"),
                                        FechaHoraFinalizo = reader.IsDBNull(reader.GetOrdinal("FechaFinalizacion"))
                                            ? null
                                            : (DateTime?)reader.GetDateTime("FechaFinalizacion"),
                                        TotalSkus = Convert.ToInt32(reader["CantidadSkus"]),
                                        TotalFardos = Convert.ToInt32(reader["CantidadFardos"]),
                                        IdInventario = reader.IsDBNull(reader.GetOrdinal("IdInventario"))
                                            ? (int?)null : reader.GetInt32("IdInventario"),
                                        Hojas = Convert.ToString(reader["Hojas"])
                                    });
                                }
                            }
                        }

                        // Las tarimas en uso aún no tienen totales guardados: se calculan al momento
                        foreach (var t in tarimas.Where(t => !t.EstaEntregada))
                        {
                            var (skus, fardos) = RepartoTarimas.ContarSkusYFardos(connection, null, t.IdPedido, t.NoTarima);
                            t.TotalSkus = skus;
                            t.TotalFardos = fardos;
                        }
                    }
                });

                Device.BeginInvokeOnMainThread(() =>
                {
                    SubtitleLabel.Text = $"Tarimas del {inicioBusqueda:dd/MM/yyyy} al {hoy:dd/MM/yyyy}";

                    PedidosCollection.ItemsSource = tarimas;
                    EmptyFrame.IsVisible = !tarimas.Any();
                    TotalLabel.Text = tarimas.Count.ToString();
                    HojasLabel.Text = tarimas.Sum(t => t.TotalSkus).ToString();
                    ProductosLabel.Text = tarimas.Sum(t => t.TotalFardos).ToString();
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar tarimas: " + ex.Message, "OK");
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

        private void OnRefreshClicked(object sender, EventArgs e)
        {
            CargarTarimas();
        }

        private async void OnPedidoSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is TarimaRealizada tarima)
            {
                try
                {
                    await Navigation.PushAsync(new DetalleMisPedidos(tarima.IdPedido, tarima.NoTarima, tarima.IdInventario));
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", "Error al abrir detalle: " + ex.Message, "OK");
                }
                finally
                {
                    PedidosCollection.SelectedItem = null;
                }
            }
        }
    }
}
