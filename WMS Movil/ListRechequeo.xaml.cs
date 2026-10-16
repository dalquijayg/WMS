using MySqlConnector;
using System;
using System.Threading;
using System.Threading.Tasks;
using WMS_Movil.Servicio;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    // Rechequeo: se lee la tarjeta NFC de la tarima (trae grabado su IdTarima) y se abre
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
        private CancellationTokenSource _nfcCancelacion;

        public ListRechequeo()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _nfcCancelacion?.Cancel();
            _nfcCancelacion = new CancellationTokenSource();
            _ = EscucharTarjetasAsync(_nfcCancelacion.Token);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _nfcCancelacion?.Cancel();
            _nfcCancelacion = null;
            DependencyService.Get<INfcService>()?.Detener();
        }

        // Mientras la pantalla está visible espera tarjetas NFC; cada tarjeta trae grabado el IdTarima.
        private async Task EscucharTarjetasAsync(CancellationToken cancelacion)
        {
            var nfc = DependencyService.Get<INfcService>();
            if (nfc == null || !nfc.TieneNfc)
            {
                MostrarEstado("⚠️", "Este teléfono no tiene NFC", "No se pueden leer las tarjetas de las tarimas.");
                return;
            }

            while (!cancelacion.IsCancellationRequested)
            {
                var espera = nfc.EsperarTarjetaAsync(cancelacion);

                // Mientras se espera, avisar si el NFC está apagado
                while (!espera.IsCompleted)
                {
                    bool activado = nfc.NfcActivado;
                    NfcActivarButton.IsVisible = !activado;
                    if (activado)
                        MostrarEstado("📶", "Esperando tarjeta…", "Acerca la tarjeta a la parte trasera del teléfono");
                    else
                        MostrarEstado("⚠️", "El NFC está apagado", "Actívalo para poder leer las tarjetas.");
                    await Task.WhenAny(espera, Task.Delay(1500));
                }

                TarjetaNfc tarjeta;
                try
                {
                    tarjeta = await espera;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (NfcException ex)
                {
                    await DisplayAlert("Error", ex.Message, "OK");
                    continue;
                }

                if (!tarjeta.Compatible)
                {
                    await DisplayAlert("Tarjeta no compatible",
                        $"Este teléfono no puede leer los datos de esta tarjeta ({tarjeta.Tipo}, UID {tarjeta.Uid}).\n\n" +
                        "Las MIFARE Classic solo se leen en teléfonos con chip NFC de NXP.", "OK");
                    continue;
                }

                if (!int.TryParse(tarjeta.Texto, out int idTarima) || idTarima <= 0)
                {
                    await DisplayAlert("Tarjeta sin tarima",
                        $"Esta tarjeta no tiene una tarima asignada.\n\nUID: {tarjeta.Uid}", "OK");
                    continue;
                }

                MostrarEstado("✅", $"Tarima Id {idTarima}", $"UID: {tarjeta.Uid}");
                await BuscarTarima(idTarima);
            }
        }

        private void MostrarEstado(string icono, string estado, string detalle)
        {
            NfcIconoLabel.Text = icono;
            NfcIconoFrame.BackgroundColor = Color.FromHex(icono == "✅" ? "#DCFCE7" : icono == "⚠️" ? "#FEF3C7" : "#E0E7FF");
            NfcEstadoLabel.Text = estado;
            NfcDetalleLabel.Text = detalle;
        }

        private void OnNfcActivarClicked(object sender, EventArgs e) =>
            DependencyService.Get<INfcService>()?.AbrirAjustesNfc();

        private string BuildConnectionString() =>
            $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

        private async void OnBackClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        private async Task BuscarTarima(int idTarima)
        {
            if (_buscando) return;

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
                    await DisplayAlert("No encontrada", $"No existe la tarima Id {idTarima} grabada en la tarjeta.", "OK");
                    return;
                }

                string nombreTarima = $"Tarima #{tarima.NoTarima} del Pedido #{tarima.IdPedido}";

                if (pendiente != null)
                {
                    await DisplayAlert("Rechequeo pendiente",
                        $"Tienes sin terminar la Tarima #{pendiente.NoTarima} del Pedido #{pendiente.IdPedido}.\n\n" +
                        "Debes finalizarla antes de rechequear otra tarima.", "OK");
                    return;
                }

                if (tarima.EnPreparacion)
                {
                    await DisplayAlert("En preparación",
                        $"La {nombreTarima} aún está en preparación. Se podrá rechequear cuando sea entregada.", "OK");
                    return;
                }

                if (tarima.YaRechequeada)
                {
                    await DisplayAlert("Tarima ya rechequeada",
                        $"La {nombreTarima} ya fue verificada completamente.", "OK");
                    return;
                }

                if (tarima.IdUsuarioChequeo.HasValue && tarima.IdUsuarioChequeo.Value != idUsuario)
                {
                    await DisplayAlert("Tarima bloqueada",
                        $"La {nombreTarima} está siendo rechequeada por {tarima.NombreUsuarioChequeo}.", "OK");
                    return;
                }

                if (tarima.Productos == 0)
                {
                    await DisplayAlert("Tarima vacía",
                        $"La {nombreTarima} no tiene productos asignados.", "OK");
                    return;
                }

                await Navigation.PushAsync(new DetalleHojarechequeo(tarima.IdPedido, tarima.NoTarima));
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al buscar la tarima: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
                _buscando = false;
            }
        }
    }
}
