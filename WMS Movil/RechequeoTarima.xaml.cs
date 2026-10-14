using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WMS_Movil.Servicio;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class RechequeoTarima : ContentPage
    {
        public enum EstadoProducto { Pendiente, Chequeado, Traslado }

        public class ProductoRechequeo
        {
            public string UPC { get; set; }
            public string Descripcion { get; set; }
            public decimal CantidadConfirmada { get; set; }
            public decimal CantidadFisica { get; set; }
            public EstadoProducto Estado { get; set; } = EstadoProducto.Pendiente;

            // Solo para productos repartidos en varias tarimas (NoTarima = 0 en el detalle)
            public int NoHoja { get; set; }
            public bool EsRepartido { get; set; }
            public string NotaReparto { get; set; }
        }

        private int _idTarima;
        private int _noTarima;
        private int _idPedido;
        private string _nombreEmpresa;

        private int _idTarimaDestino;
        private int _noTarimaDestino;
        private int _idPedidoDestino;

        private List<ProductoRechequeo> _productos = new List<ProductoRechequeo>();

        private string BuildConnectionString() =>
            $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

        public RechequeoTarima()
        {
            InitializeComponent();
        }

        private async void OnBackClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        // ══════════════════════════════════════
        // BUSCAR TARIMA
        // ══════════════════════════════════════

        private async void OnQrTarimaCompleted(object sender, EventArgs e)
        {
            var texto = QrTarimaEntry.Text?.Trim();
            if (string.IsNullOrEmpty(texto)) return;
            if (!int.TryParse(texto, out int idTarima))
            {
                await DisplayAlert("QR inválido", "El QR debe contener el IdTarima numérico.", "OK");
                QrTarimaEntry.Text = "";
                return;
            }
            await BuscarTarima(idTarima);
        }

        private async void OnBuscarTarimaClicked(object sender, EventArgs e)
        {
            var texto = QrTarimaEntry.Text?.Trim();
            if (string.IsNullOrEmpty(texto))
            {
                await DisplayAlert("Atención", "Escanea o ingresa el QR de la tarima.", "OK");
                return;
            }
            if (!int.TryParse(texto, out int idTarima))
            {
                await DisplayAlert("QR inválido", "El QR debe contener el IdTarima numérico.", "OK");
                QrTarimaEntry.Text = "";
                return;
            }
            await BuscarTarima(idTarima);
        }

        private async Task BuscarTarima(int idTarima)
        {
            try
            {
                int noTarima = 0, idPedido = 0;
                string nombreEmpresa = "";
                var productos = new List<ProductoRechequeo>();

                await Task.Run(() =>
                {
                    using (var conn = new MySqlConnection(BuildConnectionString()))
                    {
                        conn.Open();

                        using (var cmd = new MySqlCommand(@"
                            SELECT ti.NoTarima, ti.IdPedido,
                                   COALESCE(pb.NombreEmpresa, '') AS NombreEmpresa
                            FROM TarimasInventario ti
                            LEFT JOIN pedidostienda_bodega pb ON pb.IdPedidos = ti.IdPedido
                            WHERE ti.IdTarima = @IdTarima", conn))
                        {
                            cmd.Parameters.AddWithValue("@IdTarima", idTarima);
                            using (var r = cmd.ExecuteReader())
                            {
                                if (r.Read())
                                {
                                    noTarima = r.GetInt32("NoTarima");
                                    idPedido = r.GetInt32("IdPedido");
                                    nombreEmpresa = r.GetString("NombreEmpresa");
                                }
                            }
                        }

                        if (noTarima == 0) return;

                        using (var cmd = new MySqlCommand(@"
                            SELECT UPC, Descripcion,
                                   COALESCE(CantConfirmada, 0) AS CantConfirmada
                            FROM detallepedidostienda_bodega
                            WHERE IdConsolidado = @IdPedido AND NoTarima = @NoTarima
                            ORDER BY Descripcion", conn))
                        {
                            cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                            cmd.Parameters.AddWithValue("@NoTarima", noTarima);
                            using (var r = cmd.ExecuteReader())
                            {
                                while (r.Read())
                                {
                                    productos.Add(new ProductoRechequeo
                                    {
                                        UPC = r.GetString("UPC"),
                                        Descripcion = r.IsDBNull(r.GetOrdinal("Descripcion"))
                                            ? r.GetString("UPC") : r.GetString("Descripcion"),
                                        CantidadConfirmada = r.IsDBNull(r.GetOrdinal("CantConfirmada"))
                                            ? 0 : Convert.ToDecimal(r["CantConfirmada"]),
                                        Estado = EstadoProducto.Pendiente
                                    });
                                }
                            }
                        }

                        // Productos repartidos que tienen una parte en esta tarima
                        foreach (var rep in RepartoTarimas.ObtenerRepartidos(conn, null, idPedido))
                        {
                            int cantidadAqui = rep.CantidadEn(noTarima);
                            if (cantidadAqui <= 0) continue;

                            productos.Add(new ProductoRechequeo
                            {
                                UPC = rep.Upc,
                                Descripcion = rep.Descripcion,
                                CantidadConfirmada = cantidadAqui,
                                Estado = EstadoProducto.Pendiente,
                                NoHoja = rep.NoHoja,
                                EsRepartido = true,
                                NotaReparto = rep.ResumenPara(noTarima)
                            });
                        }
                        productos = productos.OrderBy(p => p.Descripcion).ToList();
                    }
                });

                if (noTarima == 0)
                {
                    await DisplayAlert("No encontrada", "No se encontró una tarima con ese QR.", "OK");
                    QrTarimaEntry.Text = "";
                    QrTarimaEntry.Focus();
                    return;
                }

                if (!productos.Any())
                {
                    await DisplayAlert("Tarima vacía", "Esta tarima no tiene productos asignados.", "OK");
                    QrTarimaEntry.Text = "";
                    QrTarimaEntry.Focus();
                    return;
                }

                _idTarima = idTarima;
                _noTarima = noTarima;
                _idPedido = idPedido;
                _nombreEmpresa = nombreEmpresa;
                _productos = productos;

                Device.BeginInvokeOnMainThread(() =>
                {
                    InfoTarimaLabel.Text = $"Tarima #{noTarima}  •  Pedido #{idPedido}  •  {nombreEmpresa}";
                    PanelScanTarima.IsVisible = false;
                    PanelInfoTarima.IsVisible = true;
                    PanelProductos.IsVisible = true;
                    BottomArea.IsVisible = true;
                    RefrescarLista();
                    UpcEntry.Focus();
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        // ══════════════════════════════════════
        // ESCANEO DE PRODUCTOS
        // ══════════════════════════════════════

        private async void OnUpcCompleted(object sender, EventArgs e)
        {
            var upc = UpcEntry.Text?.Trim();
            UpcEntry.Text = "";

            if (string.IsNullOrEmpty(upc))
            {
                UpcEntry.Focus();
                return;
            }

            var producto = _productos.FirstOrDefault(p => p.UPC == upc);
            if (producto == null)
            {
                MostrarErrorUpc();
                UpcEntry.Focus();
                return;
            }

            switch (producto.Estado)
            {
                case EstadoProducto.Pendiente:
                    // 1er scan → pedir cantidad
                    await PedirCantidad(producto);
                    break;

                case EstadoProducto.Chequeado:
                    // 2do scan → marcar para traslado
                    producto.Estado = EstadoProducto.Traslado;
                    producto.CantidadFisica = 0;
                    RefrescarLista();
                    break;

                case EstadoProducto.Traslado:
                    // 3er scan → vuelve a pendiente para re-chequear
                    producto.Estado = EstadoProducto.Pendiente;
                    RefrescarLista();
                    break;
            }

            UpcEntry.Focus();
        }

        private async Task PedirCantidad(ProductoRechequeo producto)
        {
            string mensaje = $"{producto.Descripcion}\n\nCantidad en sistema: {producto.CantidadConfirmada}";
            if (producto.EsRepartido)
                mensaje += $"\n\n{producto.NotaReparto}";

            string resultado = await DisplayPromptAsync(
                "Cantidad física",
                mensaje,
                placeholder: "Ingresa la cantidad física...",
                initialValue: producto.CantidadConfirmada.ToString("0.##"),
                keyboard: Keyboard.Numeric);

            if (resultado == null) // usuario canceló
            {
                UpcEntry.Focus();
                return;
            }

            if (!decimal.TryParse(resultado.Trim(), out decimal cantidad) || cantidad < 0)
            {
                await DisplayAlert("Cantidad inválida", "Ingresa un número válido.", "OK");
                await PedirCantidad(producto);
                return;
            }

            producto.CantidadFisica = cantidad;
            producto.Estado = EstadoProducto.Chequeado;
            RefrescarLista();
        }

        private void MostrarErrorUpc()
        {
            UpcFrame.BackgroundColor = Color.FromHex("#FEE2E2");
            UpcFrame.BorderColor = Color.FromHex("#FCA5A5");
            Device.StartTimer(TimeSpan.FromSeconds(1.5), () =>
            {
                UpcFrame.BackgroundColor = Color.FromHex("#EFF6FF");
                UpcFrame.BorderColor = Color.FromHex("#BFDBFE");
                return false;
            });
        }

        // ══════════════════════════════════════
        // REFRESCAR LISTA Y ESTADO
        // ══════════════════════════════════════

        private void RefrescarLista()
        {
            ProductosLayout.Children.Clear();

            foreach (var p in _productos)
            {
                string bgColor = p.Estado == EstadoProducto.Chequeado ? "#D1FAE5"
                               : p.Estado == EstadoProducto.Traslado  ? "#FEF3C7"
                               : "#F9FAFB";
                string icono = p.Estado == EstadoProducto.Chequeado ? "✅"
                             : p.Estado == EstadoProducto.Traslado  ? "🔄"
                             : "○";

                var fila = new Frame
                {
                    BackgroundColor = Color.FromHex(bgColor),
                    CornerRadius = 10,
                    Padding = new Thickness(12, 10),
                    HasShadow = false,
                    Margin = new Thickness(0, 2)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Info izquierda
                var info = new StackLayout { Spacing = 5 };
                info.Children.Add(new Label
                {
                    Text = p.Descripcion,
                    TextColor = Color.FromHex("#1F2937"),
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    LineBreakMode = LineBreakMode.TailTruncation
                });

                var chips = new StackLayout { Orientation = StackOrientation.Horizontal, Spacing = 8 };

                // Chip UPC
                var upcFrame = new Frame
                {
                    BackgroundColor = Color.FromHex("#EEF2FF"),
                    CornerRadius = 6, Padding = new Thickness(8, 3), HasShadow = false
                };
                var upcText = new FormattedString();
                upcText.Spans.Add(new Span { Text = "UPC  ", TextColor = Color.FromHex("#818CF8"), FontSize = 10 });
                upcText.Spans.Add(new Span { Text = p.UPC, TextColor = Color.FromHex("#3730A3"), FontSize = 12, FontAttributes = FontAttributes.Bold });
                upcFrame.Content = new Label { FormattedText = upcText };
                chips.Children.Add(upcFrame);

                // Chip cantidad (sistema vs física)
                if (p.Estado == EstadoProducto.Chequeado)
                {
                    var cantFrame = new Frame
                    {
                        BackgroundColor = Color.FromHex("#D1FAE5"),
                        CornerRadius = 6, Padding = new Thickness(8, 3), HasShadow = false
                    };
                    var cantText = new FormattedString();
                    cantText.Spans.Add(new Span { Text = "Físico  ", TextColor = Color.FromHex("#059669"), FontSize = 10 });
                    cantText.Spans.Add(new Span { Text = p.CantidadFisica.ToString("0.##"), TextColor = Color.FromHex("#065F46"), FontSize = 13, FontAttributes = FontAttributes.Bold });
                    cantFrame.Content = new Label { FormattedText = cantText };
                    chips.Children.Add(cantFrame);
                }
                else
                {
                    var cantFrame = new Frame
                    {
                        BackgroundColor = Color.FromHex("#FEF3C7"),
                        CornerRadius = 6, Padding = new Thickness(8, 3), HasShadow = false
                    };
                    var cantText = new FormattedString();
                    cantText.Spans.Add(new Span { Text = "Sistema  ", TextColor = Color.FromHex("#F59E0B"), FontSize = 10 });
                    cantText.Spans.Add(new Span { Text = p.CantidadConfirmada.ToString("0.##"), TextColor = Color.FromHex("#92400E"), FontSize = 13, FontAttributes = FontAttributes.Bold });
                    cantFrame.Content = new Label { FormattedText = cantText };
                    chips.Children.Add(cantFrame);
                }

                info.Children.Add(chips);

                if (p.EsRepartido)
                {
                    info.Children.Add(new Label
                    {
                        Text = p.NotaReparto,
                        TextColor = Color.FromHex("#065F46"),
                        FontSize = 11,
                        FontAttributes = FontAttributes.Bold,
                        LineBreakMode = LineBreakMode.WordWrap
                    });
                }

                var iconoLabel = new Label
                {
                    Text = icono,
                    FontSize = 22,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.End,
                    Margin = new Thickness(8, 0, 0, 0)
                };

                Grid.SetColumn(info, 0);
                Grid.SetColumn(iconoLabel, 1);
                grid.Children.Add(info);
                grid.Children.Add(iconoLabel);
                fila.Content = grid;
                ProductosLayout.Children.Add(fila);
            }

            ActualizarEstadoBottom();
        }

        private void ActualizarEstadoBottom()
        {
            int total = _productos.Count;
            int chequeados = _productos.Count(p => p.Estado == EstadoProducto.Chequeado);
            int traslados = _productos.Count(p => p.Estado == EstadoProducto.Traslado);
            int pendientes = _productos.Count(p => p.Estado == EstadoProducto.Pendiente);
            int verificados = chequeados + traslados;

            ContadorLabel.Text = $"{verificados} de {total} verificados  •  {pendientes} pendientes";
            ContadorBadge.Text = $"{verificados}/{total}";

            ContadorFrame.BackgroundColor = pendientes == 0
                ? Color.FromHex("#D1FAE5")
                : Color.FromHex("#FEF3C7");
            ContadorBadge.TextColor = pendientes == 0
                ? Color.FromHex("#059669")
                : Color.FromHex("#D97706");

            // Paneles bottom
            PanelProgreso.IsVisible = pendientes > 0;
            PanelFinalizarSin.IsVisible = pendientes == 0 && traslados == 0;
            PanelFinalizarCon.IsVisible = pendientes == 0 && traslados > 0;
            PanelScanDestino.IsVisible = false;
            PanelConfirmarFinal.IsVisible = false;

            if (pendientes > 0)
                ProgresoLabel.Text = $"Faltan {pendientes} producto(s) por verificar";

            if (traslados > 0)
                ResumenTrasladoLabel.Text = $"{traslados} producto(s) marcados para traslado";
        }

        // ══════════════════════════════════════
        // FINALIZAR SIN TRASLADOS
        // ══════════════════════════════════════

        private async void OnFinalizarClicked(object sender, EventArgs e)
        {
            int chequeados = _productos.Count(p => p.Estado == EstadoProducto.Chequeado);
            bool ok = await DisplayAlert("Finalizar Rechequeo",
                $"¿Confirmas el rechequeo de {chequeados} producto(s) en Tarima #{_noTarima}?",
                "Sí, finalizar", "Cancelar");
            if (!ok) return;

            await EjecutarRechequeo(sinTraslado: true);
        }

        // ══════════════════════════════════════
        // FLUJO CON TRASLADOS
        // ══════════════════════════════════════

        private void OnIrADestinoClicked(object sender, EventArgs e)
        {
            PanelFinalizarCon.IsVisible = false;
            PanelScanDestino.IsVisible = true;
            QrDestinoEntry.Focus();
        }

        private async void OnQrDestinoCompleted(object sender, EventArgs e)
        {
            var texto = QrDestinoEntry.Text?.Trim();
            if (string.IsNullOrEmpty(texto)) return;
            if (!int.TryParse(texto, out int idTarima))
            {
                await DisplayAlert("QR inválido", "El QR debe contener el IdTarima numérico.", "OK");
                QrDestinoEntry.Text = "";
                return;
            }
            await BuscarTarimaDestino(idTarima);
        }

        private async void OnBuscarDestinoClicked(object sender, EventArgs e)
        {
            var texto = QrDestinoEntry.Text?.Trim();
            if (string.IsNullOrEmpty(texto))
            {
                await DisplayAlert("Atención", "Escanea o ingresa el QR de la tarima destino.", "OK");
                return;
            }
            if (!int.TryParse(texto, out int idTarima))
            {
                await DisplayAlert("QR inválido", "El QR debe contener el IdTarima numérico.", "OK");
                QrDestinoEntry.Text = "";
                return;
            }
            await BuscarTarimaDestino(idTarima);
        }

        private async Task BuscarTarimaDestino(int idTarima)
        {
            if (idTarima == _idTarima)
            {
                await DisplayAlert("Error", "La tarima destino no puede ser la misma que la origen.", "OK");
                QrDestinoEntry.Text = "";
                QrDestinoEntry.Focus();
                return;
            }

            try
            {
                int noTarima = 0, idPedidoDestino = 0;
                await Task.Run(() =>
                {
                    using (var conn = new MySqlConnection(BuildConnectionString()))
                    {
                        conn.Open();
                        using (var cmd = new MySqlCommand(
                            "SELECT NoTarima, IdPedido FROM TarimasInventario WHERE IdTarima = @IdTarima", conn))
                        {
                            cmd.Parameters.AddWithValue("@IdTarima", idTarima);
                            using (var r = cmd.ExecuteReader())
                            {
                                if (r.Read())
                                {
                                    noTarima = r.GetInt32("NoTarima");
                                    idPedidoDestino = r.GetInt32("IdPedido");
                                }
                            }
                        }
                    }
                });

                if (noTarima == 0)
                {
                    await DisplayAlert("No encontrada", "No se encontró la tarima destino.", "OK");
                    QrDestinoEntry.Text = "";
                    QrDestinoEntry.Focus();
                    return;
                }

                _idTarimaDestino = idTarima;
                _noTarimaDestino = noTarima;
                _idPedidoDestino = idPedidoDestino;

                int chequeados = _productos.Count(p => p.Estado == EstadoProducto.Chequeado);
                int traslados = _productos.Count(p => p.Estado == EstadoProducto.Traslado);

                Device.BeginInvokeOnMainThread(() =>
                {
                    BadgeOrigenLabel.Text = $"Tarima #{_noTarima}";
                    BadgeDestinoLabel.Text = $"Tarima #{noTarima}";
                    ResumenFinalLabel.Text = $"{chequeados} chequeado(s)  •  {traslados} a trasladar";
                    PanelScanDestino.IsVisible = false;
                    PanelConfirmarFinal.IsVisible = true;
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        private void OnCambiarDestinoClicked(object sender, EventArgs e)
        {
            _idTarimaDestino = 0;
            _noTarimaDestino = 0;
            _idPedidoDestino = 0;
            QrDestinoEntry.Text = "";
            PanelConfirmarFinal.IsVisible = false;
            PanelScanDestino.IsVisible = true;
            QrDestinoEntry.Focus();
        }

        private async void OnConfirmarTodoClicked(object sender, EventArgs e)
        {
            int traslados = _productos.Count(p => p.Estado == EstadoProducto.Traslado);

            // Un producto repartido es una sola fila del pedido: sus partes solo pueden
            // moverse entre tarimas del mismo pedido.
            if (_idPedidoDestino != _idPedido
                && _productos.Any(p => p.Estado == EstadoProducto.Traslado && p.EsRepartido))
            {
                await DisplayAlert("Traslado no permitido",
                    $"La Tarima #{_noTarimaDestino} es de otro pedido. Los productos repartidos en varias tarimas " +
                    "solo se pueden trasladar a tarimas del mismo pedido.", "OK");
                return;
            }

            bool ok = await DisplayAlert("Confirmar todo",
                $"¿Confirmas el rechequeo y el traslado de {traslados} producto(s) a Tarima #{_noTarimaDestino}?",
                "Sí, confirmar", "Cancelar");
            if (!ok) return;

            await EjecutarRechequeo(sinTraslado: false);
        }

        // ══════════════════════════════════════
        // EJECUTAR EN BD
        // ══════════════════════════════════════

        private async Task EjecutarRechequeo(bool sinTraslado)
        {
            try
            {
                var chequeados = _productos.Where(p => p.Estado == EstadoProducto.Chequeado).ToList();
                var traslados = _productos.Where(p => p.Estado == EstadoProducto.Traslado).ToList();

                int idTarima = _idTarima;
                int noTarima = _noTarima;
                int idPedido = _idPedido;
                int idTarimaDestino = _idTarimaDestino;
                int noTarimaDestino = _noTarimaDestino;
                int idUsuario = App.IdUsuarioActual;
                string nombreUsuario = App.NombreUsuarioActual ?? "Usuario";

                await Task.Run(() =>
                {
                    using (var conn = new MySqlConnection(BuildConnectionString()))
                    {
                        conn.Open();
                        using (var tr = conn.BeginTransaction())
                        {
                            try
                            {
                                // INSERT en ChequeosPedidos para cada producto chequeado
                                foreach (var p in chequeados)
                                {
                                    using (var cmd = new MySqlCommand(@"
                                        INSERT INTO ChequeosPedidos
                                            (IdPedido, IdTarima, NoTarima, UpcPaquete, UpcUnidad,
                                             Descripcion, CantidadConfirmada, CantidadFisica,
                                             IdUsuario, NombreUsuario, FechaHoraChequeo)
                                        VALUES
                                            (@IdPedido, @IdTarima, @NoTarima, @UpcPaquete, @UpcUnidad,
                                             @Descripcion, @CantidadConfirmada, @CantidadFisica,
                                             @IdUsuario, @NombreUsuario, NOW())", conn, tr))
                                    {
                                        cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                        cmd.Parameters.AddWithValue("@IdTarima", idTarima);
                                        cmd.Parameters.AddWithValue("@NoTarima", noTarima);
                                        cmd.Parameters.AddWithValue("@UpcPaquete", p.UPC);
                                        cmd.Parameters.AddWithValue("@UpcUnidad", p.UPC);
                                        cmd.Parameters.AddWithValue("@Descripcion", p.Descripcion);
                                        cmd.Parameters.AddWithValue("@CantidadConfirmada", p.CantidadConfirmada);
                                        cmd.Parameters.AddWithValue("@CantidadFisica", p.CantidadFisica);
                                        cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                                        cmd.Parameters.AddWithValue("@NombreUsuario", nombreUsuario);
                                        cmd.ExecuteNonQuery();
                                    }
                                }

                                // Si hay traslados → mover productos a tarima destino
                                if (!sinTraslado && traslados.Any())
                                {
                                    var normales = traslados.Where(p => !p.EsRepartido).ToList();
                                    if (normales.Any())
                                    {
                                        var inParams = string.Join(",", normales.Select((_, i) => $"@upc{i}"));
                                        using (var cmd = new MySqlCommand($@"
                                            UPDATE detallepedidostienda_bodega
                                            SET NoTarima = @NoTarimaDestino
                                            WHERE IdConsolidado = @IdPedido
                                              AND NoTarima = @NoTarimaOrigen
                                              AND UPC IN ({inParams})", conn, tr))
                                        {
                                            cmd.Parameters.AddWithValue("@NoTarimaDestino", noTarimaDestino);
                                            cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                            cmd.Parameters.AddWithValue("@NoTarimaOrigen", noTarima);
                                            for (int i = 0; i < normales.Count; i++)
                                                cmd.Parameters.AddWithValue($"@upc{i}", normales[i].UPC);
                                            cmd.ExecuteNonQuery();
                                        }
                                    }

                                    // Repartidos: se mueve su parte dentro de la etiqueta [TARIMAS:...]
                                    var repartidos = traslados.Where(p => p.EsRepartido).ToList();
                                    if (repartidos.Any())
                                    {
                                        var enBd = RepartoTarimas.ObtenerRepartidos(conn, tr, idPedido);
                                        foreach (var p in repartidos)
                                        {
                                            var rep = enBd.FirstOrDefault(r => r.Upc == p.UPC && r.NoHoja == p.NoHoja);
                                            if (rep == null) continue;
                                            RepartoTarimas.GuardarPartes(conn, tr, idPedido, rep,
                                                RepartoTarimas.Trasladar(rep.Partes, noTarima, noTarimaDestino));
                                        }
                                    }

                                    RepartoTarimas.RecalcularTarima(conn, tr, idTarima, idPedido, noTarima);
                                    RepartoTarimas.RecalcularTarima(conn, tr, idTarimaDestino, idPedido, noTarimaDestino);
                                }

                                tr.Commit();
                            }
                            catch
                            {
                                tr.Rollback();
                                throw;
                            }
                        }
                    }
                });

                string msg = sinTraslado
                    ? $"Rechequeo completado. {chequeados.Count} producto(s) registrados."
                    : $"Rechequeo completado.\n{chequeados.Count} registrado(s)  •  {traslados.Count} trasladado(s) a Tarima #{_noTarimaDestino}.";

                await DisplayAlert("Completado", msg, "OK");
                ReiniciarPantalla();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error al guardar", ex.Message, "OK");
            }
        }

        // ══════════════════════════════════════
        // CANCELAR / REINICIAR
        // ══════════════════════════════════════

        private async void OnCancelarClicked(object sender, EventArgs e)
        {
            if (_productos.Any(p => p.Estado != EstadoProducto.Pendiente))
            {
                bool ok = await DisplayAlert("Cancelar rechequeo",
                    "¿Deseas cancelar? El progreso no se guardará.", "Sí, cancelar", "Continuar");
                if (!ok) return;
            }
            ReiniciarPantalla();
        }

        private void ReiniciarPantalla()
        {
            _idTarima = 0; _noTarima = 0; _idPedido = 0;
            _idTarimaDestino = 0; _noTarimaDestino = 0; _idPedidoDestino = 0;
            _productos.Clear();

            Device.BeginInvokeOnMainThread(() =>
            {
                QrTarimaEntry.Text = "";
                QrDestinoEntry.Text = "";
                UpcEntry.Text = "";
                ProductosLayout.Children.Clear();

                PanelScanTarima.IsVisible = true;
                PanelInfoTarima.IsVisible = false;
                PanelProductos.IsVisible = false;
                BottomArea.IsVisible = false;
                PanelProgreso.IsVisible = false;
                PanelFinalizarSin.IsVisible = false;
                PanelFinalizarCon.IsVisible = false;
                PanelScanDestino.IsVisible = false;
                PanelConfirmarFinal.IsVisible = false;

                QrTarimaEntry.Focus();
            });
        }
    }
}
