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
    // Historial del usuario por hoja: cada hoja tiene su propio inventario
    // y puede ocupar varias tarimas.
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MisPedidos : ContentPage
    {
        public class HojaRealizada
        {
            public int IdPedido { get; set; }
            public int NoHoja { get; set; }
            public string NombreEmpresa { get; set; }
            public DateTime? FechaHoraInicio { get; set; }
            public DateTime? FechaHoraFinalizo { get; set; }
            public int TotalSkus { get; set; }
            public int TotalFardos { get; set; }
            public int? IdInventario { get; set; }
            public SortedSet<int> Tarimas { get; } = new SortedSet<int>();

            // Propiedades calculadas para UI
            public bool EstaFinalizada => FechaHoraFinalizo.HasValue;
            public string TextoEstado => EstaFinalizada ? "✓ Finalizada" : "En proceso";
            public Color ColorEstado => Color.FromHex(EstaFinalizada ? "#48BB78" : "#D97706");
            public string TextoInventario => IdInventario.HasValue ? $"Inventario #{IdInventario.Value}" : "Sin inventario";
            public string TextoTarimas => Tarimas.Count == 0 ? "Sin tarimas" : $"Tarimas: {string.Join(", ", Tarimas)}";

            public string TiempoTranscurrido
            {
                get
                {
                    if (FechaHoraInicio.HasValue && FechaHoraFinalizo.HasValue)
                    {
                        var duracion = FechaHoraFinalizo.Value - FechaHoraInicio.Value;
                        if (duracion.TotalHours >= 1)
                            return $"{(int)duracion.TotalHours}h {duracion.Minutes}m";
                        else
                            return $"{duracion.Minutes}m";
                    }
                    return "En proceso";
                }
            }

            public string HoraInicio => FechaHoraInicio?.ToString("HH:mm") ?? "--:--";
            public string HoraFin => FechaHoraFinalizo?.ToString("HH:mm") ?? "--:--";
        }

        public MisPedidos()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            CargarHojas();
        }

        private async void CargarHojas()
        {
            LoadingOverlay.IsVisible = true;

            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

                DateTime hoy = DateTime.Today;
                DateTime inicioBusqueda = hoy.AddDays(-2);
                DateTime finDia = hoy.AddDays(1);
                var hojas = new List<HojaRealizada>();

                await System.Threading.Tasks.Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(connectionString))
                    {
                        connection.Open();

                        using (var command = new MySqlCommand(@"
                            SELECT
                                pp.IdPedido,
                                pp.NoHoja,
                                pp.FechaHoraInicio,
                                pp.FechaHorafinalizo,
                                pp.IdInventario,
                                COALESCE(p.NombreEmpresa, '') AS NombreEmpresa
                            FROM PreparacionPedidos pp
                            LEFT JOIN pedidostienda_bodega p ON pp.IdPedido = p.IdPedidos
                            WHERE pp.IdUsuario = @IdUsuario
                              AND pp.FechaHoraInicio >= @InicioBusqueda
                              AND pp.FechaHoraInicio < @FinDia
                            ORDER BY pp.FechaHoraInicio DESC, pp.NoHoja DESC", connection))
                        {
                            command.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                            command.Parameters.AddWithValue("@InicioBusqueda", inicioBusqueda);
                            command.Parameters.AddWithValue("@FinDia", finDia);

                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    int? idInventario = reader.IsDBNull(reader.GetOrdinal("IdInventario"))
                                        ? (int?)null : Convert.ToInt32(reader["IdInventario"]);
                                    hojas.Add(new HojaRealizada
                                    {
                                        IdPedido = Convert.ToInt32(reader["IdPedido"]),
                                        NoHoja = Convert.ToInt32(reader["NoHoja"]),
                                        NombreEmpresa = reader.GetString("NombreEmpresa"),
                                        FechaHoraInicio = reader.IsDBNull(reader.GetOrdinal("FechaHoraInicio"))
                                            ? (DateTime?)null : reader.GetDateTime("FechaHoraInicio"),
                                        FechaHoraFinalizo = reader.IsDBNull(reader.GetOrdinal("FechaHorafinalizo"))
                                            ? (DateTime?)null : reader.GetDateTime("FechaHorafinalizo"),
                                        IdInventario = idInventario > 0 ? idInventario : null
                                    });
                                }
                            }
                        }

                        if (!hojas.Any()) return;

                        // Lo preparado en cada hoja y en qué tarimas quedó
                        var pedidos = hojas.Select(h => h.IdPedido).Distinct().ToList();
                        var porHoja = hojas.ToDictionary(h => (h.IdPedido, h.NoHoja));
                        using (var command = new MySqlCommand($@"
                            SELECT IdConsolidado, NoHoja, COALESCE(NoTarima, 0) AS NoTarima,
                                   COALESCE(CantConfirmada, 0) AS CantConfirmada, Observaciones
                            FROM detallepedidostienda_bodega
                            WHERE IdConsolidado IN ({string.Join(",", pedidos.Select((_, i) => $"@p{i}"))})
                              AND COALESCE(CantConfirmada, 0) > 0", connection))
                        {
                            for (int i = 0; i < pedidos.Count; i++)
                                command.Parameters.AddWithValue($"@p{i}", pedidos[i]);

                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    var clave = (Convert.ToInt32(reader["IdConsolidado"]), Convert.ToInt32(reader["NoHoja"]));
                                    if (!porHoja.TryGetValue(clave, out var hoja)) continue;

                                    hoja.TotalSkus++;
                                    hoja.TotalFardos += Convert.ToInt32(reader["CantConfirmada"]);

                                    int noTarima = Convert.ToInt32(reader["NoTarima"]);
                                    if (noTarima > 0)
                                        hoja.Tarimas.Add(noTarima);
                                    else
                                    {
                                        string observaciones = reader.IsDBNull(reader.GetOrdinal("Observaciones"))
                                            ? "" : reader.GetString("Observaciones");
                                        foreach (var parte in RepartoTarimas.Leer(observaciones))
                                            hoja.Tarimas.Add(parte.NoTarima);
                                    }
                                }
                            }
                        }
                    }
                });

                Device.BeginInvokeOnMainThread(() =>
                {
                    SubtitleLabel.Text = $"Hojas del {inicioBusqueda:dd/MM/yyyy} al {hoy:dd/MM/yyyy}";

                    PedidosCollection.ItemsSource = hojas;
                    EmptyFrame.IsVisible = !hojas.Any();
                    TotalLabel.Text = hojas.Count.ToString();
                    HojasLabel.Text = hojas.Sum(h => h.TotalSkus).ToString();
                    ProductosLabel.Text = hojas.Sum(h => h.TotalFardos).ToString();
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar hojas: " + ex.Message, "OK");
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
            CargarHojas();
        }

        private async void OnPedidoSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is HojaRealizada hoja)
            {
                try
                {
                    await Navigation.PushAsync(new DetalleMisPedidos(hoja.IdPedido, hoja.NoHoja, hoja.IdInventario));
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
