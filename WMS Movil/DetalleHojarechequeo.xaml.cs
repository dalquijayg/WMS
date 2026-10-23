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
        private string nombreEmpresa = "";
        private string nombreDepartamento = "";
        private int? idInventarioPreparada;
        private bool? _infoEsEmbalaje;            // ventana de información abierta: null = cerrada
        private TarimaEmbalaje _tarimaEmbalaje;   // destino; null = falta tocar su tarjeta
        private bool _eligiendoDestino;           // ventana del paso 2 visible
        private bool _registrandoNueva;           // la próxima tarjeta libre crea una tarima nueva sin preguntar
        private List<TarimaEmbalaje> _abiertas = new List<TarimaEmbalaje>(); // de la sucursal, para el paso 2
        private bool _procesandoTarjeta;
        private CancellationTokenSource _nfcCancelacion;

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

        public DetalleHojarechequeo(int idTarima, int idPedido, int numeroTarima, string uidTarjetaApertura)
        {
            InitializeComponent();
            this.idTarima = idTarima;
            this.idPedido = idPedido;
            this.numeroTarima = numeroTarima;
            this.uidTarjetaApertura = uidTarjetaApertura;

            ActualizarPanelDestino();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _ = CargarInfoYPedirDestinoAsync();
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

        private string BuildConnectionString() =>
            $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

        private async void OnBackClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        // Paso 2: en cuanto se conoce la sucursal del pedido, si aún no hay tarima de embalaje se pide.
        private async Task CargarInfoYPedirDestinoAsync()
        {
            await CargarInfoPedido();
            if (_tarimaEmbalaje == null && !_eligiendoDestino)
                await AbrirSelectorDestinoAsync();
        }

        private async Task CargarInfoPedido()
        {
            try
            {
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

                        idInventarioPreparada = RepartoTarimas.LeerInventarioTarima(connection, null, idTarima);
                    }
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

        // Tarima terminada (último producto rechequeado): pregunta si la tarima de embalaje se
        // cierra o se mantiene abierta, y vuelve a la pantalla anterior para escanear la siguiente.
        private async Task RegresarAEscanearTarima()
        {
            AplicarFiltro();
            LoadingOverlay.IsVisible = false;

            var tarimaEmbalaje = _tarimaEmbalaje;
            bool cerrar = await PreguntarFinAsync(tarimaEmbalaje);
            if (cerrar && tarimaEmbalaje != null)
                await CerrarTarimaEmbalajeAsync(tarimaEmbalaje);

            await Navigation.PopAsync();
        }

        // Ventana de tarima terminada. Devuelve true si eligió cerrar la tarima de embalaje.
        private TaskCompletionSource<bool> _finEleccion;

        private async Task<bool> PreguntarFinAsync(TarimaEmbalaje tarimaEmbalaje)
        {
            int correctos = todosLosProductos.Count(p => p.RevisadoOk == true);
            int conDiferencia = todosLosProductos.Count(p => p.RevisadoOk == false);

            FinSubtituloLabel.Text = $"Tarima #{numeroTarima} del Pedido #{idPedido}";
            FinCorrectosLabel.Text = $"✓ {correctos} correcto{(correctos == 1 ? "" : "s")}";
            FinDiferenciaLabel.Text = $"✗ {conDiferencia} con diferencia";
            FinDiferenciaFrame.IsVisible = conDiferencia > 0;

            FinEmbalajeStack.IsVisible = tarimaEmbalaje != null;
            FinContinuarButton.IsVisible = tarimaEmbalaje == null;
            if (tarimaEmbalaje != null)
            {
                FinEmbalajeCodigoLabel.Text = $"Tarima #{tarimaEmbalaje.NoTarimaEmbalada}  ({tarimaEmbalaje.Codigo})";
                FinEmbalajeDetalleLabel.Text = string.Join("  •  ", new[]
                {
                    nombreSucursal,
                    $"{tarimaEmbalaje.CantidadSkus} SKU",
                    $"{tarimaEmbalaje.CantidadFardos} fardos"
                }.Where(s => !string.IsNullOrWhiteSpace(s)));
            }

            _finEleccion = new TaskCompletionSource<bool>();

            // Entra desde abajo y el ✓ "salta"
            FinOverlay.Opacity = 0;
            FinCard.Scale = 0.85;
            FinCard.TranslationY = 80;
            FinIconoFrame.Scale = 0.3;
            FinOverlay.IsVisible = true;
            try { Vibration.Vibrate(200); } catch { }
            await Task.WhenAll(
                FinOverlay.FadeTo(1, 200),
                FinCard.ScaleTo(1, 280, Easing.SpringOut),
                FinCard.TranslateTo(0, 0, 280, Easing.CubicOut));
            await FinIconoFrame.ScaleTo(1, 320, Easing.SpringOut);

            bool cerrar = await _finEleccion.Task;

            await Task.WhenAll(
                FinOverlay.FadeTo(0, 160),
                FinCard.ScaleTo(0.9, 160, Easing.CubicIn));
            FinOverlay.IsVisible = false;
            return cerrar;
        }

        private void OnFinMantenerTapped(object sender, EventArgs e) => _finEleccion?.TrySetResult(false);

        private async void OnFinCerrarTapped(object sender, EventArgs e)
        {
            var tarima = _tarimaEmbalaje;
            bool confirmar = await ConfirmarAsync("🔒", "#FFEDD5",
                $"¿Cerrar la Tarima #{tarima?.NoTarimaEmbalada}?",
                $"La tarima de embalaje {tarima?.Codigo} quedará lista para enviar a {NombreSucursalTexto} " +
                "y ya no se le podrán agregar ni quitar productos.",
                null, "🔒  Sí, cerrarla", "#EA580C", "No, dejarla abierta");
            if (confirmar)
                _finEleccion?.TrySetResult(true);
        }

        // Mientras se muestra una ventana propia, el botón "atrás" del teléfono no la salta
        // (en el diálogo equivale a "No").
        protected override bool OnBackButtonPressed()
        {
            if (DialogoOverlay.IsVisible)
            {
                _dialogoEleccion?.TrySetResult(false);
                return true;
            }
            if (FinOverlay.IsVisible) return true;
            return base.OnBackButtonPressed();
        }

        // ══════════════════════════════════════
        // DIÁLOGO AMIGABLE (reemplaza DisplayAlert en confirmaciones importantes)
        // ══════════════════════════════════════

        private TaskCompletionSource<bool> _dialogoEleccion;

        // Devuelve true si eligió el botón principal. nota (opcional) se muestra en un recuadro.
        private async Task<bool> ConfirmarAsync(string icono, string colorIcono, string titulo, string mensaje,
            string nota, string textoSi, string colorSi, string textoNo)
        {
            _dialogoEleccion?.TrySetResult(false);
            _dialogoEleccion = new TaskCompletionSource<bool>();

            DialogoIconoLabel.Text = icono;
            DialogoIconoFrame.BackgroundColor = Color.FromHex(colorIcono);
            DialogoTituloLabel.Text = titulo;
            DialogoMensajeLabel.Text = mensaje;
            DialogoNotaLabel.Text = nota ?? "";
            DialogoNotaFrame.IsVisible = !string.IsNullOrWhiteSpace(nota);
            DialogoSiButton.Text = textoSi;
            DialogoSiButton.BackgroundColor = Color.FromHex(colorSi);
            DialogoNoButton.Text = textoNo ?? "";
            DialogoNoButton.IsVisible = !string.IsNullOrEmpty(textoNo);

            DialogoOverlay.Opacity = 0;
            DialogoCard.Scale = 0.8;
            DialogoIconoFrame.Scale = 0.4;
            DialogoOverlay.IsVisible = true;
            await Task.WhenAll(
                DialogoOverlay.FadeTo(1, 160),
                DialogoCard.ScaleTo(1, 260, Easing.SpringOut));
            _ = DialogoIconoFrame.ScaleTo(1, 300, Easing.SpringOut);

            bool resultado = await _dialogoEleccion.Task;

            await Task.WhenAll(
                DialogoOverlay.FadeTo(0, 140),
                DialogoCard.ScaleTo(0.9, 140, Easing.CubicIn));
            DialogoOverlay.IsVisible = false;
            return resultado;
        }

        // Aviso con un solo botón.
        private Task AvisarAsync(string icono, string colorIcono, string titulo, string mensaje, string nota = null) =>
            ConfirmarAsync(icono, colorIcono, titulo, mensaje, nota, "Entendido", "#4C51BF", null);

        private void OnDialogoSiClicked(object sender, EventArgs e) => _dialogoEleccion?.TrySetResult(true);
        private void OnDialogoNoClicked(object sender, EventArgs e) => _dialogoEleccion?.TrySetResult(false);

        // ══════════════════════════════════════
        // TARIMA DE EMBALAJE (destino) — paso 2
        // Se escoge tocando su tarjeta. Si la tarjeta no es de ninguna tarima de embalaje
        // abierta, se ofrece crear una nueva con ella.
        // ══════════════════════════════════════

        private string NombreSucursalTexto =>
            string.IsNullOrWhiteSpace(nombreSucursal) ? "esta sucursal" : nombreSucursal;

        // Ícono de embalaje del header (con su código debajo) y ventanas abiertas.
        private void ActualizarPanelDestino()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                string codigoAnterior = EmbalajeCodigoLabel.Text;
                EmbalajeCodigoLabel.Text = _tarimaEmbalaje != null
                    ? $"Tarima #{_tarimaEmbalaje.NoTarimaEmbalada}"
                    : "Elegir";
                EmbalajeIconoFrame.BackgroundColor = Color.FromHex(_tarimaEmbalaje != null ? "#FCD34D" : "#FCA5A5");

                // Al cambiar de tarima de embalaje el ícono "late" para que se note
                if (_tarimaEmbalaje != null && codigoAnterior != EmbalajeCodigoLabel.Text)
                    _ = LatirAsync(EmbalajeIconoFrame);

                if (_infoEsEmbalaje == true)
                    LlenarInfoEmbalaje();

                ActualizarSelectorDestino();
            });
        }

        private static async Task LatirAsync(VisualElement elemento)
        {
            await elemento.ScaleTo(1.25, 140, Easing.CubicOut);
            await elemento.ScaleTo(1, 180, Easing.CubicIn);
        }

        // ══════════════════════════════════════
        // VENTANA DE INFORMACIÓN (íconos del header)
        // ══════════════════════════════════════

        private async void OnInfoPreparadaTapped(object sender, EventArgs e)
        {
            _infoEsEmbalaje = false;
            LlenarInfoPreparada();
            await MostrarInfoAsync();
        }

        private async void OnInfoEmbalajeTapped(object sender, EventArgs e)
        {
            if (_tarimaEmbalaje == null)
            {
                await AbrirSelectorDestinoAsync(); // sin tarima de embalaje: directo a elegirla
                return;
            }
            _infoEsEmbalaje = true;
            LlenarInfoEmbalaje();
            await MostrarInfoAsync();

            // Datos al día (otro usuario pudo embalar en ella o se le acaba de asignar inventario)
            int idTarimaEmbalada = _tarimaEmbalaje.IdTarimaEmbalada;
            try
            {
                var actual = await Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();
                        return EmbalajeTarimas.Leer(connection, null, idTarimaEmbalada);
                    }
                });
                if (actual != null && actual.Estado == EmbalajeTarimas.EstadoEmbalando
                    && _tarimaEmbalaje?.IdTarimaEmbalada == idTarimaEmbalada)
                {
                    _tarimaEmbalaje = actual;
                    if (_infoEsEmbalaje == true) LlenarInfoEmbalaje();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al actualizar la tarima de embalaje: {ex.Message}");
            }
        }

        private void LlenarInfoPreparada()
        {
            int total = todosLosProductos.Count;
            int correctos = todosLosProductos.Count(p => p.RevisadoOk == true);
            int conDiferencia = todosLosProductos.Count(p => p.RevisadoOk == false);
            int fardos = todosLosProductos.Sum(p => p.CantidadConfirmada);
            string preparo = string.Join(", ", todosLosProductos
                .Select(p => p.NombreUsuarioPreparo)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct());

            InfoEncabezado.BackgroundColor = Color.FromHex("#EEF2FF");
            InfoIconoFrame.BackgroundColor = Color.FromHex("#C7D2FE");
            InfoIconoLabel.IsVisible = true;
            InfoIconoImage.IsVisible = false;
            InfoSubtituloLabel.Text = "📦 TARIMA PREPARADA";
            InfoSubtituloLabel.TextColor = Color.FromHex("#4C51BF");
            InfoTituloLabel.Text = $"Tarima #{numeroTarima}";

            InfoFilasStack.Children.Clear();
            AgregarFila("Pedido", $"#{idPedido}");
            AgregarFila("Id inventario", idInventarioPreparada?.ToString() ?? "Sin inventario", "#4C51BF");
            AgregarFila("Empresa", nombreEmpresa);
            AgregarFila("Departamento", nombreDepartamento);
            AgregarFila("Sucursal", nombreSucursal);
            AgregarFila("Preparó", preparo);
            AgregarFila("Productos", $"{total}  ({fardos} fardos)");
            AgregarFila("Rechequeados", $"{correctos + conDiferencia} de {total}");
            AgregarFila("✓ Correctos", correctos.ToString(), "#38A169");
            if (conDiferencia > 0)
                AgregarFila("✗ Con diferencia", conDiferencia.ToString(), "#E53E3E");

            InfoNotaLabel.Text = "Esta es la tarima que está rechequeando. Sus productos se pasan a la tarima de embalaje.";
            InfoAccionesEmbalaje.IsVisible = false;
        }

        private void LlenarInfoEmbalaje()
        {
            var tarima = _tarimaEmbalaje;
            if (tarima == null) return;

            InfoEncabezado.BackgroundColor = Color.FromHex("#FFFBEB");
            InfoIconoFrame.BackgroundColor = Color.FromHex("#FCD34D");
            InfoIconoLabel.IsVisible = false;
            InfoIconoImage.IsVisible = true;
            InfoSubtituloLabel.Text = "TARIMA DE EMBALAJE";
            InfoSubtituloLabel.TextColor = Color.FromHex("#B45309");
            InfoTituloLabel.Text = $"Tarima {tarima.Codigo}";

            InfoFilasStack.Children.Clear();
            AgregarFila("Id inventario", tarima.IdInventario?.ToString()
                ?? "Se asigna al embalar el primer producto", "#B45309");
            AgregarFila("Sucursal", nombreSucursal);
            AgregarFila("Lote", tarima.NoLote.ToString());
            AgregarFila("No. de tarima", tarima.NoTarimaEmbalada.ToString());
            AgregarFila("SKU", tarima.CantidadSkus.ToString());
            AgregarFila("Fardos", tarima.CantidadFardos.ToString());
            AgregarFila("Creada por", tarima.NombreUsuarioCreo);
            AgregarFila("Estado", "Abierta (embalando)", "#B45309");

            InfoNotaLabel.Text = "Aquí van los productos que rechequee. También puede recibir productos de otras " +
                                 $"tarimas preparadas que vayan a {NombreSucursalTexto}.";
            InfoAccionesEmbalaje.IsVisible = true;
        }

        private void AgregarFila(string etiqueta, string valor, string colorValor = "#1E2235")
        {
            if (string.IsNullOrWhiteSpace(valor)) return;
            var fila = new Grid { ColumnSpacing = 12 };
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            fila.Children.Add(new Label { Text = etiqueta, TextColor = Color.FromHex("#6B7280"), FontSize = 13 }, 0, 0);
            fila.Children.Add(new Label
            {
                Text = valor,
                TextColor = Color.FromHex(colorValor),
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.End,
                LineBreakMode = LineBreakMode.WordWrap
            }, 1, 0);
            InfoFilasStack.Children.Add(fila);
        }

        // Entra desde abajo con un pequeño rebote; el fondo se oscurece.
        private async Task MostrarInfoAsync()
        {
            InfoOverlay.Opacity = 0;
            InfoCard.Scale = 0.85;
            InfoCard.TranslationY = 60;
            InfoOverlay.IsVisible = true;
            await Task.WhenAll(
                InfoOverlay.FadeTo(1, 180),
                InfoCard.ScaleTo(1, 260, Easing.SpringOut),
                InfoCard.TranslateTo(0, 0, 260, Easing.CubicOut));
        }

        private async Task OcultarInfoAsync()
        {
            if (!InfoOverlay.IsVisible) return;
            _infoEsEmbalaje = null;
            await Task.WhenAll(
                InfoOverlay.FadeTo(0, 160),
                InfoCard.ScaleTo(0.9, 160, Easing.CubicIn),
                InfoCard.TranslateTo(0, 40, 160, Easing.CubicIn));
            InfoOverlay.IsVisible = false;
        }

        private async void OnInfoCerrarTapped(object sender, EventArgs e) => await OcultarInfoAsync();

        private async void OnInfoCambiarClicked(object sender, EventArgs e)
        {
            await OcultarInfoAsync();
            await AbrirSelectorDestinoAsync();
        }

        private async void OnInfoCerrarTarimaClicked(object sender, EventArgs e)
        {
            await OcultarInfoAsync();
            await CerrarDestinoConConfirmacionAsync();
        }

        // Ventana del paso 2: explica para qué es la tarima de embalaje y qué tarjeta tocar.
        private void ActualizarSelectorDestino()
        {
            DestinoOverlay.IsVisible = _eligiendoDestino;
            if (!_eligiendoDestino) return;

            string sucursal = NombreSucursalTexto;
            OverlayPreparadaLabel.Text = $"Tarima #{numeroTarima} del Pedido #{idPedido}";

            if (_registrandoNueva)
            {
                OverlayTituloLabel.Text = "Crear tarima de embalaje";
                OverlayMensajeLabel.Text = _abiertas.Count == 0
                    ? $"{sucursal} todavía no tiene tarima de embalaje.\n\nAcerque la tarjeta de la tarima vacía que usará para embalar."
                    : "Acerque la tarjeta de la tarima vacía que usará como tarima de embalaje nueva.";
                OverlayAbiertasFrame.IsVisible = false;
                OverlayNuevaButton.Text = "← Usar una tarima ya creada";
                OverlayNuevaButton.IsVisible = _abiertas.Count > 0;
            }
            else
            {
                const int maxLista = 6;
                var lineas = _abiertas.Take(maxLista)
                    .Select(t => $"• {t.Codigo}  —  {t.CantidadSkus} SKU, {t.CantidadFardos} fardos")
                    .ToList();
                if (_abiertas.Count > maxLista)
                    lineas.Add($"…y {_abiertas.Count - maxLista} más");

                OverlayTituloLabel.Text = "¿Dónde pondrá los productos?";
                OverlayMensajeLabel.Text = "Acerque la tarjeta de la tarima de embalaje donde va a colocar los productos.";
                OverlayAbiertasTituloLabel.Text = $"Tarimas de embalaje abiertas para {sucursal} ({_abiertas.Count})";
                OverlayAbiertasLabel.Text = string.Join("\n", lineas);
                OverlayAbiertasFrame.IsVisible = true;
                OverlayNuevaButton.Text = "➕ Crear una tarima nueva";
                OverlayNuevaButton.IsVisible = true;
            }

            OverlayAyudaLabel.Text = "💡 En una misma tarima de embalaje puede juntar productos de varias " +
                                     $"tarimas preparadas, siempre que vayan a {sucursal}.";
            OverlayCancelarButton.Text = _tarimaEmbalaje == null
                ? "Regresar"
                : $"Seguir en la tarima {_tarimaEmbalaje.Codigo}";
        }

        // Muestra el paso 2 con las tarimas de embalaje abiertas de la sucursal. Si no hay
        // ninguna, la próxima tarjeta que se toque crea la primera.
        private async Task AbrirSelectorDestinoAsync()
        {
            if (!idSucursal.HasValue)
            {
                await DisplayAlert("Sin sucursal", $"El pedido #{idPedido} no tiene sucursal; no se puede embalar.", "OK");
                return;
            }

            try
            {
                _abiertas = await Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();
                        return EmbalajeTarimas.ListarAbiertas(connection, idSucursal.Value);
                    }
                });
            }
            catch (Exception ex)
            {
                // Sin la lista se puede seguir: tocar la tarjeta igual funciona
                System.Diagnostics.Debug.WriteLine($"Error al listar tarimas de embalaje: {ex.Message}");
                _abiertas = new List<TarimaEmbalaje>();
            }

            _registrandoNueva = _abiertas.Count == 0;
            _eligiendoDestino = true;
            ActualizarPanelDestino();
        }

        private void CerrarSelectorDestino()
        {
            _eligiendoDestino = false;
            _registrandoNueva = false;
            ActualizarPanelDestino();
        }

        private async void OnOverlayNuevaClicked(object sender, EventArgs e)
        {
            if (_registrandoNueva)
            {
                _registrandoNueva = false; // volver a "usar una ya creada"
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

        // Sin tarima de embalaje no se puede rechequear: se regresa a leer otra tarima preparada.
        private async void OnOverlayCancelarClicked(object sender, EventArgs e)
        {
            if (_tarimaEmbalaje == null)
            {
                await Navigation.PopAsync();
                return;
            }
            CerrarSelectorDestino();
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
                await UsarTarjetaComoDestinoAsync(tarjeta.Uid, idTarimaTarjeta);
            }
            finally
            {
                _procesandoTarjeta = false;
            }
        }

        // Tarjeta de una tarima de embalaje abierta: se usa esa. Tarjeta libre: se crea una
        // nueva con ella (preguntando antes, salvo que el usuario ya haya pedido crearla).
        private async Task UsarTarjetaComoDestinoAsync(string uid, int? idTarimaTarjeta)
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
                bool esLaPreparada = idTarimaTarjeta == idTarima;
                if (esLaPreparada || !_registrandoNueva)
                {
                    string pregunta = esLaPreparada
                        ? $"Esta es la tarjeta de la tarima preparada que está rechequeando (Tarima #{numeroTarima}).\n\n" +
                          "¿Quiere usar esta misma tarima como tarima de embalaje?"
                        : "Esta tarjeta todavía no es de ninguna tarima de embalaje.\n\n" +
                          $"¿Quiere crear una tarima de embalaje nueva para {NombreSucursalTexto} con esta tarjeta?";
                    bool crear = await DisplayAlert(esLaPreparada ? "Misma tarima" : "Tarjeta libre",
                        pregunta, "Sí, crear", "No");
                    if (!crear) return;
                }
                await RegistrarNuevaTarimaAsync(uid, idTarimaTarjeta);
                return;
            }

            if (tarima.IdSucursal != idSucursal)
            {
                await DisplayAlert("Esa tarima es de otra sucursal",
                    $"La tarima de embalaje {tarima.Codigo} va para otra sucursal, y este pedido va para {NombreSucursalTexto}.\n\n" +
                    "Toque la tarjeta de una tarima de esta sucursal o cree una nueva.", "OK");
                return;
            }

            if (_registrandoNueva)
                await DisplayAlert("Tarima ya creada",
                    $"Esta tarjeta ya es de la tarima de embalaje {tarima.Codigo}. Se usará esa tarima.", "OK");

            _tarimaEmbalaje = tarima;
            CerrarSelectorDestino();
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

                _tarimaEmbalaje = nueva;
                CerrarSelectorDestino();
                LoadingOverlay.IsVisible = false;
                try { Vibration.Vibrate(150); } catch { }
                await DisplayAlert("Tarima de embalaje creada",
                    $"Se creó la tarima de embalaje {nueva.Codigo} para {NombreSucursalTexto}.\n\n" +
                    "Coloque en ella los productos que vaya rechequeando. También la podrán usar " +
                    "para productos de otras tarimas preparadas de esta sucursal.", "OK");
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

        // Cualquier rechequeador puede cerrarla (Estado 2): ya no se le meten ni quitan productos
        // y su tarjeta queda libre. Si aún faltan productos, se pide otra tarima de embalaje.
        private async Task CerrarDestinoConConfirmacionAsync()
        {
            var tarima = _tarimaEmbalaje;
            if (tarima == null) return;

            int pendientes = todosLosProductos.Count(p => !p.RevisadoOk.HasValue);
            bool confirmar = await ConfirmarAsync("🔒", "#FFEDD5",
                $"¿Cerrar la Tarima #{tarima.NoTarimaEmbalada}?",
                $"La tarima de embalaje {tarima.Codigo} ({tarima.CantidadSkus} SKU • {tarima.CantidadFardos} fardos) " +
                $"quedará lista para enviar a {NombreSucursalTexto}.\n\n" +
                "Ya no se le podrán agregar ni quitar productos y su tarjeta quedará libre.",
                pendientes > 0
                    ? $"Aún faltan {pendientes} producto{(pendientes == 1 ? "" : "s")} por rechequear de esta tarima preparada. " +
                      "Después de cerrarla deberá elegir otra tarima de embalaje para ellos."
                    : null,
                "🔒  Sí, cerrarla", "#EA580C", "No, dejarla abierta");
            if (!confirmar) return;

            await CerrarTarimaEmbalajeAsync(tarima);

            if (_tarimaEmbalaje == null && todosLosProductos.Any(p => !p.RevisadoOk.HasValue))
                await AbrirSelectorDestinoAsync();
        }

        // Pasa la tarima de embalaje a Estado 2 y avisa el resultado.
        private async Task CerrarTarimaEmbalajeAsync(TarimaEmbalaje tarima)
        {
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
                try { Vibration.Vibrate(150); } catch { }
                await AvisarAsync("✅", "#DCFCE7", "Tarima de embalaje cerrada",
                    $"La Tarima #{tarima.NoTarimaEmbalada} ({tarima.Codigo}) quedó lista para enviar a {NombreSucursalTexto}.",
                    "Su tarjeta ya quedó libre para usarla en otra tarima.");
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

        // Antes de guardar un producto: debe haber tarima de embalaje. Normalmente ya se eligió
        // en el paso 2; si no, se vuelve a mostrar.
        private async Task<bool> AsegurarDestinoAsync()
        {
            if (_tarimaEmbalaje != null) return true;
            await AbrirSelectorDestinoAsync();
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
                await DisplayAlert("Tarima de embalaje cerrada",
                    ex.Message + "\n\nElija otra tarima de embalaje y vuelva a confirmar el producto.", "OK");
                await AbrirSelectorDestinoAsync();
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
