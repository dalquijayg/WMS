using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WMS_Movil.Servicio;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    [XamlCompilation(XamlCompilationOptions.Compile)]

    // ─────────────────────────────────────────────
    // CLASES DE MODELO
    // ─────────────────────────────────────────────

    public class UbicacionGrupo : Xamarin.Forms.BindableObject
    {
        public int IdUbicacion { get; set; }
        public string NombreUbicacion { get; set; }
        public int CantidadProductos => Productos?.Count ?? 0;
        public ObservableCollection<ProductoDetalle> Productos { get; set; }

        private bool _esGrupoVisible = true;
        public bool EsGrupoVisible
        {
            get => _esGrupoVisible;
            set { _esGrupoVisible = value; OnPropertyChanged(nameof(EsGrupoVisible)); }
        }

        public UbicacionGrupo()
        {
            Productos = new ObservableCollection<ProductoDetalle>();
        }
    }

    public class ProductoDetalle : Xamarin.Forms.BindableObject
    {
        public string UPC { get; set; }
        public string Descripcion { get; set; }
        public int Cantidad { get; set; }
        public int UnidadesxFardo { get; set; }
        public int Existencia { get; set; }
        public bool EstaConfirmado { get; set; }
        public string Observaciones { get; set; }
        public int IdUbicacion { get; set; }
        public string NombreSubCategoria { get; set; }
        public int IdentificacionCajas { get; set; }
        public bool MostrarIdentificacionCajas { get; set; }
        public string Rack { get; set; }
        public int Nivel { get; set; }
        public string Bloque { get; set; }
        public string Pasillo { get; set; }
        public string TextoUbicacion => $"R: {Rack}  Niv: {Nivel}  Cubo: {Bloque}  Pas: {Pasillo}";

        private bool _esProductoEnFoco = false;
        public bool EsProductoEnFoco
        {
            get => _esProductoEnFoco;
            set
            {
                _esProductoEnFoco = value;
                OnPropertyChanged(nameof(EsProductoEnFoco));
                OnPropertyChanged(nameof(ColorFondoTarjeta));
                OnPropertyChanged(nameof(ColorBordeTarjeta));
            }
        }

        public string ColorFondoTarjeta => EsProductoEnFoco ? "#FEF3C7" : "White";
        public string ColorBordeTarjeta => EstaConfirmado ? "#48BB78" : EsProductoEnFoco ? "#F59E0B" : "#e2e8f0";

        private bool _esProductoActual = true;
        public bool EsProductoActual
        {
            get => _esProductoActual;
            set { _esProductoActual = value; OnPropertyChanged(nameof(EsProductoActual)); }
        }

        public bool TieneProgresoParcial =>
             CantidadEscaneada > 0 && CantidadEscaneada < Cantidad && !EstaConfirmado;

        // ─── Progreso de escaneo parcial ───
        private int _cantidadEscaneada = 0;
        public int CantidadEscaneada
        {
            get => _cantidadEscaneada;
            set
            {
                _cantidadEscaneada = value;
                OnPropertyChanged(nameof(CantidadEscaneada));
                OnPropertyChanged(nameof(TextoProgreso));
                OnPropertyChanged(nameof(ColorProgreso));
                OnPropertyChanged(nameof(PorcentajeProgreso));
                OnPropertyChanged(nameof(TieneProgresoParcial));
            }
        }

        public string TextoProgreso => $"{CantidadEscaneada}/{Cantidad}";

        public string ColorProgreso
        {
            get
            {
                if (CantidadEscaneada == 0) return "#E53E3E";
                if (CantidadEscaneada >= Cantidad) return "#48BB78";
                return "#ECC94B";
            }
        }

        public double PorcentajeProgreso =>
            Cantidad > 0 ? Math.Min((double)CantidadEscaneada / Cantidad, 1.0) : 0;

        public bool ExistenciaBaja => Existencia < Cantidad;
        public bool SinExistencia => Existencia <= 0;
        public string ColorExistencia
        {
            get
            {
                if (SinExistencia) return "#E53E3E";
                if (ExistenciaBaja) return "#ECC94B";
                return "#2D3748";
            }
        }
    }

    public class UbicacionBodega
    {
        public int Id { get; set; }
        public string Rack { get; set; }
        public int Nivel { get; set; }
        public string Descripcion { get; set; }
    }

    // ─────────────────────────────────────────────
    // CONVERTERS
    // ─────────────────────────────────────────────

    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
            => (bool)value ? "#48BB78" : "#e2e8f0";

        public object ConvertBack(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class StringNotEmptyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            if (value is string s) return !string.IsNullOrEmpty(s);
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class ProgresoColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
            => value?.ToString() ?? "#E53E3E";

        public object ConvertBack(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }
    public class PasilloGrupo : ObservableCollection<ProductoDetalle>
    {
        public string Titulo { get; }
        public int NumeroProductos => Count;
        public PasilloGrupo(string pasillo, IEnumerable<ProductoDetalle> productos) : base(productos)
        {
            Titulo = string.IsNullOrWhiteSpace(pasillo) ? "Sin Pasillo" : $"Pasillo {pasillo}";
        }
    }

    public class MotivoPreparacion
    {
        public int IdPreparacion { get; set; }
        public string DescripcionMotivo { get; set; }
    }

    public class ProductoPaquete
    {
        public string UPCPaquete { get; set; }
        public int Cantidad { get; set; }
    }

    public class ProductoInventarioInfo
    {
        public string Upc { get; set; }
        public string DescLarga { get; set; }
        public int UnidadesFardo { get; set; }
        public int IdProveedores { get; set; }
        public int IdDepartamentos { get; set; }
        public decimal Costo { get; set; }
        public decimal Existencia1 { get; set; }
        public decimal Existencia2 { get; set; }
        public decimal Existencia3 { get; set; }
        public decimal Existencia4 { get; set; }
        public bool EsProductoPaquete { get; set; }
    }

    public partial class DetallePedidoPendientes : ContentPage
    {
        private readonly int idPedido;
        private readonly int noHoja;
        private List<ProductoDetalle> todosLosProductos;
        private ObservableCollection<PasilloGrupo> productosAgrupados;
        private string upcFiltrado = null;
        private string nombreCompleto;
        private string nombreEmpresa;
        private int numeroPedido;
        private int? idInventarioActual = null;
        private bool isConfirming = false;
        private bool modoManualActivo = false;
        private string campoUpc;
        // Producto con escaneo parcial en curso
        private ProductoDetalle productoEnProgreso = null;

        private int idSucursalPedido;
        private string nombreSucursalPedido;
        private int idDepartamentoPedido;
        private string nombreDepartamentoPedido;
        private int idRazonSocialPedido;
        private string nombreRazonSocialPedido;
        private bool mostrarIdentificacionCajas;
        private string upcManualResuelto = null;

        private Dictionary<string, ProductoInventarioInfo> productosInventarioCache;
        private Dictionary<string, (int Cantidad, string Descripcion)> _productosConfirmados = new Dictionary<string, (int, string)>();
        private string _connectionString;
        private bool _initialized = false;
        // La tarima abierta se identifica por IdTarima (no cambia); NoTarima puede
        // renumerarse si se elimina una tarima vacía, por eso se relee al confirmar.
        private int _idTarimaActual = 0;
        private int _tarimaActualNumero = 0;
        private int _cantTarimas = 0;
        private bool _toastActivo = false;

        // Popup "Varias tarimas": todas las tarimas son fijas salvo la última, que se puede editar.
        private ProductoDetalle _productoReparto = null;
        private int _repartoFardosPorTarima = 0;
        private readonly List<int> _repartoFijas = new List<int>();
        private readonly List<Label> _repartoEtiquetas = new List<Label>();
        private Entry _repartoUltimaEntry = null;
        private Frame _repartoUltimaFila = null;
        private Button _repartoAjustarButton = null;
        private int _repartoPrimerNumero = 0;
        private int _repartoTarimaAbierta = 0;
        private bool _repartoActualizando = false;
        private bool _repartoParpadeando = false;
        // Tarimas completas creadas (ya entregadas) en el último reparto: necesitan su tarjeta NFC
        private readonly List<(int IdTarima, int NoTarima)> _tarimasRepartoEntregadas = new List<(int, int)>();

        private System.Timers.Timer _cronometro;
        private TimeSpan _tiempoTranscurrido = TimeSpan.Zero;
        private bool _hojaFinalizada = false;
        private bool _parpadeoEstado = false;
        private string CronometroKey => $"crono_{idPedido}_{noHoja}";
        private string CronometroSalidaKey => $"crono_salida_{idPedido}_{noHoja}";

        public DetallePedidoPendientes(int idPedido, int noHoja)
        {
            InitializeComponent();
            this.idPedido = idPedido;
            this.noHoja = noHoja;
            this.numeroPedido = idPedido;
            string ipActual = Preferences.Get("ip_address", "");
            campoUpc = ipActual == "172.30.65.25" ? "UPCProducto" : "UPC";
            mostrarIdentificacionCajas = ipActual == "172.30.65.25";
            _connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
            productosAgrupados = new ObservableCollection<PasilloGrupo>();
            ConfigurarEntradas();
        }

        // ─────────────────────────────────────────
        // MODO MANUAL
        // ─────────────────────────────────────────

        private void OnUpcDoubleTapped(object sender, EventArgs e)
        {
            modoManualActivo = !modoManualActivo;
            MostrarOcultarCampoManual(modoManualActivo);
        }

        private void MostrarOcultarCampoManual(bool mostrar)
        {
            CantidadEntry.IsVisible = mostrar;
            ModoManualIndicador.IsVisible = mostrar;

            if (mostrar)
            {
                if (productoEnProgreso != null)
                {
                    int restante = productoEnProgreso.Cantidad - productoEnProgreso.CantidadEscaneada;
                    CantidadEntry.Text = restante > 0 ? restante.ToString() : productoEnProgreso.Cantidad.ToString();
                }
                Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
            }
            else
            {
                CantidadEntry.Text = string.Empty;
                Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
            }
        }

        private void DesactivarModoManual()
        {
            modoManualActivo = false;
            MostrarOcultarCampoManual(false);
            if (productoEnProgreso != null)
                productoEnProgreso.EsProductoEnFoco = false;
            Device.BeginInvokeOnMainThread(() =>
            {
                ToggleModoManualButton.BackgroundColor = Color.FromHex("#EDF2F7");
            });
        }
        private void ConfigurarEntradas()
        {
            UpcEntry.Completed += async (sender, e) =>
            {
                if (RepartoOverlay.IsVisible)
                {
                    UpcEntry.Text = string.Empty;
                    return;
                }

                if (modoManualActivo)
                {
                    string upcTemp = UpcEntry.Text?.Trim();
                    if (string.IsNullOrEmpty(upcTemp))
                    {
                        Device.BeginInvokeOnMainThread(() => CantidadEntry.Focus());
                        return;
                    }

                    if (upcTemp.Length < 13)
                        upcTemp = upcTemp.PadLeft(13, '0');

                    string upcResueltoManual = upcTemp;
                    var productoManual = todosLosProductos?.FirstOrDefault(p => p.UPC == upcTemp);

                    if (productoManual == null)
                    {
                        try
                        {
                            using (var connection = new MySqlConnection(BuildConnectionString()))
                            {
                                connection.Open();
                                using (var command = new MySqlCommand(
                                    "SELECT UPCPaquete FROM productospaquetes WHERE Upc = @UpcIngresado LIMIT 1", connection))
                                {
                                    command.Parameters.AddWithValue("@UpcIngresado", upcTemp);
                                    using (var reader = command.ExecuteReader())
                                    {
                                        if (reader.Read() && !reader.IsDBNull(0))
                                        {
                                            string upcPaquete = reader.GetString(0);
                                            if (upcPaquete.Length < 13)
                                                upcPaquete = upcPaquete.PadLeft(13, '0');

                                            var productoPaquete = todosLosProductos?.FirstOrDefault(p => p.UPC == upcPaquete);
                                            if (productoPaquete != null)
                                            {
                                                upcResueltoManual = upcPaquete;
                                                productoManual = productoPaquete;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            await DisplayAlert("Error", $"Error al buscar en productospaquetes: {ex.Message}", "OK");
                            Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
                            return;
                        }

                        if (productoManual == null)
                        {
                            await DisplayAlert("Error", "El UPC no existe en el pedido ni se encontró en productospaquetes", "OK");
                            UpcEntry.Text = string.Empty;
                            Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
                            return;
                        }
                    }

                    UpcEntry.Text = upcResueltoManual;
                    upcManualResuelto = upcResueltoManual;
                    upcFiltrado = upcResueltoManual;
                    AplicarFiltro();

                    if (productoEnProgreso != null && productoEnProgreso != productoManual)
                        productoEnProgreso.EsProductoEnFoco = false;
                    productoEnProgreso = productoManual;
                    productoManual.EsProductoEnFoco = true;

                    Device.BeginInvokeOnMainThread(() => CantidadEntry.Focus());
                    return;
                }

                try
                {
                    string upcIngresado = UpcEntry.Text?.Trim();
                    if (string.IsNullOrEmpty(upcIngresado))
                    {
                        await DisplayAlert("Error", "Por favor ingrese un UPC", "OK");
                        Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
                        return;
                    }

                    if (upcIngresado.Length < 13)
                    {
                        upcIngresado = upcIngresado.PadLeft(13, '0');
                        UpcEntry.Text = upcIngresado;
                    }

                    // ── PASO 1: Verificar si el UPC está directamente en la lista ──
                    string upcResuelto = upcIngresado;
                    ProductoDetalle productoResuelto = todosLosProductos?.FirstOrDefault(p => p.UPC == upcIngresado);

                    if (productoResuelto != null)
                    {
                        // Está directo en la lista, procesar escaneo
                        if (productoEnProgreso != null && productoEnProgreso.UPC != upcResuelto)
                        {
                            await DisplayAlert(
                                "Producto incompleto",
                                $"Debés completar primero:\n\"{productoEnProgreso.Descripcion}\"\n({productoEnProgreso.CantidadEscaneada}/{productoEnProgreso.Cantidad} escaneados)",
                                "OK");

                            UpcEntry.Text = string.Empty;
                            upcFiltrado = productoEnProgreso.UPC;
                            AplicarFiltro();
                            Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
                            return;
                        }

                        await ProcesarEscaneoUnitario(upcResuelto, productoResuelto);
                        return;
                    }

                    // ── Verificar si ya fue confirmado (sesión actual o BD) ──
                    if (_productosConfirmados.TryGetValue(upcIngresado, out var yaConfirmado))
                    {
                        await MostrarAlertaYaPreparado(yaConfirmado.Cantidad, yaConfirmado.Descripcion);
                        return;
                    }
                    var (confBD, cantBD, descBD, estadoBD) = await BuscarProductoConfirmadoEnBD(upcIngresado);
                    if (confBD)
                    {
                        await MostrarAlertaYaPreparado(cantBD, descBD, estadoBD);
                        return;
                    }

                    // ── PASO 2: Buscar en productospaquetes ──
                    List<ProductoPaquete> paquetes = new List<ProductoPaquete>();
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();
                        using (var command = new MySqlCommand(
                            "SELECT UPCPaquete, Cantidad FROM productospaquetes WHERE Upc = @UpcIngresado", connection))
                        {
                            command.Parameters.AddWithValue("@UpcIngresado", upcIngresado);
                            using (var reader = command.ExecuteReader())
                                while (reader.Read())
                                    paquetes.Add(new ProductoPaquete
                                    {
                                        UPCPaquete = reader.GetString("UPCPaquete"),
                                        Cantidad = reader.GetInt32("Cantidad")
                                    });
                        }
                    }

                    if (paquetes.Count == 0)
                    {
                        await DisplayAlert("Error", "El UPC escaneado no existe en el detalle", "OK");
                        UpcEntry.Text = string.Empty;
                        Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
                        return;
                    }
                    else if (paquetes.Count == 1)
                    {
                        // ── Un solo paquete ──
                        string upcPaquete = paquetes[0].UPCPaquete;
                        productoResuelto = todosLosProductos?.FirstOrDefault(p => p.UPC == upcPaquete);

                        if (productoResuelto == null)
                        {
                            if (_productosConfirmados.TryGetValue(upcPaquete, out var yaConfirmadoPaq))
                            {
                                await MostrarAlertaYaPreparado(yaConfirmadoPaq.Cantidad, yaConfirmadoPaq.Descripcion);
                                return;
                            }
                            var (confPaq, cantPaq, descPaq, estadoPaq) = await BuscarProductoConfirmadoEnBD(upcPaquete);
                            if (confPaq)
                            {
                                await MostrarAlertaYaPreparado(cantPaq, descPaq, estadoPaq);
                                return;
                            }
                            await DisplayAlert("Error", "No se encontró el producto del paquete en el pedido actual.", "OK");
                            UpcEntry.Text = string.Empty;
                            Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
                            return;
                        }

                        upcResuelto = upcPaquete;
                        UpcEntry.Text = upcPaquete;
                    }
                    else
                    {
                        // ── Múltiples paquetes — mostrar selector ──
                        string[] opciones = paquetes
                            .Select(p => $"UPC: {p.UPCPaquete} - Cantidad: {p.Cantidad} unidades").ToArray();

                        string seleccion = await DisplayActionSheet(
                            "Se encontraron múltiples productos. Seleccione uno:",
                            "Cancelar", null, opciones);

                        if (seleccion == "Cancelar" || seleccion == null)
                        {
                            UpcEntry.Text = string.Empty;
                            Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
                            return;
                        }

                        int idx = Array.IndexOf(opciones, seleccion);
                        var paqueteSeleccionado = paquetes[idx];

                        // Verificar qué productos del pedido coinciden con alguno de los UPCs
                        using (var connection = new MySqlConnection(BuildConnectionString()))
                        {
                            connection.Open();

                            var upcsDisponibles = string.Join(",", paquetes.Select(p => $"'{p.UPCPaquete}'"));

                            string upcActualEnDetalle = null;
                            string descripcionDetalle = null;

                            using (var command = new MySqlCommand($@"
                        SELECT {campoUpc}, Descripcion
                        FROM detallepedidostienda_bodega 
                        WHERE IdConsolidado = @IdPedido 
                        AND NoHoja = @NoHoja
                        AND {campoUpc} IN ({upcsDisponibles})
                        AND EstadoPreparacionproducto IN (0,4,3)
                        LIMIT 1", connection))
                            {
                                command.Parameters.AddWithValue("@IdPedido", idPedido);
                                command.Parameters.AddWithValue("@NoHoja", noHoja);

                                using (var reader = command.ExecuteReader())
                                {
                                    if (reader.Read())
                                    {
                                        upcActualEnDetalle = reader.GetString(0);
                                        descripcionDetalle = reader.GetString("Descripcion");
                                    }
                                }
                            }

                            if (upcActualEnDetalle == null)
                            {
                                await DisplayAlert("Error", "No se encontró ningún producto relacionado en el pedido actual.", "OK");
                                UpcEntry.Text = string.Empty;
                                Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
                                return;
                            }

                            if (upcActualEnDetalle != paqueteSeleccionado.UPCPaquete)
                            {
                                // El UPC seleccionado es diferente al registrado en el detalle
                                bool actualizar = await DisplayAlert(
                                    "Confirmación",
                                    $"Producto: {descripcionDetalle}\n\n" +
                                    $"UPC actual en pedido: {upcActualEnDetalle}\n" +
                                    $"UPC seleccionado: {paqueteSeleccionado.UPCPaquete}\n\n" +
                                    $"¿Desea usar el UPC seleccionado?",
                                    "Sí, usar este", "No, usar el actual");

                                if (actualizar)
                                {
                                    // Actualizar el UPC en el detalle
                                    using (var updateCmd = new MySqlCommand($@"
                                UPDATE detallepedidostienda_bodega 
                                SET {campoUpc} = @NuevoUPC
                                WHERE IdConsolidado = @IdPedido 
                                AND NoHoja = @NoHoja
                                AND {campoUpc} = @UPCActual
                                AND EstadoPreparacionproducto IN (0,4,3)", connection))
                                    {
                                        updateCmd.Parameters.AddWithValue("@NuevoUPC", paqueteSeleccionado.UPCPaquete);
                                        updateCmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                        updateCmd.Parameters.AddWithValue("@NoHoja", noHoja);
                                        updateCmd.Parameters.AddWithValue("@UPCActual", upcActualEnDetalle);

                                        int filas = updateCmd.ExecuteNonQuery();

                                        if (filas > 0)
                                        {
                                            await CargarProductos();
                                            upcResuelto = paqueteSeleccionado.UPCPaquete;
                                            UpcEntry.Text = upcResuelto;
                                        }
                                        else
                                        {
                                            await DisplayAlert("Error", "No se pudo actualizar el UPC.", "OK");
                                            UpcEntry.Text = string.Empty;
                                            Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
                                            return;
                                        }
                                    }
                                }
                                else
                                {
                                    // Usar el UPC actual que ya está en el detalle
                                    upcResuelto = upcActualEnDetalle;
                                    UpcEntry.Text = upcResuelto;
                                }
                            }
                            else
                            {
                                // Es el mismo UPC, continuar normal
                                upcResuelto = paqueteSeleccionado.UPCPaquete;
                                UpcEntry.Text = upcResuelto;
                            }
                        }

                        productoResuelto = todosLosProductos?.FirstOrDefault(p => p.UPC == upcResuelto);

                        if (productoResuelto == null)
                        {
                            await DisplayAlert("Error", "No se encontró el producto en el pedido actual.", "OK");
                            UpcEntry.Text = string.Empty;
                            Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
                            return;
                        }
                    }

                    // ── PASO 3: Verificar producto en progreso ──
                    if (productoEnProgreso != null && productoEnProgreso.UPC != upcResuelto)
                    {
                        await DisplayAlert(
                            "Producto incompleto",
                            $"Debés completar primero:\n\"{productoEnProgreso.Descripcion}\"\n({productoEnProgreso.CantidadEscaneada}/{productoEnProgreso.Cantidad} escaneados)",
                            "OK");

                        UpcEntry.Text = string.Empty;
                        upcFiltrado = productoEnProgreso.UPC;
                        AplicarFiltro();
                        Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
                        return;
                    }

                    // ── PASO 4: Procesar escaneo ──
                    await ProcesarEscaneoUnitario(upcResuelto, productoResuelto);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Error al procesar el UPC: {ex.Message}", "OK");
                    UpcEntry.Text = string.Empty;
                    Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
                }
            };

            UpcEntry.TextChanged += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.NewTextValue))
                    UpcEntry.Text = e.NewTextValue.ToUpper();
            };

            CantidadEntry.Completed += async (sender, e) =>
            {
                if (string.IsNullOrWhiteSpace(CantidadEntry.Text))
                {
                    await DisplayAlert("Error", "Por favor ingrese una cantidad", "OK");
                    CantidadEntry.Focus();
                    return;
                }

                if (!int.TryParse(CantidadEntry.Text, out int cantidad) || cantidad <= 0)
                {
                    await DisplayAlert("Error", "Por favor ingrese una cantidad válida mayor a 0", "OK");
                    CantidadEntry.Text = string.Empty;
                    CantidadEntry.Focus();
                    return;
                }

                Device.BeginInvokeOnMainThread(() =>
                {
                    LoadingLabel.Text = "Registrando...";
                    LoadingOverlay.IsVisible = true;
                });
                await OnConfirmarClickedInternal();
            };

            CantidadEntry.TextChanged += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.NewTextValue))
                    if (!int.TryParse(e.NewTextValue, out _))
                        CantidadEntry.Text = e.OldTextValue;
            };
        }

        private async Task ProcesarEscaneoUnitario(string upc, ProductoDetalle producto)
        {
            if (isConfirming) return;

            if (productoEnProgreso != null && productoEnProgreso != producto)
                productoEnProgreso.EsProductoEnFoco = false;

            productoEnProgreso = producto;
            producto.CantidadEscaneada++;
            producto.EsProductoEnFoco = true;


            upcFiltrado = upc;
            AplicarFiltro();
            ActualizarPanelProductoActual(producto);

            if (producto.CantidadEscaneada >= producto.Cantidad)
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    LoadingLabel.Text = "Confirmando...";
                    LoadingOverlay.IsVisible = true;
                });
                await ConfirmarPorEscaneo(upc, producto.Cantidad, producto);
                productoEnProgreso = null;
            }
            else
            {
                UpcEntry.Text = string.Empty;
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await Task.Delay(50);
                    UpcEntry.Focus();
                });
            }
        }

        private async Task ConfirmarPorEscaneo(string upc, int cantidadIngresada, ProductoDetalle producto)
        {
            isConfirming = true;
            try
            {
                await ConfirmarProductoConInventario(upc, cantidadIngresada, producto);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ConfirmarPorEscaneo error: {ex.Message}");
                await DisplayAlert("Error al confirmar", ex.Message, "OK");
            }
            finally
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    LoadingOverlay.IsVisible = false;
                    if (!_hojaFinalizada) UpcEntry?.Focus();
                });
                isConfirming = false;
            }
        }

        private async Task OnConfirmarClickedInternal()
        {
            if (isConfirming)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ Confirmación en progreso, ignorando...");
                return;
            }

            isConfirming = true;

            try
            {
                string upcIngresado = !string.IsNullOrEmpty(upcManualResuelto)
                    ? upcManualResuelto
                    : UpcEntry.Text?.Trim();

                if (string.IsNullOrEmpty(upcIngresado))
                {
                    await DisplayAlert("Error", "Por favor ingrese un UPC", "OK");
                    UpcEntry.Focus();
                    return;
                }

                var producto = todosLosProductos?.FirstOrDefault(p => p.UPC == upcIngresado);

                if (producto == null)
                {
                    bool encontrado = false;
                    try
                    {
                        using (var connection = new MySqlConnection(BuildConnectionString()))
                        {
                            connection.Open();
                            using (var command = new MySqlCommand(
                                "SELECT UPCPaquete FROM productospaquetes WHERE Upc = @UpcIngresado LIMIT 1", connection))
                            {
                                command.Parameters.AddWithValue("@UpcIngresado", upcIngresado);
                                using (var reader = command.ExecuteReader())
                                {
                                    if (reader.HasRows && reader.Read() && !reader.IsDBNull(0))
                                    {
                                        string upcPaquete = reader.GetString(0);
                                        if (upcPaquete.Length < 13)
                                            upcPaquete = upcPaquete.PadLeft(13, '0');

                                        var productoPaquete = todosLosProductos?.FirstOrDefault(p => p.UPC == upcPaquete);
                                        if (productoPaquete != null)
                                        {
                                            upcIngresado = upcPaquete;
                                            UpcEntry.Text = upcPaquete;
                                            producto = productoPaquete;
                                            encontrado = true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await DisplayAlert("Error", $"Error al consultar productospaquetes: {ex.Message}", "OK");
                        return;
                    }

                    if (!encontrado)
                    {
                        await DisplayAlert("Error", "El UPC no existe en el detalle ni se encontró en productospaquetes", "OK");
                        UpcEntry.Text = string.Empty;
                        UpcEntry.Focus();
                        return;
                    }
                }

                if (!int.TryParse(CantidadEntry.Text, out int cantidadIngresada))
                {
                    await DisplayAlert("Error", "Por favor ingrese una cantidad válida", "OK");
                    CantidadEntry.Focus();
                    return;
                }

                // ✅ Guardamos el valor anterior antes de modificar
                int cantidadEscaneadaAnterior = producto.CantidadEscaneada;
                producto.CantidadEscaneada = cantidadIngresada;

                if (cantidadIngresada < producto.Cantidad)
                {
                    int cantidadFaltante = producto.Cantidad - cantidadIngresada;

                    string accion = await DisplayActionSheet(
                        $"Confirmando {cantidadIngresada} de {producto.Cantidad} solicitados.\n\n¿Qué desea hacer con los {cantidadFaltante} faltantes?",
                        "Cancelar", null,
                        "Confirmar solo esta cantidad",
                        "Confirmar y enviar faltante a PUNTO");

                    if (accion == "Cancelar" || accion == null)
                    {
                        producto.CantidadEscaneada = cantidadEscaneadaAnterior; // ✅ Restaurar
                        CantidadEntry.Focus();
                        return;
                    }

                    if (accion == "Confirmar solo esta cantidad")
                        await ConfirmarProductoConInventario(upcIngresado, cantidadIngresada, producto, esModoManual: true);
                    else
                        await ConfirmarProductoYEnviarFaltanteAPuntoConInventario(
                            upcIngresado, cantidadIngresada, cantidadFaltante, producto, esModoManual: true);
                }
                else
                {
                    bool confirmarCantidad = true;
                    if (cantidadIngresada != producto.Cantidad)
                    {
                        confirmarCantidad = await DisplayAlert("Confirmación",
                            $"La cantidad ingresada ({cantidadIngresada}) es diferente a la solicitada ({producto.Cantidad}). ¿Está seguro?",
                            "Sí", "No");
                    }

                    if (confirmarCantidad)
                        await ConfirmarProductoConInventario(upcIngresado, cantidadIngresada, producto, esModoManual: true);
                    else
                    {
                        producto.CantidadEscaneada = cantidadEscaneadaAnterior; // ✅ Restaurar
                        CantidadEntry.Text = string.Empty;
                        CantidadEntry.Focus();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnConfirmarClickedInternal error: {ex.Message}");
                await DisplayAlert("Error al confirmar", ex.Message, "OK");
            }
            finally
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    LoadingOverlay.IsVisible = false;
                    if (!_hojaFinalizada) UpcEntry?.Focus();
                });
                isConfirming = false;
                DesactivarModoManual();
                productoEnProgreso = null;
                upcManualResuelto = null;
            }
        }

        // ─────────────────────────────────────────
        // CARGA DE INFORMACIÓN DEL PEDIDO
        // ─────────────────────────────────────────

        private async Task CargarInformacionPedido()
        {
            try
            {
                using (var connection = new MySqlConnection(BuildConnectionString()))
                {
                    connection.Open();
                    using (var command = new MySqlCommand(@"
                        SELECT 
                            pb.NombreEmpresa,
                            pb.IdSucursal,
                            pb.NombreSucursales,
                            pb.IdDepartamento,
                            pb.Departamento,
                            pb.IdRazonSocial,
                            pb.RazonSocial,
                            d.Nombre AS NombreDepartamento,
                            CONCAT(pb.NombreEmpresa, d.Nombre) AS NombreCompletoEmpresa
                        FROM pedidostienda_bodega pb
                        INNER JOIN departamentos d ON pb.Departamento = d.Id
                        WHERE pb.IdPedidos = @IdPedido", connection))
                    {
                        command.Parameters.AddWithValue("@IdPedido", idPedido);
                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                nombreEmpresa = reader.GetString("NombreCompletoEmpresa");
                                idSucursalPedido = reader.GetInt32("IdSucursal");
                                nombreSucursalPedido = reader.GetString("NombreSucursales");
                                idDepartamentoPedido = reader.GetInt32("IdDepartamento");
                                nombreDepartamentoPedido = reader.GetString("NombreDepartamento");
                                idRazonSocialPedido = reader.GetInt32("IdRazonSocial");
                                nombreRazonSocialPedido = reader.GetString("RazonSocial");
                            }
                        }
                    }
                }
                Device.BeginInvokeOnMainThread(() => ActualizarHeaderInformacion());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al cargar información del pedido: {ex.Message}");
                Device.BeginInvokeOnMainThread(() =>
                    InfoPedidoLabel.Text = $"Pedido #{numeroPedido} - Hoja #{noHoja}");
            }
        }

        private void ActualizarHeaderInformacion()
            => InfoPedidoLabel.Text = $"Pedido #{numeroPedido} - Hoja #{noHoja}";

        // ─────────────────────────────────────────
        // VERIFICAR HOJA Y CARGAR TARIMA EN USO
        // El inventario es por tarima (TarimasInventario.IdInventario): al entregar la
        // tarima, la siguiente crea el suyo. Las tarimas no pasan de una
        // hoja a otra: al entrar se retoma solo la tarima abierta de esta hoja.
        // Devuelve false si el usuario no puede trabajar esta hoja (la página se cierra).
        // ─────────────────────────────────────────

        // Tarima abierta del usuario en el pedido sin productos de otras hojas
        // (normales con su NoTarima o repartidos con una parte en ella).
        private const string SqlTarimaAbiertaDeHoja = @"
            SELECT ti.IdTarima, ti.NoTarima FROM TarimasInventario ti
            WHERE ti.IdPedido = @IdPedido AND ti.IdUsuario = @IdUsuario
              AND ti.FechaFinalizacion IS NULL
              AND NOT EXISTS (
                  SELECT 1 FROM detallepedidostienda_bodega d
                  WHERE d.IdConsolidado = ti.IdPedido
                    AND d.NoHoja <> @NoHoja
                    AND (d.NoTarima = ti.NoTarima
                         OR (d.NoTarima = 0 AND d.Observaciones REGEXP
                             CONCAT('^\\[TARIMAS:([0-9]+=[0-9]+;)*', ti.NoTarima, '='))))
            ORDER BY ti.NoTarima
            LIMIT 1";

        private (int IdTarima, int NoTarima) LeerTarimaAbiertaDeHoja(MySqlConnection connection, MySqlTransaction transaction)
        {
            using (var cmd = new MySqlCommand(SqlTarimaAbiertaDeHoja, connection, transaction))
            {
                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                cmd.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                cmd.Parameters.AddWithValue("@NoHoja", noHoja);
                using (var reader = cmd.ExecuteReader())
                    return reader.Read()
                        ? (reader.GetInt32("IdTarima"), reader.GetInt32("NoTarima"))
                        : (0, 0);
            }
        }

        private async Task<bool> VerificarYCargarInventario()
        {
            LoadingOverlay.IsVisible = true;
            try
            {
                int? idUsuarioHoja = null;
                int idTarima = 0, noTarima = 0, cantTarimas = 0;
                int? idInventario = null;

                await Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();

                        using (var cmd = new MySqlCommand(@"
                            SELECT IdUsuario FROM PreparacionPedidos
                            WHERE IdPedido = @IdPedido AND NoHoja = @NoHoja
                            LIMIT 1", connection))
                        {
                            cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                            cmd.Parameters.AddWithValue("@NoHoja", noHoja);
                            var res = cmd.ExecuteScalar();
                            if (res != null && res != DBNull.Value)
                                idUsuarioHoja = Convert.ToInt32(res);
                        }

                        using (var cmd = new MySqlCommand(
                            "SELECT COALESCE(CantTarimas,0) FROM pedidostienda_bodega WHERE IdPedidos = @IdPedido", connection))
                        {
                            cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                            var res = cmd.ExecuteScalar();
                            cantTarimas = res != null && res != DBNull.Value ? Convert.ToInt32(res) : 0;
                        }

                        (idTarima, noTarima) = LeerTarimaAbiertaDeHoja(connection, null);
                        if (idTarima > 0)
                            idInventario = RepartoTarimas.LeerInventarioTarima(connection, null, idTarima);
                    }
                });

                if (idUsuarioHoja.HasValue && idUsuarioHoja.Value > 0 && idUsuarioHoja.Value != App.IdUsuarioActual)
                {
                    await DisplayAlert("Acceso Denegado", "Esta hoja está asignada a otro usuario.", "OK");
                    await Navigation.PopAsync();
                    return false;
                }

                _idTarimaActual = idTarima;
                _tarimaActualNumero = noTarima;
                _cantTarimas = cantTarimas;
                idInventarioActual = idInventario;

                ActualizarBadgeTarima();
                MostrarInventarioActual();
                return true;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar la tarima en uso: " + ex.Message, "OK");
                return true;
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        // El panel superior siempre visible; el inventario es el de la tarima en uso.
        private void MostrarInventarioActual()
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                InventarioIdLabel.Text = idInventarioActual.HasValue
                    ? $"Inventario #: {idInventarioActual.Value}"
                    : "Inventario #: se crea al confirmar";
                InventarioInfoLabel.Text = $"{nombreSucursalPedido} - {nombreDepartamentoPedido} - {nombreRazonSocialPedido}";
                InventarioPanelFrame.IsVisible = true;
            });
        }

        // ─────────────────────────────────────────
        // CARGAR PRODUCTOS
        // ─────────────────────────────────────────

        private async Task CargarProductos()
        {
            LoadingOverlay.IsVisible = true;

            // Al recargar se crean objetos nuevos: conservar el escaneo parcial
            // y volver a enlazar el producto en progreso por su UPC.
            var escaneosPrevios = (todosLosProductos ?? new List<ProductoDetalle>())
                .Where(p => p.CantidadEscaneada > 0)
                .GroupBy(p => p.UPC)
                .ToDictionary(g => g.Key, g => g.First().CantidadEscaneada);
            string upcEnProgreso = productoEnProgreso?.UPC;

            try
            {
                await Task.Run(() =>
                {
                using (var connection = new MySqlConnection(BuildConnectionString()))
                {
                    connection.Open();
                    using (var command = new MySqlCommand($@"
                SELECT
                    d.{campoUpc} AS UPC,
                    d.Descripcion,
                    d.Cantidad,
                    d.Observaciones,
                    d.IdUbicacionBodega,
                    d.EstadoPreparacionproducto,
                    COALESCE(pp.Cantidad, 1) AS UnidadesxFardo,
                    CASE 
                        WHEN pp.Cantidad IS NULL OR pp.Cantidad = 0 
                        THEN COALESCE(p.Existencia, 0)
                        ELSE COALESCE(p.Existencia / pp.Cantidad, 0)
                    END AS Existencia,
                    CASE WHEN d.EstadoPreparacionproducto NOT IN(0,4,3) THEN 1 ELSE 0 END AS EstaConfirmado,
                    ub.Id AS UbicacionId,
                    ub.Rack,
                    ub.Nivel,
                    CAST(COALESCE(ub.Bloque, '') AS CHAR) AS Bloque,
                    CAST(COALESCE(ub.Pasillo, '') AS CHAR) AS Pasillo,
                    ub.Descripcion AS DescripcionUbicacion,
                    COALESCE(pp.Upc, d.{campoUpc}) as UpcBase,
                    COALESCE(NULLIF(pp.DescLarga, '0'), p.DescLarga, d.Descripcion) as DescLargaProducto,
                    COALESCE(p.IdProveedores, 0) as IdProveedores,
                    COALESCE(p.IdDepartamentos, 0) as IdDepartamentos,
                    COALESCE(p.Costo, 0) as Costo,
                    COALESCE(p.Existencia_1, 0) as Existencia_1,
                    COALESCE(p.Existencia_2, 0) as Existencia_2,
                    COALESCE(p.Existencia_3, 0) as Existencia_3,
                    COALESCE(p.Existencia_4, 0) as Existencia_4,
                    CASE WHEN pp.UPCPaquete IS NOT NULL THEN 1 ELSE 0 END as EsProductoPaquete,
                    COALESCE(sc.Nombre, '') AS NombreSubCategoria,
                    COALESCE(sc.IdentificacionCajas, 0) AS IdentificacionCajas
                FROM detallepedidostienda_bodega d
                INNER JOIN pedidostienda_bodega pb ON d.IdConsolidado = pb.IdPedidos
                LEFT JOIN productospaquetes pp ON d.{campoUpc} = pp.UPCPaquete
                LEFT JOIN productos p ON COALESCE(pp.Upc, d.{campoUpc}) = p.Upc
                INNER JOIN ubicacionesbodega ub ON d.IdUbicacionBodega = ub.Id
                LEFT JOIN subcategorias sc ON p.IdSubCategorias = sc.Id
                WHERE d.IdConsolidado = @IdPedido 
                  AND d.NoHoja = @NoHoja
                  AND d.EstadoPreparacionproducto IN (0,4,3)
                ORDER BY CAST(NULLIF(ub.Pasillo, '') AS UNSIGNED) ASC,
                         CAST(NULLIF(ub.Bloque, '') AS UNSIGNED) ASC,
                         ub.Nivel ASC,
                         ub.Rack ASC,
                         d.Cantidad ASC", connection))
                    {
                        command.Parameters.AddWithValue("@IdPedido", idPedido);
                        command.Parameters.AddWithValue("@NoHoja", noHoja);

                        using (var reader = command.ExecuteReader())
                        {
                            todosLosProductos = new List<ProductoDetalle>();
                            productosInventarioCache = new Dictionary<string, ProductoInventarioInfo>();

                            while (reader.Read())
                            {
                                int idUbicacion = reader.GetInt32("UbicacionId");
                                string upc = reader.GetString("UPC");

                                var producto = new ProductoDetalle
                                {
                                    UPC = upc,
                                    Descripcion = reader.GetString("Descripcion"),
                                    Cantidad = reader.GetInt32("Cantidad"),
                                    UnidadesxFardo = reader.GetInt32("UnidadesxFardo"),
                                    Existencia = reader.GetInt32("Existencia"),
                                    EstaConfirmado = reader.GetInt32("EstaConfirmado") == 1,
                                    Observaciones = !reader.IsDBNull(reader.GetOrdinal("Observaciones"))
                                                     ? reader.GetString("Observaciones") : string.Empty,
                                    IdUbicacion = idUbicacion,
                                    NombreSubCategoria = reader.GetString("NombreSubCategoria"),
                                    IdentificacionCajas = reader.GetInt32("IdentificacionCajas"),
                                    MostrarIdentificacionCajas = mostrarIdentificacionCajas,
                                    Rack = reader.GetString("Rack"),
                                    Nivel = reader.GetInt32("Nivel"),
                                    Bloque = reader.GetString("Bloque"),
                                    Pasillo = reader.GetString("Pasillo"),
                                    CantidadEscaneada = escaneosPrevios.TryGetValue(upc, out int escaneados) ? escaneados : 0
                                };

                                todosLosProductos.Add(producto);

                                if (!productosInventarioCache.ContainsKey(upc))
                                {
                                    productosInventarioCache[upc] = new ProductoInventarioInfo
                                    {
                                        Upc = reader.GetString("UpcBase"),
                                        DescLarga = reader.GetString("DescLargaProducto"),
                                        UnidadesFardo = reader.GetInt32("UnidadesxFardo"),
                                        IdProveedores = reader.GetInt32("IdProveedores"),
                                        IdDepartamentos = reader.GetInt32("IdDepartamentos"),
                                        Costo = reader.GetDecimal("Costo"),
                                        Existencia1 = reader.GetDecimal("Existencia_1"),
                                        Existencia2 = reader.GetDecimal("Existencia_2"),
                                        Existencia3 = reader.GetDecimal("Existencia_3"),
                                        Existencia4 = reader.GetDecimal("Existencia_4"),
                                        EsProductoPaquete = reader.GetInt32("EsProductoPaquete") == 1
                                    };
                                }
                            }

                            var grupos = todosLosProductos
                                .OrderBy(p => int.TryParse(p.Pasillo, out int pas) ? pas : 0)
                                .ThenBy(p => int.TryParse(p.Bloque, out int blo) ? blo : 0)
                                .ThenBy(p => p.Nivel)
                                .ThenBy(p => p.Rack)
                                .GroupBy(p => p.Pasillo)
                                .Select(g => new PasilloGrupo(g.Key, g))
                                .ToList();

                            Device.BeginInvokeOnMainThread(() =>
                            {
                                productosAgrupados.Clear();
                                foreach (var g in grupos)
                                    productosAgrupados.Add(g);
                                if (UbicacionesCollection.ItemsSource == null)
                                    UbicacionesCollection.ItemsSource = productosAgrupados;
                                TotalSkusPendientesLabel.Text = todosLosProductos?.Count.ToString() ?? "0";
                            });
                        }
                    }
                }
                }); // fin Task.Run

                // Si el producto en progreso ya no está pendiente (p. ej. se le aplicó
                // un motivo), se libera para no bloquear los siguientes escaneos.
                productoEnProgreso = upcEnProgreso == null
                    ? null
                    : todosLosProductos?.FirstOrDefault(p => p.UPC == upcEnProgreso);
                if (productoEnProgreso != null)
                    productoEnProgreso.EsProductoEnFoco = true;
                ActualizarPanelProductoActual(productoEnProgreso);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar productos: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
                Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
            }
        }

        // ─────────────────────────────────────────
        // CONFIRMAR CON INVENTARIO
        // ─────────────────────────────────────────

        private async Task ConfirmarProductoConInventario(string upcIngresado, int cantidadIngresada, ProductoDetalle producto, bool esModoManual = false)
        {
            await AsegurarTarimaActiva();

            if (!productosInventarioCache.TryGetValue(upcIngresado, out var infoProducto))
            {
                await DisplayAlert("Error", "No se encontró información del producto en caché", "OK");
                return;
            }

            int cantidadTotal = infoProducto.EsProductoPaquete
                ? cantidadIngresada * infoProducto.UnidadesFardo
                : cantidadIngresada;

            int idRazonSocial = DeterminarRazonSocial(idRazonSocialPedido, cantidadTotal,
                infoProducto.Existencia1, infoProducto.Existencia2,
                infoProducto.Existencia3, infoProducto.Existencia4);

            int unidadesTotales = cantidadIngresada * producto.UnidadesxFardo;
            DateTime ahora = DateTime.Now;
            string fechaActual = ahora.ToString("yyyy-MM-dd");
            string fechaHoraActual = ahora.ToString("yyyy-MM-dd HH:mm:ss");

            await Task.Run(() =>
            {
            using (var connection = new MySqlConnection(BuildConnectionString()))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        int idInventario = ObtenerOCrearInventario(connection, transaction, fechaActual, fechaHoraActual);

                        RegistrarDetalleInventario(connection, transaction, idInventario,
                            upcIngresado, infoProducto, cantidadTotal, idRazonSocial);

                        using (var cmd = new MySqlCommand($@"
                            UPDATE detallepedidostienda_bodega
                            SET CantConfirmada = @CantConfirmada,
                                NoTarima = @NoTarima,
                                FechaPreparo = CURDATE(),
                                Fechahorapreparo = NOW(),
                                EstadoPreparacionproducto = '1',
                                IdUsuariopreparo = @IdUsuario,
                                ExistenciaFardos = GREATEST(0, ExistenciaFardos - @CantidadFardos),
                                Existencia = GREATEST(0, Existencia - @CantidadUnidades)
                            WHERE IdConsolidado = @IdPedido AND {campoUpc} = @UPC AND NoHoja = @NoHoja", connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@CantConfirmada", cantidadIngresada);
                            cmd.Parameters.AddWithValue("@NoTarima", _tarimaActualNumero);
                            cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                            cmd.Parameters.AddWithValue("@UPC", upcIngresado);
                            cmd.Parameters.AddWithValue("@NoHoja", noHoja);
                            cmd.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                            cmd.Parameters.AddWithValue("@CantidadFardos", cantidadIngresada);
                            cmd.Parameters.AddWithValue("@CantidadUnidades", unidadesTotales);
                            cmd.ExecuteNonQuery();
                        }

                        if (esModoManual)
                        {
                            using (var cmd = new MySqlCommand(@"
                                INSERT INTO PedidoConfirmacionManual (
                                    IdPedido, NoHoja, IdInventario, Upc, Descripcion,
                                    Cantidad, IdUsuario, NombreUsuario, FechaHoraRegistro
                                ) VALUES (
                                    @IdPedido, @NoHoja, @IdInventario, @Upc, @Descripcion,
                                    @Cantidad, @IdUsuario, @NombreUsuario, NOW()
                                )", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                cmd.Parameters.AddWithValue("@NoHoja", noHoja);
                                cmd.Parameters.AddWithValue("@IdInventario", idInventario);
                                cmd.Parameters.AddWithValue("@Upc", upcIngresado);
                                cmd.Parameters.AddWithValue("@Descripcion", infoProducto.DescLarga);
                                cmd.Parameters.AddWithValue("@Cantidad", cantidadIngresada);
                                cmd.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                                cmd.Parameters.AddWithValue("@NombreUsuario", App.NombreUsuarioActual ?? "Usuario");
                                cmd.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                        idInventarioActual = idInventario;
                        MostrarInventarioActual();
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        throw new Exception($"Error al procesar producto: {ex.Message}");
                    }
                }
            }
            }); // fin Task.Run

            if (_productosConfirmados.TryGetValue(upcIngresado, out var prev))
                _productosConfirmados[upcIngresado] = (prev.Cantidad + cantidadIngresada, infoProducto.DescLarga);
            else
                _productosConfirmados[upcIngresado] = (cantidadIngresada, infoProducto.DescLarga);

            MostrarToast("✓ Producto registrado exitosamente");
            await ActualizarProductoLocal(upcIngresado);
            await VerificarFinalizacionHoja();

            UpcEntry.Text = string.Empty;
            CantidadEntry.Text = string.Empty;
            upcFiltrado = null;
            ActualizarPanelProductoActual(null);
            Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
        }

        private async Task ConfirmarProductoYEnviarFaltanteAPuntoConInventario(
            string upcIngresado, int cantidadConfirmada, int cantidadFaltante, ProductoDetalle producto, bool esModoManual = false)
        {
            await AsegurarTarimaActiva();

            if (!productosInventarioCache.TryGetValue(upcIngresado, out var infoProducto))
            {
                await DisplayAlert("Error", "No se encontró información del producto en caché", "OK");
                return;
            }

            int cantidadTotal = infoProducto.EsProductoPaquete
                ? cantidadConfirmada * infoProducto.UnidadesFardo
                : cantidadConfirmada;

            int idRazonSocial = DeterminarRazonSocial(idRazonSocialPedido, cantidadTotal,
                infoProducto.Existencia1, infoProducto.Existencia2,
                infoProducto.Existencia3, infoProducto.Existencia4);

            int unidadesTotales = cantidadConfirmada * producto.UnidadesxFardo;
            DateTime ahora = DateTime.Now;
            string fechaActual = ahora.ToString("yyyy-MM-dd");
            string fechaHoraActual = ahora.ToString("yyyy-MM-dd HH:mm:ss");

            await Task.Run(async () =>
            {
            using (var connection = new MySqlConnection(BuildConnectionString()))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        int idInventario = ObtenerOCrearInventario(connection, transaction, fechaActual, fechaHoraActual);

                        RegistrarDetalleInventario(connection, transaction, idInventario,
                            upcIngresado, infoProducto, cantidadTotal, idRazonSocial);

                        using (var cmd = new MySqlCommand($@"
                            UPDATE detallepedidostienda_bodega
                            SET Cantidad = @CantidadConfirmada,
                                CantConfirmada = @CantidadConfirmada,
                                NoTarima = @NoTarima,
                                FechaPreparo = CURDATE(),
                                Fechahorapreparo = NOW(),
                                EstadoPreparacionproducto = '1',
                                IdUsuariopreparo = @IdUsuario,
                                ExistenciaFardos = GREATEST(0, ExistenciaFardos - @CantidadFardos),
                                Existencia = GREATEST(0, Existencia - @CantidadUnidades)
                            WHERE IdConsolidado = @IdPedido AND {campoUpc} = @UPC AND NoHoja = @NoHoja", connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@CantidadConfirmada", cantidadConfirmada);
                            cmd.Parameters.AddWithValue("@NoTarima", _tarimaActualNumero);
                            cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                            cmd.Parameters.AddWithValue("@UPC", upcIngresado);
                            cmd.Parameters.AddWithValue("@NoHoja", noHoja);
                            cmd.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                            cmd.Parameters.AddWithValue("@CantidadFardos", cantidadConfirmada);
                            cmd.Parameters.AddWithValue("@CantidadUnidades", unidadesTotales);
                            cmd.ExecuteNonQuery();
                        }

                        int nuevaHoja = await EnviarFaltanteAPunto(connection, transaction,
                            upcIngresado, producto.Descripcion, cantidadFaltante);

                        if (esModoManual)
                        {
                            using (var cmd = new MySqlCommand(@"
                                INSERT INTO PedidoConfirmacionManual (
                                    IdPedido, NoHoja, IdInventario, Upc, Descripcion,
                                    Cantidad, IdUsuario, NombreUsuario, FechaHoraRegistro
                                ) VALUES (
                                    @IdPedido, @NoHoja, @IdInventario, @Upc, @Descripcion,
                                    @Cantidad, @IdUsuario, @NombreUsuario, NOW()
                                )", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                cmd.Parameters.AddWithValue("@NoHoja", noHoja);
                                cmd.Parameters.AddWithValue("@IdInventario", idInventario);
                                cmd.Parameters.AddWithValue("@Upc", upcIngresado);
                                cmd.Parameters.AddWithValue("@Descripcion", infoProducto.DescLarga);
                                cmd.Parameters.AddWithValue("@Cantidad", cantidadConfirmada);
                                cmd.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                                cmd.Parameters.AddWithValue("@NombreUsuario", App.NombreUsuarioActual ?? "Usuario");
                                cmd.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                        idInventarioActual = idInventario;
                        MostrarInventarioActual();

                        MostrarToast($"✓ Confirmado. Faltante enviado a PUNTO hoja #{nuevaHoja}");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        throw new Exception($"Error al procesar el producto: {ex.Message}");
                    }
                }
            }
            }); // fin Task.Run

            if (_productosConfirmados.TryGetValue(upcIngresado, out var prev2))
                _productosConfirmados[upcIngresado] = (prev2.Cantidad + cantidadConfirmada, infoProducto.DescLarga);
            else
                _productosConfirmados[upcIngresado] = (cantidadConfirmada, infoProducto.DescLarga);

            await ActualizarProductoLocal(upcIngresado);
            await VerificarFinalizacionHoja();

            UpcEntry.Text = string.Empty;
            CantidadEntry.Text = string.Empty;
            upcFiltrado = null;
            ActualizarPanelProductoActual(null);
            Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
        }

        // ─────────────────────────────────────────
        // HELPERS DE CONFIRMACIÓN (misma transacción del llamador)
        // ─────────────────────────────────────────

        // Inventario de la tarima activa: se crea con el primer producto que entra en ella.
        // También relee (y bloquea) la tarima para usar su NoTarima vigente en el resto
        // de la transacción.
        private int ObtenerOCrearInventario(MySqlConnection connection, MySqlTransaction transaction,
            string fechaActual, string fechaHoraActual)
        {
            if (_idTarimaActual == 0)
                throw new Exception("No hay una tarima activa.");

            int? idExistente;
            using (var cmd = new MySqlCommand(@"
                SELECT NoTarima, IdInventario FROM TarimasInventario
                WHERE IdTarima = @IdTarima FOR UPDATE", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@IdTarima", _idTarimaActual);
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read())
                        throw new Exception("La tarima activa ya no existe. Vuelva a entrar a la hoja.");
                    _tarimaActualNumero = r.GetInt32("NoTarima");
                    idExistente = r.IsDBNull(r.GetOrdinal("IdInventario")) || r.GetInt32("IdInventario") <= 0
                        ? (int?)null
                        : r.GetInt32("IdInventario");
                }
            }

            if (idExistente.HasValue)
                return idExistente.Value;

            int idInventario = CrearInventario(connection, transaction, fechaActual, fechaHoraActual);

            using (var cmd = new MySqlCommand(
                "UPDATE TarimasInventario SET IdInventario = @IdInventario WHERE IdTarima = @IdTarima", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@IdInventario", idInventario);
                cmd.Parameters.AddWithValue("@IdTarima", _idTarimaActual);
                cmd.ExecuteNonQuery();
            }

            RegistrarPrimerInventarioHoja(connection, transaction, idInventario);
            return idInventario;
        }

        // La hoja guarda solo su primer inventario como referencia (lo muestra el reporte Mis Pedidos);
        // el inventario de cada tarima está en TarimasInventario.IdInventario.
        private void RegistrarPrimerInventarioHoja(MySqlConnection connection, MySqlTransaction transaction, int idInventario)
        {
            using (var cmd = new MySqlCommand(@"
                UPDATE PreparacionPedidos
                SET IdInventario = @IdInventario
                WHERE IdPedido = @IdPedido AND NoHoja = @NoHoja
                  AND (IdInventario IS NULL OR IdInventario <= 0)", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@IdInventario", idInventario);
                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                cmd.Parameters.AddWithValue("@NoHoja", noHoja);
                cmd.ExecuteNonQuery();
            }
        }

        // Encabezado de inventario con los datos del pedido.
        // El Id se guarda en idInventarioActual solo después del Commit del llamador.
        private int CrearInventario(MySqlConnection connection, MySqlTransaction transaction,
            string fechaActual, string fechaHoraActual)
        {
            using (var cmd = new MySqlCommand(@"
                INSERT INTO inventarios (
                    IdUsuarios, Fecha, FechaHoraI, Operacion, Estado,
                    IdSucursales, Sucursal, Usuario, IdRazon, NombreRazon, IdDepartamentos
                ) VALUES (
                    @IdUsuarios, @Fecha, @FechaHoraI, 1, 0,
                    @IdSucursales, @Sucursal, @Usuario, @IdRazon, @NombreRazon, @IdDepartamento
                ); SELECT LAST_INSERT_ID();", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@IdUsuarios", App.IdUsuarioActual);
                cmd.Parameters.AddWithValue("@Fecha", fechaActual);
                cmd.Parameters.AddWithValue("@FechaHoraI", fechaHoraActual);
                cmd.Parameters.AddWithValue("@IdSucursales", idSucursalPedido);
                cmd.Parameters.AddWithValue("@Sucursal", nombreSucursalPedido);
                cmd.Parameters.AddWithValue("@Usuario", App.NombreUsuarioActual);
                cmd.Parameters.AddWithValue("@IdRazon", idRazonSocialPedido);
                cmd.Parameters.AddWithValue("@NombreRazon", nombreRazonSocialPedido);
                cmd.Parameters.AddWithValue("@IdDepartamento", idDepartamentoPedido);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private void RegistrarDetalleInventario(MySqlConnection connection, MySqlTransaction transaction,
            int idInventario, string upcIngresado, ProductoInventarioInfo infoProducto, int cantidadTotal, int idRazonSocial)
        {
            string sqlUpsert = infoProducto.EsProductoPaquete
                ? @"INSERT INTO detalleinventarios (
                    IdInventarios, DUN14, Upc, Cantidad, UnidadesFardo,
                    Descripcion, IdProveedor, IdDepartamento, Costo, IdRazonSocial
                ) VALUES (
                    @IdInventarios, @DUN14, @Upc, @Cantidad, @UnidadesFardo,
                    @Descripcion, @IdProveedor, @IdDepartamento, @Costo, @IdRazonSocial
                )
                ON DUPLICATE KEY UPDATE Cantidad = Cantidad + @Cantidad, IdRazonSocial = @IdRazonSocial"
                : @"INSERT INTO detalleinventarios (
                    IdInventarios, DUN14, Upc, Cantidad, UnidadesFardo,
                    Descripcion, IdProveedor, IdDepartamento, Costo, IdRazonSocial
                ) VALUES (
                    @IdInventarios, '', @Upc, @Cantidad, 0,
                    @Descripcion, @IdProveedor, @IdDepartamento, @Costo, @IdRazonSocial
                )
                ON DUPLICATE KEY UPDATE Cantidad = Cantidad + @Cantidad, IdRazonSocial = @IdRazonSocial";

            using (var cmd = new MySqlCommand(sqlUpsert, connection, transaction))
            {
                cmd.Parameters.AddWithValue("@IdInventarios", idInventario);
                cmd.Parameters.AddWithValue("@Upc", infoProducto.Upc);
                cmd.Parameters.AddWithValue("@Cantidad", cantidadTotal);
                cmd.Parameters.AddWithValue("@Descripcion", infoProducto.DescLarga);
                cmd.Parameters.AddWithValue("@IdProveedor", infoProducto.IdProveedores);
                cmd.Parameters.AddWithValue("@IdDepartamento", infoProducto.IdDepartamentos);
                cmd.Parameters.AddWithValue("@Costo", infoProducto.Costo);
                cmd.Parameters.AddWithValue("@IdRazonSocial", idRazonSocial);
                if (infoProducto.EsProductoPaquete)
                {
                    cmd.Parameters.AddWithValue("@DUN14", upcIngresado);
                    cmd.Parameters.AddWithValue("@UnidadesFardo", infoProducto.UnidadesFardo);
                }
                cmd.ExecuteNonQuery();
            }
        }

        // Crea la fila del faltante en una hoja de PUNTO y la registra en ProductosApuntos.
        // Devuelve el número de hoja donde quedó el faltante.
        private async Task<int> EnviarFaltanteAPunto(MySqlConnection connection, MySqlTransaction transaction,
            string upcIngresado, string descripcion, int cantidadFaltante)
        {
            int nuevaHoja;
            using (var cmd = new MySqlCommand(@"
                SELECT COALESCE(
                    (SELECT NoHoja FROM detallepedidostienda_bodega
                     WHERE IdConsolidado = @IdPedido AND EstadoPreparacionproducto = 4
                       AND NoHoja <> @HojaOriginal
                     GROUP BY NoHoja HAVING COUNT(*) < 25
                     ORDER BY NoHoja DESC LIMIT 1),
                    (SELECT MAX(NoHoja) + 1 FROM detallepedidostienda_bodega WHERE IdConsolidado = @IdPedido)
                ) as HojaAsignar", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                cmd.Parameters.AddWithValue("@HojaOriginal", noHoja);
                nuevaHoja = Convert.ToInt32(cmd.ExecuteScalar());
            }

            // Se copian UPC y, si aplica, la columna de UPC de esta BD (UPCProducto)
            string columnasUpc = campoUpc == "UPC" ? "UPC" : $"UPC, {campoUpc}";

            using (var cmd = new MySqlCommand($@"
                INSERT INTO detallepedidostienda_bodega (
                    IdPedidos, IdConsolidado, {columnasUpc}, Descripcion, Cantidad, CantConfirmada,
                    NoHoja, IdUbicacionBodega, EstadoPreparacionproducto,
                    FechaPreparo, Fechahorapreparo, IdUsuariopreparo, Observaciones
                )
                SELECT IdPedidos, @IdPedido, {columnasUpc}, Descripcion, @CantidadFaltante, 0,
                    @NuevaHoja, IdUbicacionBodega, 4,
                    CURDATE(), NOW(), @IdUsuario,
                    CONCAT('Faltante de Hoja #', @HojaOriginal, ' - ', IFNULL(Observaciones, ''))
                FROM detallepedidostienda_bodega
                WHERE IdConsolidado = @IdPedido AND {campoUpc} = @UPC AND NoHoja = @HojaOriginal
                LIMIT 1", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                cmd.Parameters.AddWithValue("@UPC", upcIngresado);
                cmd.Parameters.AddWithValue("@CantidadFaltante", cantidadFaltante);
                cmd.Parameters.AddWithValue("@NuevaHoja", nuevaHoja);
                cmd.Parameters.AddWithValue("@HojaOriginal", noHoja);
                cmd.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new MySqlCommand(@"
                INSERT INTO ProductosApuntos (
                    IdPedido, Sucursal, Upc, Descripcion,
                    Cantidad, IdUsuario, NombreUsuario, FechaPedido
                ) VALUES (
                    @IdPedido, @Sucursal, @Upc, @Descripcion,
                    @Cantidad, @IdUsuario, @NombreUsuario, CURDATE()
                )", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                cmd.Parameters.AddWithValue("@Sucursal", nombreSucursalPedido);
                cmd.Parameters.AddWithValue("@Upc", upcIngresado);
                cmd.Parameters.AddWithValue("@Descripcion", descripcion);
                cmd.Parameters.AddWithValue("@Cantidad", cantidadFaltante);
                cmd.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                cmd.Parameters.AddWithValue("@NombreUsuario", App.NombreUsuarioActual ?? "Usuario");
                cmd.ExecuteNonQuery();
            }

            await ActualizarTotalesHoja(connection, transaction, noHoja);
            await InsertarOActualizarPreparacionPedidos(connection, transaction, nuevaHoja);
            return nuevaHoja;
        }

        // ─────────────────────────────────────────
        // CONFIRMAR EN VARIAS TARIMAS
        // El producto queda con NoTarima = 0 y el reparto en la etiqueta [TARIMAS:...]
        // de Observaciones. Cada tarima completa se crea cerrada con su correlativo propio.
        // Si ultimaEnTarimaAbierta, la última parte va a la tarima activa (mixta), que
        // sigue abierta para los demás productos; si no hay una, se abre.
        // ─────────────────────────────────────────

        private async Task<List<ParteTarima>> ConfirmarProductoEnVariasTarimas(
            ProductoDetalle producto, List<int> cantidades, bool ultimaEnTarimaAbierta)
        {
            string upc = producto.UPC;
            if (!productosInventarioCache.TryGetValue(upc, out var infoProducto))
                throw new Exception("No se encontró información del producto en caché");

            var cantidadesNuevas = cantidades;
            if (ultimaEnTarimaAbierta)
            {
                await AsegurarTarimaActiva();
                cantidadesNuevas = cantidades.Take(cantidades.Count - 1).ToList();
            }

            // Las tarimas completas se crean entregadas: cada una necesita su tarjeta NFC
            var nfc = DependencyService.Get<INfcService>();
            if (cantidadesNuevas.Count > 0 && (nfc == null || !nfc.TieneNfc))
                throw new NfcException("Este teléfono no tiene NFC; no se pueden crear tarimas completas sin configurar su tarjeta.");
            _tarimasRepartoEntregadas.Clear();

            int totalFardos = cantidades.Sum();
            int unidadesTotales = totalFardos * producto.UnidadesxFardo;
            DateTime ahora = DateTime.Now;
            string fechaActual = ahora.ToString("yyyy-MM-dd");
            string fechaHoraActual = ahora.ToString("yyyy-MM-dd HH:mm:ss");
            var partes = new List<ParteTarima>();
            // Inventario donde quedó cada parte (el de su tarima)
            var inventarioPorParte = new List<(ParteTarima Parte, int IdInventario)>();
            int? idInventarioAbierta = null;

            // Registra la parte en el inventario indicado (unidades si es paquete, fardos si no)
            void RegistrarParte(MySqlConnection connection, MySqlTransaction transaction, int idInventario, ParteTarima parte)
            {
                int cantidadParte = infoProducto.EsProductoPaquete
                    ? parte.Cantidad * infoProducto.UnidadesFardo
                    : parte.Cantidad;
                int idRazonSocial = DeterminarRazonSocial(idRazonSocialPedido, cantidadParte,
                    infoProducto.Existencia1, infoProducto.Existencia2,
                    infoProducto.Existencia3, infoProducto.Existencia4);
                RegistrarDetalleInventario(connection, transaction, idInventario,
                    upc, infoProducto, cantidadParte, idRazonSocial);
                inventarioPorParte.Add((parte, idInventario));
            }

            await Task.Run(() =>
            {
                using (var connection = new MySqlConnection(BuildConnectionString()))
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // La tarima abierta se lee primero: fija su NoTarima vigente
                            if (ultimaEnTarimaAbierta)
                                idInventarioAbierta = ObtenerOCrearInventario(connection, transaction, fechaActual, fechaHoraActual);

                            // Correlativo real al momento de confirmar (bloqueado hasta el commit)
                            int siguienteTarima;
                            using (var cmd = new MySqlCommand(@"
                                SELECT COALESCE(MAX(NoTarima), 0) + 1
                                FROM TarimasInventario WHERE IdPedido = @IdPedido
                                FOR UPDATE", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                siguienteTarima = Convert.ToInt32(cmd.ExecuteScalar());
                            }

                            // Cada tarima completa del reparto lleva su propio inventario
                            foreach (int cantidad in cantidadesNuevas)
                            {
                                var parte = new ParteTarima { NoTarima = siguienteTarima++, Cantidad = cantidad };
                                partes.Add(parte);

                                int idInventario = CrearInventario(connection, transaction, fechaActual, fechaHoraActual);
                                RegistrarPrimerInventarioHoja(connection, transaction, idInventario);
                                RegistrarParte(connection, transaction, idInventario, parte);

                                using (var cmd = new MySqlCommand(@"
                                    INSERT INTO TarimasInventario (
                                        IdPedido, NoTarima, IdUsuario, NombreUsuario, FechaCreacion,
                                        IdInventario, FechaFinalizacion, CantidadFardos, CantidadSkus
                                    ) VALUES (
                                        @IdPedido, @NoTarima, @IdUsuario, @NombreUsuario, NOW(),
                                        @IdInventario, NOW(), @Fardos, 1
                                    );
                                    SELECT LAST_INSERT_ID();", connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                    cmd.Parameters.AddWithValue("@NoTarima", parte.NoTarima);
                                    cmd.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                                    cmd.Parameters.AddWithValue("@NombreUsuario", App.NombreUsuarioActual ?? "Usuario");
                                    cmd.Parameters.AddWithValue("@IdInventario", idInventario);
                                    cmd.Parameters.AddWithValue("@Fardos", parte.Cantidad);
                                    _tarimasRepartoEntregadas.Add((Convert.ToInt32(cmd.ExecuteScalar()), parte.NoTarima));
                                }
                            }

                            if (ultimaEnTarimaAbierta)
                            {
                                var parteAbierta = new ParteTarima { NoTarima = _tarimaActualNumero, Cantidad = cantidades.Last() };
                                partes.Add(parteAbierta);
                                RegistrarParte(connection, transaction, idInventarioAbierta.Value, parteAbierta);
                            }

                            using (var cmd = new MySqlCommand($@"
                                UPDATE detallepedidostienda_bodega
                                SET CantConfirmada = @CantConfirmada,
                                    NoTarima = 0,
                                    Observaciones = @Observaciones,
                                    FechaPreparo = CURDATE(),
                                    Fechahorapreparo = NOW(),
                                    EstadoPreparacionproducto = '1',
                                    IdUsuariopreparo = @IdUsuario,
                                    ExistenciaFardos = GREATEST(0, ExistenciaFardos - @CantidadFardos),
                                    Existencia = GREATEST(0, Existencia - @CantidadUnidades)
                                WHERE IdConsolidado = @IdPedido AND {campoUpc} = @UPC AND NoHoja = @NoHoja", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@CantConfirmada", totalFardos);
                                cmd.Parameters.AddWithValue("@Observaciones",
                                    RepartoTarimas.ArmarObservaciones(partes, producto.Observaciones));
                                cmd.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                                cmd.Parameters.AddWithValue("@CantidadFardos", totalFardos);
                                cmd.Parameters.AddWithValue("@CantidadUnidades", unidadesTotales);
                                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                cmd.Parameters.AddWithValue("@UPC", upc);
                                cmd.Parameters.AddWithValue("@NoHoja", noHoja);
                                cmd.ExecuteNonQuery();
                            }

                            // Un registro por tarima, cada uno con su inventario
                            foreach (var (parte, idInventarioParte) in inventarioPorParte)
                            {
                                using (var cmd = new MySqlCommand(@"
                                    INSERT INTO PedidoConfirmacionManual (
                                        IdPedido, NoHoja, IdInventario, Upc, Descripcion,
                                        Cantidad, IdUsuario, NombreUsuario, FechaHoraRegistro
                                    ) VALUES (
                                        @IdPedido, @NoHoja, @IdInventario, @Upc, @Descripcion,
                                        @Cantidad, @IdUsuario, @NombreUsuario, NOW()
                                    )", connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                    cmd.Parameters.AddWithValue("@NoHoja", noHoja);
                                    cmd.Parameters.AddWithValue("@IdInventario", idInventarioParte);
                                    cmd.Parameters.AddWithValue("@Upc", upc);
                                    cmd.Parameters.AddWithValue("@Descripcion", infoProducto.DescLarga);
                                    cmd.Parameters.AddWithValue("@Cantidad", parte.Cantidad);
                                    cmd.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                                    cmd.Parameters.AddWithValue("@NombreUsuario", App.NombreUsuarioActual ?? "Usuario");
                                    cmd.ExecuteNonQuery();
                                }
                            }

                            transaction.Commit();
                            // Solo cambia si la última parte quedó en la tarima abierta
                            if (idInventarioAbierta.HasValue)
                                idInventarioActual = idInventarioAbierta;
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            partes.Clear();
                            _tarimasRepartoEntregadas.Clear();
                            throw new Exception($"Error al repartir en tarimas: {ex.Message}");
                        }
                    }
                }
            }); // fin Task.Run

            MostrarInventarioActual();

            if (_productosConfirmados.TryGetValue(upc, out var prev))
                _productosConfirmados[upc] = (prev.Cantidad + totalFardos, infoProducto.DescLarga);
            else
                _productosConfirmados[upc] = (totalFardos, infoProducto.DescLarga);

            return partes;
        }

        // ─────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────

        private async Task MostrarAlertaYaPreparado(int cantidad, string descripcion, int estado = 1)
        {
            string titulo, mensaje;
            if (estado == 2)
            {
                titulo = "Sin existencia física";
                mensaje = $"⚠️ Este producto fue confirmado como sin existencia física.\n\n{descripcion}";
            }
            else
            {
                titulo = "Producto ya preparado";
                mensaje = $"✓ Este producto ya fue preparado.\n\nPreparado: {cantidad} unidad(es)\n{descripcion}";
            }
            await DisplayAlert(titulo, mensaje, "OK");
            UpcEntry.Text = string.Empty;
            Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
        }

        // ─────────────────────────────────────────
        // CRONÓMETRO
        // ─────────────────────────────────────────

        private void IniciarCronometro()
        {
            double segundosGuardados = Preferences.Get(CronometroKey, 0.0);

            // Sumar tiempo transcurrido mientras la app estuvo fuera
            long salidaTicks = Preferences.Get(CronometroSalidaKey, 0L);
            if (salidaTicks > 0)
            {
                segundosGuardados += (DateTime.Now - new DateTime(salidaTicks)).TotalSeconds;
                Preferences.Remove(CronometroSalidaKey);
            }

            _tiempoTranscurrido = TimeSpan.FromSeconds(segundosGuardados);
            Device.BeginInvokeOnMainThread(ActualizarDisplayCronometro);

            _cronometro = new System.Timers.Timer(1000);
            _cronometro.AutoReset = true;
            _cronometro.Elapsed += (s, e) =>
            {
                _tiempoTranscurrido = _tiempoTranscurrido.Add(TimeSpan.FromSeconds(1));
                Device.BeginInvokeOnMainThread(ActualizarDisplayCronometro);
            };
            _cronometro.Start();
        }

        private void PausarCronometro()
        {
            _cronometro?.Stop();
            _cronometro?.Dispose();
            _cronometro = null;
            Preferences.Set(CronometroKey, _tiempoTranscurrido.TotalSeconds);
            if (!_hojaFinalizada)
                Preferences.Set(CronometroSalidaKey, DateTime.Now.Ticks);
        }

        private void ActualizarDisplayCronometro()
        {
            var t = _tiempoTranscurrido;
            CronometroLabel.Text = $"⏱  {(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";

            if (t.TotalMinutes >= 45)
            {
                _parpadeoEstado = !_parpadeoEstado;
                CronometroLabel.TextColor = _parpadeoEstado
                    ? Color.FromHex("#E53E3E")
                    : Color.FromHex("#FCA5A5");
            }
            else
            {
                CronometroLabel.TextColor = Color.FromHex("#4c51bf");
            }
        }

        private void MostrarToast(string mensaje, bool esError = false)
        {
            if (_toastActivo) return;
            _toastActivo = true;
            Device.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    ToastFrame.BackgroundColor = esError ? Color.FromHex("#E53E3E") : Color.FromHex("#38A169");
                    ToastIconFrame.BackgroundColor = esError ? Color.FromHex("#C53030") : Color.FromHex("#2F855A");
                    ToastIconLabel.Text = esError ? "✕" : "✓";
                    ToastMessageLabel.Text = mensaje;
                    ToastFrame.TranslationY = -90;
                    ToastFrame.Opacity = 0;
                    ToastFrame.IsVisible = true;

                    await Task.WhenAll(
                        ToastFrame.TranslateTo(0, 0, 280, Easing.CubicOut),
                        ToastFrame.FadeTo(1, 280)
                    );

                    await Task.Delay(2500);

                    await Task.WhenAll(
                        ToastFrame.FadeTo(0, 250),
                        ToastFrame.TranslateTo(0, -90, 250, Easing.CubicIn)
                    );

                    ToastFrame.IsVisible = false;
                }
                finally
                {
                    _toastActivo = false;
                }
            });
        }

        private async Task<(bool Encontrado, int Cantidad, string Descripcion, int Estado)> BuscarProductoConfirmadoEnBD(string upc)
        {
            try
            {
                using (var connection = new MySqlConnection(BuildConnectionString()))
                {
                    connection.Open();
                    using (var cmd = new MySqlCommand($@"
                        SELECT CantConfirmada, Descripcion, EstadoPreparacionproducto
                        FROM detallepedidostienda_bodega
                        WHERE IdConsolidado = @IdPedido
                          AND NoHoja = @NoHoja
                          AND {campoUpc} = @UPC
                          AND EstadoPreparacionproducto NOT IN (0,4,3)
                        LIMIT 1", connection))
                    {
                        cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                        cmd.Parameters.AddWithValue("@NoHoja", noHoja);
                        cmd.Parameters.AddWithValue("@UPC", upc);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                int cant = reader.IsDBNull(reader.GetOrdinal("CantConfirmada"))
                                    ? 0 : reader.GetInt32("CantConfirmada");
                                string desc = reader.GetString("Descripcion");
                                int estadoProd = reader.GetInt32("EstadoPreparacionproducto");
                                return (true, cant, desc, estadoProd);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BuscarProductoConfirmadoEnBD error: {ex.Message}");
            }
            return (false, 0, string.Empty, 0);
        }

        private string BuildConnectionString() => _connectionString;

        private int DeterminarRazonSocial(int idRazonPedido, int cantidadTotal,
            decimal ex1, decimal ex2, decimal ex3, decimal ex4)
        {
            switch (idRazonPedido)
            {
                case 1: if (ex1 >= cantidadTotal) return 1; break;
                case 2: if (ex2 >= cantidadTotal) return 2; break;
                case 3: if (ex3 >= cantidadTotal) return 3; break;
                case 4: if (ex4 >= cantidadTotal) return 4; break;
            }
            if (ex1 >= cantidadTotal) return 1;
            if (ex2 >= cantidadTotal) return 2;
            if (ex3 >= cantidadTotal) return 3;
            if (ex4 >= cantidadTotal) return 4;
            return 0;
        }

        private async Task<ProductoDetalle> BuscarProductoEnPaquetes(string upcIngresado)
        {
            try
            {
                using (var connection = new MySqlConnection(BuildConnectionString()))
                {
                    connection.Open();
                    using (var command = new MySqlCommand(
                        "SELECT UPCPaquete FROM productospaquetes WHERE Upc = @UpcIngresado LIMIT 1", connection))
                    {
                        command.Parameters.AddWithValue("@UpcIngresado", upcIngresado);
                        var result = command.ExecuteScalar();
                        if (result != null)
                            return todosLosProductos?.FirstOrDefault(p => p.UPC == result.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en BuscarProductoEnPaquetes: {ex.Message}");
            }
            return null;
        }

        private async Task ActualizarProductoLocal(string upc)
        {
            var producto = todosLosProductos?.FirstOrDefault(p => p.UPC == upc);
            if (producto == null) return;

            producto.EsProductoEnFoco = false;
            todosLosProductos.Remove(producto);

            Device.BeginInvokeOnMainThread(() =>
            {
                var grupo = productosAgrupados?.FirstOrDefault(g => g.Contains(producto));
                if (grupo != null)
                {
                    grupo.Remove(producto);
                    if (grupo.Count == 0)
                        productosAgrupados.Remove(grupo);
                }
                upcFiltrado = null;
                TotalSkusPendientesLabel.Text = todosLosProductos?.Count.ToString() ?? "0";
            });
        }

        private Task ActualizarTotalesHoja(MySqlConnection connection, MySqlTransaction transaction, int hojaOriginal)
        {
            using (var cmd = new MySqlCommand(@"
                UPDATE PreparacionPedidos
                SET TotalSKUs   = (SELECT COUNT(DISTINCT UPC) FROM detallepedidostienda_bodega WHERE IdConsolidado = @IdPedido AND NoHoja = @HojaOriginal),
                    TotalFardos = (SELECT SUM(Cantidad)       FROM detallepedidostienda_bodega WHERE IdConsolidado = @IdPedido AND NoHoja = @HojaOriginal)
                WHERE IdPedido = @IdPedido AND NoHoja = @HojaOriginal", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                cmd.Parameters.AddWithValue("@HojaOriginal", hojaOriginal);
                cmd.ExecuteNonQuery();
            }
            return Task.CompletedTask;
        }

        private Task InsertarPreparacionPedidos(MySqlConnection connection, MySqlTransaction transaction, int nuevaHoja)
        {
            using (var cmd = new MySqlCommand(@"
                INSERT INTO PreparacionPedidos (IdPedido, NoHoja, Sucursal, TotalSKUs, TotalFardos)
                SELECT @IdPedido, @NoHoja, p.NombreEmpresa, COUNT(DISTINCT d.UPC), SUM(d.Cantidad)
                FROM detallepedidostienda_bodega d
                INNER JOIN pedidostienda_bodega p ON p.IdPedidos = d.IdConsolidado
                WHERE d.IdConsolidado = @IdPedido AND d.NoHoja = @NoHoja
                GROUP BY p.NombreEmpresa", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                cmd.Parameters.AddWithValue("@NoHoja", nuevaHoja);
                cmd.ExecuteNonQuery();
            }
            return Task.CompletedTask;
        }

        private Task ActualizarNoHojas(MySqlConnection connection, MySqlTransaction transaction)
        {
            using (var cmd = new MySqlCommand(@"
                UPDATE pedidostienda_bodega
                SET NoHojas = (SELECT COUNT(DISTINCT NoHoja) FROM detallepedidostienda_bodega WHERE IdConsolidado = @IdPedido)
                WHERE IdPedidos = @IdPedido", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                cmd.ExecuteNonQuery();
            }
            return Task.CompletedTask;
        }

        private async Task InsertarOActualizarPreparacionPedidos(MySqlConnection connection, MySqlTransaction transaction, int nuevaHoja)
        {
            bool existe;
            using (var cmd = new MySqlCommand(@"
                SELECT COUNT(*) FROM PreparacionPedidos WHERE IdPedido = @IdPedido AND NoHoja = @NoHoja", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                cmd.Parameters.AddWithValue("@NoHoja", nuevaHoja);
                existe = Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }

            if (!existe)
            {
                await InsertarPreparacionPedidos(connection, transaction, nuevaHoja);
                await ActualizarNoHojas(connection, transaction);
            }
            else
            {
                using (var cmd = new MySqlCommand(@"
                    UPDATE PreparacionPedidos 
                    SET TotalSKUs   = (SELECT COUNT(DISTINCT UPC) FROM detallepedidostienda_bodega WHERE IdConsolidado = @IdPedido AND NoHoja = @NoHoja),
                        TotalFardos = (SELECT SUM(Cantidad)       FROM detallepedidostienda_bodega WHERE IdConsolidado = @IdPedido AND NoHoja = @NoHoja)
                    WHERE IdPedido = @IdPedido AND NoHoja = @NoHoja", connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                    cmd.Parameters.AddWithValue("@NoHoja", nuevaHoja);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void AplicarFiltro() { }

        private void ActualizarPanelProductoActual(ProductoDetalle producto)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                if (producto == null)
                {
                    PanelProductoActual.IsVisible = false;
                    return;
                }
                PanelProductoActualUpc.Text = $"UPC: {producto.UPC}";
                PanelProductoActualDescripcion.Text = producto.Descripcion;
                PanelProductoActualProgreso.Text = $"{producto.CantidadEscaneada}/{producto.Cantidad}";
                PanelProductoActualProgreso.TextColor = Color.FromHex(producto.ColorProgreso);
                PanelProductoActualBarra.Progress = producto.PorcentajeProgreso;
                PanelProductoActualBarra.ProgressColor = Color.FromHex(producto.ColorProgreso);
                PanelProductoActual.IsVisible = true;
            });
        }


        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (!_initialized)
            {
                _initialized = true;
                _ = InicializarPaginaAsync();
            }
            else if (!_hojaFinalizada)
            {
                IniciarCronometro();
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await Task.Delay(150);
                    UpcEntry.Focus();
                });
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            // Con la hoja finalizada el cronómetro ya se detuvo y borró (p. ej. al abrir la tarjeta NFC)
            if (!_hojaFinalizada)
                PausarCronometro();
        }

        private async Task InicializarPaginaAsync()
        {
            await CargarInformacionPedido();
            if (!await VerificarYCargarInventario()) return;
            await CargarProductos();
            IniciarCronometro();
        }

        private async void OnProductoTapped(object sender, EventArgs e)
        {
            var frame = sender as Frame;
            if (frame?.BindingContext is ProductoDetalle producto)
                await ManejarSeleccionProducto(producto);
        }

        // ─────────────────────────────────────────
        // SISTEMA DE TARIMAS
        // ─────────────────────────────────────────

        private async Task InicializarTarimaActiva()
        {
            try
            {
                int idTarima = 0, tarimaNum = 0;
                int? idInventario = null;

                await Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();
                        using (var transaction = connection.BeginTransaction())
                        {
                            // Tarima abierta del usuario en esta hoja (p. ej. la de un reparto)
                            (idTarima, tarimaNum) = LeerTarimaAbiertaDeHoja(connection, transaction);
                            if (idTarima > 0)
                                idInventario = RepartoTarimas.LeerInventarioTarima(connection, transaction, idTarima);

                            if (idTarima == 0)
                            {
                                // Correlativo bloqueado hasta el commit: dos usuarios no toman el mismo número
                                using (var cmd = new MySqlCommand(@"
                                    SELECT COALESCE(MAX(NoTarima), 0) + 1
                                    FROM TarimasInventario WHERE IdPedido = @IdPedido
                                    FOR UPDATE", connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                    tarimaNum = Convert.ToInt32(cmd.ExecuteScalar());
                                }

                                using (var cmd = new MySqlCommand(@"
                                    INSERT INTO TarimasInventario (IdPedido, NoTarima, IdUsuario, NombreUsuario, FechaCreacion)
                                    VALUES (@IdPedido, @NoTarima, @IdUsuario, @NombreUsuario, NOW());
                                    SELECT LAST_INSERT_ID();", connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                    cmd.Parameters.AddWithValue("@NoTarima", tarimaNum);
                                    cmd.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                                    cmd.Parameters.AddWithValue("@NombreUsuario", App.NombreUsuarioActual ?? "Usuario");
                                    idTarima = Convert.ToInt32(cmd.ExecuteScalar());
                                }
                            }

                            transaction.Commit();
                        }
                    }
                });

                _idTarimaActual = idTarima;
                _tarimaActualNumero = tarimaNum;
                idInventarioActual = idInventario;

                Device.BeginInvokeOnMainThread(ActualizarBadgeTarima);
                MostrarInventarioActual();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error inicializando tarima: {ex.Message}");
                Device.BeginInvokeOnMainThread(() => MostrarToast($"Error tarima: {ex.Message}", esError: true));
            }
        }

        // Un producto normal nunca debe quedar con NoTarima = 0: ese valor está
        // reservado para los productos repartidos en varias tarimas.
        // La tarima se abre con el primer producto que se confirma en ella.
        private async Task AsegurarTarimaActiva()
        {
            if (_idTarimaActual == 0)
                await InicializarTarimaActiva();

            if (_idTarimaActual == 0)
                throw new Exception("No se pudo abrir la tarima activa. Revise la conexión e intente de nuevo.");
        }

        // CantTarimas es solo la referencia de cuántas tarimas lleva el pedido, no un límite.
        private void ActualizarBadgeTarima()
        {
            string referencia = _cantTarimas > 0 ? $"  ·  Pedido: {_cantTarimas} tarimas" : "";
            TarimaLabel.Text = _idTarimaActual > 0
                ? $"Tarima {_tarimaActualNumero}{referencia}"
                : $"Tarima nueva (se abre con el próximo producto){referencia}";
            EntregarTarimaButton.IsVisible = _idTarimaActual > 0;
            TarimaPanelFrame.IsVisible = true;
        }

        private async void OnEntregarTarimaClicked(object sender, EventArgs e)
        {
            if (_idTarimaActual == 0 || isConfirming) return;

            bool confirm = await DisplayAlert("Entregar Tarima",
                $"¿Confirmar entrega de la Tarima {_tarimaActualNumero}?", "Sí, entregar", "Cancelar");
            if (!confirm) return;

            try
            {
                int tarimaEntregada = _tarimaActualNumero;
                var resultado = await EntregarTarimaActualAsync(permiteCancelar: true);

                if (resultado == null)
                    MostrarToast($"Entrega cancelada. Sigue con la Tarima {tarimaEntregada}.");
                else
                    MostrarToast(resultado == RepartoTarimas.ResultadoEntrega.Eliminada
                        ? $"La Tarima {tarimaEntregada} estaba vacía y se eliminó."
                        : $"Tarima {tarimaEntregada} entregada. La siguiente se abre con el próximo producto.");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al entregar tarima: " + ex.Message, "OK");
            }
            finally
            {
                if (!_hojaFinalizada)
                    Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
            }
        }

        // Entrega la tarima activa. Si tiene productos, primero se graba su tarjeta NFC
        // (la página de la tarjeta la entrega en la BD al terminar); si está vacía, se elimina.
        // La siguiente tarima se abre, con su propio inventario, al confirmar el
        // próximo producto. Devuelve null si el usuario canceló la entrega.
        private async Task<RepartoTarimas.ResultadoEntrega?> EntregarTarimaActualAsync(bool permiteCancelar)
        {
            var nfc = DependencyService.Get<INfcService>();
            if (nfc == null || !nfc.TieneNfc)
                throw new NfcException("Este teléfono no tiene NFC; no se puede entregar la tarima sin configurar su tarjeta.");

            int idTarima = _idTarimaActual;
            int noTarima = _tarimaActualNumero, productos = 0;

            await Task.Run(() =>
            {
                using (var connection = new MySqlConnection(BuildConnectionString()))
                {
                    connection.Open();
                    using (var cmd = new MySqlCommand(
                        "SELECT NoTarima FROM TarimasInventario WHERE IdTarima = @IdTarima", connection))
                    {
                        cmd.Parameters.AddWithValue("@IdTarima", idTarima);
                        var res = cmd.ExecuteScalar();
                        if (res != null && res != DBNull.Value) noTarima = Convert.ToInt32(res);
                    }
                    productos = RepartoTarimas.ContarProductosEnTarima(connection, null, idPedido, noTarima);
                }
            });

            RepartoTarimas.ResultadoEntrega resultado;
            if (productos == 0)
            {
                resultado = RepartoTarimas.ResultadoEntrega.NoExiste;
                await Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();
                        using (var transaction = connection.BeginTransaction())
                        {
                            resultado = RepartoTarimas.EntregarTarima(connection, transaction, idTarima);
                            transaction.Commit();
                        }
                    }
                });
            }
            else
            {
                bool configurada = await ConfigurarTarjetaNfcPage.ConfigurarAsync(Navigation, new PendienteNfc
                {
                    IdTarima = idTarima,
                    NoTarima = noTarima,
                    IdPedido = idPedido,
                    NoHoja = noHoja,
                    YaEntregada = false,
                    PermiteCancelar = permiteCancelar
                });
                if (!configurada) return null;
                resultado = RepartoTarimas.ResultadoEntrega.Cerrada;
            }

            // El inventario es de la tarima: la siguiente crea el suyo
            _idTarimaActual = 0;
            _tarimaActualNumero = 0;
            idInventarioActual = null;
            Device.BeginInvokeOnMainThread(ActualizarBadgeTarima);
            MostrarInventarioActual();
            return resultado;
        }

        // Hoja finalizada: la tarima se entrega primero en la BD y enseguida se pide su
        // tarjeta (sin poder cancelar). Si la app se cierra antes de grabarla, la tarima
        // queda entregada con IUD vacío y Home vuelve a pedir la tarjeta al entrar.
        private async Task EntregarTarimaHojaFinalizadaAsync()
        {
            int idTarima = _idTarimaActual;
            int noTarima = 0;
            var resultado = RepartoTarimas.ResultadoEntrega.NoExiste;

            await Task.Run(() =>
            {
                using (var connection = new MySqlConnection(BuildConnectionString()))
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        resultado = RepartoTarimas.EntregarTarima(connection, transaction, idTarima);
                        if (resultado == RepartoTarimas.ResultadoEntrega.Cerrada)
                        {
                            using (var cmd = new MySqlCommand(
                                "SELECT NoTarima FROM TarimasInventario WHERE IdTarima = @IdTarima", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@IdTarima", idTarima);
                                noTarima = Convert.ToInt32(cmd.ExecuteScalar());
                            }
                        }
                        transaction.Commit();
                    }
                }
            });

            _idTarimaActual = 0;
            _tarimaActualNumero = 0;
            idInventarioActual = null;
            Device.BeginInvokeOnMainThread(ActualizarBadgeTarima);

            if (resultado == RepartoTarimas.ResultadoEntrega.Cerrada)
                await ConfigurarTarjetasEntregadasAsync(new List<(int IdTarima, int NoTarima)> { (idTarima, noTarima) });
        }

        // Tarimas que ya quedaron entregadas: pide la tarjeta de cada una (sin poder cancelar).
        // Se guardan todas como pendientes antes, por si se cierra la app a medias.
        private async Task ConfigurarTarjetasEntregadasAsync(List<(int IdTarima, int NoTarima)> tarimas)
        {
            if (tarimas.Count == 0) return;

            var nfc = DependencyService.Get<INfcService>();
            if (nfc == null || !nfc.TieneNfc)
            {
                await DisplayAlert("Sin NFC",
                    $"Este teléfono no tiene NFC. Las tarimas {string.Join(", ", tarimas.Select(t => t.NoTarima))} quedaron entregadas sin tarjeta; " +
                    "se pedirá al entrar con un teléfono que tenga NFC.", "OK");
                return;
            }

            var pendientes = tarimas
                .OrderBy(t => t.NoTarima)
                .Select(t => new PendienteNfc
                {
                    IdTarima = t.IdTarima,
                    NoTarima = t.NoTarima,
                    IdPedido = idPedido,
                    NoHoja = noHoja,
                    YaEntregada = true,
                    PermiteCancelar = false
                })
                .ToList();
            foreach (var p in pendientes)
                TarjetasNfcPendientes.Agregar(p);

            for (int i = 0; i < pendientes.Count; i++)
                await ConfigurarTarjetaNfcPage.ConfigurarAsync(Navigation, pendientes[i],
                    pendientes.Count > 1 ? $"Tarjeta {i + 1} de {pendientes.Count}" : null);
        }

        // ─────────────────────────────────────────
        // VERIFICAR FINALIZACIÓN DE HOJA
        // ─────────────────────────────────────────

        private async Task VerificarFinalizacionHoja()
        {
            try
            {
                int pendientesHoja = 0, totalProductos = 0, productosCompletados = 0;

                await Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();

                        using (var cmd = new MySqlCommand(@"
                            SELECT COUNT(*) FROM detallepedidostienda_bodega
                            WHERE IdConsolidado = @IdPedido AND NoHoja = @NoHoja
                            AND EstadoPreparacionproducto IN(0,4,3)", connection))
                        {
                            cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                            cmd.Parameters.AddWithValue("@NoHoja", noHoja);
                            pendientesHoja = Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        if (pendientesHoja > 0) return;

                        using (var cmd = new MySqlCommand(@"
                            UPDATE PreparacionPedidos SET FechaHorafinalizo = NOW()
                            WHERE IdPedido = @IdPedido AND NoHoja = @NoHoja", connection))
                        {
                            cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                            cmd.Parameters.AddWithValue("@NoHoja", noHoja);
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = new MySqlCommand(@"
                            SELECT COUNT(*) as TotalProductos,
                                SUM(CASE WHEN EstadoPreparacionproducto IN (1, 2, 5) THEN 1 ELSE 0 END) as ProductosCompletados
                            FROM detallepedidostienda_bodega WHERE IdConsolidado = @IdPedido", connection))
                        {
                            cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                            using (var reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    totalProductos = reader.GetInt32("TotalProductos");
                                    productosCompletados = reader.GetInt32("ProductosCompletados");
                                }
                            }
                        }
                    }
                });

                if (pendientesHoja > 0) return;

                bool pedidoCompleto = totalProductos > 0 && totalProductos == productosCompletados;

                // Detener cronómetro definitivamente al finalizar hoja
                _hojaFinalizada = true;
                _cronometro?.Stop();
                _cronometro?.Dispose();
                _cronometro = null;
                Preferences.Remove(CronometroKey);
                Preferences.Remove(CronometroSalidaKey);

                if (pedidoCompleto)
                {
                    // Último producto del pedido: se completa y se entregan todas sus
                    // tarimas abiertas, de cualquier usuario (las vacías se eliminan).
                    var entregadas = new List<(int IdTarima, int NoTarima)>();
                    await Task.Run(() =>
                    {
                        using (var connection = new MySqlConnection(BuildConnectionString()))
                        {
                            connection.Open();
                            using (var transaction = connection.BeginTransaction())
                            {
                                using (var cmd = new MySqlCommand(@"
                                    UPDATE pedidostienda_bodega SET Estado = 7 WHERE IdPedidos = @IdPedido", connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                    cmd.ExecuteNonQuery();
                                }

                                var abiertas = new List<int>();
                                using (var cmd = new MySqlCommand(@"
                                    SELECT IdTarima FROM TarimasInventario
                                    WHERE IdPedido = @IdPedido AND FechaFinalizacion IS NULL", connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                    using (var reader = cmd.ExecuteReader())
                                        while (reader.Read())
                                            abiertas.Add(reader.GetInt32(0));
                                }

                                var cerradas = new List<int>();
                                foreach (int idTarima in abiertas)
                                    if (RepartoTarimas.EntregarTarima(connection, transaction, idTarima)
                                        == RepartoTarimas.ResultadoEntrega.Cerrada)
                                        cerradas.Add(idTarima);

                                // NoTarima ya renumerado por si se eliminó alguna vacía
                                foreach (int idTarima in cerradas)
                                {
                                    using (var cmd = new MySqlCommand(
                                        "SELECT NoTarima FROM TarimasInventario WHERE IdTarima = @IdTarima", connection, transaction))
                                    {
                                        cmd.Parameters.AddWithValue("@IdTarima", idTarima);
                                        entregadas.Add((idTarima, Convert.ToInt32(cmd.ExecuteScalar())));
                                    }
                                }

                                transaction.Commit();
                            }
                        }
                    });

                    _idTarimaActual = 0;
                    _tarimaActualNumero = 0;

                    // Una tarjeta NFC por cada tarima entregada
                    await ConfigurarTarjetasEntregadasAsync(entregadas);

                    await DisplayAlert("¡Pedido Completado!",
                        $"✓ Hoja #{noHoja} finalizada\n✓ Pedido #{idPedido} completado\n✓ Tarimas del pedido entregadas", "OK");

                    await Navigation.PopToRootAsync();
                }
                else
                {
                    // Al terminar la hoja se entrega su tarima: la siguiente hoja empieza con una nueva
                    if (_idTarimaActual > 0)
                    {
                        await MostrarHojaCompletadaAsync();

                        while (true)
                        {
                            try
                            {
                                await EntregarTarimaHojaFinalizadaAsync();
                                break;
                            }
                            catch (Exception ex)
                            {
                                bool reintentar = await DisplayAlert("Error",
                                    "No se pudo entregar la tarima: " + ex.Message +
                                    "\n\nSi sale, la tarima queda abierta y se entregará al completarse el pedido.",
                                    "Reintentar", "Salir");
                                if (!reintentar) break;
                            }
                        }
                    }
                    else
                    {
                        await DisplayAlert("Hoja Completada", $"✓ Hoja #{noHoja} finalizada", "OK");
                    }

                    await Navigation.PopAsync();
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al verificar finalización: " + ex.Message, "OK");
            }
        }

        // ─────────────────────────────────────────
        // POPUP HOJA COMPLETADA
        // Avisa que la tarima se entrega y sigue sola a la tarjeta NFC en unos segundos
        // (o de inmediato si el usuario toca el botón).
        // ─────────────────────────────────────────

        private const int EsperaHojaCompletadaMs = 2000;
        private TaskCompletionSource<bool> _respuestaHojaCompletada;

        private async Task MostrarHojaCompletadaAsync()
        {
            _respuestaHojaCompletada = new TaskCompletionSource<bool>();

            HojaCompletadaTituloLabel.Text = $"¡Hoja #{noHoja} finalizada!";
            HojaCompletadaTarimaLabel.Text = $"Tarima #{_tarimaActualNumero}";

            UpcEntry.Unfocus();
            CantidadEntry.Unfocus();

            HojaCompletadaCard.Scale = 0.85;
            HojaCompletadaCard.Opacity = 0;
            HojaCompletadaOverlay.IsVisible = true;
            await Task.WhenAll(
                HojaCompletadaCard.ScaleTo(1, 180, Easing.CubicOut),
                HojaCompletadaCard.FadeTo(1, 180));

            await Task.WhenAny(_respuestaHojaCompletada.Task, Task.Delay(EsperaHojaCompletadaMs));

            HojaCompletadaOverlay.IsVisible = false;
            _respuestaHojaCompletada = null;
        }

        private void OnHojaCompletadaEntregarTapped(object sender, EventArgs e) =>
            _respuestaHojaCompletada?.TrySetResult(true);

        // ─────────────────────────────────────────
        // MANEJO DE PRODUCTO TAPEADO (motivos)
        // ─────────────────────────────────────────

        private async Task ManejarSeleccionProducto(ProductoDetalle producto)
        {
            try
            {
                var motivos = new List<MotivoPreparacion>();
                using (var connection = new MySqlConnection(BuildConnectionString()))
                {
                    connection.Open();
                    using (var cmd = new MySqlCommand(
                        "SELECT IdPreparacion, DescripcionMotivo FROM TiposMotivosPendiente WHERE Mostrar = 1", connection))
                    using (var reader = cmd.ExecuteReader())
                        while (reader.Read())
                            motivos.Add(new MotivoPreparacion
                            {
                                IdPreparacion = reader.GetInt32("IdPreparacion"),
                                DescripcionMotivo = reader.GetString("DescripcionMotivo")
                            });
                }

                string[] opciones = motivos.Select(m => m.DescripcionMotivo).ToArray();
                string resultado = await DisplayActionSheet("Seleccione un motivo", "Cancelar", null, opciones);

                if (resultado == "Cancelar" || resultado == null) return;

                var motivoSeleccionado = motivos.First(m => m.DescripcionMotivo == resultado);

                using (var connection = new MySqlConnection(BuildConnectionString()))
                {
                    connection.Open();

                    if (motivoSeleccionado.IdPreparacion == 3)
                    {
                        using (var transaction = connection.BeginTransaction())
                        {
                            try
                            {
                                int nuevaHoja;
                                using (var cmd = new MySqlCommand(@"
                                    SELECT COALESCE(MAX(NoHoja), 0) + 1
                                    FROM detallepedidostienda_bodega WHERE IdConsolidado = @IdPedido", connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                    nuevaHoja = Convert.ToInt32(cmd.ExecuteScalar());
                                }

                                using (var cmd = new MySqlCommand($@"
                                    UPDATE detallepedidostienda_bodega
                                    SET CantConfirmada = 0, FechaPreparo = CURDATE(), Fechahorapreparo = NOW(),
                                        EstadoPreparacionproducto = @IdPreparacion,
                                        NoHoja = @NoHoja, IdUsuariopreparo = @IdUsuario
                                    WHERE IdConsolidado = @IdPedido AND {campoUpc} = @UPC AND NoHoja = @HojaActual", connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@IdPreparacion", motivoSeleccionado.IdPreparacion);
                                    cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                    cmd.Parameters.AddWithValue("@UPC", producto.UPC);
                                    cmd.Parameters.AddWithValue("@NoHoja", nuevaHoja);
                                    cmd.Parameters.AddWithValue("@HojaActual", noHoja);
                                    cmd.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                                    cmd.ExecuteNonQuery();
                                }

                                await ActualizarTotalesHoja(connection, transaction, noHoja);
                                await InsertarPreparacionPedidos(connection, transaction, nuevaHoja);
                                await ActualizarNoHojas(connection, transaction);

                                transaction.Commit();
                                await DisplayAlert("Información",
                                    $"El producto ha sido movido a la hoja #{nuevaHoja} para preparación por volumen.", "OK");
                            }
                            catch (Exception ex)
                            {
                                transaction.Rollback();
                                throw new Exception($"Error al crear hoja por volumen: {ex.Message}");
                            }
                        }
                    }
                    else if (motivoSeleccionado.IdPreparacion == 4)
                    {
                        // Leer ubicaciones en conexión separada que se cierra ANTES del await
                        var ubicaciones = new List<UbicacionBodega>();
                        using (var connUbic = new MySqlConnection(BuildConnectionString()))
                        {
                            connUbic.Open();
                            using (var cmd = new MySqlCommand("SELECT Id, Rack, Nivel, Descripcion FROM ubicacionesbodega", connUbic))
                            using (var reader = cmd.ExecuteReader())
                                while (reader.Read())
                                    ubicaciones.Add(new UbicacionBodega
                                    {
                                        Id = reader.GetInt32("Id"),
                                        Rack = reader.GetString("Rack"),
                                        Nivel = reader.GetInt32("Nivel"),
                                        Descripcion = reader.GetString("Descripcion")
                                    });
                        } // connUbic se cierra aquí, antes del await

                        string[] opcionesUbicacion = ubicaciones
                            .Select(u => $"Rack: {u.Rack}, Nivel: {u.Nivel} - {u.Descripcion}").ToArray();
                        string ubicacionSeleccionada = await DisplayActionSheet(
                            "Seleccione ubicación", "Cancelar", null, opcionesUbicacion);

                        if (ubicacionSeleccionada == "Cancelar" || ubicacionSeleccionada == null) return;

                        var ubicacion = ubicaciones[Array.IndexOf(opcionesUbicacion, ubicacionSeleccionada)];

                        // Conexión fresca para la transacción, abierta DESPUÉS del await
                        using (var connTx = new MySqlConnection(BuildConnectionString()))
                        {
                            connTx.Open();
                            using (var transaction = connTx.BeginTransaction())
                            {
                                try
                                {
                                    int nuevaHoja;
                                    using (var cmd = new MySqlCommand(@"
                                        SELECT COALESCE(
                                            (SELECT NoHoja FROM detallepedidostienda_bodega
                                             WHERE IdConsolidado = @IdPedido AND EstadoPreparacionproducto = 4
                                             GROUP BY NoHoja HAVING COUNT(*) < 40 ORDER BY NoHoja DESC LIMIT 1),
                                            (SELECT MAX(NoHoja) + 1 FROM detallepedidostienda_bodega WHERE IdConsolidado = @IdPedido)
                                        ) as HojaAsignar", connTx, transaction))
                                    {
                                        cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                        nuevaHoja = Convert.ToInt32(cmd.ExecuteScalar());
                                    }

                                    using (var cmd = new MySqlCommand($@"
                                        UPDATE detallepedidostienda_bodega
                                        SET CantConfirmada = 0, FechaPreparo = CURDATE(), Fechahorapreparo = NOW(),
                                            EstadoPreparacionproducto = @IdPreparacion,
                                            IdUbicacionBodega = @IdUbicacion,
                                            NoHoja = @NoHoja, IdUsuariopreparo = @IdUsuario
                                        WHERE IdConsolidado = @IdPedido AND {campoUpc} = @UPC AND NoHoja = @HojaActual", connTx, transaction))
                                    {
                                        cmd.Parameters.AddWithValue("@IdPreparacion", motivoSeleccionado.IdPreparacion);
                                        cmd.Parameters.AddWithValue("@IdUbicacion", ubicacion.Id);
                                        cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                        cmd.Parameters.AddWithValue("@UPC", producto.UPC);
                                        cmd.Parameters.AddWithValue("@NoHoja", nuevaHoja);
                                        cmd.Parameters.AddWithValue("@HojaActual", noHoja);
                                        cmd.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                                        cmd.ExecuteNonQuery();
                                    }

                                    using (var cmd = new MySqlCommand(@"
                                        INSERT INTO ProductosApuntos (
                                            IdPedido, Sucursal, Upc, Descripcion,
                                            Cantidad, IdUsuario, NombreUsuario, FechaPedido
                                        ) VALUES (@IdPedido, @Sucursal, @Upc, @Descripcion, @Cantidad, @IdUsuario, @NombreUsuario, CURDATE())", connTx, transaction))
                                    {
                                        cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                                        cmd.Parameters.AddWithValue("@Sucursal", nombreSucursalPedido);
                                        cmd.Parameters.AddWithValue("@Upc", producto.UPC);
                                        cmd.Parameters.AddWithValue("@Descripcion", producto.Descripcion);
                                        cmd.Parameters.AddWithValue("@Cantidad", producto.Cantidad);
                                        cmd.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                                        cmd.Parameters.AddWithValue("@NombreUsuario", App.NombreUsuarioActual ?? "Usuario");
                                        cmd.ExecuteNonQuery();
                                    }

                                    await ActualizarTotalesHoja(connTx, transaction, noHoja);
                                    await InsertarOActualizarPreparacionPedidos(connTx, transaction, nuevaHoja);

                                    transaction.Commit();
                                }
                                catch (Exception ex)
                                {
                                    transaction.Rollback();
                                    throw new Exception("Error al actualizar los totales: " + ex.Message);
                                }
                            }
                        }
                    }
                    else if (motivoSeleccionado.IdPreparacion == 2)
                    {
                        using (var cmd = new MySqlCommand($@"
                            UPDATE detallepedidostienda_bodega 
                            SET CantConfirmada = 0, FechaPreparo = CURDATE(), Fechahorapreparo = NOW(),
                                EstadoPreparacionproducto = @IdPreparacion, IdUsuariopreparo = @IdUsuario
                            WHERE IdConsolidado = @IdPedido AND {campoUpc} = @UPC AND NoHoja = @NoHoja", connection))
                        {
                            cmd.Parameters.AddWithValue("@IdPreparacion", motivoSeleccionado.IdPreparacion);
                            cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                            cmd.Parameters.AddWithValue("@UPC", producto.UPC);
                            cmd.Parameters.AddWithValue("@NoHoja", noHoja);
                            cmd.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        using (var cmd = new MySqlCommand($@"
                            UPDATE detallepedidostienda_bodega 
                            SET CantConfirmada = 0, FechaPreparo = CURDATE(), Fechahorapreparo = NOW(),
                                EstadoPreparacionproducto = @IdPreparacion, IdUsuariopreparo = @IdUsuario
                            WHERE IdConsolidado = @IdPedido AND {campoUpc} = @UPC AND NoHoja = @NoHoja", connection))
                        {
                            cmd.Parameters.AddWithValue("@IdPreparacion", motivoSeleccionado.IdPreparacion);
                            cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                            cmd.Parameters.AddWithValue("@UPC", producto.UPC);
                            cmd.Parameters.AddWithValue("@NoHoja", noHoja);
                            cmd.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                await CargarProductos();
                await VerificarFinalizacionHoja();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al procesar el motivo: " + ex.Message, "OK");
            }
        }

        private void OnToggleModoManualClicked(object sender, EventArgs e)
        {
            modoManualActivo = !modoManualActivo;
            MostrarOcultarCampoManual(modoManualActivo);

            ToggleModoManualButton.BackgroundColor = modoManualActivo
                ? Color.FromHex("#FEF3C7")
                : Color.FromHex("#EDF2F7");
        }

        private async void OnConfirmarParcialClicked(object sender, EventArgs e)
        {
            if (isConfirming) return;

            var button = sender as Button;
            var producto = (button?.BindingContext) as ProductoDetalle;
            if (producto == null) return;

            int escaneados = producto.CantidadEscaneada;
            int faltantes = producto.Cantidad - escaneados;

            string accion = await DisplayActionSheet(
                $"Confirmar {escaneados} de {producto.Cantidad} solicitados.\n¿Qué hacemos con los {faltantes} faltante(s)?",
                "Cancelar", null,
                "Confirmar solo lo escaneado",
                "Confirmar y enviar faltante a PUNTO");

            if (accion == "Cancelar" || accion == null) return;

            isConfirming = true;
            Device.BeginInvokeOnMainThread(() =>
            {
                LoadingLabel.Text = "Confirmando...";
                LoadingOverlay.IsVisible = true;
            });
            try
            {
                if (accion == "Confirmar solo lo escaneado")
                    await ConfirmarProductoConInventario(producto.UPC, escaneados, producto, esModoManual: true);
                else
                    await ConfirmarProductoYEnviarFaltanteAPuntoConInventario(
                        producto.UPC, escaneados, faltantes, producto, esModoManual: true);

                productoEnProgreso = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnConfirmarParcialClicked error: {ex.Message}");
                MostrarToast("✕ Error al confirmar. Intenta de nuevo.", esError: true);
            }
            finally
            {
                Device.BeginInvokeOnMainThread(() => LoadingOverlay.IsVisible = false);
                isConfirming = false;
            }
        }

        // ─────────────────────────────────────────
        // POPUP "VARIAS TARIMAS"
        // ─────────────────────────────────────────

        private const int MaxTarimasReparto = 30;

        private async void OnVariasTarimasTapped(object sender, EventArgs e)
        {
            if (isConfirming || RepartoOverlay.IsVisible) return;
            if (!((sender as BindableObject)?.BindingContext is ProductoDetalle producto)) return;

            if (productoEnProgreso != null && productoEnProgreso != producto)
            {
                await DisplayAlert(
                    "Producto incompleto",
                    $"Debés completar primero:\n\"{productoEnProgreso.Descripcion}\"\n({productoEnProgreso.CantidadEscaneada}/{productoEnProgreso.Cantidad} escaneados)",
                    "OK");
                return;
            }

            // Vista previa de numeración: siguiente correlativo y la tarima abierta de esta
            // hoja (si tiene). El correlativo definitivo se toma al guardar.
            int siguiente = 1, abierta = _idTarimaActual > 0 ? _tarimaActualNumero : 0;
            try
            {
                await Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();
                        using (var cmd = new MySqlCommand(@"
                            SELECT COALESCE(MAX(NoTarima), 0) + 1
                            FROM TarimasInventario WHERE IdPedido = @IdPedido", connection))
                        {
                            cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                            siguiente = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "No se pudieron consultar las tarimas del pedido: " + ex.Message, "OK");
                return;
            }

            _productoReparto = producto;
            _repartoPrimerNumero = siguiente;
            _repartoTarimaAbierta = abierta;
            RepartoProductoLabel.Text = producto.Descripcion;
            RepartoSolicitadoLabel.Text = $"Solicitado: {producto.Cantidad} fardos";

            _repartoActualizando = true;
            RepartoFardosEntry.Text = string.Empty;
            RepartoTarimaActualCheck.IsChecked = false;
            _repartoActualizando = false;

            ReconstruirFilasReparto(0, 0);
            RepartoOverlay.IsVisible = true;
            Device.BeginInvokeOnMainThread(() => RepartoFardosEntry.Focus());
        }

        // La cantidad de tarimas sale solo de los fardos por tarima; no se edita.
        private void OnRepartoFardosChanged(object sender, TextChangedEventArgs e)
        {
            if (_repartoActualizando || _productoReparto == null) return;

            if (!int.TryParse(e.NewTextValue, out int fardosPorTarima) || fardosPorTarima <= 0)
            {
                ReconstruirFilasReparto(0, 0);
                return;
            }

            int cantTarimas = Math.Max((int)Math.Ceiling((double)_productoReparto.Cantidad / fardosPorTarima), 1);
            if (cantTarimas > MaxTarimasReparto)
            {
                ReconstruirFilasReparto(0, 0);
                RepartoTotalLabel.Text = $"Serían {cantTarimas} tarimas (máximo {MaxTarimasReparto})";
                return;
            }

            ReconstruirFilasReparto(fardosPorTarima, cantTarimas);
        }

        private void OnRepartoCancelarClicked(object sender, EventArgs e) => CerrarReparto();

        protected override bool OnBackButtonPressed()
        {
            // La tarima se entrega al terminar la hoja: Atrás no cierra ese popup
            if (HojaCompletadaOverlay.IsVisible)
                return true;
            if (RepartoOverlay.IsVisible)
            {
                if (!isConfirming) CerrarReparto();
                return true;
            }
            return base.OnBackButtonPressed();
        }

        private void CerrarReparto()
        {
            RepartoOverlay.IsVisible = false;
            _productoReparto = null;
            LimpiarFilasReparto();
            Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
        }

        private void LimpiarFilasReparto()
        {
            _repartoParpadeando = false;
            _repartoFijas.Clear();
            _repartoEtiquetas.Clear();
            _repartoUltimaEntry = null;
            _repartoUltimaFila = null;
            _repartoAjustarButton = null;
            RepartoTarimasLayout.Children.Clear();
        }

        private int UltimaTarimaReparto =>
            _repartoUltimaEntry != null && int.TryParse(_repartoUltimaEntry.Text, out int v) ? v : 0;

        private List<int> LeerCantidadesReparto()
        {
            var valores = new List<int>(_repartoFijas);
            if (_repartoUltimaEntry != null)
                valores.Add(UltimaTarimaReparto);
            return valores;
        }

        // Lo que debe llevar la última tarima para completar exactamente lo solicitado.
        private int UltimaParaCuadrar => _productoReparto == null ? 0 : _productoReparto.Cantidad - _repartoFijas.Sum();

        private bool UsaTarimaAbierta => RepartoTarimaActualFrame.IsVisible && RepartoTarimaActualCheck.IsChecked;

        // Número de tarima que se mostrará en la fila i (vista previa).
        private int NumeroFilaReparto(int i, int totalFilas)
        {
            bool esUltima = i == totalFilas - 1;
            if (UsaTarimaAbierta)
            {
                // Si no tiene tarima abierta, se abre una con el siguiente correlativo
                int abierta = _repartoTarimaAbierta > 0 ? _repartoTarimaAbierta : _repartoPrimerNumero;
                if (esUltima) return abierta;
                int baseNuevas = _repartoTarimaAbierta > 0 ? _repartoPrimerNumero : _repartoPrimerNumero + 1;
                return baseNuevas + i;
            }
            return _repartoPrimerNumero + i;
        }

        private void ReconstruirFilasReparto(int fardosPorTarima, int cantTarimas)
        {
            LimpiarFilasReparto();
            _repartoFardosPorTarima = fardosPorTarima;

            for (int i = 0; i < cantTarimas; i++)
            {
                bool esUltima = i == cantTarimas - 1;

                var etiqueta = new Label
                {
                    TextColor = Color.FromHex("#2d3748"),
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    VerticalOptions = LayoutOptions.Center
                };
                _repartoEtiquetas.Add(etiqueta);

                var fila = new Grid { ColumnSpacing = 6 };
                fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
                fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(etiqueta, 0);
                fila.Children.Add(etiqueta);

                if (!esUltima)
                {
                    // Tarimas completas: fijas
                    _repartoFijas.Add(fardosPorTarima);
                    var valor = new Label
                    {
                        Text = $"{fardosPorTarima}  🔒",
                        TextColor = Color.FromHex("#4a5568"),
                        FontSize = 16,
                        FontAttributes = FontAttributes.Bold,
                        WidthRequest = 90,
                        HorizontalTextAlignment = TextAlignment.Center,
                        VerticalOptions = LayoutOptions.Center
                    };
                    Grid.SetColumn(valor, 2);
                    fila.Children.Add(valor);

                    RepartoTarimasLayout.Children.Add(new Frame
                    {
                        BackgroundColor = Color.FromHex("#F7FAFC"),
                        CornerRadius = 8,
                        Padding = new Thickness(10, 4),
                        HasShadow = false,
                        HeightRequest = 44,
                        Content = fila
                    });
                    continue;
                }

                // Última tarima: editable, con botón de ajuste
                _repartoAjustarButton = new Button
                {
                    BackgroundColor = Color.FromHex("#F59E0B"),
                    TextColor = Color.White,
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    CornerRadius = 14,
                    Padding = new Thickness(10, 0),
                    HeightRequest = 32,
                    VerticalOptions = LayoutOptions.Center,
                    IsVisible = false
                };
                _repartoAjustarButton.Clicked += OnRepartoAjustarClicked;
                Grid.SetColumn(_repartoAjustarButton, 1);
                fila.Children.Add(_repartoAjustarButton);

                _repartoUltimaEntry = new Entry
                {
                    Text = fardosPorTarima.ToString(),
                    Keyboard = Keyboard.Numeric,
                    WidthRequest = 90,
                    HeightRequest = 44,
                    FontSize = 18,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromHex("#1a202c"),
                    BackgroundColor = Color.White,
                    PlaceholderColor = Color.FromHex("#A0AEC0"),
                    HorizontalTextAlignment = TextAlignment.Center
                };
                _repartoUltimaEntry.TextChanged += (s, e) => ActualizarTotalReparto();
                Grid.SetColumn(_repartoUltimaEntry, 2);
                fila.Children.Add(_repartoUltimaEntry);

                _repartoUltimaFila = new Frame
                {
                    BackgroundColor = Color.FromHex("#F7FAFC"),
                    BorderColor = Color.FromHex("#F59E0B"),
                    CornerRadius = 8,
                    Padding = new Thickness(10, 2),
                    HasShadow = false,
                    Content = fila
                };
                RepartoTarimasLayout.Children.Add(_repartoUltimaFila);
            }

            RepartoCantTarimasLabel.Text = cantTarimas.ToString();
            RepartoScroll.HeightRequest = Math.Min(Math.Max(cantTarimas, 1), 5) * 50;
            RepartoScroll.IsVisible = cantTarimas > 0;
            ActualizarTotalReparto();
        }

        private void OnRepartoAjustarClicked(object sender, EventArgs e)
        {
            int ajuste = UltimaParaCuadrar;
            if (_repartoUltimaEntry == null || ajuste <= 0) return;
            _repartoUltimaEntry.Text = ajuste.ToString();
        }

        private void OnRepartoTarimaActualChanged(object sender, CheckedChangedEventArgs e)
        {
            if (_repartoActualizando) return;
            ActualizarTotalReparto();
        }

        private void OnRepartoTarimaActualLabelTapped(object sender, EventArgs e)
        {
            RepartoTarimaActualCheck.IsChecked = !RepartoTarimaActualCheck.IsChecked;
        }

        private void ActualizarTotalReparto()
        {
            if (_productoReparto == null) return;

            var valores = LeerCantidadesReparto();
            int total = valores.Sum();
            int solicitado = _productoReparto.Cantidad;
            int ultima = UltimaTarimaReparto;
            bool cuadra = valores.Count > 0 && total == solicitado;

            // Botón "Ajustar" y parpadeo de la última tarima mientras no cuadre
            int ajuste = UltimaParaCuadrar;
            if (_repartoAjustarButton != null)
            {
                _repartoAjustarButton.IsVisible = !cuadra && ajuste > 0;
                _repartoAjustarButton.Text = $"Ajustar a {ajuste}";
            }
            if (!cuadra && _repartoUltimaFila != null) IniciarParpadeoUltimaTarima();
            else DetenerParpadeoUltimaTarima();

            // Mensaje de tarima abierta: solo si la última tarima no va completa
            bool incompleta = valores.Count > 0 && ultima > 0 && ultima < _repartoFardosPorTarima;
            RepartoTarimaActualFrame.IsVisible = incompleta;
            if (!incompleta && RepartoTarimaActualCheck.IsChecked)
            {
                _repartoActualizando = true;
                RepartoTarimaActualCheck.IsChecked = false;
                _repartoActualizando = false;
            }
            if (incompleta)
            {
                RepartoTarimaActualLabel.Text = _repartoTarimaAbierta > 0
                    ? $"¿Poner los {ultima} fardos de la última tarima en tu Tarima {_repartoTarimaAbierta} (abierta) para no generar una tarima más?"
                    : $"¿Poner los {ultima} fardos de la última tarima en una tarima abierta nueva (Tarima {_repartoPrimerNumero}), que seguirá abierta para los demás productos?";
            }

            // Nombres de las filas según la numeración que quedaría
            for (int i = 0; i < _repartoEtiquetas.Count; i++)
            {
                int numero = NumeroFilaReparto(i, _repartoEtiquetas.Count);
                bool esAbierta = UsaTarimaAbierta && i == _repartoEtiquetas.Count - 1;
                _repartoEtiquetas[i].Text = esAbierta ? $"Tarima {numero} (abierta)" : $"Tarima {numero}";
            }

            string texto, fondo, color;
            if (valores.Count == 0)
            {
                texto = "Ingrese los fardos por tarima";
                fondo = "#EDF2F7"; color = "#4a5568";
            }
            else if (cuadra)
            {
                texto = $"✅ Total: {total} / {solicitado}  ·  Cuadra";
                fondo = "#F0FFF4"; color = "#276749";
            }
            else if (total > solicitado)
            {
                texto = $"⚠ Total: {total} / {solicitado}  ·  {total - solicitado} de más";
                fondo = "#FFFAF0"; color = "#C05621";
            }
            else
            {
                texto = $"✕ Total: {total} / {solicitado}  ·  {solicitado - total} menos";
                fondo = "#FFF5F5"; color = "#C53030";
            }

            RepartoTotalLabel.Text = texto;
            RepartoTotalLabel.TextColor = Color.FromHex(color);
            RepartoTotalFrame.BackgroundColor = Color.FromHex(fondo);

            if (valores.Count == 0)
            {
                RepartoAsignacionLabel.Text = string.Empty;
            }
            else
            {
                int cantNuevas = UsaTarimaAbierta ? valores.Count - 1 : valores.Count;
                var nuevas = Enumerable.Range(0, cantNuevas).Select(i => NumeroFilaReparto(i, valores.Count)).ToList();
                string textoNuevas = nuevas.Count == 0 ? ""
                    : nuevas.Count == 1 ? $"Se creará la Tarima {nuevas[0]}"
                    : $"Se crearán las Tarimas {FormatearListaTarimas(nuevas)}";
                string textoAbierta = UsaTarimaAbierta
                    ? $"{(nuevas.Count > 0 ? " y l" : "L")}os {ultima} fardos van en tu Tarima {NumeroFilaReparto(valores.Count - 1, valores.Count)} (abierta)"
                    : "";
                RepartoAsignacionLabel.Text = $"{textoNuevas}{textoAbierta}. El número final se confirma al guardar.";
            }
        }

        private void IniciarParpadeoUltimaTarima()
        {
            if (_repartoParpadeando) return;
            _repartoParpadeando = true;
            bool encendido = false;

            Device.StartTimer(TimeSpan.FromMilliseconds(500), () =>
            {
                var fila = _repartoUltimaFila;
                if (!_repartoParpadeando || fila == null || !RepartoOverlay.IsVisible)
                {
                    if (fila != null) fila.BackgroundColor = Color.FromHex("#F7FAFC");
                    _repartoParpadeando = false;
                    return false;
                }
                encendido = !encendido;
                fila.BackgroundColor = Color.FromHex(encendido ? "#FDE68A" : "#FFFBEB");
                return true;
            });
        }

        private void DetenerParpadeoUltimaTarima()
        {
            _repartoParpadeando = false;
            if (_repartoUltimaFila != null)
                _repartoUltimaFila.BackgroundColor = Color.FromHex("#F7FAFC");
        }

        private static string FormatearListaTarimas(IEnumerable<int> numeros)
        {
            var lista = numeros.Select(n => n.ToString()).ToList();
            if (lista.Count <= 1) return string.Join("", lista);
            return string.Join(", ", lista.Take(lista.Count - 1)) + " y " + lista.Last();
        }

        private async void OnRepartoConfirmarClicked(object sender, EventArgs e)
        {
            if (isConfirming || _productoReparto == null) return;

            var producto = _productoReparto;
            var cantidades = LeerCantidadesReparto();

            if (cantidades.Count == 0)
            {
                await DisplayAlert("Varias tarimas", "Indique cuántos fardos van por tarima.", "OK");
                return;
            }
            if (cantidades.Last() <= 0)
            {
                await DisplayAlert("Varias tarimas", "La última tarima debe llevar al menos 1 fardo.", "OK");
                return;
            }

            int total = cantidades.Sum();
            int solicitado = producto.Cantidad;
            bool usarAbierta = UsaTarimaAbierta;

            if (total != solicitado)
            {
                string diferencia = total > solicitado
                    ? $"Son {total - solicitado} más de lo solicitado ({solicitado})."
                    : $"Son {solicitado - total} menos de lo solicitado ({solicitado}).";
                bool seguro = await DisplayAlert("Confirmar cantidad",
                    $"¿Está seguro de mandar {total} fardos?\n\n{diferencia}", "Sí, confirmar", "No");
                if (!seguro) return;
            }

            isConfirming = true;
            LoadingLabel.Text = "Registrando tarimas...";
            LoadingOverlay.IsVisible = true;

            // Todo cabe en una sola tarima y va a la abierta: es una confirmación normal
            if (usarAbierta && cantidades.Count == 1)
            {
                CerrarReparto();
                try
                {
                    await ConfirmarProductoConInventario(producto.UPC, total, producto, esModoManual: true);
                    if (productoEnProgreso == producto)
                        productoEnProgreso = null;
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error al confirmar", ex.Message, "OK");
                }
                finally
                {
                    LoadingOverlay.IsVisible = false;
                    isConfirming = false;
                }
                return;
            }

            List<ParteTarima> partes;
            try
            {
                partes = await ConfirmarProductoEnVariasTarimas(producto, cantidades, usarAbierta);
            }
            catch (Exception ex)
            {
                LoadingOverlay.IsVisible = false;
                isConfirming = false;
                await DisplayAlert("Error al confirmar", ex.Message, "OK");
                return;
            }

            LoadingOverlay.IsVisible = false;
            CerrarReparto();
            if (productoEnProgreso == producto)
                productoEnProgreso = null;

            int tarimaAbierta = usarAbierta ? partes.Last().NoTarima : 0;
            string detalle = string.Join("\n", partes
                .OrderBy(p => p.NoTarima)
                .Select(p => $"Tarima {p.NoTarima}{(p.NoTarima == tarimaAbierta ? " (abierta)" : "")}:  {p.Cantidad} fardos"));
            string mensaje = $"{producto.Descripcion}\n\n" +
                             $"Registrado en las Tarimas {FormatearListaTarimas(partes.Select(p => p.NoTarima).OrderBy(n => n))}:\n\n" +
                             $"{detalle}\n\nTotal: {total} fardos";

            await DisplayAlert("✅ Producto repartido", mensaje, "OK");

            // Tarjeta NFC para cada tarima completa (ya quedaron entregadas)
            await ConfigurarTarjetasEntregadasAsync(_tarimasRepartoEntregadas.ToList());

            try
            {
                await ActualizarProductoLocal(producto.UPC);
                UpcEntry.Text = string.Empty;
                upcFiltrado = null;
                ActualizarPanelProductoActual(null);
                await VerificarFinalizacionHoja();
            }
            finally
            {
                isConfirming = false;
            }

            if (!_hojaFinalizada)
                Device.BeginInvokeOnMainThread(() => UpcEntry.Focus());
        }
    }
}
