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
    // Graba el IdTarima en una tarjeta NFC (borrando lo que tuviera) y, si la tarima
    // aún no estaba entregada, la entrega en la BD. El pendiente se guarda en el
    // teléfono: si se cierra la app, Home vuelve a mostrar esta página.
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ConfigurarTarjetaNfcPage : ContentPage
    {
        private readonly PendienteNfc _pendiente;
        private readonly TaskCompletionSource<bool> _resultado = new TaskCompletionSource<bool>();
        private readonly CancellationTokenSource _cancelacion = new CancellationTokenSource();
        private bool _iniciada;
        private bool _grabando;

        private static bool _procesandoPendientes;

        private ConfigurarTarjetaNfcPage(PendienteNfc pendiente, string progreso)
        {
            InitializeComponent();
            _pendiente = pendiente;

            TarimaTituloLabel.Text = pendiente.YaEntregada ? "TARIMA ENTREGADA" : "TARIMA A ENTREGAR";
            TarimaLabel.Text = $"Tarima #{pendiente.NoTarima}  ·  Id {pendiente.IdTarima}";
            PedidoLabel.Text = $"Pedido #{pendiente.IdPedido} - Hoja #{pendiente.NoHoja}";
            ProgresoLabel.Text = progreso ?? "";
            ProgresoLabel.IsVisible = !string.IsNullOrEmpty(progreso);
            CancelarButton.IsVisible = pendiente.PermiteCancelar && !pendiente.YaEntregada;
            MostrarEstado("📶", "Acerque la tarjeta al teléfono y manténgala quieta");
        }

        // Muestra la página y espera: true si la tarjeta quedó configurada (y la tarima
        // entregada); false si se canceló la entrega o el teléfono no tiene NFC.
        public static async Task<bool> ConfigurarAsync(INavigation navegacion, PendienteNfc pendiente, string progreso = null)
        {
            var nfc = DependencyService.Get<INfcService>();
            if (nfc == null || !nfc.TieneNfc)
                return false;

            TarjetasNfcPendientes.Agregar(pendiente);
            var pagina = new ConfigurarTarjetaNfcPage(pendiente, progreso);
            await navegacion.PushModalAsync(pagina, false);
            bool configurada = await pagina._resultado.Task;
            await navegacion.PopModalAsync(false);
            return configurada;
        }

        // Pendientes que quedaron de una sesión anterior (la app se cerró a medias).
        public static async Task ProcesarPendientesAsync(INavigation navegacion)
        {
            if (_procesandoPendientes) return;
            var nfc = DependencyService.Get<INfcService>();
            if (nfc == null || !nfc.TieneNfc) return;

            _procesandoPendientes = true;
            try
            {
                var pendientes = TarjetasNfcPendientes.Leer();
                for (int i = 0; i < pendientes.Count; i++)
                    await ConfigurarAsync(navegacion, pendientes[i],
                        pendientes.Count > 1 ? $"Tarjeta {i + 1} de {pendientes.Count}" : null);
            }
            finally
            {
                _procesandoPendientes = false;
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (_iniciada) return;
            _iniciada = true;
            _ = EsperarTarjetaAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _cancelacion.Cancel();
            DependencyService.Get<INfcService>()?.Detener();
        }

        // Obligatorio: Atrás no cierra la página
        protected override bool OnBackButtonPressed() => true;

        private async Task EsperarTarjetaAsync()
        {
            var nfc = DependencyService.Get<INfcService>();

            while (!_cancelacion.IsCancellationRequested)
            {
                var espera = nfc.EsperarTarjetaAsync(_cancelacion.Token);

                // Mientras se espera, avisar si apagan el NFC
                while (!espera.IsCompleted)
                {
                    bool activado = nfc.NfcActivado;
                    ActivarButton.IsVisible = !activado;
                    if (!activado)
                        MostrarEstado("⚠️", "El NFC está apagado. Actívelo para continuar.");
                    else if (IconoLabel.Text == "⚠️")
                        MostrarEstado("📶", "Acerque la tarjeta al teléfono y manténgala quieta");
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
                    MostrarError(ex.Message);
                    continue;
                }

                if (!tarjeta.Compatible)
                {
                    MostrarError($"Este teléfono no puede grabar datos en esta tarjeta ({tarjeta.Tipo}, UID {tarjeta.Uid}).\n" +
                        "Las MIFARE Classic solo se graban en teléfonos con chip NFC de NXP; use otro teléfono o tarjetas NTAG213/215.");
                    continue;
                }

                _grabando = true;
                CancelarButton.IsEnabled = false;
                try
                {
                    // Si la tarjeta es de otra tarima que aún no se rechequea, no se puede reutilizar
                    if (int.TryParse(tarjeta.Texto, out int idTarimaAnterior) && idTarimaAnterior != _pendiente.IdTarima)
                    {
                        string ocupada = await Task.Run(() => DescribirTarimaSinRechequear(idTarimaAnterior));
                        if (ocupada != null)
                        {
                            MostrarError($"Esta tarjeta pertenece a la {ocupada}, que aún no se rechequea. Use otra tarjeta.");
                            continue;
                        }
                    }

                    MostrarEstado("⏳", "Grabando… no retire la tarjeta");
                    try
                    {
                        await nfc.EscribirTextoAsync(tarjeta, _pendiente.IdTarima.ToString());
                    }
                    catch (NfcException ex)
                    {
                        MostrarError(ex.Message);
                        MostrarEstado("📶", "Acerque la tarjeta al teléfono y manténgala quieta");
                        continue;
                    }

                    if (!_pendiente.YaEntregada)
                    {
                        MostrarEstado("⏳", "Entregando tarima…");
                        try
                        {
                            await Task.Run(EntregarTarima);
                        }
                        catch (Exception ex)
                        {
                            MostrarError("La tarjeta quedó grabada, pero no se pudo entregar la tarima: " + ex.Message +
                                "\nVuelva a acercar la tarjeta para reintentar.");
                            MostrarEstado("📶", "Acerque la tarjeta al teléfono y manténgala quieta");
                            continue;
                        }
                    }
                }
                finally
                {
                    _grabando = false;
                    CancelarButton.IsEnabled = true;
                }

                TarjetasNfcPendientes.Quitar(_pendiente.IdTarima);
                MostrarError(null);
                MostrarEstado("✅", $"Tarjeta configurada\nUID: {tarjeta.Uid}");
                CancelarButton.IsVisible = false;
                try { Vibration.Vibrate(150); } catch { }
                await Task.Delay(1800);
                _resultado.TrySetResult(true);
                return;
            }
        }

        private async void OnCancelarClicked(object sender, EventArgs e)
        {
            if (_grabando) return;

            bool cancelar = await DisplayAlert("Cancelar entrega",
                $"La Tarima #{_pendiente.NoTarima} no se entregará y podrá seguir usándola.",
                "Sí, cancelar", "No");
            if (!cancelar || _grabando) return;

            _cancelacion.Cancel();
            TarjetasNfcPendientes.Quitar(_pendiente.IdTarima);
            _resultado.TrySetResult(false);
        }

        private void OnActivarClicked(object sender, EventArgs e) =>
            DependencyService.Get<INfcService>()?.AbrirAjustesNfc();

        private static string BuildConnectionString() =>
            $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

        private void EntregarTarima()
        {
            using (var connection = new MySqlConnection(BuildConnectionString()))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    RepartoTarimas.EntregarTarima(connection, transaction, _pendiente.IdTarima);
                    transaction.Commit();
                }
            }
        }

        // "Tarima #N del Pedido #P" si la tarima existe y no ha terminado su rechequeo; null si no.
        private static string DescribirTarimaSinRechequear(int idTarima)
        {
            using (var connection = new MySqlConnection(BuildConnectionString()))
            {
                connection.Open();
                using (var cmd = new MySqlCommand(@"
                    SELECT IdPedido, NoTarima FROM TarimasInventario
                    WHERE IdTarima = @IdTarima
                      AND FechaHoraFin IS NULL
                      AND COALESCE(EstadoChequeo, 0) <> 1", connection))
                {
                    cmd.Parameters.AddWithValue("@IdTarima", idTarima);
                    using (var r = cmd.ExecuteReader())
                        return r.Read()
                            ? $"Tarima #{Convert.ToInt32(r["NoTarima"])} del Pedido #{Convert.ToInt32(r["IdPedido"])}"
                            : null;
                }
            }
        }

        private void MostrarEstado(string icono, string mensaje)
        {
            IconoLabel.Text = icono;
            IconoFrame.BackgroundColor = Color.FromHex(icono == "✅" ? "#DCFCE7" : icono == "⚠️" ? "#FEF3C7" : "#E0E7FF");
            EstadoLabel.Text = mensaje;
        }

        private void MostrarError(string mensaje)
        {
            ErrorLabel.Text = mensaje ?? "";
            ErrorFrame.IsVisible = !string.IsNullOrEmpty(mensaje);
            if (!string.IsNullOrEmpty(mensaje))
                try { Vibration.Vibrate(400); } catch { }
        }
    }
}
