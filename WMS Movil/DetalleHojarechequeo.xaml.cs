using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class DetalleHojarechequeo : ContentPage
    {
        private readonly int idPedido;
        private readonly int? hojaPreseleccionada;
        private List<DetalleProducto> todosLosProductos;
        private Entry _activeEntry;
        public class DetalleProducto
        {
            public string UpcPaquete { get; set; }
            public string CodigoUnidad { get; set; }
            public int UnidadPaquete { get; set; }
            public string Descripcion { get; set; }
            public int CantidadSolicitada { get; set; }
            public int CantidadPreparada { get; set; }
            public string NombreCompleto { get; set; }
            public string DescripcionMotivo { get; set; }
            public int IdPreparacion { get; set; }
            public string Observaciones { get; set; }
            // Propiedades para UI
            public bool EstaVerificado { get; set; }
            public Color ColorEstado => EstaVerificado ? Color.FromHex("#48BB78") : Color.FromHex("#E2E8F0");
            public string EstadoVerificacion => EstaVerificado ? "Verificado" : "Pendiente";
            public bool CantidadCero => CantidadPreparada == 0;
            public bool CantidadMayor => CantidadPreparada > CantidadSolicitada;
            public bool CantidadMenor => CantidadPreparada < CantidadSolicitada;
            public string MensajeAlerta
            {
                get
                {
                    if (CantidadCero) return $"Motivo: {DescripcionMotivo}";
                    if (CantidadMayor) return "Cantidad preparada mayor a la solicitada";
                    if (CantidadMenor) return "Cantidad preparada menor a la solicitada";
                    return string.Empty;
                }
            }

            public Color ColorAlerta
            {
                get
                {
                    if (CantidadCero) return Color.FromHex("#E53E3E"); // Rojo
                    if (CantidadMayor) return Color.FromHex("#ECC94B"); // Amarillo
                    if (CantidadMenor) return Color.FromHex("#ED8936"); // Naranja
                    return Color.Transparent;
                }
            }
        }
        public class HojaInfo
        {
            public int NoHoja { get; set; }
            public int TotalProductos { get; set; }
            public int ProductosVerificados { get; set; }
            public string DisplayText => $"Hoja {NoHoja} ({ProductosVerificados}/{TotalProductos})";
        }
        public class SucursalInfo
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
            public int IdSucursalSincronizacion { get; set; }
            public string RazonSocial { get; set; }
            public string NombreRazon { get; set; }
            public string DisplayText => Nombre;
        }
        public class DepartamentoInfo
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
            public string DisplayText => Nombre;
        }
        public DetalleHojarechequeo(int idPedido, int? hojaPreseleccionada)
        {
            InitializeComponent();
            this.idPedido = idPedido;
            this.hojaPreseleccionada = hojaPreseleccionada;
            ConfigurarEntradas();
            UpcEntry.Focused += OnEntryFocused;
            CantidadEntry.Focused += OnEntryFocused;
            Device.BeginInvokeOnMainThread(async () => {
                bool tienePermiso = await VerificarPermisoAcceso();
                if (!tienePermiso)
                {
                    // Si no tiene permiso, volver a la pantalla anterior
                    await Navigation.PopAsync();
                    return;
                }

                // Si tiene permiso, continuar con la carga normal
                CargarHojas();
                _ = CargarSucursales();
                _ = CargarDepartamentos();
            });
        }
        // 1. Primero, actualizar el método ConfigurarEntradas para verificar si un producto ya está verificado
        private void ConfigurarEntradas()
        {
            // Configurar el evento Completed del UpcEntry
            UpcEntry.Completed += async (sender, e) =>
            {
                LoadingOverlay.IsVisible = true; // Mostrar indicador de carga

                try
                {
                    string upcIngresado = UpcEntry.Text?.Trim();
                    if (string.IsNullOrEmpty(upcIngresado))
                    {
                        await DisplayAlert("Error", "Por favor ingrese un UPC", "OK");
                        return;
                    }

                    // Modificación del formato para asegurar 13 dígitos
                    if (long.TryParse(upcIngresado, out long upcNumerico))
                    {
                        // Aplicamos el formato D13 (13 dígitos con ceros a la izquierda)
                        upcIngresado = upcNumerico.ToString("D13");
                        UpcEntry.Text = upcIngresado; // Actualiza el campo con el formato correcto
                    }

                    // Verificar si se ha seleccionado una hoja
                    if (HojaPicker.SelectedItem is HojaInfo hojaSeleccionada)
                    {
                        // Verificar si el UPC existe en el listado de la hoja actual
                        bool existeEnHoja = false;
                        var productoEnHoja = todosLosProductos?.FirstOrDefault(p => p.UpcPaquete == upcIngresado);

                        if (productoEnHoja != null)
                        {
                            existeEnHoja = true;

                            // Si el producto ya está verificado, mostrar mensaje
                            if (productoEnHoja.EstaVerificado)
                            {
                                bool continuar = await DisplayAlert(
                                    "Producto ya verificado",
                                    $"El producto '{productoEnHoja.Descripcion}' ya ha sido verificado. ¿Desea actualizar la cantidad?",
                                    "Sí, actualizar", "No, cancelar");

                                if (continuar)
                                {
                                    CantidadEntry.Focus();
                                }
                                else
                                {
                                    UpcEntry.Text = string.Empty;
                                    UpcEntry.Focus();
                                }
                                LoadingOverlay.IsVisible = false;
                                return;
                            }

                            // Producto encontrado en la hoja y no verificado, pasar al campo cantidad
                            CantidadEntry.Focus();
                            LoadingOverlay.IsVisible = false;
                            return;
                        }

                        // Si no encuentra en la hoja, verificar en la base de datos
                        string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

                        using (var connection = new MySqlConnection(connectionString))
                        {
                            try
                            {
                                connection.Open();

                                // Si no encuentra directo en la hoja, verificar si existe en productospaquetes
                                string upcPaquete = null;

                                using (var command = new MySqlCommand("SELECT UPCPaquete FROM productospaquetes WHERE Upc = @UpcIngresado LIMIT 1", connection))
                                {
                                    command.Parameters.Add(new MySqlParameter("@UpcIngresado", upcIngresado));

                                    using (var reader = command.ExecuteReader())
                                    {
                                        if (reader.HasRows && reader.Read() && !reader.IsDBNull(0))
                                        {
                                            upcPaquete = reader.GetString(0);

                                            // Aplicar formato de 13 dígitos al UPC del paquete
                                            if (long.TryParse(upcPaquete, out long paqueteNumerico))
                                            {
                                                upcPaquete = paqueteNumerico.ToString("D13");
                                            }
                                        }
                                    }
                                }

                                if (!string.IsNullOrEmpty(upcPaquete))
                                {
                                    // Verificar si el UPCPaquete existe en la lista de productos
                                    var productoPaquete = todosLosProductos?.FirstOrDefault(p => p.UpcPaquete == upcPaquete);

                                    if (productoPaquete != null)
                                    {
                                        // Si existe, mostrar mensaje informativo y actualizar el campo
                                        await DisplayAlert("Información",
                                            $"El UPC escaneado corresponde a una unidad del paquete. Se utilizará el UPC del paquete: {upcPaquete}",
                                            "OK");

                                        UpcEntry.Text = upcPaquete;

                                        // Si el producto ya está verificado, mostrar mensaje
                                        if (productoPaquete.EstaVerificado)
                                        {
                                            bool continuar = await DisplayAlert(
                                                "Producto ya verificado",
                                                $"El producto '{productoPaquete.Descripcion}' ya ha sido verificado. ¿Desea actualizar la cantidad?",
                                                "Sí, actualizar", "No, cancelar");

                                            if (continuar)
                                            {
                                                CantidadEntry.Focus();
                                            }
                                            else
                                            {
                                                UpcEntry.Text = string.Empty;
                                                UpcEntry.Focus();
                                            }
                                            LoadingOverlay.IsVisible = false;
                                            return;
                                        }

                                        CantidadEntry.Focus();
                                        LoadingOverlay.IsVisible = false;
                                        return;
                                    }
                                }

                                // Si no se encontró ni en la hoja ni en productospaquetes,
                                // verificar si el UPC existe en productos (para saber si es un producto válido)
                                bool productoExiste = false;

                                using (var command = new MySqlCommand(@"
                            SELECT COUNT(*) FROM productos WHERE Upc = @Upc", connection))
                                {
                                    command.Parameters.AddWithValue("@Upc", upcIngresado);
                                    int count = Convert.ToInt32(command.ExecuteScalar());

                                    if (count > 0)
                                    {
                                        productoExiste = true;
                                    }
                                }

                                if (!productoExiste && string.IsNullOrEmpty(upcPaquete))
                                {
                                    // UPC no encontrado en ninguna tabla
                                    await DisplayAlert("Error", "El UPC escaneado no existe en la base de datos", "OK");
                                    UpcEntry.Text = string.Empty;
                                    UpcEntry.Focus();
                                    LoadingOverlay.IsVisible = false;
                                    return;
                                }
                                // Si el producto existe en la base de datos pero no en esta hoja específica
                                UpcEntry.Text = string.Empty;
                                UpcEntry.Focus();
                            }
                            catch (Exception ex)
                            {
                                await DisplayAlert("Error", $"Error al verificar UPC: {ex.Message}", "OK");
                                UpcEntry.Text = string.Empty;
                            }
                        }
                    }
                    else
                    {
                        await DisplayAlert("Error", "Por favor seleccione una hoja primero", "OK");
                        UpcEntry.Text = string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Error al procesar el UPC: {ex.Message}", "OK");
                }
                finally
                {
                    LoadingOverlay.IsVisible = false; // Ocultar indicador de carga
                }
            };

            // Configurar el evento TextChanged para convertir a mayúsculas (opcional)
            UpcEntry.TextChanged += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.NewTextValue))
                {
                    UpcEntry.Text = e.NewTextValue.ToUpper();
                }
            };

            // Configurar el evento Completed del CantidadEntry
            CantidadEntry.Completed += (sender, e) =>
            {
                // Aquí disparamos directamente el evento de confirmar
                if (!string.IsNullOrEmpty(CantidadEntry.Text))
                {
                    OnConfirmarClicked(sender, e);
                }
            };
        }

        private async void CargarHojas()
        {
            LoadingOverlay.IsVisible = true;

            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new MySqlCommand(@"
                        SELECT 
                            NoHoja,
                            COUNT(*) as TotalProductos,
                        SUM(CASE WHEN EstadoPreparacionproducto = 5 THEN 1 ELSE 0 END) as ProductosVerificados
                        FROM detallepedidostienda_bodega
                        WHERE IdConsolidado = @IdPedido
                        GROUP BY NoHoja
                        ORDER BY NoHoja", connection))
                    {
                        command.Parameters.AddWithValue("@IdPedido", idPedido);
                        var hojas = new List<HojaInfo>();

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                hojas.Add(new HojaInfo
                                {
                                    NoHoja = reader.GetInt32("NoHoja"),
                                    TotalProductos = reader.GetInt32("TotalProductos"),
                                    ProductosVerificados = reader.GetInt32("ProductosVerificados")
                                });
                            }
                        }

                        Device.BeginInvokeOnMainThread(() => {
                            HojaPicker.ItemsSource = hojas;
                            HojaPicker.ItemDisplayBinding = new Binding("DisplayText");

                            if (hojaPreseleccionada.HasValue)
                            {
                                var hojaSeleccionada = hojas.FirstOrDefault(h => h.NoHoja == hojaPreseleccionada.Value);
                                if (hojaSeleccionada != null)
                                {
                                    HojaPicker.SelectedItem = hojaSeleccionada;
                                }
                            }
                            else if (hojas.Any())
                            {
                                HojaPicker.SelectedIndex = 0;
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar hojas: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        private async void OnHojaSelected(object sender, EventArgs e)
        {
            if (HojaPicker.SelectedItem is HojaInfo hojaSeleccionada)
            {
                // Verificar si hay una hoja actual que no esté completada
                bool puedeSeleccionarNuevaHoja = true;
                int hojaActualNoHoja = -1;

                // Obtener la hoja actual y verificar si está completa
                if (ProductosCollection.ItemsSource != null && ProductosCollection.ItemsSource.Cast<DetalleProducto>().Any())
                {
                    // Hay productos cargados, lo que significa que hay una hoja activa
                    string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                    using (var connection = new MySqlConnection(connectionString))
                    {
                        try
                        {
                            connection.Open();

                            // Primero, determina la hoja actual
                            if (todosLosProductos != null && todosLosProductos.Any())
                            {
                                // Obtener el UPC del primer producto para identificar la hoja
                                string upcProducto = todosLosProductos.First().UpcPaquete;

                                using (var cmdHojaActual = new MySqlCommand(@"
                                    SELECT NoHoja FROM detallepedidostienda_bodega 
                                    WHERE IdConsolidado = @IdPedido AND UPC = @UPC", connection))
                                {
                                    cmdHojaActual.Parameters.AddWithValue("@IdPedido", idPedido);
                                    cmdHojaActual.Parameters.AddWithValue("@UPC", upcProducto);

                                    var result = cmdHojaActual.ExecuteScalar();
                                    if (result != null && result != DBNull.Value)
                                    {
                                        hojaActualNoHoja = Convert.ToInt32(result);

                                        // Verificar si todos los productos de esta hoja están verificados
                                        using (var cmdVerificar = new MySqlCommand(@"
                                                SELECT 
                                                    COUNT(*) as Total,
                                                    SUM(CASE WHEN EstadoPreparacionproducto = 5 THEN 1 ELSE 0 END) as Verificados
                                                FROM detallepedidostienda_bodega
                                                WHERE IdConsolidado = @IdPedido AND NoHoja = @NoHoja", connection))
                                        {
                                            cmdVerificar.Parameters.AddWithValue("@IdPedido", idPedido);
                                            cmdVerificar.Parameters.AddWithValue("@NoHoja", hojaActualNoHoja);

                                            using (var reader = cmdVerificar.ExecuteReader())
                                            {
                                                if (reader.Read())
                                                {
                                                    int total = !reader.IsDBNull(reader.GetOrdinal("Total"))
                                                        ? reader.GetInt32("Total")
                                                        : 0;

                                                    int verificados = !reader.IsDBNull(reader.GetOrdinal("Verificados"))
                                                        ? reader.GetInt32("Verificados")
                                                        : 0;

                                                    // Si no están todos verificados y está intentando cambiar a otra hoja
                                                    if (total != verificados && hojaActualNoHoja != hojaSeleccionada.NoHoja)
                                                    {
                                                        puedeSeleccionarNuevaHoja = false;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            await DisplayAlert("Error", "Error al verificar estado de hoja actual: " + ex.Message, "OK");
                            // Por seguridad, si hay un error, impedimos el cambio
                            puedeSeleccionarNuevaHoja = false;
                        }
                    }
                }

                // Si no puede seleccionar una nueva hoja, mostrar mensaje y revertir selección
                if (!puedeSeleccionarNuevaHoja)
                {
                    await DisplayAlert("Acción no permitida",
                        $"Debe completar todos los productos de la Hoja {hojaActualNoHoja} antes de cambiar a otra hoja.",
                        "OK");

                    // Revertir a la selección anterior
                    var hojaAnterior = ((List<HojaInfo>)HojaPicker.ItemsSource).FirstOrDefault(h => h.NoHoja == hojaActualNoHoja);
                    if (hojaAnterior != null)
                    {
                        HojaPicker.SelectedItem = hojaAnterior;
                    }

                    return;
                }

                // Si llegamos aquí, puede seleccionar la nueva hoja
                LoadingOverlay.IsVisible = true;

                try
                {
                    // Primero verificar si ya existe un inventario para esta hoja
                    string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                    using (var connection = new MySqlConnection(connectionString))
                    {
                        connection.Open();

                        // Consulta para obtener información del inventario de esta hoja
                        using (var command = new MySqlCommand(@"
        SELECT 
            h.IdInventario, 
            h.IdRechequeo, 
            i.IdSucursales, 
            i.IdDepartamentos, 
            i.IdUsuarios, 
            i.IdRazon,
            i.NombreRazon,
            departamentos.Nombre AS NombreDepartamento, 
            sucursales.Nombre AS NombreSucursal
        FROM 
            HistorialPreparacionPedidos AS h
            LEFT JOIN inventarios AS i ON h.IdInventario = i.idInventarios
            LEFT JOIN sucursales ON i.IdSucursales = sucursales.Id
            LEFT JOIN departamentos ON i.IdDepartamentos = departamentos.Id
        WHERE 
            h.IdPedido = @IdPedido AND
            h.Nohoja = @NoHoja", connection))
                        {
                            command.Parameters.AddWithValue("@IdPedido", idPedido);
                            command.Parameters.AddWithValue("@NoHoja", hojaSeleccionada.NoHoja);

                            using (var reader = command.ExecuteReader())
                            {
                                if (reader.Read() && !reader.IsDBNull(reader.GetOrdinal("IdInventario")))
                                {
                                    // La hoja ya tiene un inventario asignado
                                    int idInventario = reader.GetInt32("IdInventario");
                                    int idUsuarioInventario = reader.GetInt32("IdUsuarios");

                                    // Verificar si el inventario es de otro usuario
                                    if (idUsuarioInventario != App.IdUsuarioActual)
                                    {
                                        // El inventario pertenece a otro usuario
                                        reader.Close();
                                        LoadingOverlay.IsVisible = false;

                                        await DisplayAlert("Acceso Denegado",
                                            $"Esta hoja ya tiene asignado el inventario #{idInventario} por otro usuario. No tiene permisos para modificarlo.",
                                            "OK");

                                        return;
                                    }

                                    // El inventario pertenece al usuario actual
                                    // Guardar información temporal para uso posterior
                                    int idSucursal = !reader.IsDBNull(reader.GetOrdinal("IdSucursales"))
                                        ? reader.GetInt32("IdSucursales")
                                        : 0;

                                    int idDepartamento = !reader.IsDBNull(reader.GetOrdinal("IdDepartamentos"))
                                        ? reader.GetInt32("IdDepartamentos")
                                        : 0;

                                    string nombreSucursal = !reader.IsDBNull(reader.GetOrdinal("NombreSucursal"))
                                        ? reader.GetString("NombreSucursal")
                                        : "Sin sucursal";

                                    string nombreDepartamento = !reader.IsDBNull(reader.GetOrdinal("NombreDepartamento"))
                                        ? reader.GetString("NombreDepartamento")
                                        : "Sin departamento";

                                    // Obtener la información de razón social con manejo seguro de tipos
                                    string idRazon = null;
                                    string nombreRazon = null;

                                    // Manejo seguro de IdRazon
                                    if (!reader.IsDBNull(reader.GetOrdinal("IdRazon")))
                                    {
                                        try
                                        {
                                            idRazon = reader.GetString("IdRazon");
                                        }
                                        catch (InvalidCastException)
                                        {
                                            try
                                            {
                                                idRazon = reader.GetInt32("IdRazon").ToString();
                                            }
                                            catch (Exception)
                                            {
                                                idRazon = reader.GetValue(reader.GetOrdinal("IdRazon")).ToString();
                                            }
                                        }
                                    }

                                    // Manejo seguro de NombreRazon
                                    if (!reader.IsDBNull(reader.GetOrdinal("NombreRazon")))
                                    {
                                        nombreRazon = reader.GetString("NombreRazon");
                                    }

                                    reader.Close();

                                    // Mostrar el número de inventario
                                    Device.BeginInvokeOnMainThread(() => {
                                        InventarioIdLabel.Text = $"Inventario #: {idInventario}";
                                        InventarioIdLabel.IsVisible = true;

                                        // Mostrar la razón social si está disponible
                                        if (!string.IsNullOrEmpty(nombreRazon))
                                        {
                                            RazonSocialLabel.Text = $"Razón Social: {nombreRazon}";
                                            RazonSocialLabel.IsVisible = true;
                                        }
                                        else
                                        {
                                            RazonSocialLabel.IsVisible = false;
                                        }

                                        // Deshabilitar controles después de cargar el inventario
                                        SucursalPicker.IsEnabled = false;
                                        DepartamentoPicker.IsEnabled = false;
                                    });

                                    // Cargar datos para los Pickers
                                    try
                                    {
                                        // Primero crear las listas para los combobox
                                        var sucursales = new List<SucursalInfo>();
                                        var departamentos = new List<DepartamentoInfo>();

                                        // Agregar solo los elementos seleccionados con la información de razón social
                                        sucursales.Add(new SucursalInfo
                                        {
                                            Id = idSucursal,
                                            Nombre = nombreSucursal,
                                            IdSucursalSincronizacion = 0, // Valor por defecto
                                            RazonSocial = idRazon,
                                            NombreRazon = nombreRazon
                                        });

                                        departamentos.Add(new DepartamentoInfo
                                        {
                                            Id = idDepartamento,
                                            Nombre = nombreDepartamento
                                        });

                                        // Asignar las listas a los pickers directamente
                                        Device.BeginInvokeOnMainThread(() => {
                                            SucursalPicker.ItemsSource = sucursales;
                                            SucursalPicker.SelectedIndex = 0;

                                            DepartamentoPicker.ItemsSource = departamentos;
                                            DepartamentoPicker.SelectedIndex = 0;

                                            // Para depuración
                                            Console.WriteLine($"Sucursal ID: {idSucursal}, Nombre: {nombreSucursal}, Razón Social ID: {idRazon}, Razón Social Nombre: {nombreRazon}");
                                            Console.WriteLine($"Departamento ID: {idDepartamento}, Nombre: {nombreDepartamento}");
                                        });
                                    }
                                    catch (Exception ex)
                                    {
                                        await DisplayAlert("Error", "Error al configurar pickers: " + ex.Message, "OK");
                                    }
                                }
                                else
                                {
                                    // No tiene inventario asignado, cerrar reader
                                    reader.Close();

                                    // Verificar si hay un inventario activo del usuario en otra hoja del mismo pedido
                                    // que pueda ser reutilizado
                                    using (var cmdInventarioExistente = new MySqlCommand(@"
                        SELECT 
                            h.IdInventario, i.idInventarios, i.IdSucursales, i.IdDepartamentos, 
                            i.IdRazon, i.NombreRazon, s.Nombre AS SucursalNombre, d.Nombre AS DepartamentoNombre
                        FROM HistorialPreparacionPedidos h
                        INNER JOIN inventarios i ON h.IdInventario = i.idInventarios
                        LEFT JOIN sucursales s ON i.IdSucursales = s.Id
                        LEFT JOIN departamentos d ON i.IdDepartamentos = d.Id
                        WHERE h.IdPedido = @IdPedido 
                        AND h.IdInventario IS NOT NULL 
                        AND i.IdUsuarios = @IdUsuario
                        AND i.Estado = 0
                        LIMIT 1", connection))
                                    {
                                        cmdInventarioExistente.Parameters.AddWithValue("@IdPedido", idPedido);
                                        cmdInventarioExistente.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);

                                        using (var readerInventario = cmdInventarioExistente.ExecuteReader())
                                        {
                                            if (readerInventario.Read() && !readerInventario.IsDBNull(readerInventario.GetOrdinal("IdInventario")))
                                            {
                                                // Existe un inventario activo en otra hoja del mismo pedido
                                                int idInventarioExistente = readerInventario.GetInt32("IdInventario");

                                                int idSucursal = !readerInventario.IsDBNull(readerInventario.GetOrdinal("IdSucursales"))
                                                    ? readerInventario.GetInt32("IdSucursales")
                                                    : 0;

                                                int idDepartamento = !readerInventario.IsDBNull(readerInventario.GetOrdinal("IdDepartamentos"))
                                                    ? readerInventario.GetInt32("IdDepartamentos")
                                                    : 0;

                                                string nombreSucursal = !readerInventario.IsDBNull(readerInventario.GetOrdinal("SucursalNombre"))
                                                    ? readerInventario.GetString("SucursalNombre")
                                                    : "Sin sucursal";

                                                string nombreDepartamento = !readerInventario.IsDBNull(readerInventario.GetOrdinal("DepartamentoNombre"))
                                                    ? readerInventario.GetString("DepartamentoNombre")
                                                    : "Sin departamento";

                                                // Obtener información de razón social con manejo seguro
                                                string idRazon = null;
                                                string nombreRazon = null;

                                                if (!readerInventario.IsDBNull(readerInventario.GetOrdinal("IdRazon")))
                                                {
                                                    try
                                                    {
                                                        idRazon = readerInventario.GetString("IdRazon");
                                                    }
                                                    catch (Exception)
                                                    {
                                                        idRazon = readerInventario.GetValue(readerInventario.GetOrdinal("IdRazon")).ToString();
                                                    }
                                                }

                                                if (!readerInventario.IsDBNull(readerInventario.GetOrdinal("NombreRazon")))
                                                {
                                                    nombreRazon = readerInventario.GetString("NombreRazon");
                                                }

                                                readerInventario.Close();

                                                // Mostrar mensaje simple con un solo botón de continuar
                                                await DisplayAlert(
                                                    "Inventario existente",
                                                    $"Ya tiene un inventario activo (#{idInventarioExistente}) para este pedido. Se usará el mismo inventario para esta hoja.",
                                                    "Continuar");

                                                // Primero, verificar si ya existe un registro para esta hoja en HistorialPreparacionPedidos
                                                bool registroExistente = false;
                                                using (var cmdVerificarRegistro = new MySqlCommand(@"
                                            SELECT COUNT(*) FROM HistorialPreparacionPedidos 
                                            WHERE IdPedido = @IdPedido AND Nohoja = @NoHoja", connection))
                                                {
                                                    cmdVerificarRegistro.Parameters.AddWithValue("@IdPedido", idPedido);
                                                    cmdVerificarRegistro.Parameters.AddWithValue("@NoHoja", hojaSeleccionada.NoHoja);

                                                    int count = Convert.ToInt32(cmdVerificarRegistro.ExecuteScalar());
                                                    registroExistente = (count > 0);
                                                }

                                                if (registroExistente)
                                                {
                                                    // Actualizar el registro existente
                                                    using (var cmdActualizarHistorial = new MySqlCommand(@"
                                                UPDATE HistorialPreparacionPedidos
                                                SET IdInventario = @IdInventario, IdRechequeo = @IdRechequeo
                                                WHERE IdPedido = @IdPedido AND Nohoja = @NoHoja", connection))
                                                    {
                                                        cmdActualizarHistorial.Parameters.AddWithValue("@IdPedido", idPedido);
                                                        cmdActualizarHistorial.Parameters.AddWithValue("@NoHoja", hojaSeleccionada.NoHoja);
                                                        cmdActualizarHistorial.Parameters.AddWithValue("@IdInventario", idInventarioExistente);
                                                        cmdActualizarHistorial.Parameters.AddWithValue("@IdRechequeo", App.IdUsuarioActual);

                                                        cmdActualizarHistorial.ExecuteNonQuery();
                                                    }
                                                }
                                                else
                                                {
                                                    // Insertar nuevo registro solo si no existe
                                                    using (var cmdInsertarHistorial = new MySqlCommand(@"
                                                INSERT INTO HistorialPreparacionPedidos
                                                    (IdPedido, Nohoja, IdInventario, IdRechequeo)
                                                VALUES
                                                    (@IdPedido, @NoHoja, @IdInventario, @IdRechequeo)", connection))
                                                    {
                                                        cmdInsertarHistorial.Parameters.AddWithValue("@IdPedido", idPedido);
                                                        cmdInsertarHistorial.Parameters.AddWithValue("@NoHoja", hojaSeleccionada.NoHoja);
                                                        cmdInsertarHistorial.Parameters.AddWithValue("@IdInventario", idInventarioExistente);
                                                        cmdInsertarHistorial.Parameters.AddWithValue("@IdRechequeo", App.IdUsuarioActual);

                                                        cmdInsertarHistorial.ExecuteNonQuery();
                                                    }
                                                }

                                                // Mostrar información del inventario
                                                Device.BeginInvokeOnMainThread(() => {
                                                    InventarioIdLabel.Text = $"Inventario #: {idInventarioExistente}";
                                                    InventarioIdLabel.IsVisible = true;

                                                    if (!string.IsNullOrEmpty(nombreRazon))
                                                    {
                                                        RazonSocialLabel.Text = $"Razón Social: {nombreRazon}";
                                                        RazonSocialLabel.IsVisible = true;
                                                    }
                                                    else
                                                    {
                                                        RazonSocialLabel.IsVisible = false;
                                                    }

                                                    // Deshabilitar controles
                                                    SucursalPicker.IsEnabled = false;
                                                    DepartamentoPicker.IsEnabled = false;
                                                });

                                                // Cargar datos para los Pickers
                                                try
                                                {
                                                    var sucursales = new List<SucursalInfo>();
                                                    var departamentos = new List<DepartamentoInfo>();

                                                    sucursales.Add(new SucursalInfo
                                                    {
                                                        Id = idSucursal,
                                                        Nombre = nombreSucursal,
                                                        IdSucursalSincronizacion = 0,
                                                        RazonSocial = idRazon,
                                                        NombreRazon = nombreRazon
                                                    });

                                                    departamentos.Add(new DepartamentoInfo
                                                    {
                                                        Id = idDepartamento,
                                                        Nombre = nombreDepartamento
                                                    });

                                                    Device.BeginInvokeOnMainThread(() => {
                                                        SucursalPicker.ItemsSource = sucursales;
                                                        SucursalPicker.SelectedIndex = 0;

                                                        DepartamentoPicker.ItemsSource = departamentos;
                                                        DepartamentoPicker.SelectedIndex = 0;
                                                    });
                                                }
                                                catch (Exception ex)
                                                {
                                                    await DisplayAlert("Error", "Error al configurar pickers: " + ex.Message, "OK");
                                                }
                                            }
                                            else
                                            {
                                                // No hay inventario existente para este pedido
                                                // Habilitar la selección de sucursal y departamento para crear un nuevo inventario
                                                Device.BeginInvokeOnMainThread(() => {
                                                    // Ocultar etiquetas de inventario y razón social
                                                    InventarioIdLabel.IsVisible = false;
                                                    RazonSocialLabel.IsVisible = false;

                                                    // Habilitar controles para que pueda seleccionar
                                                    SucursalPicker.IsEnabled = true;
                                                    DepartamentoPicker.IsEnabled = true;

                                                    // Verificar si ya tenemos sucursales cargadas
                                                    if (SucursalPicker.ItemsSource == null || !((List<SucursalInfo>)SucursalPicker.ItemsSource).Any())
                                                    {
                                                        // Cargar todas las sucursales y departamentos disponibles
                                                        CargarSucursales();
                                                    }

                                                    if (DepartamentoPicker.ItemsSource == null || !((List<DepartamentoInfo>)DepartamentoPicker.ItemsSource).Any())
                                                    {
                                                        CargarDepartamentos();
                                                    }
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // Finalmente, cargar los productos
                        await CargarProductos(hojaSeleccionada.NoHoja);
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", "Error al verificar inventario: " + ex.Message, "OK");
                }
                finally
                {
                    LoadingOverlay.IsVisible = false;
                }
            }
        }

        private async Task CargarProductos(int noHoja)
        {
            LoadingOverlay.IsVisible = true;

            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    // Consulta modificada para capturar todos los productos, incluso si fueron escaneados manualmente
                    using (var command = new MySqlCommand(@"
                        SELECT
                            detallepedidostienda_bodega.UPC as UpcPaquete, 
                            COALESCE(productospaquetes.Upc, detallepedidostienda_bodega.UPC) as CodigoUnidad, 
                            COALESCE(productospaquetes.Cantidad, 1) as UnidadPaquete, 
                            detallepedidostienda_bodega.Descripcion, 
                            detallepedidostienda_bodega.Cantidad as CantidadSolicitada, 
                            detallepedidostienda_bodega.CantConfirmada AS CantidadPreparada,
                            detallepedidostienda_bodega.Observaciones,
                            usuarios.NombreCompleto, 
                            TiposMotivosPendiente.DescripcionMotivo,
                            detallepedidostienda_bodega.EstadoPreparacionproducto as IdPreparacion,
                            CASE WHEN detallepedidostienda_bodega.EstadoPreparacionproducto = 5 THEN 1 ELSE 0 END as EstaVerificado
                        FROM detallepedidostienda_bodega
                        LEFT JOIN productospaquetes ON detallepedidostienda_bodega.UPC = productospaquetes.UPCPaquete
                        INNER JOIN usuarios ON detallepedidostienda_bodega.IdUsuariopreparo = usuarios.Id
                        INNER JOIN TiposMotivosPendiente ON detallepedidostienda_bodega.EstadoPreparacionproducto = TiposMotivosPendiente.Idpreparacion
                        WHERE detallepedidostienda_bodega.IdConsolidado = @IdPedido 
                        AND detallepedidostienda_bodega.NoHoja = @NoHoja", connection))
                    {
                        command.Parameters.AddWithValue("@IdPedido", idPedido);
                        command.Parameters.AddWithValue("@NoHoja", noHoja);

                        var productos = new List<DetalleProducto>();
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                productos.Add(new DetalleProducto
                                {
                                    UpcPaquete = reader.GetString("UpcPaquete"),
                                    CodigoUnidad = reader.GetString("CodigoUnidad"),
                                    UnidadPaquete = reader.GetInt32("UnidadPaquete"),
                                    Descripcion = reader.GetString("Descripcion"),
                                    CantidadSolicitada = reader.GetInt32("CantidadSolicitada"),
                                    CantidadPreparada = reader.GetInt32("CantidadPreparada"),
                                    Observaciones = !reader.IsDBNull(reader.GetOrdinal("Observaciones")) ?
                                        reader.GetString("Observaciones") : string.Empty,
                                    NombreCompleto = reader.GetString("NombreCompleto"),
                                    DescripcionMotivo = reader.GetString("DescripcionMotivo"),
                                    IdPreparacion = reader.GetInt32("IdPreparacion"),
                                    EstaVerificado = reader.GetBoolean("EstaVerificado")
                                });
                            }
                        }

                        todosLosProductos = productos;
                        Device.BeginInvokeOnMainThread(() => {
                            ProductosCollection.ItemsSource = productos.Where(p => !p.EstaVerificado).ToList();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar productos: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
                UpcEntry.Focus();
            }
        }
        // Modificación del método OnConfirmarClicked para incluir la lógica de IdRazonSocial

        private async void OnConfirmarClicked(object sender, EventArgs e)
        {
            LoadingOverlay.IsVisible = true;

            try
            {
                // Verificar que se tenga un UPC
                string upcIngresado = UpcEntry.Text?.Trim();
            if (string.IsNullOrEmpty(upcIngresado))
            {
                await DisplayAlert("Error", "Por favor ingrese un UPC", "OK");
                return;
            }

            // Verificar que se tenga una cantidad
            if (string.IsNullOrEmpty(CantidadEntry.Text) || !int.TryParse(CantidadEntry.Text, out int cantidadIngresada))
            {
                await DisplayAlert("Error", "Por favor ingrese una cantidad válida", "OK");
                return;
            }

            // Verificar que se haya seleccionado una hoja
            if (HojaPicker.SelectedItem == null)
            {
                await DisplayAlert("Error", "Por favor seleccione una hoja", "OK");
                return;
            }

            var hojaSeleccionada = HojaPicker.SelectedItem as HojaInfo;

            // Formatear UPC a 13 dígitos
            if (long.TryParse(upcIngresado, out long upcNumerico))
            {
                upcIngresado = upcNumerico.ToString("D13");
                UpcEntry.Text = upcIngresado;
            }
            if (cantidadIngresada == 0)
            {
                await DisplayAlert("Error", "La cantidad no puede ser cero. Por favor ingrese un valor mayor a cero.", "OK");
                LoadingOverlay.IsVisible = false;
                return;
            }
            // Verificar si el UPC existe en el listado de la hoja actual
            bool existeEnHoja = false;
            var productoEnHoja = todosLosProductos?.FirstOrDefault(p => p.UpcPaquete == upcIngresado);

            if (productoEnHoja == null)
            {
                LoadingOverlay.IsVisible = true;

                try
                {
                    // Si no encuentra en la hoja, verificar en productospaquetes
                    string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                    using (var connection = new MySqlConnection(connectionString))
                    {
                        connection.Open();
                        string upcPaquete = null;

                        using (var command = new MySqlCommand("SELECT UPCPaquete FROM productospaquetes WHERE Upc = @UpcIngresado LIMIT 1", connection))
                        {
                            command.Parameters.Add(new MySqlParameter("@UpcIngresado", upcIngresado));

                            using (var reader = command.ExecuteReader())
                            {
                                if (reader.HasRows && reader.Read() && !reader.IsDBNull(0))
                                {
                                    upcPaquete = reader.GetString(0);

                                    // Aplicar formato de 13 dígitos al UPC del paquete
                                    if (long.TryParse(upcPaquete, out long paqueteNumerico))
                                    {
                                        upcPaquete = paqueteNumerico.ToString("D13");
                                    }
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(upcPaquete))
                        {
                            // Verificar si el UPCPaquete existe en la lista de productos
                            var productoPaquete = todosLosProductos?.FirstOrDefault(p => p.UpcPaquete == upcPaquete);

                            if (productoPaquete != null)
                            {
                                // Si existe, mostrar mensaje informativo y actualizar el campo
                                upcIngresado = upcPaquete;
                                UpcEntry.Text = upcPaquete;
                                productoEnHoja = productoPaquete;
                                existeEnHoja = true;
                            }
                        }

                        // Si no se encuentra en productospaquetes, verificar si existe como producto
                        if (productoEnHoja == null && !existeEnHoja)
                        {
                            bool productoExiste = false;

                            using (var command = new MySqlCommand(@"
                                SELECT COUNT(*) FROM productos WHERE Upc = @Upc", connection))
                            {
                                command.Parameters.AddWithValue("@Upc", upcIngresado);
                                int count = Convert.ToInt32(command.ExecuteScalar());

                                if (count > 0)
                                {
                                    productoExiste = true;
                                }
                            }

                            if (!productoExiste && string.IsNullOrEmpty(upcPaquete))
                            {
                                // UPC no encontrado en ninguna tabla
                                await DisplayAlert("Error", "El UPC escaneado no existe en la base de datos", "OK");
                                UpcEntry.Text = string.Empty;
                                UpcEntry.Focus();
                                LoadingOverlay.IsVisible = false;
                                return;
                            }

                            // Si el producto existe en la base de datos pero no en esta hoja específica
                            await DisplayAlert("Advertencia",
                                "El UPC escaneado es un producto válido, pero no está incluido en esta hoja de recheque",
                                "OK");
                            UpcEntry.Text = string.Empty;
                            UpcEntry.Focus();
                            LoadingOverlay.IsVisible = false;
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Error al verificar UPC: {ex.Message}", "OK");
                    LoadingOverlay.IsVisible = false;
                    return;
                }
                finally
                {
                    LoadingOverlay.IsVisible = false;
                }
            }
            else
            {
                existeEnHoja = true;
            }

            // Si llegamos aquí y el producto no existe en la hoja, salir
            if (!existeEnHoja || productoEnHoja == null)
            {
                await DisplayAlert("Error", "El UPC no existe en la hoja actual", "OK");
                UpcEntry.Text = string.Empty;
                UpcEntry.Focus();
                return;
            }

            // Obtener datos necesarios
            var sucursalSeleccionada = SucursalPicker.SelectedItem as SucursalInfo;
            var departamentoSeleccionado = DepartamentoPicker.SelectedItem as DepartamentoInfo;

            if (sucursalSeleccionada == null)
            {
                await DisplayAlert("Error", "Por favor seleccione una sucursal", "OK");
                return;
            }

            if (departamentoSeleccionado == null)
            {
                await DisplayAlert("Error", "Por favor seleccione un departamento", "OK");
                return;
            }

            LoadingOverlay.IsVisible = true;

            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // 1. Verificar si ya existe un inventario para esta hoja
                            int idInventario;
                            bool inventarioExistente = false;

                            using (var commandVerificar = new MySqlCommand(@"
                                SELECT h.IdInventario
                                FROM HistorialPreparacionPedidos AS h
                                WHERE h.IdPedido = @IdPedido AND h.Nohoja = @NoHoja", connection, transaction))
                            {
                                commandVerificar.Parameters.AddWithValue("@IdPedido", idPedido);
                                commandVerificar.Parameters.AddWithValue("@NoHoja", hojaSeleccionada.NoHoja);

                                var resultado = commandVerificar.ExecuteScalar();

                                if (resultado != null && resultado != DBNull.Value)
                                {
                                    // Ya existe un inventario
                                    idInventario = Convert.ToInt32(resultado);
                                    inventarioExistente = true;
                                }
                                else
                                {
                                    // 2. Crear el inventario nuevo si no existe
                                    DateTime ahora = DateTime.Now;
                                    string fechaActual = ahora.ToString("yyyy-MM-dd");
                                    string fechaHoraActual = ahora.ToString("yyyy-MM-dd HH:mm:ss");

                                    using (var command = new MySqlCommand(@"
                                        INSERT INTO inventarios (
                                            IdUsuarios, 
                                            Fecha, 
                                            FechaHoraI, 
                                            Operacion, 
                                            Estado, 
                                            IdSucursales, 
                                            Sucursal, 
                                            Usuario, 
                                            IdRazon, 
                                            NombreRazon,
                                            IdDepartamentos
                                        ) VALUES (
                                            @IdUsuarios, 
                                            @Fecha, 
                                            @FechaHoraI, 
                                            @Operacion, 
                                            @Estado, 
                                            @IdSucursales, 
                                            @Sucursal, 
                                            @Usuario, 
                                            @IdRazon, 
                                            @NombreRazon,
                                            @IdDepartamento
                                        ); SELECT LAST_INSERT_ID();", connection, transaction))
                                    {
                                        command.Parameters.AddWithValue("@IdUsuarios", App.IdUsuarioActual);
                                        command.Parameters.AddWithValue("@Fecha", fechaActual);
                                        command.Parameters.AddWithValue("@FechaHoraI", fechaHoraActual);
                                        command.Parameters.AddWithValue("@Operacion", 1);
                                        command.Parameters.AddWithValue("@Estado", 0);
                                        command.Parameters.AddWithValue("@IdSucursales", sucursalSeleccionada.Id);
                                        command.Parameters.AddWithValue("@Sucursal", sucursalSeleccionada.Nombre);
                                        command.Parameters.AddWithValue("@Usuario", App.NombreUsuarioActual);
                                        command.Parameters.AddWithValue("@IdRazon", sucursalSeleccionada.RazonSocial);
                                        command.Parameters.AddWithValue("@NombreRazon", sucursalSeleccionada.NombreRazon);
                                        command.Parameters.AddWithValue("@IdDepartamento", departamentoSeleccionado.Id);

                                        // Obtener el ID del inventario recién creado
                                        idInventario = Convert.ToInt32(command.ExecuteScalar());
                                    }

                                    // 3. Actualizar historialPrepracacionPedidos con el nuevo idInventario
                                    using (var historialCommand = new MySqlCommand(@"
                                        UPDATE HistorialPreparacionPedidos
                                        SET IdInventario = @IdInventario,
                                            IdRechequeo = @IdRechequeo
                                        WHERE IdPedido = @IdPedido 
                                        AND Nohoja = @NoHoja", connection, transaction))
                                    {
                                        historialCommand.Parameters.AddWithValue("@IdPedido", idPedido);
                                        historialCommand.Parameters.AddWithValue("@NoHoja", hojaSeleccionada.NoHoja);
                                        historialCommand.Parameters.AddWithValue("@IdInventario", idInventario);
                                        historialCommand.Parameters.AddWithValue("@IdRechequeo", App.IdUsuarioActual);

                                        int filasActualizadas = historialCommand.ExecuteNonQuery();
                                        if (filasActualizadas == 0)
                                        {
                                            // Si no hay filas actualizadas, podría ser porque no existe el registro
                                            await DisplayAlert("Error", "No se encontró el registro para actualizar en historialPreparacionPedidos", "OK");
                                            transaction.Rollback();
                                            return;
                                        }
                                    }
                                }
                            }

                            // 4. Obtener datos del producto escaneado
                            string upc = "";
                            string descLarga = "";
                            int unidadesFardo = 0;
                            int idProveedores = 0;
                            int idDepartamentosProducto = 0;
                            decimal costo = 0;
                            decimal existencia1 = 0;
                            decimal existencia2 = 0;
                            decimal existencia3 = 0;
                            decimal existencia4 = 0;
                            bool esProductoPaquete = false;

                                // Primero verificar si existe en productospaquetes
                                using (var commandProductoPaquete = new MySqlCommand(@"
                                        SELECT 
                                            pp.Upc, 
                                            pp.DescLarga, 
                                            pp.Cantidad,
                                            p.DescLarga AS ProductoDescLarga,
                                            p.IdProveedores,
                                            p.IdDepartamentos,
                                            p.Costo,
                                            p.Existencia_1,
                                            p.Existencia_2,
                                            p.Existencia_3,
                                            p.Existencia_4
                                        FROM 
                                            productospaquetes pp
                                            INNER JOIN productos p ON pp.Upc = p.Upc
                                        WHERE 
                                            pp.UPCPaquete = @UpcPaquete", connection, transaction))
                                {
                                    commandProductoPaquete.Parameters.AddWithValue("@UpcPaquete", upcIngresado);

                                    using (var reader = commandProductoPaquete.ExecuteReader())
                                    {
                                        if (reader.Read())
                                        {
                                            // Es un producto de paquete
                                            esProductoPaquete = true;
                                            upc = reader.GetString("Upc");

                                            // Verificar si DescLarga de productospaquetes es "0"
                                            string descLargaPaquete = reader.GetString("DescLarga");
                                            if (descLargaPaquete == "0" || string.IsNullOrWhiteSpace(descLargaPaquete))
                                            {
                                                // Si es "0" o está vacío, usar DescLarga de productos
                                                descLarga = reader.GetString("ProductoDescLarga");
                                            }
                                            else
                                            {
                                                // Caso normal, usar DescLarga de productospaquetes
                                                descLarga = descLargaPaquete;
                                            }

                                            unidadesFardo = reader.GetInt32("Cantidad");
                                            idProveedores = reader.GetInt32("IdProveedores");
                                            idDepartamentosProducto = reader.GetInt32("IdDepartamentos");
                                            costo = reader.GetDecimal("Costo");
                                            existencia1 = reader.GetDecimal("Existencia_1");
                                            existencia2 = reader.GetDecimal("Existencia_2");
                                            existencia3 = reader.GetDecimal("Existencia_3");
                                            existencia4 = reader.GetDecimal("Existencia_4");
                                        }
                                    }
                                }

                                // Si no es un producto de paquete, buscar directamente en productos
                                if (!esProductoPaquete)
                                {
                                    using (var commandProducto = new MySqlCommand(@"
                                            SELECT 
                                                Upc,
                                                DescLarga,
                                                IdProveedores,
                                                IdDepartamentos,
                                                Costo,
                                                Existencia_1,
                                                Existencia_2,
                                                Existencia_3,
                                                Existencia_4
                                            FROM 
                                                productos
                                            WHERE 
                                                Upc = @Upc", connection, transaction))
                                    {
                                        commandProducto.Parameters.AddWithValue("@Upc", upcIngresado);

                                        using (var reader = commandProducto.ExecuteReader())
                                        {
                                            if (reader.Read())
                                            {
                                                // Es un producto individual
                                                upc = reader.GetString("Upc");
                                                descLarga = reader.GetString("DescLarga");
                                                unidadesFardo = 1; // No hay paquete, una unidad
                                                idProveedores = reader.GetInt32("IdProveedores");
                                                idDepartamentosProducto = reader.GetInt32("IdDepartamentos");
                                                costo = reader.GetDecimal("Costo");
                                                existencia1 = reader.GetDecimal("Existencia_1");
                                                existencia2 = reader.GetDecimal("Existencia_2");
                                                existencia3 = reader.GetDecimal("Existencia_3");
                                                existencia4 = reader.GetDecimal("Existencia_4");
                                            }
                                            else
                                            {
                                                // No se encontró el producto en ninguna tabla
                                                await DisplayAlert("Error", "El UPC escaneado no existe en la base de datos", "OK");
                                                transaction.Rollback();
                                                return;
                                            }
                                        }
                                    }
                                }

                                // 5. Calcular la cantidad total
                                int cantidadTotal;
                            if (esProductoPaquete)
                            {
                                // Si es un producto de paquete, multiplicar por unidades por fardo
                                cantidadTotal = cantidadIngresada * unidadesFardo;
                            }
                            else
                            {
                                // Si es un producto individual, usar la cantidad ingresada directamente
                                cantidadTotal = cantidadIngresada;
                            }

                            // 5.1 Determinar el IdRazonSocial basado en la existencia disponible
                            int idRazonSocial = 0;

                            // Obtener el ID de la razón social de la sucursal seleccionada
                            int idRazonSucursal = 0; // Valor predeterminado

                            // Verificar si RazonSocial no es nulo antes de intentar convertirlo
                            if (!string.IsNullOrEmpty(sucursalSeleccionada.RazonSocial))
                            {
                                if (int.TryParse(sucursalSeleccionada.RazonSocial, out int tempId))
                                {
                                    idRazonSucursal = tempId;
                                }
                            }

                            // Primero verificar si hay existencia suficiente en la razón social de la sucursal seleccionada
                            bool existenciaSuficiente = false;

                            switch (idRazonSucursal)
                            {
                                case 1:
                                    if (existencia1 >= cantidadTotal)
                                    {
                                        idRazonSocial = 1;
                                        existenciaSuficiente = true;
                                    }
                                    break;
                                case 2:
                                    if (existencia2 >= cantidadTotal)
                                    {
                                        idRazonSocial = 2;
                                        existenciaSuficiente = true;
                                    }
                                    break;
                                case 3:
                                    if (existencia3 >= cantidadTotal)
                                    {
                                        idRazonSocial = 3;
                                        existenciaSuficiente = true;
                                    }
                                    break;
                                case 4:
                                    if (existencia4 >= cantidadTotal)
                                    {
                                        idRazonSocial = 4;
                                        existenciaSuficiente = true;
                                    }
                                    break;
                            }

                            // Si no hay existencia suficiente en la razón social seleccionada,
                            // buscar en las otras razones sociales
                            if (!existenciaSuficiente)
                            {
                                // Verificar Existencia_1
                                if (existencia1 >= cantidadTotal)
                                {
                                    idRazonSocial = 1;
                                }
                                // Verificar Existencia_2
                                else if (existencia2 >= cantidadTotal)
                                {
                                    idRazonSocial = 2;
                                }
                                // Verificar Existencia_3
                                else if (existencia3 >= cantidadTotal)
                                {
                                    idRazonSocial = 3;
                                }
                                // Verificar Existencia_4
                                else if (existencia4 >= cantidadTotal)
                                {
                                    idRazonSocial = 4;
                                }
                                // Si ninguna tiene existencia suficiente, idRazonSocial queda como 0
                            }

                            // 6. Verificar si ya existe un registro con este mismo UPC en el inventario
                            bool registroExistente = false;
                            using (var commandVerificarDetalle = new MySqlCommand(@"
                                    SELECT COUNT(*) FROM detalleinventarios 
                                    WHERE IdInventarios = @IdInventario AND " +
                                    (esProductoPaquete ? "DUN14 = @DUN14" : "Upc = @Upc"), connection, transaction))
                            {
                                commandVerificarDetalle.Parameters.AddWithValue("@IdInventario", idInventario);
                                if (esProductoPaquete)
                                {
                                    commandVerificarDetalle.Parameters.AddWithValue("@DUN14", upcIngresado);
                                }
                                else
                                {
                                    commandVerificarDetalle.Parameters.AddWithValue("@Upc", upcIngresado);
                                }

                                int count = Convert.ToInt32(commandVerificarDetalle.ExecuteScalar());
                                registroExistente = count > 0;
                            }

                            // 7. Insertar o actualizar en detalleinventarios
                            if (registroExistente)
                            {
                                // Actualizar el registro existente
                                string updateQuery = @"
                                            UPDATE detalleinventarios
                                            SET Cantidad = Cantidad + @Cantidad, IdRazonSocial = @IdRazonSocial
                                            WHERE IdInventarios = @IdInventario AND " +
                                        (esProductoPaquete ? "DUN14 = @DUN14" : "Upc = @Upc");

                                using (var commandUpdateDetalle = new MySqlCommand(updateQuery, connection, transaction))
                                {
                                    commandUpdateDetalle.Parameters.AddWithValue("@IdInventario", idInventario);
                                    if (esProductoPaquete)
                                    {
                                        commandUpdateDetalle.Parameters.AddWithValue("@DUN14", upcIngresado);
                                    }
                                    else
                                    {
                                        commandUpdateDetalle.Parameters.AddWithValue("@Upc", upcIngresado);
                                    }
                                    commandUpdateDetalle.Parameters.AddWithValue("@Cantidad", cantidadTotal);
                                    commandUpdateDetalle.Parameters.AddWithValue("@IdRazonSocial", idRazonSocial);

                                    commandUpdateDetalle.ExecuteNonQuery();
                                }
                            }
                            else
                            {
                                // Insertar nuevo registro dependiendo del tipo de producto
                                string sqlInsert;
                                if (esProductoPaquete)
                                {
                                    // Producto de paquete - guardar todos los campos
                                    sqlInsert = @"
                                        INSERT INTO detalleinventarios (
                                            IdInventarios,
                                            DUN14,
                                            Upc,
                                            Cantidad,
                                            UnidadesFardo,
                                            Descripcion,
                                            IdProveedor,
                                            IdDepartamento,
                                            Costo,
                                            IdRazonSocial
                                        ) VALUES (
                                            @IdInventarios,
                                            @DUN14,
                                            @Upc,
                                            @Cantidad,
                                            @UnidadesFardo,
                                            @Descripcion,
                                            @IdProveedor,
                                            @IdDepartamento,
                                            @Costo,
                                            @IdRazonSocial
                                        )";
                                }
                                else
                                {
                                    // Producto individual - no guardar DUN14 ni UnidadesFardo
                                    sqlInsert = @"
                                    INSERT INTO detalleinventarios (
                                        IdInventarios,
                                        Upc,
                                        Cantidad,
                                        Descripcion,
                                        IdProveedor,
                                        IdDepartamento,
                                        Costo,
                                        IdRazonSocial
                                    ) VALUES (
                                        @IdInventarios,
                                        @Upc,
                                        @Cantidad,
                                        @Descripcion,
                                        @IdProveedor,
                                        @IdDepartamento,
                                        @Costo,
                                        @IdRazonSocial
                                    )";
                                }

                                using (var commandInsertDetalle = new MySqlCommand(sqlInsert, connection, transaction))
                                {
                                    commandInsertDetalle.Parameters.AddWithValue("@IdInventarios", idInventario);
                                    if (esProductoPaquete)
                                    {
                                        commandInsertDetalle.Parameters.AddWithValue("@DUN14", upcIngresado);
                                        commandInsertDetalle.Parameters.AddWithValue("@UnidadesFardo", unidadesFardo);
                                    }
                                    commandInsertDetalle.Parameters.AddWithValue("@Upc", upc);
                                    commandInsertDetalle.Parameters.AddWithValue("@Cantidad", cantidadTotal);
                                    commandInsertDetalle.Parameters.AddWithValue("@Descripcion", descLarga);
                                    commandInsertDetalle.Parameters.AddWithValue("@IdProveedor", idProveedores);
                                    commandInsertDetalle.Parameters.AddWithValue("@IdDepartamento", idDepartamentosProducto);
                                    commandInsertDetalle.Parameters.AddWithValue("@Costo", costo);
                                    commandInsertDetalle.Parameters.AddWithValue("@IdRazonSocial", idRazonSocial);

                                    commandInsertDetalle.ExecuteNonQuery();
                                }
                            }

                            // Para depuración - mostrar información sobre IdRazonSocial
                            Console.WriteLine($"UPC: {upcIngresado}, Cantidad: {cantidadTotal}");
                            Console.WriteLine($"Existencias - Razón 1: {existencia1}, Razón 2: {existencia2}, Razón 3: {existencia3}, Razón 4: {existencia4}");
                            Console.WriteLine($"Razón Social de Sucursal: {idRazonSucursal}, IdRazonSocial determinado: {idRazonSocial}");

                            using (var commandActualizarEstado = new MySqlCommand(@"
                                    UPDATE detallepedidostienda_bodega
                                    SET EstadoPreparacionproducto = 5
                                    WHERE IdConsolidado = @IdPedido 
                                    AND NoHoja = @NoHoja
                                    AND UPC = @UPC", connection, transaction))
                            {
                                commandActualizarEstado.Parameters.AddWithValue("@IdPedido", idPedido);
                                commandActualizarEstado.Parameters.AddWithValue("@NoHoja", hojaSeleccionada.NoHoja);
                                commandActualizarEstado.Parameters.AddWithValue("@UPC", upcIngresado);

                                int filasActualizadas = commandActualizarEstado.ExecuteNonQuery();

                                if (filasActualizadas == 0)
                                {
                                    // No se encontró el producto en la hoja - podría ser un UPC que no está en esta hoja específica
                                    Console.WriteLine($"Advertencia: No se encontró el UPC {upcIngresado} en la hoja {hojaSeleccionada.NoHoja} del pedido {idPedido}");
                                    // No lanzamos error porque el producto ya se registró en detalleinventarios
                                }
                                else
                                {
                                    Console.WriteLine($"Se actualizó el estado a 'Verificado' para el UPC {upcIngresado} en la hoja {hojaSeleccionada.NoHoja}");
                                }
                            }

                            bool todosVerificados = false;
                            using (var commandVerificarTodos = new MySqlCommand(@"
                                SELECT COUNT(*) as Total,
                                       SUM(CASE WHEN EstadoPreparacionproducto = 5 THEN 1 ELSE 0 END) as Verificados
                                FROM detallepedidostienda_bodega
                                WHERE IdConsolidado = @IdPedido 
                                AND NoHoja = @NoHoja", connection, transaction))
                            {
                                commandVerificarTodos.Parameters.AddWithValue("@IdPedido", idPedido);
                                commandVerificarTodos.Parameters.AddWithValue("@NoHoja", hojaSeleccionada.NoHoja);

                                using (var reader = commandVerificarTodos.ExecuteReader())
                                {
                                    if (reader.Read())
                                    {
                                        int total = reader.GetInt32("Total");
                                        int verificados = reader.GetInt32("Verificados");

                                        todosVerificados = (total > 0 && total == verificados);
                                    }
                                }
                            }

                            // Confirmar la transacción
                            transaction.Commit();

                            string mensajeRegistro;
                            if (registroExistente)
                            {
                                mensajeRegistro = $"Producto actualizado correctamente en el inventario. Cantidad total registrada: {cantidadTotal}";
                            }
                            else
                            {
                                if (inventarioExistente)
                                {
                                    mensajeRegistro = $"Producto registrado correctamente en el inventario";
                                }
                                else
                                {
                                    mensajeRegistro = $"Inventario #{idInventario} creado y producto registrado correctamente";
                                }
                            }

                            // Actualizar UI
                            Device.BeginInvokeOnMainThread(async () => {
                                InventarioIdLabel.Text = $"Inventario #: {idInventario}";
                                InventarioIdLabel.IsVisible = true;

                                // Deshabilitar controles después de crear el inventario
                                SucursalPicker.IsEnabled = false;
                                DepartamentoPicker.IsEnabled = false;

                                // Si todos los productos de la hoja actual están verificados, verificar el pedido completo
                                if (todosVerificados)
                                {
                                    bool pedidoCompleto = false;

                                    try
                                    {
                                        // Verificar si todas las hojas del pedido están completadas
                                        using (var connection2 = new MySqlConnection(connectionString))
                                        {
                                            connection2.Open();
                                            using (var commandVerificarPedido = new MySqlCommand(@"
                                            SELECT 
                                                COUNT(*) as TotalProductos,
                                                SUM(CASE WHEN EstadoPreparacionproducto = 5 THEN 1 ELSE 0 END) as ProductosVerificados
                                            FROM detallepedidostienda_bodega
                                            WHERE IdConsolidado = @IdPedido", connection2))
                                            {
                                                commandVerificarPedido.Parameters.AddWithValue("@IdPedido", idPedido);

                                                using (var reader = commandVerificarPedido.ExecuteReader())
                                                {
                                                    if (reader.Read())
                                                    {
                                                        int totalProductos = reader.GetInt32("TotalProductos");
                                                        int productosVerificados = reader.GetInt32("ProductosVerificados");

                                                        pedidoCompleto = (totalProductos > 0 && totalProductos == productosVerificados);
                                                    }
                                                }
                                            }

                                            // Si el pedido está completo, actualizar su estado a 7
                                            if (pedidoCompleto)
                                            {
                                                using (var commandActualizarPedido = new MySqlCommand(@"
                                                    UPDATE pedidostienda_bodega
                                                    SET Estado = 7
                                                    WHERE IdPedidos = @IdPedido", connection2))
                                                {
                                                    commandActualizarPedido.Parameters.AddWithValue("@IdPedido", idPedido);
                                                    commandActualizarPedido.ExecuteNonQuery();

                                                    Console.WriteLine($"Pedido {idPedido} completado. Estado actualizado a 7.");
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        await DisplayAlert("Error", "Error al verificar estado del pedido: " + ex.Message, "OK");
                                    }

                                    // Recargar la lista de productos y hojas para asegurar que todo esté actualizado
                                    await CargarProductos(hojaSeleccionada.NoHoja);
                                    CargarHojas();

                                    // Mostrar mensaje de éxito para el producto actual
                                    await DisplayAlert("Éxito", mensajeRegistro, "OK");

                                    // Mostrar mensaje apropiado según el estado del pedido
                                    if (pedidoCompleto)
                                    {
                                        await DisplayAlert("¡Pedido Completado!", $"Has verificado todos los productos del pedido {idPedido}. Regresando a la pantalla principal.", "OK");

                                        // Regresar a la pantalla principal (Home)
                                        await Navigation.PopToRootAsync();
                                    }
                                    else
                                    {
                                        // Mostrar mensaje de hoja completada y regresar a la pantalla anterior
                                        await DisplayAlert("¡Hoja Completada!", $"Has verificado todos los productos de la Hoja {hojaSeleccionada.NoHoja} del pedido {idPedido}. Regresando a la pantalla anterior.", "OK");

                                        // Regresar a la pantalla anterior
                                        await Navigation.PopAsync();
                                    }
                                }
                                else
                                {
                                    // Limpiar los campos para el siguiente escaneo
                                    UpcEntry.Text = string.Empty;
                                    CantidadEntry.Text = string.Empty;

                                    // Recargar la lista de productos para reflejar los cambios
                                    await CargarProductos(hojaSeleccionada.NoHoja);

                                    // Recargar las hojas para actualizar los contadores
                                    CargarHojas();

                                    // Devolver el foco al campo de UPC para continuar escaneando
                                    UpcEntry.Focus();

                                    // Mostrar mensaje de éxito
                                    await DisplayAlert("Éxito", mensajeRegistro, "OK");
                                }
                            });
                        }
                            catch (Exception ex)
                            {
                                // Revertir la transacción en caso de error
                                transaction.Rollback();
                                LoadingOverlay.IsVisible = false; // Ocultar overlay para mostrar mensaje de error
                                await DisplayAlert("Error", "Error al procesar el producto: " + ex.Message, "OK");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoadingOverlay.IsVisible = false; // Ocultar overlay para mostrar mensaje de error
                    await DisplayAlert("Error", "Error de conexión: " + ex.Message, "OK");
                }
            }
            catch (Exception ex)
            {
                // Capturar cualquier excepción no manejada
                LoadingOverlay.IsVisible = false; // Asegurar que se oculte el overlay
                await DisplayAlert("Error", $"Error inesperado: {ex.Message}", "OK");
            }
            finally
            {
                // Asegurarnos de que el overlay siempre se oculte al finalizar
                LoadingOverlay.IsVisible = false;
            }
        }


        private async Task CargarSucursales()
        {
            LoadingOverlay.IsVisible = true;

            try
            {
                // Conexión a la primera base de datos
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new MySqlCommand(@"
                        SELECT
                            sucursales.Id, 
                            sucursales.Nombre, 
                            sucursales.IdSucursalSincronizacion
                        FROM
                            sucursales
                        WHERE
                            sucursales.ModeloSucursal = 1
                        ORDER BY
                            sucursales.Nombre ASC", connection))
                    {
                        var sucursales = new List<SucursalInfo>();
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                sucursales.Add(new SucursalInfo
                                {
                                    Id = reader.GetInt32("Id"),
                                    Nombre = reader.GetString("Nombre"),
                                    IdSucursalSincronizacion = reader.GetInt32("IdSucursalSincronizacion")
                                });
                            }
                        }

                        Device.BeginInvokeOnMainThread(() => {
                            SucursalPicker.ItemsSource = sucursales;
                            SucursalPicker.ItemDisplayBinding = new Binding("DisplayText");
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar sucursales: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        private async void SucursalPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (SucursalPicker.SelectedItem is SucursalInfo sucursalSeleccionada)
            {
                LoadingOverlay.IsVisible = true;
                try
                {
                    // Conexión a la segunda base de datos
                    string connectionString2 = "Server=172.30.1.27;Database=dbsucursales;User ID=compras;Password=bode.24451988;";
                    using (var connection = new MySqlConnection(connectionString2))
                    {
                        connection.Open();

                        // Buscar RazonSocial por IdSucursalSincronizacion
                        using (var command = new MySqlCommand(@"
                    SELECT RazonSocial 
                    FROM sucursales
                    WHERE idSucursal = @IdSucursalSincronizacion", connection))
                        {
                            command.Parameters.AddWithValue("@IdSucursalSincronizacion", sucursalSeleccionada.IdSucursalSincronizacion);

                            var razonSocialId = command.ExecuteScalar()?.ToString();

                            if (!string.IsNullOrEmpty(razonSocialId))
                            {
                                // Buscar NombreRazon
                                using (var commandRazon = new MySqlCommand(@"
                            SELECT NombreRazon 
                            FROM razonessociales 
                            WHERE Id = @RazonSocialId", connection))
                                {
                                    commandRazon.Parameters.AddWithValue("@RazonSocialId", razonSocialId);

                                    var nombreRazon = commandRazon.ExecuteScalar()?.ToString();

                                    if (!string.IsNullOrEmpty(nombreRazon))
                                    {
                                        sucursalSeleccionada.RazonSocial = razonSocialId;
                                        sucursalSeleccionada.NombreRazon = nombreRazon;

                                        Device.BeginInvokeOnMainThread(() => {
                                            RazonSocialLabel.Text = $"Razón Social: {nombreRazon}";
                                            RazonSocialLabel.IsVisible = true;
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", "Error al obtener razón social: " + ex.Message, "OK");
                }
                finally
                {
                    LoadingOverlay.IsVisible = false;
                }
            }
        }
        private async Task CargarDepartamentos()
        {
            LoadingOverlay.IsVisible = true;

            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new MySqlCommand(@"
                SELECT
                    departamentos.Id,
                    departamentos.Nombre
                FROM
                    departamentos", connection))
                    {
                        var departamentos = new List<DepartamentoInfo>();
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                departamentos.Add(new DepartamentoInfo
                                {
                                    Id = reader.GetInt32("Id"),
                                    Nombre = reader.GetString("Nombre")
                                });
                            }
                        }

                        Device.BeginInvokeOnMainThread(() => {
                            DepartamentoPicker.ItemsSource = departamentos;
                            DepartamentoPicker.ItemDisplayBinding = new Binding("DisplayText");
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar departamentos: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }
        private void OnDepartamentoSelected(object sender, EventArgs e)
        {
            // Puedes implementar cualquier lógica que necesites cuando se selecciona un departamento
            if (DepartamentoPicker.SelectedItem is DepartamentoInfo departamentoSeleccionado)
            {
                // Hacer algo con el departamento seleccionado
            }
        }
        private async Task<bool> VerificarPermisoAcceso()
        {
            try
            {
                LoadingOverlay.IsVisible = true;

                // Si hay hoja preseleccionada, verificar permiso para esa hoja específica
                if (hojaPreseleccionada.HasValue)
                {
                    return await VerificarPermisoHoja(hojaPreseleccionada.Value);
                }

                // Si no hay hoja preseleccionada, verificar si hay al menos una hoja a la que tenga acceso
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    // Contar cuántas hojas tiene el pedido
                    using (var commandCount = new MySqlCommand(
                        "SELECT COUNT(DISTINCT NoHoja) FROM detallepedidostienda_bodega WHERE IdConsolidado = @IdPedido",
                        connection))
                    {
                        commandCount.Parameters.AddWithValue("@IdPedido", idPedido);
                        int totalHojas = Convert.ToInt32(commandCount.ExecuteScalar());

                        // Si solo hay una hoja, verificar permiso para esa hoja
                        if (totalHojas == 1)
                        {
                            // Obtener el número de esa única hoja
                            using (var commandHoja = new MySqlCommand(
                                "SELECT DISTINCT NoHoja FROM detallepedidostienda_bodega WHERE IdConsolidado = @IdPedido LIMIT 1",
                                connection))
                            {
                                commandHoja.Parameters.AddWithValue("@IdPedido", idPedido);
                                int noHoja = Convert.ToInt32(commandHoja.ExecuteScalar());

                                // Verificar permiso para esta hoja
                                return await VerificarPermisoHoja(noHoja);
                            }
                        }
                    }
                }

                // Si hay múltiples hojas, permitir acceso y la selección se validará después
                return true;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al verificar permisos: " + ex.Message, "OK");
                return false;
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        // Método para verificar permiso para una hoja específica
        private async Task<bool> VerificarPermisoHoja(int noHoja)
        {
            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    // Verificar si la hoja ya tiene un inventario asignado
                    using (var command = new MySqlCommand(@"
                SELECT 
                    h.IdInventario, 
                    i.IdUsuarios
                FROM 
                    HistorialPreparacionPedidos AS h
                    LEFT JOIN inventarios AS i ON h.IdInventario = i.idInventarios
                WHERE 
                    h.IdPedido = @IdPedido AND
                    h.Nohoja = @NoHoja", connection))
                    {
                        command.Parameters.AddWithValue("@IdPedido", idPedido);
                        command.Parameters.AddWithValue("@NoHoja", noHoja);

                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read() && !reader.IsDBNull(reader.GetOrdinal("IdInventario")))
                            {
                                // La hoja ya tiene inventario asignado
                                int idInventario = reader.GetInt32("IdInventario");
                                int idUsuarioInventario = reader.GetInt32("IdUsuarios");

                                // Verificar si el inventario pertenece al usuario actual
                                if (idUsuarioInventario != App.IdUsuarioActual)
                                {
                                    // El inventario pertenece a otro usuario
                                    reader.Close();

                                    // Mostrar mensaje y denegar acceso
                                    await DisplayAlert("Acceso Denegado",
                                        $"La hoja {noHoja} ya tiene asignado el inventario #{idInventario} por otro usuario. No tiene permisos para modificarlo.",
                                        "OK");

                                    return false;
                                }
                            }
                        }
                    }
                }

                // Si no tiene inventario o pertenece al usuario actual, permitir acceso
                return true;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al verificar permisos de hoja: " + ex.Message, "OK");
                return false;
            }
        }
        private void OnEntryFocused(object sender, FocusEventArgs e)
        {
            // Establecer el Entry activo cuando recibe el foco
            _activeEntry = (Entry)sender;
        }
        private void OnNumericButtonClicked(object sender, EventArgs e)
        {
            try
            {
                Vibration.Vibrate(TimeSpan.FromMilliseconds(40));
            }
            catch (FeatureNotSupportedException)
            {
                // La vibración no es soportada en este dispositivo
            }
            catch (Exception)
            {
                // Manejar cualquier otra excepción
            }
            // Verificar que haya un Entry activo
            if (_activeEntry == null) return;

            // Obtener el texto del botón presionado
            var button = (Button)sender;
            string number = button.Text;

            // Agregar el número al texto del Entry activo
            _activeEntry.Text += number;
        }

        private void OnBackspaceButtonClicked(object sender, EventArgs e)
        {
            try
            {
                Vibration.Vibrate(TimeSpan.FromMilliseconds(40));
            }
            catch (FeatureNotSupportedException)
            {
                // La vibración no es soportada en este dispositivo
            }
            catch (Exception)
            {
                // Manejar cualquier otra excepción
            }
            // Verificar que haya un Entry activo
            if (_activeEntry == null || string.IsNullOrEmpty(_activeEntry.Text)) return;

            // Eliminar el último carácter
            _activeEntry.Text = _activeEntry.Text.Substring(0, _activeEntry.Text.Length - 1);
        }

        private void OnClearButtonClicked(object sender, EventArgs e)
        {
            try
            {
                Vibration.Vibrate(TimeSpan.FromMilliseconds(40));
            }
            catch (FeatureNotSupportedException)
            {
                // La vibración no es soportada en este dispositivo
            }
            catch (Exception)
            {
                // Manejar cualquier otra excepción
            }
            // Verificar que haya un Entry activo
            if (_activeEntry == null) return;

            // Limpiar el texto del Entry activo
            _activeEntry.Text = string.Empty;
        }
        private async void OnProductoSelected(object sender, SelectionChangedEventArgs e)
        {
            // Obtener el producto seleccionado
            if (e.CurrentSelection.FirstOrDefault() is DetalleProducto producto)
            {
                try
                {
                    // Verificar si este producto tiene IdPreparacion = 2
                    string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

                    int estadoPreparacion = 0;
                    int noHoja = ((HojaInfo)HojaPicker.SelectedItem).NoHoja;

                    // Primer bloque de conexión para verificar el estado
                    using (var connection = new MySqlConnection(connectionString))
                    {
                        connection.Open();

                        using (var command = new MySqlCommand(@"
                            SELECT EstadoPreparacionproducto 
                            FROM detallepedidostienda_bodega 
                            WHERE IdConsolidado = @IdPedido 
                            AND NoHoja = @NoHoja 
                            AND UPC = @UPC", connection))
                        {
                            command.Parameters.AddWithValue("@IdPedido", idPedido);
                            command.Parameters.AddWithValue("@NoHoja", noHoja);
                            command.Parameters.AddWithValue("@UPC", producto.UpcPaquete);

                            var resultado = command.ExecuteScalar();
                            if (resultado != null)
                            {
                                estadoPreparacion = Convert.ToInt32(resultado);
                            }
                        }
                    } // La primera conexión se cierra aquí

                    // Si es IdPreparacion = 2
                    if (estadoPreparacion == 2)
                    {
                        // Mostrar diálogo de confirmación
                        bool confirmar = await DisplayAlert("Confirmación",
                            $"¿Desea confirmar el chequeo del producto '{producto.Descripcion}' sin afectar inventario?",
                            "Sí, confirmar", "No");

                        if (confirmar)
                        {
                            LoadingOverlay.IsVisible = true;

                            try
                            {
                                // Nueva conexión para la actualización
                                using (var connection = new MySqlConnection(connectionString))
                                {
                                    connection.Open();

                                    // Actualizar el estado del producto a verificado (5) sin afectar inventario
                                    using (var updateCommand = new MySqlCommand(@"
                                        UPDATE detallepedidostienda_bodega 
                                        SET EstadoPreparacionproducto = 5,
                                            Chequeo = '0'
                                        WHERE IdConsolidado = @IdPedido 
                                        AND NoHoja = @NoHoja 
                                        AND UPC = @UPC", connection))
                                    {
                                        updateCommand.Parameters.AddWithValue("@IdPedido", idPedido);
                                        updateCommand.Parameters.AddWithValue("@NoHoja", noHoja);
                                        updateCommand.Parameters.AddWithValue("@UPC", producto.UpcPaquete);

                                        updateCommand.ExecuteNonQuery();

                                        // Actualizar estado del producto en la lista local
                                        producto.EstaVerificado = true;

                                        // Verificar si todos los productos de la hoja están verificados
                                        bool todosVerificados = false;
                                        using (var commandVerificarTodos = new MySqlCommand(@"
                                            SELECT COUNT(*) as Total,
                                                SUM(CASE WHEN EstadoPreparacionproducto = 5 THEN 1 ELSE 0 END) as Verificados
                                            FROM detallepedidostienda_bodega
                                            WHERE IdConsolidado = @IdPedido 
                                            AND NoHoja = @NoHoja", connection))
                                        {
                                            commandVerificarTodos.Parameters.AddWithValue("@IdPedido", idPedido);
                                            commandVerificarTodos.Parameters.AddWithValue("@NoHoja", noHoja);

                                            using (var reader = commandVerificarTodos.ExecuteReader())
                                            {
                                                if (reader.Read())
                                                {
                                                    int total = reader.GetInt32("Total");
                                                    int verificados = reader.GetInt32("Verificados");

                                                    todosVerificados = (total > 0 && total == verificados);
                                                }
                                            }
                                        }

                                        // Si todos están verificados, verificar el pedido completo
                                        bool pedidoCompleto = false;
                                        if (todosVerificados)
                                        {
                                            using (var commandVerificarPedido = new MySqlCommand(@"
                                                SELECT 
                                                    COUNT(*) as TotalProductos,
                                                    SUM(CASE WHEN EstadoPreparacionproducto = 5 THEN 1 ELSE 0 END) as ProductosVerificados
                                                FROM detallepedidostienda_bodega
                                                WHERE IdConsolidado = @IdPedido", connection))
                                            {
                                                commandVerificarPedido.Parameters.AddWithValue("@IdPedido", idPedido);

                                                using (var reader = commandVerificarPedido.ExecuteReader())
                                                {
                                                    if (reader.Read())
                                                    {
                                                        int totalProductos = reader.GetInt32("TotalProductos");
                                                        int productosVerificados = reader.GetInt32("ProductosVerificados");

                                                        pedidoCompleto = (totalProductos > 0 && totalProductos == productosVerificados);
                                                    }
                                                }
                                            }

                                            // Si el pedido está completo, actualizar su estado a 7
                                            if (pedidoCompleto)
                                            {
                                                using (var commandActualizarPedido = new MySqlCommand(@"
                                                    UPDATE pedidostienda_bodega
                                                    SET Estado = 7
                                                    WHERE IdPedidos = @IdPedido", connection))
                                                {
                                                    commandActualizarPedido.Parameters.AddWithValue("@IdPedido", idPedido);
                                                    commandActualizarPedido.ExecuteNonQuery();
                                                }
                                            }
                                        }

                                        await DisplayAlert("Éxito",
                                            $"Producto '{producto.Descripcion}' verificado correctamente sin afectar inventario.",
                                            "OK");

                                        // Recargar datos según el estado de verificación
                                        await CargarProductos(noHoja);
                                        CargarHojas();

                                        // Si todos los productos de la hoja están verificados
                                        if (todosVerificados)
                                        {
                                            if (pedidoCompleto)
                                            {
                                                await DisplayAlert("¡Pedido Completado!",
                                                    $"Has verificado todos los productos del pedido {idPedido}. Regresando a la pantalla principal.",
                                                    "OK");
                                                await Navigation.PopToRootAsync();
                                            }
                                            else
                                            {
                                                await DisplayAlert("¡Hoja Completada!",
                                                    $"Has verificado todos los productos de la Hoja {noHoja} del pedido {idPedido}. Regresando a la pantalla anterior.",
                                                    "OK");
                                                await Navigation.PopAsync();
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                await DisplayAlert("Error", $"Error al actualizar el producto: {ex.Message}", "OK");
                            }
                            finally
                            {
                                LoadingOverlay.IsVisible = false;
                            }
                        }
                    }
                    else
                    {
                        // Si no es IdPreparacion = 2, mostrar un mensaje informativo
                        await DisplayAlert("Información",
                            "Este producto debe ser verificado normalmente mediante el escaneo de su UPC.",
                            "OK");
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Error al verificar el producto: {ex.Message}", "OK");
                }

                // Siempre quitar la selección después de procesar
                ProductosCollection.SelectedItem = null;
            }
        }
    }
    public class StringNotEmptyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return !string.IsNullOrEmpty(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}