using MySqlConnector;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WMS_Movil.Servicio;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    // NOTA: StringNotEmptyConverter (usado por el XAML de esta página para la
    // visibilidad de Observaciones) ya está definido en DetallePedidoPendientes.xaml.cs.

    // Atenúa los botones de revisión una vez que el producto ya fue marcado.
    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => (value is bool puedeMarcar && puedeMarcar) ? 1.0 : 0.55;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    // Rechequeo + embalaje: cada producto que se rechequea (✓ o ✗) se pone en la tarima de
    // embalaje escogida (TarimasEmbalaje) en la misma transacción. Ver Servicio/EmbalajeTarimas.
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class DetalleHojarechequeo : ContentPage
    {
        private readonly int idTarima;
        private readonly int idPedido;
        private readonly int numeroTarima;
        private List<DetalleProducto> todosLosProductos = new List<DetalleProducto>();

        private int? idSucursal;
        private string nombreSucursal = "";
        private TarimaEmbalaje _tarimaEmbalaje;   // destino; null = falta tocar su tarjeta
        private bool _destinoInicializado;
        private bool _registrandoNueva;           // la próxima tarjeta registra una tarima nueva
        private bool _procesandoTarjeta;
        private CancellationTokenSource _nfcCancelacion;

        // Lo decide ListRechequeo antes de abrir el detalle (preguntando al usuario si hace falta).
        public enum ModoDestino { Retomar, CrearNueva, UsarCreada }
        private readonly ModoDestino modoDestino;

        // La tarjeta con la que se abrió el detalle puede seguir junto al teléfono y volver a
        // leerse al empezar a escuchar: se ignora durante unos segundos.
        private readonly string uidTarjetaApertura;
        private DateTime _ignorarAperturaHasta;

        // RevisadoOk: null = sin revisar, true = marcado como bien (✓), false = marcado como mal (✗).
        // Se guarda en la tabla TarimasRechequeadas al tocar cualquiera de los dos botones.
        public class DetalleProducto : INotifyPropertyChanged
        {
            public string Upc { get; set; }
            public string UpcUnidad { get; set; }
            public int NoHoja { get; set; }
            public string Descripcion { get; set; }
            public int CantidadSolicitada { get; set; }
            public int CantidadConfirmada { get; set; }
            public decimal CantidadUniFardo { get; set; }
            public string Observaciones { get; set; }
            public int? IdUsuarioPreparo { get; set; }
            public string NombreUsuarioPreparo { get; set; }

            // Solo para productos repartidos en varias tarimas: CantidadConfirmada es lo de esta
            // tarima y el estado se evalúa con el total repartido contra lo solicitado.
            public bool EsRepartido { get; set; }
            public int TotalConfirmadoReparto { get; set; }
            public string DetalleReparto { get; set; }
            public string TextoConfirmado => EsRepartido ? " en esta tarima" : " confirmado";

            private int ConfirmadoParaEstado => EsRepartido ? TotalConfirmadoReparto : CantidadConfirmada;
            public bool CantidadCoincide => ConfirmadoParaEstado == CantidadSolicitada;
            public bool CantidadMenor => ConfirmadoParaEstado < CantidadSolicitada;

            public string ColorEstado => CantidadCoincide ? "#48BB78" : (CantidadMenor ? "#ED8936" : "#E53E3E");
            public string TextoEstado => CantidadCoincide ? "✓ Completo" : (CantidadMenor ? "⚠ Faltante" : "⚠ Excedente");

            private bool? _revisadoOk;
            public bool? RevisadoOk
            {
                get => _revisadoOk;
                set
                {
                    _revisadoOk = value;
                    OnPropertyChanged(nameof(RevisadoOk));
                    OnPropertyChanged(nameof(ColorBotonBien));
                    OnPropertyChanged(nameof(ColorBotonMal));
                    OnPropertyChanged(nameof(ColorIconoBien));
                    OnPropertyChanged(nameof(ColorIconoMal));
                    OnPropertyChanged(nameof(TieneRevision));
                    OnPropertyChanged(nameof(TextoRevision));
                    OnPropertyChanged(nameof(ColorTextoRevision));
                    OnPropertyChanged(nameof(PuedeMarcar));
                }
            }

            // Una vez chequeado (bien o mal), ya no se puede volver a marcar.
            public bool PuedeMarcar => !RevisadoOk.HasValue;

            private int? _cantidadEncontrada;
            public int? CantidadEncontrada
            {
                get => _cantidadEncontrada;
                set { _cantidadEncontrada = value; OnPropertyChanged(nameof(CantidadEncontrada)); OnPropertyChanged(nameof(TextoRevision)); }
            }

            private string _comentario;
            public string Comentario
            {
                get => _comentario;
                set { _comentario = value; OnPropertyChanged(nameof(Comentario)); OnPropertyChanged(nameof(TextoRevision)); }
            }

            public string ColorBotonBien => RevisadoOk == true ? "#48BB78" : "#F0FFF4";
            public string ColorBotonMal => RevisadoOk == false ? "#E53E3E" : "#FFF5F5";
            public string ColorIconoBien => RevisadoOk == true ? "White" : "#48BB78";
            public string ColorIconoMal => RevisadoOk == false ? "White" : "#E53E3E";

            public bool TieneRevision => RevisadoOk.HasValue;
            public string ColorTextoRevision => RevisadoOk == false ? "#E53E3E" : "#38A169";
            public string TextoRevision
            {
                get
                {
                    if (RevisadoOk == true)
                        return $"✓ Chequeado correcto ({CantidadConfirmada} unidades)";
                    if (RevisadoOk == false)
                        return $"✗ Encontrado: {CantidadEncontrada}" +
                               (string.IsNullOrWhiteSpace(Comentario) ? "" : $"  •  {Comentario}");
                    return "";
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string propertyName) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public DetalleHojarechequeo(int idTarima, int idPedido, int numeroTarima,
            ModoDestino modoDestino, string uidTarjetaApertura)
        {
            InitializeComponent();
            this.idTarima = idTarima;
            this.idPedido = idPedido;
            this.numeroTarima = numeroTarima;
            this.modoDestino = modoDestino;
            this.uidTarjetaApertura = uidTarjetaApertura;
            _registrandoNueva = modoDestino == ModoDestino.CrearNueva;

            InfoPedidoLabel.Text = $"Pedido #{idPedido}  •  Tarima #{this.numeroTarima}";
            ActualizarPanelDestino();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _ = CargarInfoYDestinoAsync();
            CargarProductos();

            _ignorarAperturaHasta = DateTime.Now.AddSeconds(3);
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

        private async Task CargarInfoYDestinoAsync()
        {
            await CargarInfoPedido();
            if (_destinoInicializado) return;
            _destinoInicializado = true;
            await InicializarDestinoAsync();
        }

        private string BuildConnectionString() =>
            $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

        private async void OnBackClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        private async Task CargarInfoPedido()
        {
            try
            {
                string nombreEmpresa = "";
                string nombreDepartamento = "";

                await Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();
                        using (var cmd = new MySqlCommand(@"
                            SELECT pb.NombreEmpresa, COALESCE(dep.Nombre, '') AS NombreDepartamento,
                                   pb.IdSucursal, COALESCE(pb.NombreSucursales, '') AS NombreSucursal
                            FROM pedidostienda_bodega pb
                            LEFT JOIN departamentos dep ON pb.Departamento = dep.Id
                            WHERE pb.IdPedidos = @IdPedido
                            LIMIT 1", connection))
                        {
                            cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                            using (var reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    nombreEmpresa = reader.IsDBNull(reader.GetOrdinal("NombreEmpresa"))
                                        ? "" : reader.GetString("NombreEmpresa");
                                    nombreDepartamento = reader.GetString("NombreDepartamento");
                                    idSucursal = reader.IsDBNull(reader.GetOrdinal("IdSucursal"))
                                        ? (int?)null : Convert.ToInt32(reader["IdSucursal"]);
                                    nombreSucursal = reader.GetString("NombreSucursal");
                                }
                            }
                        }
                    }
                });

                Device.BeginInvokeOnMainThread(() =>
                {
                    string texto = string.Join("  •  ", new[] { nombreEmpresa, nombreDepartamento }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));
                    EmpresaDepartamentoLabel.Text = texto;
                    EmpresaDepartamentoLabel.IsVisible = !string.IsNullOrWhiteSpace(texto);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al cargar info del pedido: {ex.Message}");
            }
        }

        private async void CargarProductos()
        {
            LoadingOverlay.IsVisible = true;
            try
            {
                var productos = new List<DetalleProducto>();

                await Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();
                        // Filas de esta tarima + productos repartidos en varias tarimas
                        // (NoTarima = 0 y etiqueta [TARIMAS:...] en Observaciones).
                        using (var cmd = new MySqlCommand(@"
                            SELECT
                                d.UPC,
                                COALESCE(pp.Upc, d.UPC) AS UpcUnidad,
                                COALESCE(pp.Cantidad, 1) AS CantidadUniFardo,
                                d.Descripcion,
                                d.Cantidad AS CantidadSolicitada,
                                COALESCE(d.CantConfirmada, 0) AS CantidadConfirmada,
                                d.Observaciones,
                                d.NoTarima,
                                d.NoHoja,
                                d.IdUsuariopreparo,
                                COALESCE(u.NombreCompleto, '') AS NombreUsuarioPreparo
                            FROM detallepedidostienda_bodega d
                            LEFT JOIN productospaquetes pp ON d.UPC = pp.UPCPaquete
                            LEFT JOIN usuarios u ON d.IdUsuariopreparo = u.Id
                            WHERE d.IdConsolidado = @IdPedido
                              AND (d.NoTarima = @NoTarima
                                   OR (d.NoTarima = 0 AND d.CantConfirmada > 0
                                       AND d.Observaciones LIKE '[TARIMAS:%'))
                            ORDER BY d.Descripcion ASC", connection))
                        {
                            cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                            cmd.Parameters.AddWithValue("@NoTarima", numeroTarima);

                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    var producto = new DetalleProducto
                                    {
                                        Upc = reader.GetString("UPC"),
                                        UpcUnidad = reader.GetString("UpcUnidad"),
                                        NoHoja = Convert.ToInt32(reader["NoHoja"]),
                                        CantidadUniFardo = Convert.ToDecimal(reader["CantidadUniFardo"]),
                                        Descripcion = reader.IsDBNull(reader.GetOrdinal("Descripcion"))
                                            ? "" : reader.GetString("Descripcion"),
                                        CantidadSolicitada = Convert.ToInt32(reader["CantidadSolicitada"]),
                                        CantidadConfirmada = Convert.ToInt32(reader["CantidadConfirmada"]),
                                        Observaciones = reader.IsDBNull(reader.GetOrdinal("Observaciones"))
                                            ? "" : reader.GetString("Observaciones"),
                                        IdUsuarioPreparo = reader.IsDBNull(reader.GetOrdinal("IdUsuariopreparo"))
                                            ? (int?)null : Convert.ToInt32(reader["IdUsuariopreparo"]),
                                        NombreUsuarioPreparo = reader.GetString("NombreUsuarioPreparo")
                                    };

                                    bool esRepartido = !reader.IsDBNull(reader.GetOrdinal("NoTarima"))
                                        && Convert.ToInt32(reader["NoTarima"]) == 0
                                        && RepartoTarimas.TieneReparto(producto.Observaciones);

                                    if (esRepartido)
                                    {
                                        // En esta tarima solo se revisa su parte del reparto
                                        var reparto = new ProductoRepartido
                                        {
                                            CantidadSolicitada = producto.CantidadSolicitada,
                                            Partes = RepartoTarimas.Leer(producto.Observaciones)
                                        };
                                        int cantidadAqui = reparto.CantidadEn(numeroTarima);
                                        if (cantidadAqui <= 0) continue;

                                        // Solicitado = lo que pidieron; confirmado = lo de esta tarima
                                        producto.EsRepartido = true;
                                        producto.CantidadConfirmada = cantidadAqui;
                                        producto.TotalConfirmadoReparto = reparto.TotalRepartido;
                                        producto.DetalleReparto = reparto.ResumenPara(numeroTarima);
                                        producto.Observaciones = RepartoTarimas.ObservacionOriginal(producto.Observaciones);
                                    }

                                    productos.Add(producto);
                                }
                            }
                        }

                        // Traer los rechequeos ya guardados para esta tarima, para que al
                        // reabrir la pantalla se respete lo que ya se marcó antes.
                        using (var cmdRevisados = new MySqlCommand(@"
                            SELECT UpcPaquete, CantidadChequeada, Observacion
                            FROM TarimasRechequeadas
                            WHERE IdPedido = @IdPedido AND NoTarima = @NoTarima", connection))
                        {
                            cmdRevisados.Parameters.AddWithValue("@IdPedido", idPedido);
                            cmdRevisados.Parameters.AddWithValue("@NoTarima", numeroTarima);

                            using (var reader = cmdRevisados.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    string upc = reader.GetString("UpcPaquete");
                                    var producto = productos.FirstOrDefault(p => p.Upc == upc);
                                    if (producto == null) continue;

                                    int cantidadChequeada = Convert.ToInt32(reader["CantidadChequeada"]);
                                    string observacion = reader.IsDBNull(reader.GetOrdinal("Observacion"))
                                        ? "" : reader.GetString("Observacion");

                                    producto.CantidadEncontrada = cantidadChequeada;
                                    producto.Comentario = observacion;
                                    producto.RevisadoOk = cantidadChequeada == producto.CantidadConfirmada
                                        && string.IsNullOrWhiteSpace(observacion);
                                }
                            }
                        }
                    }
                });

                Device.BeginInvokeOnMainThread(() =>
                {
                    todosLosProductos = productos;
                    AplicarFiltro();
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar productos: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        // ══════════════════════════════════════
        // BÚSQUEDA
        // No modifica nada en la base de datos: solo filtra la lista visible.
        // ══════════════════════════════════════

        private void OnFiltroCambiado(object sender, TextChangedEventArgs e)
        {
            AplicarFiltro();
        }

        private void OnLimpiarFiltroTapped(object sender, EventArgs e)
        {
            UpcBuscarEntry.Text = string.Empty;
        }

        private void AplicarFiltro()
        {
            string upcTexto = UpcBuscarEntry.Text?.Trim() ?? string.Empty;

            // Solo se listan los productos que faltan por rechequear: los ya
            // confirmados (✓ o ✗) desaparecen del listado.
            var pendientes = todosLosProductos.Where(p => !p.RevisadoOk.HasValue).ToList();

            var filtrados = string.IsNullOrEmpty(upcTexto)
                ? pendientes
                : pendientes.Where(p => p.Upc.Contains(upcTexto)).ToList();

            ProductosCollection.ItemsSource = filtrados;
            bool sinProductos = filtrados.Count == 0;
            ProductosCollection.IsVisible = !sinProductos;
            SinProductosFrame.IsVisible = sinProductos;
            TotalProductosLabel.Text = $"{pendientes.Count} pendiente{(pendientes.Count == 1 ? "" : "s")} de {todosLosProductos.Count}";

            if (todosLosProductos.Count == 0)
                SinProductosLabel.Text = "Esta tarima no tiene productos registrados";
            else if (pendientes.Count == 0)
                SinProductosLabel.Text = "Todos los productos de esta tarima ya fueron rechequeados";
            else
                SinProductosLabel.Text = "Ningún producto pendiente coincide con ese UPC";

            LimpiarFiltroLabel.IsVisible = !string.IsNullOrEmpty(upcTexto);
        }

        // Tras confirmar un producto: limpia el UPC (vuelve a mostrar toda la lista)
        // y deja el cursor listo para el siguiente escaneo.
        private void ReiniciarEscaneo()
        {
            UpcBuscarEntry.Text = string.Empty;
            AplicarFiltro(); // si el UPC ya estaba vacío, TextChanged no se dispara
            Device.BeginInvokeOnMainThread(() => UpcBuscarEntry.Focus());
        }

        // ══════════════════════════════════════
        // BOTONES DE REVISIÓN (✓ bien / ✗ mal)
        // Cada toque guarda (o actualiza) una fila en TarimasRechequeadas.
        // ══════════════════════════════════════

        private async void OnMarcarBienTapped(object sender, EventArgs e)
        {
            if (!((sender as Element)?.BindingContext is DetalleProducto producto)) return;
            if (producto.RevisadoOk.HasValue) return; // ya fue chequeado, no se puede volver a marcar

            LoadingOverlay.IsVisible = true;
            try
            {
                if (!await GuardarRechequeo(producto, producto.CantidadConfirmada, "")) return;
                producto.CantidadEncontrada = producto.CantidadConfirmada;
                producto.Comentario = "";
                producto.RevisadoOk = true;
                if (await FinalizarTarimaSiEstaCompleta())
                {
                    await RegresarAEscanearTarima();
                    return;
                }
                ReiniciarEscaneo();
            }
            catch (TarimaBloqueadaException ex)
            {
                LoadingOverlay.IsVisible = false;
                await DisplayAlert("Tarima bloqueada",
                    $"La Tarima #{numeroTarima} del Pedido #{idPedido} está siendo rechequeada por {ex.Message}. No se guardó el producto.", "OK");
                await Navigation.PopAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al guardar el rechequeo: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        private async void OnMarcarMalTapped(object sender, EventArgs e)
        {
            if (!((sender as Element)?.BindingContext is DetalleProducto producto)) return;
            if (producto.RevisadoOk.HasValue) return; // ya fue chequeado, no se puede volver a marcar

            string resultadoCantidad = await DisplayPromptAsync(
                "Cantidad encontrada",
                $"{producto.Descripcion}\n\nCantidad preparada: {producto.CantidadConfirmada}",
                accept: "Siguiente",
                cancel: "Cancelar",
                placeholder: "Ingresa la cantidad física encontrada",
                initialValue: producto.CantidadEncontrada?.ToString() ?? "",
                keyboard: Keyboard.Numeric);

            if (resultadoCantidad == null) return; // canceló

            if (!int.TryParse(resultadoCantidad.Trim(), out int cantidadEncontrada) || cantidadEncontrada < 0)
            {
                await DisplayAlert("Cantidad inválida", "Ingresa un número válido.", "OK");
                return;
            }

            string comentario = await DisplayPromptAsync(
                "Comentario",
                "Describe brevemente qué encontraste (opcional)",
                accept: "Guardar",
                cancel: "Cancelar",
                placeholder: "Ej. faltaban 2 unidades, empaque dañado...",
                initialValue: producto.Comentario ?? "");

            if (comentario == null) return; // canceló

            LoadingOverlay.IsVisible = true;
            try
            {
                if (!await GuardarRechequeo(producto, cantidadEncontrada, comentario.Trim())) return;
                producto.CantidadEncontrada = cantidadEncontrada;
                producto.Comentario = comentario.Trim();
                producto.RevisadoOk = false;
                if (await FinalizarTarimaSiEstaCompleta())
                {
                    await RegresarAEscanearTarima();
                    return;
                }
                ReiniciarEscaneo();
            }
            catch (TarimaBloqueadaException ex)
            {
                LoadingOverlay.IsVisible = false;
                await DisplayAlert("Tarima bloqueada",
                    $"La Tarima #{numeroTarima} del Pedido #{idPedido} está siendo rechequeada por {ex.Message}. No se guardó el producto.", "OK");
                await Navigation.PopAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al guardar el rechequeo: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        // ══════════════════════════════════════
        // TarimasInventario: inicio y fin del rechequeo de la tarima
        // ══════════════════════════════════════

        // La tarima ya la tiene otro usuario: no se guarda nada.
        private class TarimaBloqueadaException : Exception
        {
            public TarimaBloqueadaException(string nombreUsuario) : base(nombreUsuario) { }
        }

        // Se llama antes de guardar cualquier marca. Toma la tarima para este usuario
        // si nadie la tiene (registrando quién empezó y cuándo) y comprueba que siga
        // siendo suya; si la tiene otro usuario lanza TarimaBloqueadaException.
        private async Task TomarTarimaParaRechequeo()
        {
            int idUsuarioActual = App.IdUsuarioActual;

            await Task.Run(() =>
            {
                using (var connection = new MySqlConnection(BuildConnectionString()))
                {
                    connection.Open();

                    // Un solo UPDATE: si dos usuarios marcan a la vez, solo uno se queda la tarima.
                    using (var cmd = new MySqlCommand(@"
                        UPDATE TarimasInventario
                        SET IdUsuarioChequeo = @IdUsuarioChequeo,
                            FechaHoraInicio = COALESCE(FechaHoraInicio, NOW())
                        WHERE IdPedido = @IdPedido AND NoTarima = @NoTarima
                          AND (IdUsuarioChequeo IS NULL OR IdUsuarioChequeo = @IdUsuarioChequeo)", connection))
                    {
                        cmd.Parameters.AddWithValue("@IdUsuarioChequeo", idUsuarioActual);
                        cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                        cmd.Parameters.AddWithValue("@NoTarima", numeroTarima);
                        cmd.ExecuteNonQuery();
                    }

                    using (var cmd = new MySqlCommand(@"
                        SELECT ti.IdUsuarioChequeo, COALESCE(u.NombreCompleto, 'otro usuario') AS NombreUsuarioChequeo
                        FROM TarimasInventario ti
                        LEFT JOIN usuarios u ON ti.IdUsuarioChequeo = u.Id
                        WHERE ti.IdPedido = @IdPedido AND ti.NoTarima = @NoTarima
                        LIMIT 1", connection))
                    {
                        cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                        cmd.Parameters.AddWithValue("@NoTarima", numeroTarima);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read()
                                && !reader.IsDBNull(reader.GetOrdinal("IdUsuarioChequeo"))
                                && Convert.ToInt32(reader["IdUsuarioChequeo"]) != idUsuarioActual)
                            {
                                throw new TarimaBloqueadaException(reader.GetString("NombreUsuarioChequeo"));
                            }
                        }
                    }
                }
            });
        }

        // Se llama tras guardar cualquier marca. Si ya no queda ningún producto
        // sin revisar (✓ o ✗) en la tarima, registra la fecha/hora de fin.
        // Devuelve true si la tarima quedó finalizada.
        private async Task<bool> FinalizarTarimaSiEstaCompleta()
        {
            bool todosRevisados = todosLosProductos.Count > 0
                && todosLosProductos.All(p => p.RevisadoOk.HasValue);

            if (!todosRevisados) return false;

            await Task.Run(() =>
            {
                using (var connection = new MySqlConnection(BuildConnectionString()))
                {
                    connection.Open();
                    using (var cmd = new MySqlCommand(@"
                        UPDATE TarimasInventario
                        SET FechaHoraFin = NOW(), EstadoChequeo = 1
                        WHERE IdPedido = @IdPedido AND NoTarima = @NoTarima
                          AND FechaHoraFin IS NULL", connection))
                    {
                        cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                        cmd.Parameters.AddWithValue("@NoTarima", numeroTarima);
                        cmd.ExecuteNonQuery();
                    }
                }
            });

            return true;
        }

        // Tarima terminada: avisa y vuelve a la pantalla anterior para escanear la siguiente.
        private async Task RegresarAEscanearTarima()
        {
            AplicarFiltro();
            LoadingOverlay.IsVisible = false;
            await DisplayAlert("Tarima rechequeada",
                $"Se completó el rechequeo de la Tarima #{numeroTarima} del Pedido #{idPedido}.", "OK");
            await Navigation.PopAsync();
        }

        // ══════════════════════════════════════
        // TARIMA DE EMBALAJE (destino)
        // El destino se escoge tocando la tarjeta de la tarima de embalaje. Con "+ Nueva" la
        // tarjeta que se toque queda registrada como la de una tarima de embalaje nueva.
        // ══════════════════════════════════════

        private void ActualizarPanelDestino()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                if (_registrandoNueva)
                {
                    DestinoCodigoLabel.Text = "Registrando tarima nueva";
                    DestinoDetalleLabel.Text = "📶 Toque la tarjeta de la tarima que será la nueva tarima de embalaje";
                    NuevaDestinoButton.Text = "Cancelar";
                    CerrarDestinoButton.IsVisible = false;
                }
                else if (_tarimaEmbalaje != null)
                {
                    DestinoCodigoLabel.Text = $"Tarima {_tarimaEmbalaje.Codigo}";
                    DestinoDetalleLabel.Text = string.Join("  •  ", new[]
                    {
                        nombreSucursal,
                        $"{_tarimaEmbalaje.CantidadSkus} SKU",
                        $"{_tarimaEmbalaje.CantidadFardos} fardos"
                    }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    NuevaDestinoButton.Text = "+ Nueva";
                    CerrarDestinoButton.IsVisible = true;
                }
                else
                {
                    DestinoCodigoLabel.Text = "Sin tarima de embalaje";
                    DestinoDetalleLabel.Text = "📶 Toque la tarjeta de la tarima donde pondrá los productos, o \"+ Nueva\" para registrar una";
                    NuevaDestinoButton.Text = "+ Nueva";
                    CerrarDestinoButton.IsVisible = false;
                }
            });
        }

        // Al entrar: la pregunta "crear nueva / usar una creada" ya se hizo en ListRechequeo.
        // Solo al retomar se carga la última tarima de embalaje (aún abierta) donde este usuario
        // puso productos de esta tarima de preparación.
        private async Task InicializarDestinoAsync()
        {
            if (modoDestino != ModoDestino.Retomar) return;
            try
            {
                int idUsuario = App.IdUsuarioActual;
                var ultima = await Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();
                        return EmbalajeTarimas.LeerUltimaDelUsuario(connection, idTarima, idUsuario);
                    }
                });

                if (_tarimaEmbalaje == null && ultima != null && ultima.IdSucursal == idSucursal)
                {
                    _tarimaEmbalaje = ultima;
                    ActualizarPanelDestino();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error cargando la tarima de embalaje: {ex.Message}");
            }
        }

        // Mientras la pantalla está visible espera tarjetas: escoger destino o registrar una nueva.
        private async Task EscucharTarjetasAsync(CancellationToken cancelacion)
        {
            var nfc = DependencyService.Get<INfcService>();
            if (nfc == null || !nfc.TieneNfc) return;

            while (!cancelacion.IsCancellationRequested)
            {
                TarjetaNfc tarjeta;
                try
                {
                    tarjeta = await nfc.EsperarTarjetaAsync(cancelacion);
                }
                catch (OperationCanceledException)
                {
                    if (cancelacion.IsCancellationRequested) return;
                    // La canceló otra pantalla al navegar (el lector atiende una espera a la vez)
                    await Task.Delay(300);
                    continue;
                }
                catch (NfcException ex)
                {
                    await DisplayAlert("Error", ex.Message, "OK");
                    continue;
                }

                await ProcesarTarjetaAsync(tarjeta);
            }
        }

        private async Task ProcesarTarjetaAsync(TarjetaNfc tarjeta)
        {
            if (_procesandoTarjeta) return;
            if (!string.IsNullOrEmpty(uidTarjetaApertura) && tarjeta.Uid == uidTarjetaApertura
                && DateTime.Now < _ignorarAperturaHasta)
                return;
            _procesandoTarjeta = true;
            try
            {
                int? idTarimaTarjeta = int.TryParse(tarjeta.Texto, out int id) && id > 0 ? id : (int?)null;
                if (_registrandoNueva)
                    await RegistrarNuevaTarimaAsync(tarjeta.Uid, idTarimaTarjeta);
                else
                    await SeleccionarDestinoAsync(tarjeta.Uid, idTarimaTarjeta);
            }
            finally
            {
                _procesandoTarjeta = false;
            }
        }

        private async Task SeleccionarDestinoAsync(string uid, int? idTarimaTarjeta)
        {
            TarimaEmbalaje tarima;
            LoadingOverlay.IsVisible = true;
            try
            {
                tarima = await Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();
                        return EmbalajeTarimas.BuscarAbiertaPorTarjeta(connection, null, uid, idTarimaTarjeta);
                    }
                });
            }
            catch (Exception ex)
            {
                LoadingOverlay.IsVisible = false;
                await DisplayAlert("Error", "No se pudo buscar la tarima de embalaje: " + ex.Message, "OK");
                return;
            }
            LoadingOverlay.IsVisible = false;

            if (tarima == null)
            {
                await DisplayAlert("Tarjeta sin tarima de embalaje",
                    "Esta tarjeta no es de ninguna tarima de embalaje abierta.\n\n" +
                    "Si esta tarima será una tarima de embalaje nueva, toque \"+ Nueva\" y vuelva a acercar la tarjeta.", "OK");
                return;
            }

            if (tarima.IdSucursal != idSucursal)
            {
                await DisplayAlert("Otra sucursal",
                    $"La tarima {tarima.Codigo} es de otra sucursal. No se pueden poner aquí productos de este pedido.", "OK");
                return;
            }

            _tarimaEmbalaje = tarima;
            ActualizarPanelDestino();
            try { Vibration.Vibrate(150); } catch { }
        }

        private async Task RegistrarNuevaTarimaAsync(string uid, int? idTarimaTarjeta)
        {
            if (!idSucursal.HasValue)
            {
                await DisplayAlert("Sin sucursal", $"El pedido #{idPedido} no tiene sucursal; no se puede embalar.", "OK");
                return;
            }

            LoadingOverlay.IsVisible = true;
            try
            {
                var nueva = await Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();
                        using (var transaction = connection.BeginTransaction())
                        {
                            try
                            {
                                var t = EmbalajeTarimas.CrearTarima(connection, transaction, idSucursal.Value,
                                    App.IdUsuarioActual, App.NombreUsuarioActual, uid, idTarimaTarjeta);
                                transaction.Commit();
                                return t;
                            }
                            catch
                            {
                                transaction.Rollback();
                                throw;
                            }
                        }
                    }
                });

                _registrandoNueva = false;
                _tarimaEmbalaje = nueva;
                ActualizarPanelDestino();
                LoadingOverlay.IsVisible = false;
                try { Vibration.Vibrate(150); } catch { }
                await DisplayAlert("Tarima de embalaje creada",
                    $"Se creó la tarima {nueva.Codigo} y quedó registrada con esta tarjeta.", "OK");
            }
            catch (Exception ex)
            {
                LoadingOverlay.IsVisible = false;
                await DisplayAlert("No se creó la tarima", ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        private async void OnNuevaDestinoClicked(object sender, EventArgs e)
        {
            if (_registrandoNueva)
            {
                _registrandoNueva = false;
                ActualizarPanelDestino();
                return;
            }

            var nfc = DependencyService.Get<INfcService>();
            if (nfc == null || !nfc.TieneNfc)
            {
                await DisplayAlert("Sin NFC", "Este teléfono no tiene NFC; no se puede registrar la tarjeta de la tarima.", "OK");
                return;
            }

            _registrandoNueva = true;
            ActualizarPanelDestino();
        }

        // Cualquier rechequeador puede cerrarla (Estado 2): ya no se le meten ni quitan productos
        // y su tarjeta queda libre.
        private async void OnCerrarDestinoClicked(object sender, EventArgs e)
        {
            var tarima = _tarimaEmbalaje;
            if (tarima == null) return;

            bool confirmar = await DisplayAlert("Cerrar tarima",
                $"¿Cerrar la tarima {tarima.Codigo}?\n\nYa no se podrán meter ni quitar productos y su tarjeta quedará libre.",
                "Sí, cerrar", "Cancelar");
            if (!confirmar) return;

            LoadingOverlay.IsVisible = true;
            try
            {
                await Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();
                        using (var transaction = connection.BeginTransaction())
                        {
                            EmbalajeTarimas.CerrarTarima(connection, transaction, tarima.IdTarimaEmbalada, App.IdUsuarioActual);
                            transaction.Commit();
                        }
                    }
                });
                _tarimaEmbalaje = null;
                ActualizarPanelDestino();
                LoadingOverlay.IsVisible = false;
                await DisplayAlert("Tarima cerrada", $"La tarima {tarima.Codigo} quedó embalada.", "OK");
            }
            catch (TarimaEmbalajeCerradaException ex)
            {
                _tarimaEmbalaje = null;
                ActualizarPanelDestino();
                LoadingOverlay.IsVisible = false;
                await DisplayAlert("Tarima cerrada", ex.Message, "OK");
            }
            catch (Exception ex)
            {
                LoadingOverlay.IsVisible = false;
                await DisplayAlert("Error", ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        // Antes de guardar un producto: debe haber tarima de embalaje. Si la sucursal aún no tiene
        // ninguna abierta, se pasa directo a registrar la tarjeta de la nueva.
        private async Task<bool> AsegurarDestinoAsync()
        {
            if (_tarimaEmbalaje != null) return true;
            if (!idSucursal.HasValue)
            {
                await DisplayAlert("Sin sucursal", $"El pedido #{idPedido} no tiene sucursal; no se puede embalar.", "OK");
                return false;
            }

            int abiertas = await Task.Run(() =>
            {
                using (var connection = new MySqlConnection(BuildConnectionString()))
                {
                    connection.Open();
                    return EmbalajeTarimas.ListarAbiertas(connection, idSucursal.Value).Count;
                }
            });

            if (abiertas == 0)
            {
                _registrandoNueva = true;
                ActualizarPanelDestino();
                await DisplayAlert("Registrar tarima de embalaje",
                    "Esta sucursal aún no tiene tarimas de embalaje abiertas.\n\n" +
                    "Toque la tarjeta de la tarima que será la tarima de embalaje y luego vuelva a confirmar el producto.", "OK");
            }
            else
            {
                await DisplayAlert("Falta la tarima de embalaje",
                    "Toque la tarjeta de la tarima donde pondrá este producto (o \"+ Nueva\" para registrar una) " +
                    "y luego vuelva a confirmar el producto.", "OK");
            }
            return false;
        }

        // ══════════════════════════════════════
        // GUARDAR: TarimasRechequeadas + embalaje, en una sola transacción
        // ══════════════════════════════════════

        // Devuelve false si no se guardó (falta tarima de embalaje o la cerró otro usuario).
        private async Task<bool> GuardarRechequeo(DetalleProducto producto, int cantidadChequeada, string observacion)
        {
            int idUsuarioActual = App.IdUsuarioActual;
            string nombreUsuarioActual = App.NombreUsuarioActual ?? "";

            LoadingOverlay.IsVisible = false;
            if (!await AsegurarDestinoAsync()) return false;
            LoadingOverlay.IsVisible = true;

            await TomarTarimaParaRechequeo();

            var embalar = new ProductoAEmbalar
            {
                IdTarima = idTarima,
                IdPedido = idPedido,
                NoHoja = producto.NoHoja,
                Upc = producto.Upc,
                UpcUnidad = producto.UpcUnidad,
                CantidadConfirmada = producto.CantidadConfirmada
            };
            int idTarimaEmbalada = _tarimaEmbalaje.IdTarimaEmbalada;

            try
            {
                _tarimaEmbalaje = await Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();
                        using (var transaction = connection.BeginTransaction())
                        {
                            try
                            {
                                // Si ya estaba rechequeado (y embalado) solo se actualiza el rechequeo
                                bool yaRechequeado = GuardarEnTarimasRechequeadas(connection, transaction, producto,
                                    cantidadChequeada, observacion, idUsuarioActual, nombreUsuarioActual);
                                if (!yaRechequeado)
                                    EmbalajeTarimas.EmbalarProducto(connection, transaction, idTarimaEmbalada,
                                        embalar, idUsuarioActual);

                                transaction.Commit();
                            }
                            catch
                            {
                                transaction.Rollback();
                                throw;
                            }
                        }
                        return EmbalajeTarimas.Leer(connection, null, idTarimaEmbalada);
                    }
                });
                ActualizarPanelDestino();
                return true;
            }
            catch (TarimaEmbalajeCerradaException ex)
            {
                _tarimaEmbalaje = null;
                ActualizarPanelDestino();
                LoadingOverlay.IsVisible = false;
                await DisplayAlert("Tarima cerrada",
                    ex.Message + "\n\nToque la tarjeta de otra tarima de embalaje (o \"+ Nueva\") y vuelva a confirmar el producto.", "OK");
                return false;
            }
        }

        // Inserta o actualiza el rechequeo del producto. Devuelve true si ya existía.
        private bool GuardarEnTarimasRechequeadas(MySqlConnection connection, MySqlTransaction transaction,
            DetalleProducto producto, int cantidadChequeada, string observacion,
            int idUsuarioActual, string nombreUsuarioActual)
        {
            int? idExistente = null;
            using (var cmdBuscar = new MySqlCommand(@"
                SELECT IdRechequeo FROM TarimasRechequeadas
                WHERE IdPedido = @IdPedido AND NoTarima = @NoTarima AND UpcPaquete = @UpcPaquete
                LIMIT 1 FOR UPDATE", connection, transaction))
            {
                cmdBuscar.Parameters.AddWithValue("@IdPedido", idPedido);
                cmdBuscar.Parameters.AddWithValue("@NoTarima", numeroTarima);
                cmdBuscar.Parameters.AddWithValue("@UpcPaquete", producto.Upc);
                var res = cmdBuscar.ExecuteScalar();
                if (res != null && res != DBNull.Value)
                    idExistente = Convert.ToInt32(res);
            }

            if (idExistente.HasValue)
            {
                using (var cmdUpdate = new MySqlCommand(@"
                    UPDATE TarimasRechequeadas SET
                        UpcUnidad = @UpcUnidad,
                        Descripcion = @Descripcion,
                        CantidadUniFardo = @CantidadUniFardo,
                        CantidadPreparada = @CantidadPreparada,
                        CantidadChequeada = @CantidadChequeada,
                        IdUsuarioPreparo = @IdUsuarioPreparo,
                        NombreUsuarioPreparo = @NombreUsuarioPreparo,
                        IdUsuarioChequeo = @IdUsuarioChequeo,
                        NombreUsuarioChequeo = @NombreUsuarioChequeo,
                        FechahoraChequeo = NOW(),
                        Observacion = @Observacion
                    WHERE IdRechequeo = @IdRechequeo", connection, transaction))
                {
                    AgregarParametrosComunes(cmdUpdate, producto, cantidadChequeada, observacion,
                        idUsuarioActual, nombreUsuarioActual);
                    cmdUpdate.Parameters.AddWithValue("@IdRechequeo", idExistente.Value);
                    cmdUpdate.ExecuteNonQuery();
                }
                return true;
            }

            using (var cmdInsert = new MySqlCommand(@"
                INSERT INTO TarimasRechequeadas
                    (UpcPaquete, UpcUnidad, Descripcion, CantidadUniFardo, CantidadPreparada,
                     CantidadChequeada, IdPedido, NoTarima, IdUsuarioPreparo, NombreUsuarioPreparo,
                     IdUsuarioChequeo, NombreUsuarioChequeo, FechahoraChequeo, Observacion)
                VALUES
                    (@UpcPaquete, @UpcUnidad, @Descripcion, @CantidadUniFardo, @CantidadPreparada,
                     @CantidadChequeada, @IdPedido, @NoTarima, @IdUsuarioPreparo, @NombreUsuarioPreparo,
                     @IdUsuarioChequeo, @NombreUsuarioChequeo, NOW(), @Observacion)", connection, transaction))
            {
                cmdInsert.Parameters.AddWithValue("@UpcPaquete", producto.Upc);
                AgregarParametrosComunes(cmdInsert, producto, cantidadChequeada, observacion,
                    idUsuarioActual, nombreUsuarioActual);
                cmdInsert.Parameters.AddWithValue("@IdPedido", idPedido);
                cmdInsert.Parameters.AddWithValue("@NoTarima", numeroTarima);
                cmdInsert.ExecuteNonQuery();
            }
            return false;
        }

        private void AgregarParametrosComunes(MySqlCommand cmd, DetalleProducto producto,
            int cantidadChequeada, string observacion, int idUsuarioActual, string nombreUsuarioActual)
        {
            cmd.Parameters.AddWithValue("@UpcUnidad", producto.UpcUnidad);
            cmd.Parameters.AddWithValue("@Descripcion", producto.Descripcion);
            cmd.Parameters.AddWithValue("@CantidadUniFardo", producto.CantidadUniFardo);
            cmd.Parameters.AddWithValue("@CantidadPreparada", producto.CantidadConfirmada);
            cmd.Parameters.AddWithValue("@CantidadChequeada", cantidadChequeada);
            cmd.Parameters.AddWithValue("@IdUsuarioPreparo", producto.IdUsuarioPreparo ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@NombreUsuarioPreparo", producto.NombreUsuarioPreparo ?? "");
            cmd.Parameters.AddWithValue("@IdUsuarioChequeo", idUsuarioActual);
            cmd.Parameters.AddWithValue("@NombreUsuarioChequeo", nombreUsuarioActual);
            cmd.Parameters.AddWithValue("@Observacion", observacion ?? "");
        }
    }
}
