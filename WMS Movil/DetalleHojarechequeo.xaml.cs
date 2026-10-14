using MySqlConnector;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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

    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class DetalleHojarechequeo : ContentPage
    {
        private readonly int idPedido;
        private readonly int numeroTarima;
        private List<DetalleProducto> todosLosProductos = new List<DetalleProducto>();

        // RevisadoOk: null = sin revisar, true = marcado como bien (✓), false = marcado como mal (✗).
        // Se guarda en la tabla TarimasRechequeadas al tocar cualquiera de los dos botones.
        public class DetalleProducto : INotifyPropertyChanged
        {
            public string Upc { get; set; }
            public string UpcUnidad { get; set; }
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

        public DetalleHojarechequeo(int idPedido, int? numeroTarima)
        {
            InitializeComponent();
            this.idPedido = idPedido;
            this.numeroTarima = numeroTarima ?? 0;

            InfoPedidoLabel.Text = $"Pedido #{idPedido}  •  Tarima #{this.numeroTarima}";
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            CargarInfoPedido();
            CargarProductos();
        }

        private string BuildConnectionString() =>
            $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

        private async void OnBackClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        private async void CargarInfoPedido()
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
                            SELECT pb.NombreEmpresa, COALESCE(dep.Nombre, '') AS NombreDepartamento
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
                await GuardarRechequeo(producto, producto.CantidadConfirmada, "");
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
                await GuardarRechequeo(producto, cantidadEncontrada, comentario.Trim());
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
        // GUARDAR EN TarimasRechequeadas (insert o update)
        // ══════════════════════════════════════

        private async Task GuardarRechequeo(DetalleProducto producto, int cantidadChequeada, string observacion)
        {
            int idUsuarioActual = App.IdUsuarioActual;
            string nombreUsuarioActual = App.NombreUsuarioActual ?? "";

            await TomarTarimaParaRechequeo();

            await Task.Run(() =>
            {
                using (var connection = new MySqlConnection(BuildConnectionString()))
                {
                    connection.Open();

                    int? idExistente = null;
                    using (var cmdBuscar = new MySqlCommand(@"
                        SELECT IdRechequeo FROM TarimasRechequeadas
                        WHERE IdPedido = @IdPedido AND NoTarima = @NoTarima AND UpcPaquete = @UpcPaquete
                        LIMIT 1", connection))
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
                            WHERE IdRechequeo = @IdRechequeo", connection))
                        {
                            AgregarParametrosComunes(cmdUpdate, producto, cantidadChequeada, observacion,
                                idUsuarioActual, nombreUsuarioActual);
                            cmdUpdate.Parameters.AddWithValue("@IdRechequeo", idExistente.Value);
                            cmdUpdate.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        using (var cmdInsert = new MySqlCommand(@"
                            INSERT INTO TarimasRechequeadas
                                (UpcPaquete, UpcUnidad, Descripcion, CantidadUniFardo, CantidadPreparada,
                                 CantidadChequeada, IdPedido, NoTarima, IdUsuarioPreparo, NombreUsuarioPreparo,
                                 IdUsuarioChequeo, NombreUsuarioChequeo, FechahoraChequeo, Observacion)
                            VALUES
                                (@UpcPaquete, @UpcUnidad, @Descripcion, @CantidadUniFardo, @CantidadPreparada,
                                 @CantidadChequeada, @IdPedido, @NoTarima, @IdUsuarioPreparo, @NombreUsuarioPreparo,
                                 @IdUsuarioChequeo, @NombreUsuarioChequeo, NOW(), @Observacion)", connection))
                        {
                            cmdInsert.Parameters.AddWithValue("@UpcPaquete", producto.Upc);
                            AgregarParametrosComunes(cmdInsert, producto, cantidadChequeada, observacion,
                                idUsuarioActual, nombreUsuarioActual);
                            cmdInsert.Parameters.AddWithValue("@IdPedido", idPedido);
                            cmdInsert.Parameters.AddWithValue("@NoTarima", numeroTarima);
                            cmdInsert.ExecuteNonQuery();
                        }
                    }
                }
            });
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
