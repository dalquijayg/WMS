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
    public partial class Embalaje : ContentPage
    {
        public class ProductoTarima
        {
            public string UPC { get; set; }
            public string UPCProducto { get; set; }
            public string Descripcion { get; set; }
            public decimal Cantidad { get; set; }
            public bool Seleccionado { get; set; } = false;

            // Solo para productos repartidos en varias tarimas (NoTarima = 0 en el detalle)
            public int NoHoja { get; set; }
            public bool EsRepartido { get; set; }
            public string NotaReparto { get; set; }
        }

        private int _idTarimaOrigen;
        private int _noTarimaOrigen;
        private int _idPedidoOrigen;
        private int _idTarimaVacia;
        private int _noTarimaVacia;
        private int _idPedidoVacia;

        private int _idTarimaDestino;
        private int _noTarimaDestino;
        private int _idPedidoDestino;

        private List<ProductoTarima> _productos = new List<ProductoTarima>();

        // pedidostienda_bodega.Estado de un pedido con todas sus hojas preparadas
        private const int EstadoPedidoCompleto = 7;

        private string BuildConnectionString() =>
            $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

        public Embalaje()
        {
            InitializeComponent();
        }

        private async void OnBackClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        // ══════════════════════════════════════
        // BUSCAR TARIMA ORIGEN
        // ══════════════════════════════════════

        private async void OnQrOrigenCompleted(object sender, EventArgs e)
        {
            var texto = QrOrigenEntry.Text?.Trim();
            if (string.IsNullOrEmpty(texto)) return;
            if (!int.TryParse(texto, out int idTarima))
            {
                await DisplayAlert("QR inválido", "El QR debe contener el IdTarima numérico.", "OK");
                QrOrigenEntry.Text = "";
                return;
            }
            await BuscarTarimaOrigen(idTarima);
        }

        private async void OnBuscarOrigenClicked(object sender, EventArgs e)
        {
            var texto = QrOrigenEntry.Text?.Trim();
            if (string.IsNullOrEmpty(texto))
            {
                await DisplayAlert("Atención", "Escanea o ingresa el QR de la tarima.", "OK");
                return;
            }
            if (!int.TryParse(texto, out int idTarima))
            {
                await DisplayAlert("QR inválido", "El QR debe contener el IdTarima numérico.", "OK");
                QrOrigenEntry.Text = "";
                return;
            }
            await BuscarTarimaOrigen(idTarima);
        }

        private async Task BuscarTarimaOrigen(int idTarima)
        {
            LoadingOverlay.IsVisible = true;
            try
            {
                int noTarima = 0, idPedido = 0, estadoPedido = 0;
                string nombreEmpresa = "";
                var productos = new List<ProductoTarima>();
                int? idInventario = null, estadoInventario = null;

                await Task.Run(() =>
                {
                    using (var conn = new MySqlConnection(BuildConnectionString()))
                    {
                        conn.Open();

                        using (var cmd = new MySqlCommand(@"
                            SELECT ti.NoTarima, ti.IdPedido,
                                   COALESCE(pb.NombreEmpresa, '') AS NombreEmpresa,
                                   COALESCE(pb.Estado, 0) AS EstadoPedido
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
                                    estadoPedido = Convert.ToInt32(r["EstadoPedido"]);
                                }
                            }
                        }

                        // Solo se embala cuando el pedido está completo
                        if (noTarima == 0 || estadoPedido != EstadoPedidoCompleto) return;

                        (idInventario, estadoInventario) =
                            RepartoTarimas.LeerEstadoInventarioTarima(conn, null, idTarima);

                        using (var cmd = new MySqlCommand(@"
                            SELECT UPC, UPCProducto, Descripcion,
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
                                    productos.Add(new ProductoTarima
                                    {
                                        UPC = r.GetString("UPC"),
                                        UPCProducto = r.IsDBNull(r.GetOrdinal("UPCProducto"))
                                            ? "" : r.GetString("UPCProducto"),
                                        Descripcion = r.IsDBNull(r.GetOrdinal("Descripcion"))
                                            ? r.GetString("UPC") : r.GetString("Descripcion"),
                                        Cantidad = r.IsDBNull(r.GetOrdinal("CantConfirmada"))
                                            ? 0 : Convert.ToDecimal(r["CantConfirmada"])
                                    });
                                }
                            }
                        }

                        // Productos repartidos que tienen una parte en esta tarima
                        foreach (var rep in RepartoTarimas.ObtenerRepartidos(conn, null, idPedido))
                        {
                            int cantidadAqui = rep.CantidadEn(noTarima);
                            if (cantidadAqui <= 0) continue;

                            productos.Add(new ProductoTarima
                            {
                                UPC = rep.Upc,
                                UPCProducto = rep.UpcProducto,
                                Descripcion = rep.Descripcion,
                                Cantidad = cantidadAqui,
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
                    LoadingOverlay.IsVisible = false;
                    await DisplayAlert("No encontrada", "No se encontró una tarima con ese QR.", "OK");
                    QrOrigenEntry.Text = "";
                    QrOrigenEntry.Focus();
                    return;
                }

                if (estadoPedido != EstadoPedidoCompleto)
                {
                    LoadingOverlay.IsVisible = false;
                    await DisplayAlert("Pedido incompleto",
                        $"El pedido #{idPedido} todavía no está completo.\nSolo se puede embalar cuando todas sus hojas están preparadas.", "OK");
                    QrOrigenEntry.Text = "";
                    QrOrigenEntry.Focus();
                    return;
                }

                if (!productos.Any())
                {
                    _idTarimaVacia  = idTarima;
                    _noTarimaVacia  = noTarima;
                    _idPedidoVacia  = idPedido;
                    LoadingOverlay.IsVisible = false;
                    TarimaVaciaInfoLabel.Text = $"Tarima #{noTarima}  •  Pedido #{idPedido}  •  {nombreEmpresa}";
                    PanelScanOrigen.IsVisible  = false;
                    PanelTarimaVacia.IsVisible = true;
                    return;
                }

                if (estadoInventario.HasValue && estadoInventario.Value != 0)
                {
                    LoadingOverlay.IsVisible = false;
                    await DisplayAlert("Inventario confirmado",
                        $"El inventario #{idInventario} de la Tarima #{noTarima} ya está confirmado.\nNo se pueden trasladar productos de esta tarima.", "OK");
                    QrOrigenEntry.Text = "";
                    QrOrigenEntry.Focus();
                    return;
                }

                _idTarimaOrigen = idTarima;
                _noTarimaOrigen = noTarima;
                _idPedidoOrigen = idPedido;
                _productos = productos;

                LoadingOverlay.IsVisible = false;
                InfoTarimaOrigenLabel.Text = $"Tarima #{noTarima}  •  Pedido #{idPedido}  •  {nombreEmpresa}";
                PanelScanOrigen.IsVisible = false;
                TopArea.IsVisible         = true;
                PanelProductos.IsVisible  = true;
                RefrescarLista();
                UpcEntry.Focus();
            }
            catch (Exception ex)
            {
                LoadingOverlay.IsVisible = false;
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        private void OnCambiarOrigenClicked(object sender, EventArgs e) => ReiniciarPantalla();

        // ══════════════════════════════════════
        // TARIMA VACÍA — ELIMINAR
        // ══════════════════════════════════════

        private async void OnEliminarTarimaClicked(object sender, EventArgs e)
        {
            bool ok = await DisplayAlert("Eliminar tarima",
                "¿Confirmas que deseas eliminar esta tarima vacía?\nLas tarimas restantes del pedido serán renumeradas automáticamente.",
                "Sí, eliminar", "Cancelar");
            if (!ok) return;

            LoadingOverlay.IsVisible = true;
            try
            {
                int idTarima  = _idTarimaVacia;
                int noTarima  = _noTarimaVacia;
                int idPedido  = _idPedidoVacia;

                await Task.Run(() =>
                {
                    using (var conn = new MySqlConnection(BuildConnectionString()))
                    {
                        conn.Open();
                        using (var tr = conn.BeginTransaction())
                        {
                            try
                            {
                                RepartoTarimas.EliminarTarimaVacia(conn, tr, idTarima, idPedido, noTarima);
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

                LoadingOverlay.IsVisible = false;
                await DisplayAlert("Eliminada", "La tarima fue eliminada y las restantes fueron renumeradas correctamente.", "OK");
                ReiniciarPantalla();
            }
            catch (Exception ex)
            {
                LoadingOverlay.IsVisible = false;
                await DisplayAlert("Error al eliminar", ex.Message, "OK");
            }
        }

        // ══════════════════════════════════════
        // TARIMA DESTINO
        // ══════════════════════════════════════

        private async void OnAbrirDestinoTapped(object sender, EventArgs e)
        {
            if (_idTarimaOrigen == 0)
            {
                await DisplayAlert("Atención", "Primero escanea la tarima origen.", "OK");
                return;
            }

            int seleccionados = _productos.Count(p => p.Seleccionado);
            DestinoOverlaySubLabel.Text = seleccionados > 0
                ? $"{seleccionados} producto(s) seleccionado(s) de Tarima #{_noTarimaOrigen}"
                : $"Origen: Tarima #{_noTarimaOrigen}";

            QrDestinoEntry.Text = "";
            PasoConfirmarTraslado.IsVisible = false;
            PasoEscanearDestino.IsVisible   = true;
            DestinoOverlay.IsVisible = true;
            QrDestinoEntry.Focus();
        }

        private void OnCerrarDestinoTapped(object sender, EventArgs e) => CerrarOverlayDestino();

        private void CerrarOverlayDestino()
        {
            QrDestinoEntry.Unfocus();
            QrDestinoEntry.Text = "";
            DestinoOverlay.IsVisible = false;
            PasoConfirmarTraslado.IsVisible = false;
            PasoEscanearDestino.IsVisible   = true;
            UpcEntry.Focus();
        }

        // Paso 2 de la ventana: resumen de lo que se trasladará + botón confirmar
        private void MostrarPasoConfirmar()
        {
            var seleccionados = _productos.Where(p => p.Seleccionado).ToList();

            ResumenOrigenLabel.Text  = $"#{_noTarimaOrigen}";
            ResumenDestinoLabel.Text = $"#{_noTarimaDestino}";
            ResumenOtroPedidoLabel.Text      = $"⚠ La tarima destino es de otro pedido (#{_idPedidoDestino})";
            ResumenOtroPedidoLabel.IsVisible = _idPedidoDestino != _idPedidoOrigen;
            ResumenCantidadLabel.Text = $"{seleccionados.Count} producto(s)";

            ResumenProductosLayout.Children.Clear();
            foreach (var p in seleccionados)
            {
                var fila = new Grid { ColumnSpacing = 8 };
                fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
                fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var desc = new Label
                {
                    Text = p.Descripcion,
                    TextColor = Color.FromHex("#374151"),
                    FontSize = 11,
                    LineBreakMode = LineBreakMode.TailTruncation,
                    VerticalOptions = LayoutOptions.Center
                };
                var cant = new Label
                {
                    Text = $"× {p.Cantidad:0.##}",
                    TextColor = Color.FromHex("#3730A3"),
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    VerticalOptions = LayoutOptions.Center
                };
                Grid.SetColumn(cant, 1);
                fila.Children.Add(desc);
                fila.Children.Add(cant);
                ResumenProductosLayout.Children.Add(fila);
            }
            // Hasta ~6 filas visibles; con más, la lista se desplaza
            ResumenProductosScroll.HeightRequest = Math.Min(seleccionados.Count * 22, 140);

            QrDestinoEntry.Unfocus();
            PasoEscanearDestino.IsVisible   = false;
            PasoConfirmarTraslado.IsVisible = true;
        }

        private async void OnConfirmarDesdeOverlayClicked(object sender, EventArgs e)
        {
            var seleccionados = _productos.Where(p => p.Seleccionado).ToList();
            if (!seleccionados.Any())
            {
                CerrarOverlayDestino();
                return;
            }

            if (!await ValidarTraslado(seleccionados))
            {
                // El destino no sirve para estos productos: volver a escanear otro
                OnQuitarDestinoClicked(this, EventArgs.Empty);
                PasoConfirmarTraslado.IsVisible = false;
                PasoEscanearDestino.IsVisible   = true;
                QrDestinoEntry.Focus();
                return;
            }

            DestinoOverlay.IsVisible = false;
            PasoConfirmarTraslado.IsVisible = false;
            PasoEscanearDestino.IsVisible   = true;
            await EjecutarTraslado(seleccionados);
        }

        // Botón atrás de Android: primero cierra la ventana de destino
        protected override bool OnBackButtonPressed()
        {
            if (DestinoOverlay.IsVisible)
            {
                CerrarOverlayDestino();
                return true;
            }
            return base.OnBackButtonPressed();
        }

        private void LimpiarYEnfocarDestino()
        {
            QrDestinoEntry.Text = "";
            if (DestinoOverlay.IsVisible) QrDestinoEntry.Focus();
        }

        private async void OnQrDestinoCompleted(object sender, EventArgs e)
        {
            var texto = QrDestinoEntry.Text?.Trim();
            if (string.IsNullOrEmpty(texto)) return;
            if (!int.TryParse(texto, out int idTarima))
            {
                await DisplayAlert("QR inválido", "El QR debe contener el IdTarima numérico.", "OK");
                LimpiarYEnfocarDestino();
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
                LimpiarYEnfocarDestino();
                return;
            }
            await BuscarTarimaDestino(idTarima);
        }

        private async Task BuscarTarimaDestino(int idTarima)
        {
            if (_idTarimaOrigen > 0 && idTarima == _idTarimaOrigen)
            {
                await DisplayAlert("Error", "La tarima destino no puede ser la misma que la origen.", "OK");
                LimpiarYEnfocarDestino();
                return;
            }

            try
            {
                int noTarima = 0;
                int idPedidoDestino = 0;
                int estadoPedidoDestino = 0;
                int idSucursalOrigen = 0;
                int idSucursalDestino = 0;
                int? idInventarioDestino = null, estadoInventarioDestino = null;

                await Task.Run(() =>
                {
                    using (var conn = new MySqlConnection(BuildConnectionString()))
                    {
                        conn.Open();

                        using (var cmd = new MySqlCommand(@"
                            SELECT ti.NoTarima, ti.IdPedido, COALESCE(pb.Estado, 0) AS EstadoPedido
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
                                    idPedidoDestino = r.GetInt32("IdPedido");
                                    estadoPedidoDestino = Convert.ToInt32(r["EstadoPedido"]);
                                }
                            }
                        }

                        if (noTarima > 0)
                            (idInventarioDestino, estadoInventarioDestino) =
                                RepartoTarimas.LeerEstadoInventarioTarima(conn, null, idTarima);

                        // Solo verificar sucursal si los pedidos son distintos
                        if (noTarima > 0 && idPedidoDestino != _idPedidoOrigen)
                        {
                            using (var cmd = new MySqlCommand(
                                "SELECT IdSucursal FROM pedidostienda_bodega WHERE IdPedidos = @Id", conn))
                            {
                                cmd.Parameters.AddWithValue("@Id", _idPedidoOrigen);
                                var res = cmd.ExecuteScalar();
                                if (res != null && res != DBNull.Value)
                                    idSucursalOrigen = Convert.ToInt32(res);
                            }

                            using (var cmd = new MySqlCommand(
                                "SELECT IdSucursal FROM pedidostienda_bodega WHERE IdPedidos = @Id", conn))
                            {
                                cmd.Parameters.AddWithValue("@Id", idPedidoDestino);
                                var res = cmd.ExecuteScalar();
                                if (res != null && res != DBNull.Value)
                                    idSucursalDestino = Convert.ToInt32(res);
                            }
                        }
                    }
                });

                if (noTarima == 0)
                {
                    await DisplayAlert("No encontrada", "No se encontró la tarima destino.", "OK");
                    LimpiarYEnfocarDestino();
                    return;
                }

                if (estadoPedidoDestino != EstadoPedidoCompleto)
                {
                    await DisplayAlert("Pedido incompleto",
                        $"La tarima #{noTarima} es del pedido #{idPedidoDestino}, que todavía no está completo.\nSolo se puede embalar entre pedidos completos.", "OK");
                    LimpiarYEnfocarDestino();
                    return;
                }

                if (idPedidoDestino != _idPedidoOrigen)
                {
                    if (idSucursalOrigen == 0 || idSucursalDestino == 0 || idSucursalOrigen != idSucursalDestino)
                    {
                        await DisplayAlert("Sucursal diferente",
                            $"La tarima #{noTarima} pertenece al pedido #{idPedidoDestino} de una sucursal diferente.\nSolo puedes trasladar entre tarimas del mismo pedido o de la misma sucursal.",
                            "OK");
                        LimpiarYEnfocarDestino();
                        return;
                    }
                }

                if (estadoInventarioDestino.HasValue && estadoInventarioDestino.Value != 0)
                {
                    await DisplayAlert("Inventario confirmado",
                        $"El inventario #{idInventarioDestino} de la Tarima #{noTarima} ya está confirmado.\nNo se pueden trasladar productos a esta tarima.", "OK");
                    LimpiarYEnfocarDestino();
                    return;
                }

                _idTarimaDestino  = idTarima;
                _noTarimaDestino  = noTarima;
                _idPedidoDestino  = idPedidoDestino;

                Device.BeginInvokeOnMainThread(() =>
                {
                    DestinoActivoLabel.Text      = $"Tarima #{noTarima}";
                    DestinoPedidoLabel.Text      = $"⚠ Otro pedido: #{idPedidoDestino}";
                    DestinoPedidoLabel.IsVisible = idPedidoDestino != _idPedidoOrigen;
                    PanelDestinoActivo.IsVisible = true;
                    RefrescarLista();

                    // Con productos seleccionados se muestra de una vez el resumen
                    // para confirmar; sin selección solo se asigna el destino.
                    if (_productos.Any(p => p.Seleccionado))
                    {
                        MostrarPasoConfirmar();
                    }
                    else
                    {
                        CerrarOverlayDestino();
                        ResaltarDestino();
                    }
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
                LimpiarYEnfocarDestino();
            }
        }

        // Pequeño "pulso" para que el usuario note que el destino quedó asignado
        private async void ResaltarDestino()
        {
            await PanelDestinoActivo.ScaleTo(1.04, 120, Easing.CubicOut);
            await PanelDestinoActivo.ScaleTo(1.0, 120, Easing.CubicIn);
        }

        private void OnQuitarDestinoClicked(object sender, EventArgs e)
        {
            _idTarimaDestino = 0;
            _noTarimaDestino = 0;
            _idPedidoDestino = 0;
            QrDestinoEntry.Text = "";
            PanelDestinoActivo.IsVisible = false;
            RefrescarLista();
            UpcEntry.Focus();
        }

        // ══════════════════════════════════════
        // ESCANEO DE PRODUCTOS (UPC)
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

            // Autocompletar con ceros a la izquierda si tiene menos de 13 dígitos
            if (upc.Length < 13 && upc.All(char.IsDigit))
                upc = upc.PadLeft(13, '0');

            var producto = _productos.FirstOrDefault(p =>
                p.UPC == upc || (!string.IsNullOrEmpty(p.UPCProducto) && p.UPCProducto == upc));
            if (producto == null)
            {
                MostrarErrorUpc();
                await DisplayAlert("No encontrado", $"El código {upc} no pertenece a esta tarima.", "OK");
                UpcEntry.Focus();
                return;
            }

            // Toggle selección
            producto.Seleccionado = !producto.Seleccionado;
            RefrescarLista();
            UpcEntry.Focus();
        }

        private void MostrarErrorUpc()
        {
            UpcFrame.BackgroundColor = Color.FromHex("#FEE2E2");
            UpcFrame.BorderColor     = Color.FromHex("#FCA5A5");
            Device.StartTimer(TimeSpan.FromSeconds(1.5), () =>
            {
                UpcFrame.BackgroundColor = Color.FromHex("#F8FAFF");
                UpcFrame.BorderColor     = Color.FromHex("#C7D2FE");
                return false;
            });
        }

        // ══════════════════════════════════════
        // REFRESCAR LISTA Y BOTTOM
        // ══════════════════════════════════════

        private void RefrescarLista()
        {
            ProductosLayout.Children.Clear();

            foreach (var p in _productos)
            {
                bool sel = p.Seleccionado;

                var fila = new Frame
                {
                    BackgroundColor = Color.FromHex(sel ? "#EEF2FF" : "#F9FAFB"),
                    BorderColor     = Color.FromHex(sel ? "#C7D2FE" : "#F1F5F9"),
                    CornerRadius = 10,
                    Padding  = new Thickness(10, 8),
                    HasShadow = false,
                    Margin   = new Thickness(0, 2)
                };

                // Col 0: icono  |  Col 1: info  |  Col 2: badge cantidad/destino
                var root = new Grid { ColumnSpacing = 8 };
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // ── Icono ──
                var check = new Label
                {
                    Text = sel ? "✔" : "○",
                    TextColor = Color.FromHex(sel ? "#4C51BF" : "#D1D5DB"),
                    FontSize = 18,
                    FontAttributes = sel ? FontAttributes.Bold : FontAttributes.None,
                    VerticalOptions = LayoutOptions.Center
                };

                // ── Info ──
                var info = new StackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };

                info.Children.Add(new Label
                {
                    Text = p.Descripcion,
                    TextColor = Color.FromHex(sel ? "#3730A3" : "#1F2937"),
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    LineBreakMode = LineBreakMode.TailTruncation
                });

                // Fila de códigos: UPC · Prod (sin frames pesados)
                var codsRow = new StackLayout { Orientation = StackOrientation.Horizontal, Spacing = 4 };

                codsRow.Children.Add(new Label
                {
                    Text = "UPC",
                    TextColor = Color.FromHex("#818CF8"),
                    FontSize = 9,
                    VerticalOptions = LayoutOptions.Center
                });
                codsRow.Children.Add(new Label
                {
                    Text = p.UPC,
                    TextColor = Color.FromHex("#3730A3"),
                    FontSize = 10,
                    FontAttributes = FontAttributes.Bold,
                    VerticalOptions = LayoutOptions.Center
                });

                if (!string.IsNullOrEmpty(p.UPCProducto))
                {
                    codsRow.Children.Add(new Label
                    {
                        Text = "·",
                        TextColor = Color.FromHex("#D1D5DB"),
                        FontSize = 11,
                        VerticalOptions = LayoutOptions.Center
                    });
                    codsRow.Children.Add(new Label
                    {
                        Text = "Prod",
                        TextColor = Color.FromHex("#22C55E"),
                        FontSize = 9,
                        VerticalOptions = LayoutOptions.Center
                    });
                    codsRow.Children.Add(new Label
                    {
                        Text = p.UPCProducto,
                        TextColor = Color.FromHex("#166534"),
                        FontSize = 10,
                        FontAttributes = FontAttributes.Bold,
                        VerticalOptions = LayoutOptions.Center
                    });
                }

                info.Children.Add(codsRow);

                if (p.EsRepartido)
                {
                    info.Children.Add(new Label
                    {
                        Text = p.NotaReparto,
                        TextColor = Color.FromHex("#065F46"),
                        FontSize = 10,
                        FontAttributes = FontAttributes.Bold,
                        LineBreakMode = LineBreakMode.WordWrap
                    });
                }

                // ── Badge derecho: cantidad o destino ──
                Frame badge;
                if (sel && _noTarimaDestino > 0)
                {
                    var t = new FormattedString();
                    t.Spans.Add(new Span { Text = "→ ", TextColor = Color.FromHex("#D97706"), FontSize = 10 });
                    t.Spans.Add(new Span { Text = $"#{_noTarimaDestino}", TextColor = Color.FromHex("#92400E"), FontSize = 12, FontAttributes = FontAttributes.Bold });
                    badge = new Frame
                    {
                        BackgroundColor = Color.FromHex("#FEF3C7"),
                        CornerRadius = 8, Padding = new Thickness(8, 4), HasShadow = false,
                        Content = new Label { FormattedText = t, VerticalOptions = LayoutOptions.Center }
                    };
                }
                else
                {
                    var t = new FormattedString();
                    t.Spans.Add(new Span { Text = "×", TextColor = Color.FromHex(sel ? "#6366F1" : "#9CA3AF"), FontSize = 10 });
                    t.Spans.Add(new Span { Text = $" {p.Cantidad:0.##}", TextColor = Color.FromHex(sel ? "#3730A3" : "#374151"), FontSize = 13, FontAttributes = FontAttributes.Bold });
                    badge = new Frame
                    {
                        BackgroundColor = Color.FromHex(sel ? "#E0E7FF" : "#F3F4F6"),
                        CornerRadius = 8, Padding = new Thickness(8, 4), HasShadow = false,
                        Content = new Label { FormattedText = t, VerticalOptions = LayoutOptions.Center }
                    };
                }

                Grid.SetColumn(check, 0);
                Grid.SetColumn(info,  1);
                Grid.SetColumn(badge, 2);
                root.Children.Add(check);
                root.Children.Add(info);
                root.Children.Add(badge);

                fila.Content = root;
                ProductosLayout.Children.Add(fila);
            }

            ActualizarBottom();
        }

        private void ActualizarBottom()
        {
            int total        = _productos.Count;
            int seleccionados = _productos.Count(p => p.Seleccionado);

            ContadorLabel.Text = $"{seleccionados} seleccionado(s) de {total}";
            ContadorBadge.Text = $"{seleccionados}/{total}";

            bool haySeleccion = seleccionados > 0;
            bool hayDestino   = _idTarimaDestino > 0;

            BottomArea.IsVisible         = haySeleccion;
            SeleccionadosAviso.IsVisible = haySeleccion && !hayDestino;
            BtnConfirmar.IsVisible       = haySeleccion && hayDestino;

            if (haySeleccion && !hayDestino)
                SeleccionadosLabel.Text = $"{seleccionados} seleccionado(s)  •  Toca aquí o el ícono de tarima para escanear el destino";
            if (haySeleccion && hayDestino)
                BtnConfirmar.Text = $"✔ Trasladar {seleccionados} producto(s) → Tarima #{_noTarimaDestino}";
        }

        // ══════════════════════════════════════
        // CONFIRMAR TRASLADO
        // ══════════════════════════════════════

        // Un producto repartido es una sola fila del pedido: sus partes solo pueden
        // moverse entre tarimas del mismo pedido.
        private async Task<bool> ValidarTraslado(List<ProductoTarima> seleccionados)
        {
            if (_idPedidoDestino != _idPedidoOrigen && seleccionados.Any(p => p.EsRepartido))
            {
                await DisplayAlert("Traslado no permitido",
                    $"La Tarima #{_noTarimaDestino} es de otro pedido. Los productos repartidos en varias tarimas " +
                    "solo se pueden trasladar a tarimas del mismo pedido.", "OK");
                return false;
            }
            return true;
        }

        private async void OnConfirmarClicked(object sender, EventArgs e)
        {
            var seleccionados = _productos.Where(p => p.Seleccionado).ToList();
            if (!seleccionados.Any()) return;
            if (!await ValidarTraslado(seleccionados)) return;

            bool ok = await DisplayAlert("Confirmar traslado",
                $"{seleccionados.Count} producto(s) de Tarima #{_noTarimaOrigen} → Tarima #{_noTarimaDestino}\n\n¿Confirmar?",
                "Sí, confirmar", "Cancelar");
            if (!ok) return;

            await EjecutarTraslado(seleccionados);
        }

        private async Task EjecutarTraslado(List<ProductoTarima> seleccionados)
        {
            LoadingOverlay.IsVisible = true;
            try
            {
                int idPedido        = _idPedidoOrigen;
                int noTarima        = _noTarimaOrigen;
                int idTarima        = _idTarimaOrigen;
                int idTarimaDestino = _idTarimaDestino;
                int noTarimaDestino = _noTarimaDestino;
                int idPedidoDestino = _idPedidoDestino;
                int idUsuario       = App.IdUsuarioActual;
                string nombreUsuario = App.NombreUsuarioActual ?? "Usuario";
                bool tarimaOrigenEliminada = false;

                await Task.Run(() =>
                {
                    using (var conn = new MySqlConnection(BuildConnectionString()))
                    {
                        conn.Open();
                        using (var tr = conn.BeginTransaction())
                        {
                            try
                            {
                                // Los inventarios de ambas tarimas deben seguir abiertos (Estado = 0);
                                // se bloquean para que nadie los confirme mientras se traslada
                                RepartoTarimas.ValidarInventarioAbierto(conn, tr, idTarima, noTarima);
                                RepartoTarimas.ValidarInventarioAbierto(conn, tr, idTarimaDestino, noTarimaDestino);

                                // Mover productos a tarima destino
                                var normales = seleccionados.Where(p => !p.EsRepartido).ToList();
                                if (normales.Any())
                                {
                                    var inParams = string.Join(",",
                                        normales.Select((_, i) => $"@upc{i}"));

                                    // Si es pedido diferente también actualiza IdConsolidado
                                    string setCols = idPedidoDestino != idPedido
                                        ? "SET IdConsolidado = @IdPedidoDestino, NoTarima = @NoTarimaDestino"
                                        : "SET NoTarima = @NoTarimaDestino";

                                    using (var cmd = new MySqlCommand($@"
                                        UPDATE detallepedidostienda_bodega
                                        {setCols}
                                        WHERE IdConsolidado = @IdPedido
                                          AND NoTarima = @NoTarimaOrigen
                                          AND UPC IN ({inParams})", conn, tr))
                                    {
                                        cmd.Parameters.AddWithValue("@NoTarimaDestino", noTarimaDestino);
                                        cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                        cmd.Parameters.AddWithValue("@NoTarimaOrigen", noTarima);
                                        if (idPedidoDestino != idPedido)
                                            cmd.Parameters.AddWithValue("@IdPedidoDestino", idPedidoDestino);
                                        for (int i = 0; i < normales.Count; i++)
                                            cmd.Parameters.AddWithValue($"@upc{i}", normales[i].UPC);
                                        cmd.ExecuteNonQuery();
                                    }
                                }

                                // Repartidos: se mueve su parte dentro de la etiqueta [TARIMAS:...]
                                var repartidos = seleccionados.Where(p => p.EsRepartido).ToList();
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

                                // El inventario va con la tarima: lo trasladado sale del inventario
                                // de la tarima origen y entra al de la tarima destino
                                foreach (var p in seleccionados)
                                    RepartoTarimas.MoverProductoEntreInventarios(conn, tr,
                                        idTarima, idTarimaDestino, p.UPC, p.UPCProducto, p.Cantidad);

                                // Registrar en Trasladoproductostarima
                                foreach (var p in seleccionados)
                                {
                                    using (var cmd = new MySqlCommand(@"
                                        INSERT INTO Trasladoproductostarima
                                            (IdPedido, UpcPaquete, UpcUnidad, Descripcion, Cantidad,
                                             NoTarimaOrigen, NoTarimaDestino,
                                             IdUsuario, NombreUsuario, FechaHoraTraslado)
                                        VALUES
                                            (@IdPedido, @UpcPaquete, @UpcUnidad, @Descripcion, @Cantidad,
                                             @NoTarimaOrigen, @NoTarimaDestino,
                                             @IdUsuario, @NombreUsuario, NOW())", conn, tr))
                                    {
                                        cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                        cmd.Parameters.AddWithValue("@UpcPaquete", p.UPC);
                                        cmd.Parameters.AddWithValue("@UpcUnidad", p.UPC);
                                        cmd.Parameters.AddWithValue("@Descripcion", p.Descripcion);
                                        cmd.Parameters.AddWithValue("@Cantidad", p.Cantidad);
                                        cmd.Parameters.AddWithValue("@NoTarimaOrigen", noTarima);
                                        cmd.Parameters.AddWithValue("@NoTarimaDestino", noTarimaDestino);
                                        cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                                        cmd.Parameters.AddWithValue("@NombreUsuario", nombreUsuario);
                                        cmd.ExecuteNonQuery();
                                    }
                                }

                                RepartoTarimas.RecalcularTarima(conn, tr, idTarima, idPedido, noTarima);
                                RepartoTarimas.RecalcularTarima(conn, tr, idTarimaDestino, idPedidoDestino, noTarimaDestino);

                                // Si la tarima origen quedó vacía (sin filas normales ni partes
                                // de productos repartidos), eliminarla y renumerar
                                int restantes = RepartoTarimas.ContarProductosEnTarima(conn, tr, idPedido, noTarima);
                                if (restantes == 0)
                                {
                                    RepartoTarimas.EliminarTarimaVacia(conn, tr, idTarima, idPedido, noTarima);
                                    tarimaOrigenEliminada = true;
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

                LoadingOverlay.IsVisible = false;
                string mensaje = tarimaOrigenEliminada
                    ? $"{seleccionados.Count} producto(s) trasladados a Tarima #{_noTarimaDestino}.\n\nLa tarima origen quedó vacía y fue eliminada. Las tarimas del pedido fueron renumeradas automáticamente."
                    : $"{seleccionados.Count} producto(s) trasladados a Tarima #{_noTarimaDestino}.";
                await DisplayAlert("¡Listo!", mensaje, "OK");
                ReiniciarPantalla();
            }
            catch (Exception ex)
            {
                LoadingOverlay.IsVisible = false;
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        // ══════════════════════════════════════
        // REINICIAR
        // ══════════════════════════════════════

        private void ReiniciarPantalla()
        {
            _idTarimaOrigen  = 0; _noTarimaOrigen  = 0; _idPedidoOrigen  = 0;
            _idTarimaDestino = 0; _noTarimaDestino = 0; _idPedidoDestino = 0;
            _idTarimaVacia   = 0; _noTarimaVacia   = 0; _idPedidoVacia   = 0;
            _productos.Clear();

            Device.BeginInvokeOnMainThread(() =>
            {
                QrOrigenEntry.Text  = "";
                QrDestinoEntry.Text = "";
                UpcEntry.Text       = "";
                ProductosLayout.Children.Clear();

                PanelScanOrigen.IsVisible    = true;
                PanelTarimaVacia.IsVisible   = false;
                TopArea.IsVisible            = false;
                PanelProductos.IsVisible     = false;
                BottomArea.IsVisible         = false;
                DestinoOverlay.IsVisible     = false;

                PanelDestinoActivo.IsVisible = false;
                SeleccionadosAviso.IsVisible = false;
                BtnConfirmar.IsVisible       = false;

                QrOrigenEntry.Focus();
            });
        }
    }
}
