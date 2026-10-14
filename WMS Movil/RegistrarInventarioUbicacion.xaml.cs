using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    public class UbicacionFisica
    {
        public int Id { get; set; }
        public string Rack { get; set; }
        public string Bloque { get; set; }
        public string Nivel { get; set; }
        public string Codigo => $"R{Rack}-B{Bloque}-N{Nivel}";
        public string Detalle => $"Rack {Rack} · Bloque {Bloque} · Nivel {Nivel}";
    }

    public class ProductoEnUbicacion : Xamarin.Forms.BindableObject
    {
        public int IdInventarioUbicacion { get; set; }
        public int IdUbicacion { get; set; }
        public string UpcPaquete { get; set; }
        public string Upc { get; set; }
        public string Descripcion { get; set; }
        public int CantidadPorPaquete { get; set; }

        private DateTime? _fechaVencimiento;
        public DateTime? FechaVencimiento
        {
            get => _fechaVencimiento;
            set
            {
                _fechaVencimiento = value;
                OnPropertyChanged(nameof(FechaVencimiento));
                OnPropertyChanged(nameof(TieneFechaVencimiento));
                OnPropertyChanged(nameof(TextoFechaVencimiento));
            }
        }
        public bool TieneFechaVencimiento => FechaVencimiento.HasValue;
        public string TextoFechaVencimiento => FechaVencimiento.HasValue
            ? $"Vence: {FechaVencimiento.Value:dd/MM/yyyy}"
            : string.Empty;

        private decimal _existenciaFardo;
        public decimal ExistenciaFardo
        {
            get => _existenciaFardo;
            set { _existenciaFardo = value; OnPropertyChanged(nameof(ExistenciaFardo)); }
        }

        private decimal _existenciaUnidad;
        public decimal ExistenciaUnidad
        {
            get => _existenciaUnidad;
            set { _existenciaUnidad = value; OnPropertyChanged(nameof(ExistenciaUnidad)); }
        }
    }

    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class RegistrarInventarioUbicacion : ContentPage
    {
        private readonly string _connectionString;
        private UbicacionFisica _ubicacionActual;
        private readonly ObservableCollection<ProductoEnUbicacion> _productos;
        private bool _toastActivo;

        private string _upcPaqueteActual;
        private string _upcActual;
        private string _descripcionActual;
        private int _idInventarioActual;
        private int _cantidadPorPaquete;

        private ProductoEnUbicacion _productoTraslado;
        private UbicacionFisica _ubicacionDestino;

        public RegistrarInventarioUbicacion()
        {
            InitializeComponent();
            _connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
            _productos = new ObservableCollection<ProductoEnUbicacion>();
            ProductosCollection.ItemsSource = _productos;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Device.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(200);
                EntradaEntry.Focus();
            });
        }

        // ─────────────────────────────────────────
        // ESTADOS DE PANTALLA
        // ─────────────────────────────────────────

        private void ModoSinUbicacion()
        {
            PanelSinUbicacion.IsVisible = true;
            PanelUbicacion.IsVisible = false;
            PanelProducto.IsVisible = false;
            PanelTraslado.IsVisible = false;
            TituloListaLabel.IsVisible = false;
            HeaderSubtituloLabel.Text = "Escanee el QR de la ubicación";
            EntradaEntry.Placeholder = "Escanee el QR de la ubicación";
            EntradaEntry.Text = string.Empty;
        }

        private void ModoConUbicacion()
        {
            PanelSinUbicacion.IsVisible = false;
            PanelUbicacion.IsVisible = true;
            UbicacionCodigoLabel.Text = _ubicacionActual.Codigo;
            UbicacionDetalleLabel.Text = _ubicacionActual.Detalle;
            HeaderSubtituloLabel.Text = _ubicacionActual.Codigo;
            EntradaEntry.Placeholder = "Escanee UPC del producto";
            EntradaEntry.Text = string.Empty;
            TituloListaLabel.IsVisible = _productos.Count > 0;
        }

        // ─────────────────────────────────────────
        // ENTRADA UNIFICADA
        // ─────────────────────────────────────────

        private async void OnEntradaCompleted(object sender, EventArgs e)
        {
            string texto = EntradaEntry.Text?.Trim();
            if (string.IsNullOrEmpty(texto)) return;

            if (_ubicacionActual == null)
                await ProcesarQRUbicacion(texto);
            else
                await ProcesarUPCProducto(texto);
        }

        // ─────────────────────────────────────────
        // CARGAR UBICACIÓN
        // ─────────────────────────────────────────

        private async Task ProcesarQRUbicacion(string texto)
        {
            int idUbicacion;

            if (texto.StartsWith("LOC:", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(texto.Substring(4), out idUbicacion))
                {
                    await DisplayAlert("Error", "Código QR de ubicación no válido.", "OK");
                    EntradaEntry.Text = string.Empty;
                    EntradaEntry.Focus();
                    return;
                }
            }
            else if (int.TryParse(texto, out idUbicacion) && idUbicacion > 0 && idUbicacion < 100000)
            {
                // ID numérico directo (también válido)
            }
            else
            {
                await DisplayAlert("Error", "Escanee el código QR de una ubicación de bodega.", "OK");
                EntradaEntry.Text = string.Empty;
                EntradaEntry.Focus();
                return;
            }

            await CargarUbicacion(idUbicacion);
        }

        private async Task CargarUbicacion(int id)
        {
            LoadingLabel.Text = "Cargando ubicación...";
            LoadingOverlay.IsVisible = true;

            UbicacionFisica ubicacion = null;
            var productosLista = new List<ProductoEnUbicacion>();

            try
            {
                await Task.Run(() =>
                {
                    using (var conn = new MySqlConnection(_connectionString))
                    {
                        conn.Open();

                        using (var cmd = new MySqlCommand(
                            "SELECT Id, Rack, Bloque, Nivel FROM ubicacionesbodega WHERE Id = @Id", conn))
                        {
                            cmd.Parameters.AddWithValue("@Id", id);
                            using (var reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                    ubicacion = new UbicacionFisica
                                    {
                                        Id = reader.GetInt32("Id"),
                                        Rack = reader["Rack"]?.ToString() ?? "",
                                        Bloque = reader["Bloque"]?.ToString() ?? "",
                                        Nivel = reader["Nivel"]?.ToString() ?? ""
                                    };
                            }
                        }

                        if (ubicacion == null) return;

                        using (var cmd = new MySqlCommand(@"
                            SELECT iu.IdInventarioUbicacion, iu.IdUbicacion,
                                   COALESCE(iu.UpcPaquete,'') AS UpcPaquete,
                                   iu.Upc, iu.Descripcion, iu.ExistenciaFardo, iu.ExistenciaUnidad,
                                   iu.FechaVencimiento,
                                   COALESCE(pp.Cantidad, 0) AS CantidadPorPaquete
                            FROM InventarioUbicaciones iu
                            LEFT JOIN productospaquetes pp ON iu.Upc = pp.Upc
                            WHERE iu.IdUbicacion = @Id
                            ORDER BY iu.Descripcion", conn))
                        {
                            cmd.Parameters.AddWithValue("@Id", id);
                            using (var reader = cmd.ExecuteReader())
                                while (reader.Read())
                                    productosLista.Add(new ProductoEnUbicacion
                                    {
                                        IdInventarioUbicacion = reader.GetInt32("IdInventarioUbicacion"),
                                        IdUbicacion = reader.GetInt32("IdUbicacion"),
                                        UpcPaquete = reader.GetString("UpcPaquete"),
                                        Upc = reader.GetString("Upc"),
                                        Descripcion = reader.GetString("Descripcion"),
                                        ExistenciaFardo = reader.GetDecimal("ExistenciaFardo"),
                                        ExistenciaUnidad = reader.GetDecimal("ExistenciaUnidad"),
                                        FechaVencimiento = reader.IsDBNull(reader.GetOrdinal("FechaVencimiento"))
                                            ? (DateTime?)null
                                            : reader.GetDateTime("FechaVencimiento"),
                                        CantidadPorPaquete = reader.GetInt32("CantidadPorPaquete")
                                    });
                        }
                    }
                });

                if (ubicacion == null)
                {
                    await DisplayAlert("Error", $"No se encontró la ubicación con ID {id}.", "OK");
                    EntradaEntry.Text = string.Empty;
                    EntradaEntry.Focus();
                    return;
                }

                _ubicacionActual = ubicacion;

                Device.BeginInvokeOnMainThread(() =>
                {
                    _productos.Clear();
                    foreach (var p in productosLista)
                        _productos.Add(p);
                    ModoConUbicacion();
                    EntradaEntry.Focus();
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Error al cargar ubicación: {ex.Message}", "OK");
                EntradaEntry.Text = string.Empty;
                EntradaEntry.Focus();
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        // ─────────────────────────────────────────
        // BUSCAR PRODUCTO POR UPC
        // ─────────────────────────────────────────

        private async Task ProcesarUPCProducto(string upcEscaneado)
        {

            LoadingLabel.Text = "Buscando producto...";
            LoadingOverlay.IsVisible = true;

            string upcFinal = upcEscaneado;
            string upcPaquete = string.Empty;
            string descripcion = string.Empty;
            int cantidadPorPaquete = 0;

            try
            {
                await Task.Run(() =>
                {
                    using (var conn = new MySqlConnection(_connectionString))
                    {
                        conn.Open();

                        // Intentar como UpcPaquete (fardo)
                        using (var cmd = new MySqlCommand(@"
                            SELECT pp.Upc, COALESCE(p.DescLarga, pp.Upc) AS Descripcion, pp.Cantidad
                            FROM productospaquetes pp
                            LEFT JOIN productos p ON pp.Upc = p.Upc
                            WHERE pp.UPCPaquete = @Upc LIMIT 1", conn))
                        {
                            cmd.Parameters.AddWithValue("@Upc", upcEscaneado);
                            using (var reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    upcPaquete = upcEscaneado;
                                    upcFinal = reader.GetString("Upc");
                                    descripcion = reader.GetString("Descripcion");
                                    cantidadPorPaquete = reader.IsDBNull(reader.GetOrdinal("Cantidad"))
                                        ? 0 : reader.GetInt32("Cantidad");
                                }
                            }
                        }

                        // Si no es paquete, buscar como UPC unitario
                        if (string.IsNullOrEmpty(descripcion))
                        {
                            using (var cmd = new MySqlCommand(
                                "SELECT COALESCE(DescLarga, Upc) AS Descripcion FROM productos WHERE Upc = @Upc LIMIT 1", conn))
                            {
                                cmd.Parameters.AddWithValue("@Upc", upcFinal);
                                using (var reader = cmd.ExecuteReader())
                                {
                                    if (reader.Read())
                                        descripcion = reader.GetString("Descripcion");
                                }
                            }

                            // Buscar si el UPC unitario tiene paquete asociado
                            if (!string.IsNullOrEmpty(descripcion))
                            {
                                using (var cmd = new MySqlCommand(
                                    "SELECT UPCPaquete, Cantidad FROM productospaquetes WHERE Upc = @Upc LIMIT 1", conn))
                                {
                                    cmd.Parameters.AddWithValue("@Upc", upcFinal);
                                    using (var reader = cmd.ExecuteReader())
                                    {
                                        if (reader.Read())
                                        {
                                            upcPaquete = reader.IsDBNull(reader.GetOrdinal("UPCPaquete"))
                                                ? string.Empty : reader.GetString("UPCPaquete");
                                            cantidadPorPaquete = reader.IsDBNull(reader.GetOrdinal("Cantidad"))
                                                ? 0 : reader.GetInt32("Cantidad");
                                        }
                                    }
                                }
                            }
                        }
                    }
                });

                if (string.IsNullOrEmpty(descripcion))
                {
                    await DisplayAlert("Error", "No se encontró el producto para el UPC escaneado.", "OK");
                    EntradaEntry.Text = string.Empty;
                    EntradaEntry.Focus();
                    return;
                }

                var existente = _productos.FirstOrDefault(p => p.Upc == upcFinal);
                _upcPaqueteActual = upcPaquete;
                _upcActual = upcFinal;
                _descripcionActual = descripcion;
                _idInventarioActual = existente?.IdInventarioUbicacion ?? 0;
                _cantidadPorPaquete = cantidadPorPaquete;

                Device.BeginInvokeOnMainThread(() =>
                {
                    ProductoDescLabel.Text = descripcion;
                    ProductoUpcLabel.Text = upcFinal;

                    bool tienePaquete = !string.IsNullOrEmpty(upcPaquete) && cantidadPorPaquete > 0;
                    PanelPaquete.IsVisible = tienePaquete;
                    if (tienePaquete)
                    {
                        UpcPaqueteLabel.Text = upcFinal;
                        CodigoPaqueteLabel.Text = upcPaquete;
                        CantidadPaqueteLabel.Text = cantidadPorPaquete.ToString();
                    }

                    CantidadEntry.Text = existente != null ? existente.ExistenciaUnidad.ToString("0") : "";
                    PanelProducto.IsVisible = true;
                    EntradaEntry.Text = upcFinal;
                    CantidadEntry.Focus();
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Error al buscar producto: {ex.Message}", "OK");
                EntradaEntry.Text = string.Empty;
                EntradaEntry.Focus();
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        // ─────────────────────────────────────────
        // GUARDAR PRODUCTO EN UBICACIÓN
        // ─────────────────────────────────────────

        private async void OnGuardarProductoClicked(object sender, EventArgs e)
        {
            string cantidadTexto = string.IsNullOrWhiteSpace(CantidadEntry.Text) ? "0" : CantidadEntry.Text.Replace(",", ".");
            if (!decimal.TryParse(cantidadTexto, out decimal cantidad) || cantidad < 0)
            {
                await DisplayAlert("Error", "Ingrese una cantidad válida.", "OK");
                CantidadEntry.Focus();
                return;
            }
            decimal fardos = cantidad;
            decimal unidades = _cantidadPorPaquete > 0 ? cantidad * _cantidadPorPaquete : cantidad;
            DateTime? fechaVenc = TieneFechaVencimientoSwitch.IsToggled
                ? FechaVencimientoDatePicker.Date
                : (DateTime?)null;

            LoadingLabel.Text = "Guardando...";
            LoadingOverlay.IsVisible = true;

            int idUbicacion = _ubicacionActual.Id;
            string upc = _upcActual;
            string upcPaquete = _upcPaqueteActual;
            string descripcion = _descripcionActual;
            int idInventario = _idInventarioActual;
            int nuevoId = idInventario;

            try
            {
                await Task.Run(() =>
                {
                    using (var conn = new MySqlConnection(_connectionString))
                    {
                        conn.Open();

                        if (idInventario > 0)
                        {
                            using (var cmd = new MySqlCommand(@"
                                UPDATE InventarioUbicaciones
                                SET ExistenciaFardo = @Fardos, ExistenciaUnidad = @Unidades,
                                    FechaVencimiento = @FechaVencimiento
                                WHERE IdInventarioUbicacion = @Id", conn))
                            {
                                cmd.Parameters.AddWithValue("@Fardos", fardos);
                                cmd.Parameters.AddWithValue("@Unidades", unidades);
                                cmd.Parameters.AddWithValue("@FechaVencimiento",
                                    fechaVenc.HasValue ? (object)fechaVenc.Value : DBNull.Value);
                                cmd.Parameters.AddWithValue("@Id", idInventario);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            using (var cmd = new MySqlCommand(@"
                                INSERT INTO InventarioUbicaciones
                                    (IdUbicacion, UpcPaquete, Upc, Descripcion, ExistenciaFardo, ExistenciaUnidad, FechaVencimiento)
                                VALUES (@IdUbicacion, @UpcPaquete, @Upc, @Descripcion, @Fardos, @Unidades, @FechaVencimiento);
                                SELECT LAST_INSERT_ID();", conn))
                            {
                                cmd.Parameters.AddWithValue("@IdUbicacion", idUbicacion);
                                cmd.Parameters.AddWithValue("@UpcPaquete", upcPaquete ?? "");
                                cmd.Parameters.AddWithValue("@Upc", upc);
                                cmd.Parameters.AddWithValue("@Descripcion", descripcion);
                                cmd.Parameters.AddWithValue("@Fardos", fardos);
                                cmd.Parameters.AddWithValue("@Unidades", unidades);
                                cmd.Parameters.AddWithValue("@FechaVencimiento",
                                    fechaVenc.HasValue ? (object)fechaVenc.Value : DBNull.Value);
                                nuevoId = Convert.ToInt32(cmd.ExecuteScalar());
                            }
                        }
                    }
                });

                Device.BeginInvokeOnMainThread(() =>
                {
                    var existente = _productos.FirstOrDefault(p => p.Upc == upc);
                    if (existente != null)
                    {
                        existente.ExistenciaFardo = fardos;
                        existente.ExistenciaUnidad = unidades;
                        existente.FechaVencimiento = fechaVenc;
                    }
                    else
                    {
                        _productos.Add(new ProductoEnUbicacion
                        {
                            IdInventarioUbicacion = nuevoId,
                            IdUbicacion = idUbicacion,
                            UpcPaquete = upcPaquete ?? "",
                            Upc = upc,
                            Descripcion = descripcion,
                            ExistenciaFardo = fardos,
                            ExistenciaUnidad = unidades,
                            FechaVencimiento = fechaVenc,
                            CantidadPorPaquete = _cantidadPorPaquete
                        });
                    }

                    TituloListaLabel.IsVisible = true;
                    PanelProducto.IsVisible = false;
                    TieneFechaVencimientoSwitch.IsToggled = false;
                    EntradaEntry.Text = string.Empty;
                    EntradaEntry.Focus();
                });

                MostrarToast("✓ Guardado correctamente");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Error al guardar: {ex.Message}", "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        // ─────────────────────────────────────────
        // EDITAR / ELIMINAR PRODUCTO
        // ─────────────────────────────────────────

        private void OnEditarProductoClicked(object sender, EventArgs e)
        {
            var producto = (sender as Button)?.BindingContext as ProductoEnUbicacion;
            if (producto == null) return;

            _upcPaqueteActual = producto.UpcPaquete;
            _upcActual = producto.Upc;
            _descripcionActual = producto.Descripcion;
            _idInventarioActual = producto.IdInventarioUbicacion;
            _cantidadPorPaquete = producto.CantidadPorPaquete;

            ProductoDescLabel.Text = producto.Descripcion;
            ProductoUpcLabel.Text = producto.Upc;

            bool tienePaquete = !string.IsNullOrEmpty(producto.UpcPaquete);
            PanelPaquete.IsVisible = tienePaquete;
            if (tienePaquete)
            {
                UpcPaqueteLabel.Text = producto.Upc;
                CodigoPaqueteLabel.Text = producto.UpcPaquete;
                CantidadPaqueteLabel.Text = _cantidadPorPaquete > 0 ? _cantidadPorPaquete.ToString() : "-";
            }

            bool tieneFecha = producto.FechaVencimiento.HasValue;
            TieneFechaVencimientoSwitch.IsToggled = tieneFecha;
            FechaVencimientoFrame.IsVisible = tieneFecha;
            if (tieneFecha)
                FechaVencimientoDatePicker.Date = producto.FechaVencimiento.Value;

            CantidadEntry.Text = producto.ExistenciaUnidad.ToString("0");
            PanelProducto.IsVisible = true;
            CantidadEntry.Focus();
        }

        private void OnFechaVencimientoToggled(object sender, ToggledEventArgs e)
        {
            FechaVencimientoFrame.IsVisible = e.Value;
        }

        private async void OnEliminarProductoClicked(object sender, EventArgs e)
        {
            var producto = (sender as Button)?.BindingContext as ProductoEnUbicacion;
            if (producto == null) return;

            bool confirmar = await DisplayAlert("Confirmar",
                $"¿Eliminar \"{producto.Descripcion}\" de esta ubicación?", "Sí", "No");
            if (!confirmar) return;

            try
            {
                if (producto.IdInventarioUbicacion > 0)
                {
                    await Task.Run(() =>
                    {
                        using (var conn = new MySqlConnection(_connectionString))
                        {
                            conn.Open();
                            using (var cmd = new MySqlCommand(
                                "DELETE FROM InventarioUbicaciones WHERE IdInventarioUbicacion = @Id", conn))
                            {
                                cmd.Parameters.AddWithValue("@Id", producto.IdInventarioUbicacion);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    });
                }

                Device.BeginInvokeOnMainThread(() =>
                {
                    _productos.Remove(producto);
                    TituloListaLabel.IsVisible = _productos.Count > 0;
                });

                MostrarToast("✓ Producto eliminado");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Error al eliminar: {ex.Message}", "OK");
            }
        }

        // ─────────────────────────────────────────
        // CONTROLES
        // ─────────────────────────────────────────

        private void OnCancelarProductoClicked(object sender, EventArgs e)
        {
            PanelProducto.IsVisible = false;
            EntradaEntry.Text = string.Empty;
            EntradaEntry.Focus();
        }

        private void OnCambiarUbicacionClicked(object sender, EventArgs e)
        {
            _ubicacionActual = null;
            _productos.Clear();
            PanelProducto.IsVisible = false;
            ModoSinUbicacion();
            EntradaEntry.Focus();
        }

        private async void OnVolverClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        // ─────────────────────────────────────────
        // TRASLADO
        // ─────────────────────────────────────────

        private void OnTrasladarProductoClicked(object sender, EventArgs e)
        {
            var producto = (sender as Button)?.BindingContext as ProductoEnUbicacion;
            if (producto == null) return;

            _productoTraslado = producto;
            _ubicacionDestino = null;

            TrasladoDescLabel.Text = producto.Descripcion;
            TrasladoDisponiblesLabel.Text = $"{producto.ExistenciaFardo:0} fardos";
            TrasladoFardosEntry.Text = "";
            TrasladoDestinoEntry.Text = "";
            TrasladoSinDestinoPanel.IsVisible = true;
            TrasladoConDestinoPanel.IsVisible = false;
            PanelProducto.IsVisible = false;
            PanelTraslado.IsVisible = true;
            TrasladoFardosEntry.Focus();
        }

        private void OnTrasladoFardosCompleted(object sender, EventArgs e)
        {
            TrasladoDestinoEntry.Focus();
        }

        private async void OnTrasladoDestinoCompleted(object sender, EventArgs e)
        {
            string texto = TrasladoDestinoEntry.Text?.Trim();
            if (string.IsNullOrEmpty(texto)) return;

            int idUbicacion;
            if (texto.StartsWith("LOC:", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(texto.Substring(4), out idUbicacion))
                {
                    await DisplayAlert("Error", "Código QR de ubicación no válido.", "OK");
                    return;
                }
            }
            else if (int.TryParse(texto, out idUbicacion) && idUbicacion > 0 && idUbicacion < 100000)
            {
                // ID numérico válido
            }
            else
            {
                await DisplayAlert("Error", "Escanee el código QR de una ubicación de bodega.", "OK");
                TrasladoDestinoEntry.Text = string.Empty;
                return;
            }

            if (idUbicacion == _ubicacionActual?.Id)
            {
                await DisplayAlert("Error", "La ubicación destino no puede ser la misma que el origen.", "OK");
                TrasladoDestinoEntry.Text = string.Empty;
                return;
            }

            await CargarUbicacionDestino(idUbicacion);
        }

        private async Task CargarUbicacionDestino(int id)
        {
            LoadingLabel.Text = "Buscando ubicación...";
            LoadingOverlay.IsVisible = true;
            UbicacionFisica ubicacion = null;

            try
            {
                await Task.Run(() =>
                {
                    using (var conn = new MySqlConnection(_connectionString))
                    {
                        conn.Open();
                        using (var cmd = new MySqlCommand(
                            "SELECT Id, Rack, Bloque, Nivel FROM ubicacionesbodega WHERE Id = @Id", conn))
                        {
                            cmd.Parameters.AddWithValue("@Id", id);
                            using (var reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                    ubicacion = new UbicacionFisica
                                    {
                                        Id = reader.GetInt32("Id"),
                                        Rack = reader["Rack"]?.ToString() ?? "",
                                        Bloque = reader["Bloque"]?.ToString() ?? "",
                                        Nivel = reader["Nivel"]?.ToString() ?? ""
                                    };
                            }
                        }
                    }
                });

                if (ubicacion == null)
                {
                    await DisplayAlert("Error", "No se encontró la ubicación destino.", "OK");
                    TrasladoDestinoEntry.Text = string.Empty;
                    return;
                }

                _ubicacionDestino = ubicacion;
                Device.BeginInvokeOnMainThread(() =>
                {
                    TrasladoDestinoCodigoLabel.Text = ubicacion.Codigo;
                    TrasladoDestinoDetalleLabel.Text = ubicacion.Detalle;
                    TrasladoSinDestinoPanel.IsVisible = false;
                    TrasladoConDestinoPanel.IsVisible = true;
                    CambiarDestinoBtn.IsVisible = true;
                    TrasladoDestinoEntry.Text = string.Empty;
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Error al buscar ubicación: {ex.Message}", "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        private void OnCambiarDestinoClicked(object sender, EventArgs e)
        {
            _ubicacionDestino = null;
            TrasladoSinDestinoPanel.IsVisible = true;
            TrasladoConDestinoPanel.IsVisible = false;
            CambiarDestinoBtn.IsVisible = false;
            TrasladoDestinoEntry.Text = string.Empty;
            TrasladoDestinoEntry.Focus();
        }

        private async void OnConfirmarTrasladoClicked(object sender, EventArgs e)
        {
            if (_productoTraslado == null) return;

            if (_ubicacionDestino == null)
            {
                await DisplayAlert("Error", "Escanee la ubicación destino.", "OK");
                return;
            }

            string fardosTexto = string.IsNullOrWhiteSpace(TrasladoFardosEntry.Text)
                ? "0" : TrasladoFardosEntry.Text.Replace(",", ".");
            if (!decimal.TryParse(fardosTexto, out decimal fardosTraslado) || fardosTraslado <= 0)
            {
                await DisplayAlert("Error", "Ingrese una cantidad de fardos válida mayor a 0.", "OK");
                TrasladoFardosEntry.Focus();
                return;
            }

            if (fardosTraslado > _productoTraslado.ExistenciaFardo)
            {
                await DisplayAlert("Error",
                    $"No puede trasladar más fardos de los disponibles ({_productoTraslado.ExistenciaFardo:0}).", "OK");
                TrasladoFardosEntry.Focus();
                return;
            }

            LoadingLabel.Text = "Realizando traslado...";
            LoadingOverlay.IsVisible = true;

            var producto = _productoTraslado;
            var destino = _ubicacionDestino;
            int cantPorPaquete = 0;

            try
            {
                await Task.Run(() =>
                {
                    using (var conn = new MySqlConnection(_connectionString))
                    {
                        conn.Open();

                        // Obtener unidades por paquete (fuera de transacción, solo lectura)
                        using (var cmd = new MySqlCommand(
                            "SELECT Cantidad FROM productospaquetes WHERE Upc = @Upc LIMIT 1", conn))
                        {
                            cmd.Parameters.AddWithValue("@Upc", producto.Upc);
                            var result = cmd.ExecuteScalar();
                            cantPorPaquete = result != null && result != DBNull.Value
                                ? Convert.ToInt32(result) : 0;
                        }

                        decimal unidadesTraslado = cantPorPaquete > 0
                            ? fardosTraslado * cantPorPaquete : fardosTraslado;

                        using (var transaction = conn.BeginTransaction())
                        {
                        try
                        {

                        // Descontar del origen
                        using (var cmd = new MySqlCommand(@"
                            UPDATE InventarioUbicaciones
                            SET ExistenciaFardo  = ExistenciaFardo  - @Fardos,
                                ExistenciaUnidad = ExistenciaUnidad - @Unidades
                            WHERE IdInventarioUbicacion = @Id", conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@Fardos", fardosTraslado);
                            cmd.Parameters.AddWithValue("@Unidades", unidadesTraslado);
                            cmd.Parameters.AddWithValue("@Id", producto.IdInventarioUbicacion);
                            cmd.ExecuteNonQuery();
                        }

                        // Verificar si el producto ya existe en el destino
                        int idDestinoInventario = 0;
                        using (var cmd = new MySqlCommand(@"
                            SELECT IdInventarioUbicacion FROM InventarioUbicaciones
                            WHERE IdUbicacion = @IdUbicacion AND Upc = @Upc LIMIT 1", conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@IdUbicacion", destino.Id);
                            cmd.Parameters.AddWithValue("@Upc", producto.Upc);
                            var result = cmd.ExecuteScalar();
                            idDestinoInventario = result != null && result != DBNull.Value
                                ? Convert.ToInt32(result) : 0;
                        }

                        if (idDestinoInventario > 0)
                        {
                            // Sumar al destino existente
                            using (var cmd = new MySqlCommand(@"
                                UPDATE InventarioUbicaciones
                                SET ExistenciaFardo  = ExistenciaFardo  + @Fardos,
                                    ExistenciaUnidad = ExistenciaUnidad + @Unidades
                                WHERE IdInventarioUbicacion = @Id", conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@Fardos", fardosTraslado);
                                cmd.Parameters.AddWithValue("@Unidades", unidadesTraslado);
                                cmd.Parameters.AddWithValue("@Id", idDestinoInventario);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            // Insertar en el destino
                            using (var cmd = new MySqlCommand(@"
                                INSERT INTO InventarioUbicaciones
                                    (IdUbicacion, UpcPaquete, Upc, Descripcion, ExistenciaFardo, ExistenciaUnidad)
                                VALUES (@IdUbicacion, @UpcPaquete, @Upc, @Descripcion, @Fardos, @Unidades)", conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@IdUbicacion", destino.Id);
                                cmd.Parameters.AddWithValue("@UpcPaquete", producto.UpcPaquete ?? "");
                                cmd.Parameters.AddWithValue("@Upc", producto.Upc);
                                cmd.Parameters.AddWithValue("@Descripcion", producto.Descripcion);
                                cmd.Parameters.AddWithValue("@Fardos", fardosTraslado);
                                cmd.Parameters.AddWithValue("@Unidades", unidadesTraslado);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                        }
                    }
                });

                decimal unidadesDesc = cantPorPaquete > 0
                    ? fardosTraslado * cantPorPaquete : fardosTraslado;

                Device.BeginInvokeOnMainThread(() =>
                {
                    producto.ExistenciaFardo -= fardosTraslado;
                    producto.ExistenciaUnidad -= unidadesDesc;

                    if (producto.ExistenciaFardo <= 0)
                        _productos.Remove(producto);

                    TituloListaLabel.IsVisible = _productos.Count > 0;
                    PanelTraslado.IsVisible = false;
                    _productoTraslado = null;
                    _ubicacionDestino = null;
                    EntradaEntry.Text = string.Empty;
                    EntradaEntry.Focus();
                });

                MostrarToast($"✓ Traslado a {destino.Codigo} completado");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Error al trasladar: {ex.Message}", "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        private void OnCancelarTrasladoClicked(object sender, EventArgs e)
        {
            PanelTraslado.IsVisible = false;
            _productoTraslado = null;
            _ubicacionDestino = null;
            EntradaEntry.Text = string.Empty;
            EntradaEntry.Focus();
        }

        // ─────────────────────────────────────────
        // TOAST
        // ─────────────────────────────────────────

        private void MostrarToast(string mensaje, bool esError = false)
        {
            if (_toastActivo) return;
            _toastActivo = true;
            Device.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    ToastFrame.BackgroundColor = esError ? Color.FromHex("#E53E3E") : Color.FromHex("#38A169");
                    ToastLabel.Text = mensaje;
                    ToastFrame.TranslationY = -60;
                    ToastFrame.Opacity = 0;
                    ToastFrame.IsVisible = true;
                    await Task.WhenAll(
                        ToastFrame.TranslateTo(0, 0, 260, Easing.CubicOut),
                        ToastFrame.FadeTo(1, 260));
                    await Task.Delay(2200);
                    await Task.WhenAll(
                        ToastFrame.FadeTo(0, 220),
                        ToastFrame.TranslateTo(0, -60, 220, Easing.CubicIn));
                    ToastFrame.IsVisible = false;
                }
                finally { _toastActivo = false; }
            });
        }
    }
}