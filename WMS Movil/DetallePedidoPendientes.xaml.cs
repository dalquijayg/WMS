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
    public class ProductoDetalle
    {
        public string UPC { get; set; }
        public string Descripcion { get; set; }
        public int Cantidad { get; set; }
        public int UnidadesxFardo { get; set; }
        public int Existencia { get; set; }
        public bool EstaConfirmado { get; set; }
        public bool ExistenciaBaja => Existencia < Cantidad;
        public bool SinExistencia => Existencia <= 0;
        public string ColorExistencia
        {
            get
            {
                if (SinExistencia) return "#E53E3E"; // Rojo para sin existencia
                if (ExistenciaBaja) return "#ECC94B"; // Amarillo para existencia baja
                return "#2D3748"; // Color normal
            }
        }
        public string MensajeExistencia
        {
            get
            {
                if (SinExistencia) return "Sin existencia";
                if (ExistenciaBaja) return "Existencia insuficiente";
                return "";
            }
        }
    }
    public class UbicacionBodega
    {
        public int Id { get; set; }
        public int Rack { get; set; }
        public int Nivel { get; set; }
        public string Descripcion { get; set; }
    }
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            bool isConfirmed = (bool)value;
            return isConfirmed ? "#48BB78" : "#e2e8f0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class DetallePedidoPendientes : ContentPage
	{
        private readonly int idPedido;
        private readonly int idUbicacion;
        private readonly int noHoja;
        private List<ProductoDetalle> productos;
        private string nombreCompleto;
        private bool isUpcFieldActive = true;
        private Entry _activeEntry;

        public DetallePedidoPendientes(int idPedido, int idUbicacion, int noHoja)
        {
            InitializeComponent();
            this.idPedido = idPedido;
            this.idUbicacion = idUbicacion;
            this.noHoja = noHoja;
            ConfigurarEntradas();
            _=CargarProductos();
            UpcEntry.Focused += OnEntryFocused;
            CantidadEntry.Focused += OnEntryFocused;
        }
        
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
                    // Primero verificamos si es numérico
                    if (long.TryParse(upcIngresado, out long upcNumerico))
                    {
                        // Aplicamos el formato D13 (13 dígitos con ceros a la izquierda)
                        upcIngresado = upcNumerico.ToString("D13");
                        UpcEntry.Text = upcIngresado; // Actualiza el campo con el formato correcto
                    }

                    // Verificar si el UPC existe en la lista de productos
                    var productoEncontrado = productos?.FirstOrDefault(p => p.UPC == upcIngresado);
                    if (productoEncontrado != null)
                    {
                        // Si existe, enfocar el campo de cantidad
                        CantidadEntry.Focus();
                        return;
                    }

                    // Si no existe, buscar en la tabla productospaquetes
                    string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

                    string upcPaquete = null;

                    using (var connection = new MySqlConnection(connectionString))
                    {
                        try
                        {
                            connection.Open();

                            // Verificar que la conexión está abierta
                            if (connection.State != System.Data.ConnectionState.Open)
                            {
                                await DisplayAlert("Error", "No se pudo establecer conexión con la base de datos", "OK");
                                return;
                            }

                            // Imprimir el UPC para depuración
                            System.Diagnostics.Debug.WriteLine($"Buscando UPC: '{upcIngresado}'");

                            using (var command = new MySqlCommand("SELECT UPCPaquete FROM productospaquetes WHERE Upc = @UpcIngresado LIMIT 1", connection))
                            {
                                command.Parameters.Add(new MySqlParameter("@UpcIngresado", upcIngresado));

                                // Usar ExecuteReader en lugar de ExecuteScalar para mejor control
                                using (var reader = command.ExecuteReader())
                                {
                                    if (reader.HasRows && reader.Read())
                                    {
                                        // Comprobar que la columna existe
                                        if (!reader.IsDBNull(0))
                                        {
                                            upcPaquete = reader.GetString(0);
                                            // Aplicar formato de 13 dígitos también al UPC del paquete
                                            if (long.TryParse(upcPaquete, out long paqueteNumerico))
                                            {
                                                upcPaquete = paqueteNumerico.ToString("D13");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (MySqlException mySqlEx)
                        {
                            await DisplayAlert("Error de base de datos", $"Error MySQL: {mySqlEx.Number} - {mySqlEx.Message}", "OK");
                            return;
                        }
                        catch (Exception dbEx)
                        {
                            await DisplayAlert("Error", $"Error al consultar la base de datos: {dbEx.Message}", "OK");
                            return;
                        }
                    }

                    // Procesar el resultado después de cerrar la conexión
                    if (!string.IsNullOrEmpty(upcPaquete))
                    {
                        // Verificar si el UPCPaquete existe en la lista de productos
                        var productoPaquete = productos?.FirstOrDefault(p => p.UPC == upcPaquete);

                        if (productoPaquete != null)
                        {
                            // Si existe, mostrar mensaje informativo y actualizar el campo
                            UpcEntry.Text = upcPaquete;
                            CantidadEntry.Focus();
                        }
                        else
                        {
                            // El UPCPaquete no está en la lista de productos
                            UpcEntry.Text = string.Empty;
                            UpcEntry.Focus();
                        }
                    }
                    else
                    {
                        // No se encontró en productospaquetes
                        await DisplayAlert("Error", "El UPC escaneado no existe en el detalle ni se encontró en productospaquetes", "OK");
                        UpcEntry.Text = string.Empty;
                        UpcEntry.Focus();
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Error al procesar el UPC: {ex.Message}\n{ex.StackTrace}", "OK");
                    UpcEntry.Text = string.Empty;
                    UpcEntry.Focus();
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
                // Aquí puedes agregar lógica adicional cuando se complete la cantidad
                // Por ejemplo, validar la cantidad o activar el botón de confirmar
            };
        }
        private async Task CargarProductos()
        {
            LoadingOverlay.IsVisible = true;

            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};Allow User Variables=True;";
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new MySqlCommand(@"
                    SELECT
                        detallepedidostienda_bodega.UPC,
                        detallepedidostienda_bodega.Descripcion,
                        detallepedidostienda_bodega.Cantidad,
                        productospaquetes.Cantidad AS UnidadesxFardo,
                        productos.Existencia / productospaquetes.Cantidad AS Existencia,
                        CASE WHEN detallepedidostienda_bodega.EstadoPreparacionproducto NOT IN(0,4) THEN 1 ELSE 0 END AS EstaConfirmado
                    FROM
                        detallepedidostienda_bodega
                        INNER JOIN pedidostienda_bodega ON detallepedidostienda_bodega.IdConsolidado = pedidostienda_bodega.IdPedidos
                        INNER JOIN productospaquetes ON detallepedidostienda_bodega.UPC = productospaquetes.UPCPaquete
                        INNER JOIN productos ON productospaquetes.Upc = productos.Upc
                    WHERE
                        detallepedidostienda_bodega.IdConsolidado = @IdPedido 
                        AND detallepedidostienda_bodega.IdUbicacionBodega = @IdUbicacion
                        AND detallepedidostienda_bodega.NoHoja = @NoHoja
                        AND detallepedidostienda_bodega.EstadoPreparacionproducto IN (0,4)", connection))
                    {
                        command.Parameters.AddWithValue("@IdPedido", idPedido);
                        command.Parameters.AddWithValue("@IdUbicacion", idUbicacion);
                        command.Parameters.AddWithValue("@NoHoja", noHoja);

                        using (var reader = command.ExecuteReader())
                        {
                            productos = new List<ProductoDetalle>();
                            while (reader.Read())
                            {
                                productos.Add(new ProductoDetalle
                                {
                                    UPC = reader.GetString("UPC"),
                                    Descripcion = reader.GetString("Descripcion"),
                                    Cantidad = reader.GetInt32("Cantidad"),
                                    UnidadesxFardo = reader.GetInt32("UnidadesxFardo"),
                                    Existencia = reader.GetInt32("Existencia"),
                                    EstaConfirmado = reader.GetInt32("EstaConfirmado") == 1
                                });
                            }
                            ProductosCollection.ItemsSource = productos;
                        }
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
        private async void OnConfirmarClicked(object sender, EventArgs e)
        {
            try
            {
                LoadingOverlay.IsVisible = true;

                // Validar que haya un UPC ingresado y aplicar el mismo procesamiento que en ConfigurarEntradas
                string upcIngresado = UpcEntry.Text?.Trim();
                if (string.IsNullOrEmpty(upcIngresado))
                {
                    await DisplayAlert("Error", "Por favor ingrese un UPC", "OK");
                    UpcEntry.Focus();
                    return;
                }

                // Formatear con ceros a la izquierda si es numérico
                if (long.TryParse(upcIngresado, out long upcNumerico))
                {
                    upcIngresado = upcNumerico.ToString("D13");
                    UpcEntry.Text = upcIngresado;
                }

                // Verificar si el UPC existe en la lista de productos
                var producto = productos?.FirstOrDefault(p => p.UPC == upcIngresado);

                // Si no existe en la lista, buscar en productospaquetes
                if (producto == null)
                {
                    bool encontrado = false;

                    try
                    {
                        string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

                        using (var connection = new MySqlConnection(connectionString))
                        {
                            connection.Open();

                            using (var command = new MySqlCommand("SELECT UPCPaquete FROM productospaquetes WHERE Upc = @UpcIngresado LIMIT 1", connection))
                            {
                                command.Parameters.Add(new MySqlParameter("@UpcIngresado", upcIngresado));

                                using (var reader = command.ExecuteReader())
                                {
                                    if (reader.HasRows && reader.Read() && !reader.IsDBNull(0))
                                    {
                                        string upcPaquete = reader.GetString(0);

                                        if (long.TryParse(upcPaquete, out long paqueteNumerico))
                                        {
                                            upcPaquete = paqueteNumerico.ToString("D13");
                                        }

                                        // Verificar si el UPCPaquete existe en la lista de productos
                                        var productoPaquete = productos?.FirstOrDefault(p => p.UPC == upcPaquete);

                                        if (productoPaquete != null)
                                        {
                                            await DisplayAlert("Información",
                                                $"Se encontró el código UPC {upcIngresado} en la tabla productospaquetes.\nSe utilizará el UPCPaquete: {upcPaquete}",
                                                "OK");

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

                    // Si no se encontró el UPC ni en la lista ni en productospaquetes
                    if (!encontrado)
                    {
                        await DisplayAlert("Error", "El UPC no existe en el detalle ni se encontró en productospaquetes", "OK");
                        UpcEntry.Text = string.Empty;
                        UpcEntry.Focus();
                        return;
                    }
                }

                // Validar que haya una cantidad ingresada
                if (!int.TryParse(CantidadEntry.Text, out int cantidadIngresada))
                {
                    await DisplayAlert("Error", "Por favor ingrese una cantidad válida", "OK");
                    CantidadEntry.Focus();
                    return;
                }

                // Verificar si la cantidad es diferente
                bool confirmarCantidad = true;
                if (cantidadIngresada != producto.Cantidad)
                {
                    confirmarCantidad = await DisplayAlert("Confirmación",
                        $"La cantidad ingresada ({cantidadIngresada}) es diferente a la solicitada ({producto.Cantidad}). ¿Está seguro de confirmar esta cantidad?",
                        "Sí", "No");
                }

                if (confirmarCantidad)
                {
                    // Código original para actualizar la base de datos
                    string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                    using (var connection = new MySqlConnection(connectionString))
                    {
                        connection.Open();
                        using (var command = new MySqlCommand(@"
                        UPDATE detallepedidostienda_bodega 
                        SET CantConfirmada = @CantConfirmada,
                            FechaPreparo = CURDATE(),
                            Fechahorapreparo = NOW(),
                            EstadoPreparacionproducto = '1',
                            IdUsuariopreparo = @Idusuario
                        WHERE IdConsolidado = @IdPedido 
                        AND UPC = @UPC
                        AND NoHoja = @NoHoja", connection))
                        {
                            command.Parameters.AddWithValue("@CantConfirmada", cantidadIngresada);
                            command.Parameters.AddWithValue("@IdPedido", idPedido);
                            command.Parameters.AddWithValue("@UPC", upcIngresado);
                            command.Parameters.AddWithValue("@NoHoja", noHoja);
                            command.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);

                            command.ExecuteNonQuery();
                        }
                    }

                    // Recargar los productos para actualizar la vista
                    await CargarProductos();
                    await VerificarFinalizacionUbicacion();

                    // Limpiar y preparar para el siguiente producto
                    UpcEntry.Text = string.Empty;
                    CantidadEntry.Text = string.Empty;
                    UpcEntry.Focus();
                }
                else
                {
                    // Si no confirma, limpiar solo la cantidad y enfocar el campo
                    CantidadEntry.Text = string.Empty;
                    CantidadEntry.Focus();
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al confirmar producto: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        private async void ProductosCollection_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is ProductoDetalle producto)
            {
                try
                {
                    // Cargar motivos
                    List<MotivoPreparacion> motivos = new List<MotivoPreparacion>();
                    string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

                    using (var connection = new MySqlConnection(connectionString))
                    {
                        connection.Open();
                        using (var command = new MySqlCommand("SELECT IdPreparacion, DescripcionMotivo FROM TiposMotivosPendiente WHERE Mostrar = 1", connection))
                        {
                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    motivos.Add(new MotivoPreparacion
                                    {
                                        IdPreparacion = reader.GetInt32("IdPreparacion"),
                                        DescripcionMotivo = reader.GetString("DescripcionMotivo")
                                    });
                                }
                            }
                        }
                    }

                    // Crear lista de opciones para el usuario
                    string[] opciones = motivos.Select(m => m.DescripcionMotivo).ToArray();
                    string resultado = await DisplayActionSheet("Seleccione un motivo", "Cancelar", null, opciones);

                    if (resultado != "Cancelar" && resultado != null)
                    {
                        // Obtener el IdPreparacion seleccionado
                        var motivoSeleccionado = motivos.First(m => m.DescripcionMotivo == resultado);

                        // Si el IdPreparacion es 4, mostrar listado de ubicaciones
                        if (motivoSeleccionado.IdPreparacion == 4)
                        {
                            List<UbicacionBodega> ubicaciones = new List<UbicacionBodega>();
                            using (var connection = new MySqlConnection(connectionString))
                            {
                                connection.Open();
                                using (var command = new MySqlCommand(@"
                            SELECT
                                Id,
                                Rack,
                                Nivel,
                                Descripcion
                            FROM
                                ubicacionesbodega", connection))
                                {
                                    using (var reader = command.ExecuteReader())
                                    {
                                        while (reader.Read())
                                        {
                                            ubicaciones.Add(new UbicacionBodega
                                            {
                                                Id = reader.GetInt32("Id"),
                                                Rack = reader.GetInt32("Rack"),
                                                Nivel = reader.GetInt32("Nivel"),
                                                Descripcion = reader.GetString("Descripcion")
                                            });
                                        }
                                    }
                                }
                            }

                            // Mostrar ubicaciones en ActionSheet
                            string[] opcionesUbicacion = ubicaciones.Select(u =>
                                $"Rack: {u.Rack}, Nivel: {u.Nivel} - {u.Descripcion}").ToArray();
                            string ubicacionSeleccionada = await DisplayActionSheet(
                                "Seleccione ubicación", "Cancelar", null, opcionesUbicacion);

                            if (ubicacionSeleccionada != "Cancelar" && ubicacionSeleccionada != null)
                            {
                                var ubicacion = ubicaciones[Array.IndexOf(opcionesUbicacion, ubicacionSeleccionada)];

                                using (var connection = new MySqlConnection(connectionString))
                                {
                                    connection.Open();
                                    using (var transaction = connection.BeginTransaction())
                                    {
                                        try
                                        {
                                            // Obtener nueva hoja
                                            int nuevaHoja;
                                            using (var cmdHoja = new MySqlCommand(@"
                                        SELECT 
                                            COALESCE(
                                                (
                                                    SELECT NoHoja
                                                    FROM detallepedidostienda_bodega
                                                    WHERE IdConsolidado = @IdPedido
                                                    AND EstadoPreparacionproducto = 4
                                                    GROUP BY NoHoja
                                                    HAVING COUNT(*) < 25
                                                    ORDER BY NoHoja DESC
                                                    LIMIT 1
                                                ),
                                                (SELECT MAX(NoHoja) + 1 FROM detallepedidostienda_bodega WHERE IdConsolidado = @IdPedido)
                                            ) as HojaAsignar", connection, transaction))
                                            {
                                                cmdHoja.Parameters.AddWithValue("@IdPedido", idPedido);
                                                nuevaHoja = Convert.ToInt32(cmdHoja.ExecuteScalar());
                                            }

                                            // Actualizar el producto
                                            using (var updateCommand = new MySqlCommand(@"
                                            UPDATE detallepedidostienda_bodega 
                                                SET CantConfirmada = 0,
                                                    FechaPreparo = CURDATE(),
                                                    Fechahorapreparo = NOW(),
                                                    EstadoPreparacionproducto = @IdPreparacion,
                                                    IdUbicacionBodega = @IdUbicacion,
                                                    NoHoja = @NoHoja,
                                                    IdUsuariopreparo = @IdUsuario
                                                WHERE IdConsolidado = @IdPedido 
                                                AND UPC = @UPC
                                                AND NoHoja = @HojaActual", connection, transaction))
                                            {
                                                updateCommand.Parameters.AddWithValue("@IdPreparacion", motivoSeleccionado.IdPreparacion);
                                                updateCommand.Parameters.AddWithValue("@IdUbicacion", ubicacion.Id);
                                                updateCommand.Parameters.AddWithValue("@IdPedido", idPedido);
                                                updateCommand.Parameters.AddWithValue("@UPC", producto.UPC);
                                                updateCommand.Parameters.AddWithValue("@NoHoja", nuevaHoja);
                                                updateCommand.Parameters.AddWithValue("@HojaActual", noHoja);
                                                updateCommand.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                                                updateCommand.ExecuteNonQuery();
                                            }

                                            // Actualizar totales en hoja original
                                            using (var updateOriginalCommand = new MySqlCommand(@"
                                        UPDATE HistorialPreparacionPedidos 
                                        SET TotalSKUs = (
                                                SELECT COUNT(DISTINCT UPC)
                                                FROM detallepedidostienda_bodega
                                                WHERE IdConsolidado = @IdPedido AND NoHoja = @HojaOriginal
                                            ),
                                            TotalFardos = (
                                                SELECT SUM(Cantidad)
                                                FROM detallepedidostienda_bodega
                                                WHERE IdConsolidado = @IdPedido AND NoHoja = @HojaOriginal
                                            )
                                        WHERE IdPedido = @IdPedido AND NoHoja = @HojaOriginal", connection, transaction))
                                            {
                                                updateOriginalCommand.Parameters.AddWithValue("@IdPedido", idPedido);
                                                updateOriginalCommand.Parameters.AddWithValue("@HojaOriginal", noHoja);
                                                updateOriginalCommand.ExecuteNonQuery();
                                            }

                                            // Verificar si necesitamos crear o actualizar el registro en HistorialPreparacionPedidos para la nueva hoja
                                            using (var checkCommand = new MySqlCommand(@"
                                        SELECT COUNT(*) 
                                        FROM HistorialPreparacionPedidos 
                                        WHERE IdPedido = @IdPedido AND NoHoja = @NoHoja", connection, transaction))
                                            {
                                                checkCommand.Parameters.AddWithValue("@IdPedido", idPedido);
                                                checkCommand.Parameters.AddWithValue("@NoHoja", nuevaHoja);
                                                int exists = Convert.ToInt32(checkCommand.ExecuteScalar());

                                                if (exists == 0)
                                                {
                                                    // Insertar nuevo registro
                                                    using (var insertCommand = new MySqlCommand(@"
                                                INSERT INTO HistorialPreparacionPedidos (IdPedido, NoHoja, Sucursal, TotalSKUs, TotalFardos)
                                                SELECT 
                                                    @IdPedido,
                                                    @NoHoja,
                                                    p.NombreEmpresa,
                                                    COUNT(DISTINCT d.UPC),
                                                    SUM(d.Cantidad)
                                                FROM detallepedidostienda_bodega d
                                                INNER JOIN pedidostienda_bodega p ON p.IdPedidos = d.IdConsolidado
                                                WHERE d.IdConsolidado = @IdPedido AND d.NoHoja = @NoHoja
                                                GROUP BY p.NombreEmpresa", connection, transaction))
                                                    {
                                                        insertCommand.Parameters.AddWithValue("@IdPedido", idPedido);
                                                        insertCommand.Parameters.AddWithValue("@NoHoja", nuevaHoja);
                                                        insertCommand.ExecuteNonQuery();
                                                    }
                                                }
                                                else
                                                {
                                                    // Actualizar registro existente
                                                    using (var updateNewCommand = new MySqlCommand(@"
                                                UPDATE HistorialPreparacionPedidos 
                                                SET TotalSKUs = (
                                                        SELECT COUNT(DISTINCT UPC)
                                                        FROM detallepedidostienda_bodega
                                                        WHERE IdConsolidado = @IdPedido AND NoHoja = @NoHoja
                                                    ),
                                                    TotalFardos = (
                                                        SELECT SUM(Cantidad)
                                                        FROM detallepedidostienda_bodega
                                                        WHERE IdConsolidado = @IdPedido AND NoHoja = @NoHoja
                                                    )
                                                WHERE IdPedido = @IdPedido AND NoHoja = @NoHoja", connection, transaction))
                                                    {
                                                        updateNewCommand.Parameters.AddWithValue("@IdPedido", idPedido);
                                                        updateNewCommand.Parameters.AddWithValue("@NoHoja", nuevaHoja);
                                                        updateNewCommand.ExecuteNonQuery();
                                                    }
                                                }
                                            }

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
                        }
                        else
                        {
                            // Comportamiento normal para otros motivos
                            using (var connection = new MySqlConnection(connectionString))
                            {
                                connection.Open();
                                using (var command = new MySqlCommand(@"
                                UPDATE detallepedidostienda_bodega 
                                    SET CantConfirmada = 0,
                                        FechaPreparo = CURDATE(),
                                        Fechahorapreparo = NOW(),
                                        EstadoPreparacionproducto = @IdPreparacion,
                                        IdUsuariopreparo = @IdUsuario
                                    WHERE IdConsolidado = @IdPedido 
                                    AND UPC = @UPC
                                    AND NoHoja = @NoHoja", connection))
                                {
                                    command.Parameters.AddWithValue("@IdPreparacion", motivoSeleccionado.IdPreparacion);
                                    command.Parameters.AddWithValue("@IdPedido", idPedido);
                                    command.Parameters.AddWithValue("@UPC", producto.UPC);
                                    command.Parameters.AddWithValue("@NoHoja", noHoja);
                                    command.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                                    command.ExecuteNonQuery();
                                }
                            }
                        }

                        // Recargar los productos
                        await CargarProductos();
                        await VerificarFinalizacionUbicacion();
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", "Error al procesar el motivo: " + ex.Message, "OK");
                }
                finally
                {
                    // Limpiar la selección
                    ProductosCollection.SelectedItem = null;
                }
            }
        }
        private async Task VerificarFinalizacionUbicacion()
        {
            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    // Primero verificamos si quedan productos pendientes en la ubicación actual
                    using (var command = new MySqlCommand(@"
                        SELECT COUNT(*) 
                        FROM detallepedidostienda_bodega d
                        WHERE d.IdConsolidado = @IdPedido 
                        AND d.NoHoja = @NoHoja
                        AND d.IdUbicacionBodega = @IdUbicacion
                        AND d.EstadoPreparacionproducto IN (0,4)", connection))
                    {
                        command.Parameters.AddWithValue("@IdPedido", idPedido);
                        command.Parameters.AddWithValue("@NoHoja", noHoja);
                        command.Parameters.AddWithValue("@IdUbicacion", idUbicacion);

                        int pendientesUbicacion = Convert.ToInt32(command.ExecuteScalar());

                        if (pendientesUbicacion == 0)
                        {
                            // Verificar si toda la hoja del pedido está completa
                            using (var cmdHoja = new MySqlCommand(@"
                                SELECT COUNT(*) 
                                FROM detallepedidostienda_bodega 
                                WHERE IdConsolidado = @IdPedido 
                                AND NoHoja = @NoHoja 
                                AND EstadoPreparacionproducto = 0", connection))
                            {
                                cmdHoja.Parameters.AddWithValue("@IdPedido", idPedido);
                                cmdHoja.Parameters.AddWithValue("@NoHoja", noHoja);

                                int pendientesHoja = Convert.ToInt32(cmdHoja.ExecuteScalar());

                                if (pendientesHoja == 0)
                                {
                                    // Actualizar la fecha de finalización en HistorialPreparacionPedidos
                                    using (var cmdUpdate = new MySqlCommand(@"
                                        UPDATE HistorialPreparacionPedidos 
                                        SET FechaHorafinalizo = NOW()
                                        WHERE IdPedido = @IdPedido 
                                        AND NoHoja = @NoHoja", connection))
                                    {
                                        cmdUpdate.Parameters.AddWithValue("@IdPedido", idPedido);
                                        cmdUpdate.Parameters.AddWithValue("@NoHoja", noHoja);

                                        cmdUpdate.ExecuteNonQuery();

                                        // Verificar si todo el pedido está completo
                                        // Modificación aquí: Ahora verifica que todos los productos tengan estado 1, 2 o 3
                                        using (var cmdPedido = new MySqlCommand(@"
                                            SELECT 
                                                CASE 
                                                    WHEN EXISTS (
                                                        SELECT 1 
                                                        FROM detallepedidostienda_bodega 
                                                        WHERE IdConsolidado = @IdPedido 
                                                        AND EstadoPreparacionproducto NOT IN (1, 2, 3)
                                                    ) THEN 0
                                                    ELSE 1
                                                END as PedidoCompleto", connection))
                                        {
                                            cmdPedido.Parameters.AddWithValue("@IdPedido", idPedido);

                                            bool pedidoCompleto = Convert.ToBoolean(cmdPedido.ExecuteScalar());

                                            if (pedidoCompleto)
                                            {
                                                // Actualizar el estado del pedido a 6
                                                using (var cmdUpdatePedido = new MySqlCommand(@"
                                                    UPDATE pedidostienda_bodega 
                                                    SET Estado = 6 
                                                    WHERE IdPedidos = @IdPedido", connection))
                                                {
                                                    cmdUpdatePedido.Parameters.AddWithValue("@IdPedido", idPedido);
                                                    cmdUpdatePedido.ExecuteNonQuery();

                                                    await DisplayAlert("¡Completado!", "El pedido ha sido finalizado completamente", "OK");
                                                    await Navigation.PopToRootAsync();
                                                    await Navigation.PushAsync(new Home(nombreCompleto));
                                                    return;
                                                }
                                            }
                                            else
                                            {
                                                await DisplayAlert("¡Completado!", "Ha finalizado toda la hoja del pedido", "OK");
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    await DisplayAlert("¡Completado!", "Ha finalizado todos los productos de esta ubicación", "OK");
                                }
                            }

                            await Navigation.PopAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al verificar finalización: " + ex.Message, "OK");
            }
        }
        private void OnEntryFocused(object sender, FocusEventArgs e)
        {
            // Establecer el Entry activo cuando recibe el foco
            _activeEntry = (Entry)sender;
        }

        private void OnNumericButtonClicked(object sender, EventArgs e)
        {
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
            // Verificar que haya un Entry activo
            if (_activeEntry == null || string.IsNullOrEmpty(_activeEntry.Text)) return;

            // Eliminar el último carácter
            _activeEntry.Text = _activeEntry.Text.Substring(0, _activeEntry.Text.Length - 1);
        }

        private void OnClearButtonClicked(object sender, EventArgs e)
        {
            // Verificar que haya un Entry activo
            if (_activeEntry == null) return;

            // Limpiar el texto del Entry activo
            _activeEntry.Text = string.Empty;
        }
    }
}