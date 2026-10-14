using MySqlConnector;
using System;
using System.Threading.Tasks;
using WMS_Movil.Servicio;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    // Rechequeo: se escanea el código de barras de la tarima (su IdTarima) y se abre
    // directo el detalle, sin pasar por el número de pedido.
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ListRechequeo : ContentPage
    {
        private class TarimaEscaneada
        {
            public int IdPedido { get; set; }
            public int NoTarima { get; set; }
            public bool EnPreparacion { get; set; }
            public bool YaRechequeada { get; set; }
            public int? IdUsuarioChequeo { get; set; }
            public string NombreUsuarioChequeo { get; set; }
            public int Productos { get; set; }
        }

        private class TarimaPendiente
        {
            public int IdPedido { get; set; }
            public int NoTarima { get; set; }
        }

        private bool _buscando = false;

        public ListRechequeo()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            TarimaCodigoEntry.Text = string.Empty;
            Device.BeginInvokeOnMainThread(() => TarimaCodigoEntry.Focus());
        }

        private string BuildConnectionString() =>
            $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

        private async void OnBackClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        private async void OnTarimaEscaneada(object sender, EventArgs e)
        {
            await BuscarTarima();
        }

        private async void OnBuscarTarimaClicked(object sender, EventArgs e)
        {
            await BuscarTarima();
        }

        private async Task BuscarTarima()
        {
            if (_buscando) return;

            var texto = TarimaCodigoEntry.Text?.Trim();

            if (string.IsNullOrEmpty(texto))
            {
                await DisplayAlert("Atención", "Escanea el código de barras de la tarima.", "OK");
                ReiniciarEscaneo();
                return;
            }

            if (!int.TryParse(texto, out int idTarima) || idTarima <= 0)
            {
                await DisplayAlert("Código inválido", "El código de la tarima debe ser numérico.", "OK");
                ReiniciarEscaneo();
                return;
            }

            _buscando = true;
            LoadingOverlay.IsVisible = true;
            try
            {
                TarimaEscaneada tarima = null;
                TarimaPendiente pendiente = null;
                int idUsuario = App.IdUsuarioActual;

                await Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();

                        using (var cmd = new MySqlCommand(@"
                            SELECT ti.IdPedido, ti.NoTarima, ti.FechaFinalizacion,
                                   ti.IdUsuarioChequeo, ti.FechaHoraFin,
                                   COALESCE(ti.EstadoChequeo, 0) AS EstadoChequeo,
                                   u.NombreCompleto AS NombreUsuarioChequeo
                            FROM TarimasInventario ti
                            LEFT JOIN usuarios u ON ti.IdUsuarioChequeo = u.Id
                            WHERE ti.IdTarima = @IdTarima", connection))
                        {
                            cmd.Parameters.AddWithValue("@IdTarima", idTarima);
                            using (var r = cmd.ExecuteReader())
                            {
                                if (r.Read())
                                {
                                    tarima = new TarimaEscaneada
                                    {
                                        IdPedido = Convert.ToInt32(r["IdPedido"]),
                                        NoTarima = Convert.ToInt32(r["NoTarima"]),
                                        EnPreparacion = r.IsDBNull(r.GetOrdinal("FechaFinalizacion")),
                                        YaRechequeada = !r.IsDBNull(r.GetOrdinal("FechaHoraFin"))
                                                        || Convert.ToInt32(r["EstadoChequeo"]) == 1,
                                        IdUsuarioChequeo = r.IsDBNull(r.GetOrdinal("IdUsuarioChequeo"))
                                            ? (int?)null : Convert.ToInt32(r["IdUsuarioChequeo"]),
                                        NombreUsuarioChequeo = r.IsDBNull(r.GetOrdinal("NombreUsuarioChequeo"))
                                            ? "otro usuario" : r.GetString("NombreUsuarioChequeo")
                                    };
                                }
                            }
                        }

                        if (tarima == null) return;

                        // Rechequeo que este usuario empezó y no ha terminado, en cualquier pedido
                        using (var cmd = new MySqlCommand(@"
                            SELECT IdPedido, NoTarima
                            FROM TarimasInventario
                            WHERE IdUsuarioChequeo = @IdUsuario
                              AND FechaHoraFin IS NULL
                              AND IdTarima <> @IdTarima
                            ORDER BY FechaHoraInicio
                            LIMIT 1", connection))
                        {
                            cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                            cmd.Parameters.AddWithValue("@IdTarima", idTarima);
                            using (var r = cmd.ExecuteReader())
                            {
                                if (r.Read())
                                {
                                    pendiente = new TarimaPendiente
                                    {
                                        IdPedido = Convert.ToInt32(r["IdPedido"]),
                                        NoTarima = Convert.ToInt32(r["NoTarima"])
                                    };
                                }
                            }
                        }

                        // Productos normales + partes de productos repartidos
                        tarima.Productos = RepartoTarimas.ContarProductosEnTarima(
                            connection, null, tarima.IdPedido, tarima.NoTarima);
                    }
                });

                if (tarima == null)
                {
                    await DisplayAlert("No encontrada", $"No existe una tarima con el código {idTarima}.", "OK");
                    ReiniciarEscaneo();
                    return;
                }

                string nombreTarima = $"Tarima #{tarima.NoTarima} del Pedido #{tarima.IdPedido}";

                if (pendiente != null)
                {
                    await DisplayAlert("Rechequeo pendiente",
                        $"Tienes sin terminar la Tarima #{pendiente.NoTarima} del Pedido #{pendiente.IdPedido}.\n\n" +
                        "Debes finalizarla antes de rechequear otra tarima.", "OK");
                    ReiniciarEscaneo();
                    return;
                }

                if (tarima.EnPreparacion)
                {
                    await DisplayAlert("En preparación",
                        $"La {nombreTarima} aún está en preparación. Se podrá rechequear cuando sea entregada.", "OK");
                    ReiniciarEscaneo();
                    return;
                }

                if (tarima.YaRechequeada)
                {
                    await DisplayAlert("Tarima ya rechequeada",
                        $"La {nombreTarima} ya fue verificada completamente.", "OK");
                    ReiniciarEscaneo();
                    return;
                }

                if (tarima.IdUsuarioChequeo.HasValue && tarima.IdUsuarioChequeo.Value != idUsuario)
                {
                    await DisplayAlert("Tarima bloqueada",
                        $"La {nombreTarima} está siendo rechequeada por {tarima.NombreUsuarioChequeo}.", "OK");
                    ReiniciarEscaneo();
                    return;
                }

                if (tarima.Productos == 0)
                {
                    await DisplayAlert("Tarima vacía",
                        $"La {nombreTarima} no tiene productos asignados.", "OK");
                    ReiniciarEscaneo();
                    return;
                }

                TarimaCodigoEntry.Text = string.Empty;
                await Navigation.PushAsync(new DetalleHojarechequeo(tarima.IdPedido, tarima.NoTarima));
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al buscar la tarima: " + ex.Message, "OK");
                ReiniciarEscaneo();
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
                _buscando = false;
            }
        }

        private void ReiniciarEscaneo()
        {
            TarimaCodigoEntry.Text = string.Empty;
            Device.BeginInvokeOnMainThread(() => TarimaCodigoEntry.Focus());
        }
    }
}
