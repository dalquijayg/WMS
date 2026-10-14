using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil.Reportes
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MisChequeos : ContentPage
    {
        public class ChequeoRealizado
        {
            public int IdPedido { get; set; }
            public int NoTarima { get; set; }
            public int CantidadSkus { get; set; }
            public int CantidadFardos { get; set; }
            public DateTime FechaHoraInicio { get; set; }
            public DateTime? FechaHoraFin { get; set; }
            public int IdUsuarioChequeo { get; set; }
            public int IdInventario { get; set; }
            public string NombreEmpresa { get; set; }

            // Propiedades calculadas para UI
            public string TiempoTranscurrido
            {
                get
                {
                    if (FechaHoraFin.HasValue)
                    {
                        var duracion = FechaHoraFin.Value - FechaHoraInicio;
                        if (duracion.TotalHours >= 1)
                            return $"{duracion.Hours}h {duracion.Minutes}m";
                        else
                            return $"{duracion.Minutes}m";
                    }
                    return "En proceso";
                }
            }

            public string HoraInicio => FechaHoraInicio.ToString("HH:mm");
            public string HoraFin => FechaHoraFin?.ToString("HH:mm") ?? "--:--";
        }

        public MisChequeos()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            CargarChequeos();
        }

        private async void CargarChequeos()
        {
            LoadingOverlay.IsVisible = true;

            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    // Obtener fecha actual para filtrar solo del día de hoy
                    DateTime hoy = DateTime.Today;
                    DateTime inicioDia = hoy;
                    DateTime finDia = hoy.AddDays(1);

                    using (var command = new MySqlCommand(@"
                        SELECT
                            TarimasInventario.IdPedido, 
                            TarimasInventario.NoTarima, 
                            TarimasInventario.CantidadSkus, 
                            TarimasInventario.CantidadFardos, 
                            TarimasInventario.FechaHoraInicio, 
                            TarimasInventario.FechaHoraFin, 
                            TarimasInventario.IdUsuarioChequeo, 
                            TarimasInventario.IdInventario, 
                            pedidostienda_bodega.NombreEmpresa
                        FROM TarimasInventario
                        INNER JOIN pedidostienda_bodega
                            ON TarimasInventario.IdPedido = pedidostienda_bodega.IdPedidos
                        WHERE TarimasInventario.IdUsuarioChequeo = @IdUsuario
                        AND TarimasInventario.FechaHoraInicio >= @InicioDia
                        AND TarimasInventario.FechaHoraInicio < @FinDia
                        ORDER BY TarimasInventario.FechaHoraInicio DESC", connection))
                    {
                        command.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                        command.Parameters.AddWithValue("@InicioDia", inicioDia);
                        command.Parameters.AddWithValue("@FinDia", finDia);

                        var chequeos = new List<ChequeoRealizado>();
                        int totalTarimas = 0;
                        int totalSkus = 0;
                        int totalFardos = 0;
                        var inventariosUnicos = new HashSet<int>();

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var chequeo = new ChequeoRealizado
                                {
                                    IdPedido = reader.GetInt32("IdPedido"),
                                    NoTarima = reader.GetInt32("NoTarima"),
                                    CantidadSkus = reader.GetInt32("CantidadSkus"),
                                    CantidadFardos = reader.GetInt32("CantidadFardos"),
                                    FechaHoraInicio = reader.GetDateTime("FechaHoraInicio"),
                                    FechaHoraFin = reader.IsDBNull(reader.GetOrdinal("FechaHoraFin"))
                                        ? null
                                        : (DateTime?)reader.GetDateTime("FechaHoraFin"),
                                    IdUsuarioChequeo = reader.GetInt32("IdUsuarioChequeo"),
                                    IdInventario = reader.GetInt32("IdInventario"),
                                    NombreEmpresa = reader.GetString("NombreEmpresa")
                                };

                                chequeos.Add(chequeo);
                                totalTarimas++;
                                totalSkus += chequeo.CantidadSkus;
                                totalFardos += chequeo.CantidadFardos;
                                inventariosUnicos.Add(chequeo.IdInventario);
                            }
                        }

                        Device.BeginInvokeOnMainThread(() =>
                        {
                            SubtitleLabel.Text = $"Chequeos del día {hoy:dd/MM/yyyy}";

                            if (chequeos.Any())
                            {
                                ChequeosCollection.ItemsSource = chequeos;
                                TotalLabel.Text = totalTarimas.ToString();
                                SkusLabel.Text = totalSkus.ToString();
                                FardosLabel.Text = totalFardos.ToString();
                                InventariosLabel.Text = inventariosUnicos.Count.ToString();
                                EmptyFrame.IsVisible = false;
                            }
                            else
                            {
                                EmptyFrame.IsVisible = true;
                                TotalLabel.Text = "0";
                                SkusLabel.Text = "0";
                                FardosLabel.Text = "0";
                                InventariosLabel.Text = "0";
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar chequeos: " + ex.Message, "OK");
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
            CargarChequeos();
        }

        private async void OnChequeoSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is ChequeoRealizado chequeo)
            {
                try
                {
                    await Navigation.PushAsync(new DetalleMisChequeos(
                        chequeo.IdPedido,
                        chequeo.NoTarima,
                        chequeo.IdInventario));
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", "Error al abrir detalle: " + ex.Message, "OK");
                }
                finally
                {
                    ChequeosCollection.SelectedItem = null;
                }
            }
        }
    }
}