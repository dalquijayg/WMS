using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WMS_Movil.Servicio;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    // Graba el IdTarima en una tarjeta NFC (borrando lo que tuviera), guarda el UID
    // de la tarjeta en TarimasInventario.IUD y, si la tarima aún no estaba
    // entregada, la entrega en la BD. El pendiente se guarda en el
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
            PedidoLabel.Text = pendiente.NoHoja > 0
                ? $"Pedido #{pendiente.IdPedido} - Hoja #{pendiente.NoHoja}"
                : $"Pedido #{pendiente.IdPedido}";
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

        // Pendientes que quedaron de una sesión anterior (la app se cerró a medias): los
        // guardados en el teléfono y las tarimas del usuario entregadas sin tarjeta en la BD.
        public static async Task ProcesarPendientesAsync(INavigation navegacion)
        {
            if (_procesandoPendientes) return;
            var nfc = DependencyService.Get<INfcService>();
            if (nfc == null || !nfc.TieneNfc) return;

            _procesandoPendientes = true;
            try
            {
                var pendientes = TarjetasNfcPendientes.Leer();
                try
                {
                    var sinTarjeta = await Task.Run(() => LeerTarimasEntregadasSinTarjeta(App.IdUsuarioActual));
                    pendientes.AddRange(sinTarjeta.Where(t => !pendientes.Any(p => p.IdTarima == t.IdTarima)));
                }
                catch (Exception ex)
                {
                    // Sin conexión: se revisa de nuevo la próxima vez que se entre a Home
                    System.Diagnostics.Debug.WriteLine($"Error leyendo tarimas sin tarjeta: {ex.Message}");
                }

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
                    // La tarjeta solo se puede reutilizar cuando su tarima terminó el rechequeo
                    // (EstadoChequeo = 1) y no identifica a una tarima de embalaje abierta.
                    // Se busca por el IdTarima grabado y por el UID.
                    string ocupada;
                    try
                    {
                        int? idTarimaGrabada = int.TryParse(tarjeta.Texto, out int id) ? id : (int?)null;
                        ocupada = await Task.Run(() =>
                            DescribirUsoDeLaTarjeta(idTarimaGrabada, tarjeta.Uid, _pendiente.IdTarima));
                    }
                    catch (Exception ex)
                    {
                        MostrarError("No se pudo verificar la tarjeta: " + ex.Message + "\nVuelva a acercarla para reintentar.");
                        continue;
                    }
                    if (ocupada != null)
                    {
                        MostrarError(ocupada + " Use otra tarjeta.");
                        continue;
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

                    MostrarEstado("⏳", _pendiente.YaEntregada ? "Registrando tarjeta…" : "Entregando tarima…");
                    try
                    {
                        await Task.Run(() => RegistrarTarjetaYEntregar(tarjeta.Uid));
                    }
                    catch (Exception ex)
                    {
                        MostrarError((_pendiente.YaEntregada
                                ? "La tarjeta quedó grabada, pero no se pudo registrar en la tarima: "
                                : "La tarjeta quedó grabada, pero no se pudo entregar la tarima: ") + ex.Message +
                            "\nVuelva a acercar la tarjeta para reintentar.");
                        MostrarEstado("📶", "Acerque la tarjeta al teléfono y manténgala quieta");
                        continue;
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

        // Guarda el UID de la tarjeta en la tarima (TarimasInventario.IUD) y, si aún
        // no estaba entregada, la entrega en la misma transacción.
        private void RegistrarTarjetaYEntregar(string uid)
        {
            using (var connection = new MySqlConnection(BuildConnectionString()))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    using (var cmd = new MySqlCommand(
                        "UPDATE TarimasInventario SET IUD = @Uid WHERE IdTarima = @IdTarima", connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@Uid", uid);
                        cmd.Parameters.AddWithValue("@IdTarima", _pendiente.IdTarima);
                        cmd.ExecuteNonQuery();
                    }

                    if (!_pendiente.YaEntregada)
                        RepartoTarimas.EntregarTarima(connection, transaction, _pendiente.IdTarima);

                    transaction.Commit();
                }
            }
        }

        // Tarimas del usuario ya entregadas (FechaFinalizacion) que no tienen tarjeta (IUD vacío)
        // y no han terminado el rechequeo (EstadoChequeo <> 1). NoHoja es la primera hoja con productos en la tarima (0 si
        // solo tiene productos repartidos).
        private static List<PendienteNfc> LeerTarimasEntregadasSinTarjeta(int idUsuario)
        {
            var lista = new List<PendienteNfc>();
            using (var connection = new MySqlConnection(BuildConnectionString()))
            {
                connection.Open();
                using (var cmd = new MySqlCommand(@"
                    SELECT ti.IdTarima, ti.NoTarima, ti.IdPedido,
                           COALESCE((SELECT MIN(d.NoHoja) FROM detallepedidostienda_bodega d
                                     WHERE d.IdConsolidado = ti.IdPedido AND d.NoTarima = ti.NoTarima), 0) AS NoHoja
                    FROM TarimasInventario ti
                    WHERE ti.IdUsuario = @IdUsuario
                      AND ti.FechaFinalizacion IS NOT NULL
                      AND (ti.IUD IS NULL OR ti.IUD = '')
                      AND COALESCE(ti.EstadoChequeo, 0) <> 1
                    ORDER BY ti.IdPedido, ti.NoTarima", connection))
                {
                    cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            lista.Add(new PendienteNfc
                            {
                                IdTarima = Convert.ToInt32(r["IdTarima"]),
                                NoTarima = Convert.ToInt32(r["NoTarima"]),
                                IdPedido = Convert.ToInt32(r["IdPedido"]),
                                NoHoja = Convert.ToInt32(r["NoHoja"]),
                                YaEntregada = true,
                                PermiteCancelar = false
                            });
                        }
                    }
                }
            }
            return lista;
        }

        // Por qué la tarjeta no está disponible, o null si lo está. Está ocupada si:
        // - otra tarima de preparación la usa (grabada con su IdTarima o con su UID en IUD) y no ha
        //   terminado el rechequeo (EstadoChequeo <> 1), o
        // - identifica a una tarima de embalaje que aún no se cierra (Estado 1).
        private static string DescribirUsoDeLaTarjeta(int? idTarimaGrabada, string uid, int idTarimaActual)
        {
            using (var connection = new MySqlConnection(BuildConnectionString()))
            {
                connection.Open();

                var embalaje = EmbalajeTarimas.BuscarAbiertaPorTarjeta(connection, null, uid, idTarimaGrabada);
                if (embalaje != null)
                    return $"Esta tarjeta es de la tarima de embalaje {embalaje.Codigo}, que aún no se cierra.";

                using (var cmd = new MySqlCommand(@"
                    SELECT IdPedido, NoTarima, IdInventario FROM TarimasInventario
                    WHERE IdTarima <> @IdTarimaActual
                      AND (IdTarima = @IdTarimaGrabada OR (@Uid <> '' AND IUD = @Uid))
                      AND COALESCE(EstadoChequeo, 0) <> 1
                    ORDER BY IdTarima DESC
                    LIMIT 1", connection))
                {
                    cmd.Parameters.AddWithValue("@IdTarimaActual", idTarimaActual);
                    cmd.Parameters.AddWithValue("@IdTarimaGrabada", (object)idTarimaGrabada ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Uid", uid ?? "");
                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read()) return null;
                        string inventario = r.IsDBNull(r.GetOrdinal("IdInventario"))
                            ? "sin inventario"
                            : $"Inventario #{Convert.ToInt32(r["IdInventario"])}";
                        return $"Esta tarjeta pertenece a la Tarima #{Convert.ToInt32(r["NoTarima"])} del Pedido " +
                               $"#{Convert.ToInt32(r["IdPedido"])} ({inventario}), que aún no termina el rechequeo.";
                    }
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
            // Xamarin no siempre vuelve a medir la tarjeta al mostrar/ocultar el error
            ContenidoStack.ForceLayout();
            TarjetaFrame.ForceLayout();
            if (!string.IsNullOrEmpty(mensaje))
                try { Vibration.Vibrate(400); } catch { }
        }
    }
}
