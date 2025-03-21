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
    public class UbicacionModel
    {
        public int Id { get; set; }
        public int Rack { get; set; }
        public int Nivel { get; set; }
        public string Descripcion { get; set; }
        public int NoHoja { get; set; }
        public int TotalUPC { get; set; }
        public int LineasCompletadas { get; set; }
        public int TotalLineas { get; set; }
        public string IdUsuarioPreparador { get; set; }
        public string NombrePreparador { get; set; }
        public bool EstaEnPreparacion => !string.IsNullOrEmpty(IdUsuarioPreparador);
        public string EstadoPreparacion => EstaEnPreparacion ?
            $"En preparación por: {NombrePreparador}" :
            "Disponible";
        public Color ColorEstado => EstaEnPreparacion ?
            Color.FromHex("#E53E3E") :
            Color.FromHex("#48BB78");

        public double ProgressPercentage => (double)LineasCompletadas / TotalLineas;
        public string ProgressText => $"{(ProgressPercentage * 100):F0}%";
        public double ProgressWidth => 750 * ProgressPercentage;
        public string ProgressColor
        {
            get
            {
                if (ProgressPercentage == 1.0)
                    return "#48BB78";
                else if (ProgressPercentage > 0.5)
                    return "#4C51BF";
                else
                    return "#E53E3E";
            }
        }
    }
    public class MotivoPreparacion
    {
        public int IdPreparacion { get; set; }
        public string DescripcionMotivo { get; set; }
    }
    public class HojaInfo
    {
        public int NoHoja { get; set; }
        public string Niveles { get; set; }
        public string Descripcion { get; set; }
        public bool EsPuntos { get; set; }
        public string DisplayText => $"Hoja {NoHoja} / {Descripcion} Nivel= {(EsPuntos ? "Puntos" : Niveles)}";
    }
    public partial class DetalleHojaunbicaciones : ContentPage
    {
        private readonly int idPedido;
        private readonly int? hojaPreseleccionada;
        private List<UbicacionModel> todasLasUbicaciones;
        public DetalleHojaunbicaciones(int idPedido, int? hojaPreseleccionada)
        {
            InitializeComponent();
            this.idPedido = idPedido;
            this.hojaPreseleccionada = hojaPreseleccionada;
            CargarUbicaciones();
        }
        private async void CargarUbicaciones()
        {
            LoadingOverlay.IsVisible = true;

            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};Allow User Variables=True;";
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    List<HojaInfo> hojas = new List<HojaInfo>();
                    // Primero, obtener la información de las hojas
                    using (var commandHojas = new MySqlCommand(@"
                SELECT 
                    d.NoHoja,
                    u.Descripcion,
                    GROUP_CONCAT(DISTINCT u.Nivel ORDER BY u.Nivel SEPARATOR ', ') as Niveles,
                    CASE 
                        WHEN EXISTS (
                            SELECT 1 
                            FROM detallepedidostienda_bodega d2 
                            WHERE d2.IdConsolidado = d.IdConsolidado 
                            AND d2.NoHoja = d.NoHoja 
                        ) THEN 1
                        ELSE 0
                    END as EsPuntos
                FROM detallepedidostienda_bodega d
                INNER JOIN ubicacionesbodega u ON d.IdUbicacionBodega = u.Id
                WHERE d.IdConsolidado = @idPedido
                GROUP BY d.NoHoja", connection))
                    {
                        commandHojas.Parameters.AddWithValue("@idPedido", idPedido);
                        using (var reader = commandHojas.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                hojas.Add(new HojaInfo
                                {
                                    NoHoja = reader.GetInt32("NoHoja"),
                                    Niveles = reader.GetString("Niveles"),
                                    EsPuntos = reader.GetBoolean("EsPuntos"),
                                    Descripcion = reader. GetString("Descripcion")
                                });
                            }
                        }

                        // Actualizar el Picker con la información de las hojas
                        Device.BeginInvokeOnMainThread(() => {
                            HojaPicker.ItemsSource = hojas;
                            HojaPicker.ItemDisplayBinding = new Binding("DisplayText");
                        });
                    }

                    // Cargar las ubicaciones
                    using (var command = new MySqlCommand(@"
                SELECT 
                    ub.Id, 
                    CAST(ub.Rack AS SIGNED) AS Rack, 
                    CAST(ub.Nivel AS SIGNED) AS Nivel, 
                    ub.Descripcion, 
                    IFNULL(dpb.NoHoja, 1) AS NoHoja,
                    COUNT(DISTINCT pp.UPC) AS TotalUPC,
                    IFNULL(SUM(CASE WHEN dpb.EstadoPreparacionproducto NOT IN (0,4) THEN 1 ELSE 0 END), 0) AS LineasCompletadas,
                    COUNT(dpb.Id) AS TotalLineas,
                    hp.IdUsuario,
                    IFNULL(CONCAT(u.Nombres, ' ', u.Apellidos), '') AS NombrePreparador
                FROM ubicacionesbodega ub
                INNER JOIN detallepedidostienda_bodega dpb ON ub.Id = dpb.IdUbicacionBodega
                INNER JOIN productospaquetes pp ON pp.UPCPaquete = dpb.UPC
                LEFT JOIN productos p ON p.UPC = pp.UPC
                LEFT JOIN HistorialPreparacionPedidos hp ON dpb.IdConsolidado = hp.IdPedido AND dpb.NoHoja = hp.NoHoja 
                    AND hp.FechaHorafinalizo IS NULL
                LEFT JOIN usuarios u ON hp.IdUsuario = u.Id
                WHERE dpb.IdConsolidado = @idPedido
                GROUP BY ub.Id, ub.Rack, ub.Nivel, ub.Descripcion, dpb.NoHoja, hp.IdUsuario, NombrePreparador
                ORDER BY dpb.NoHoja, ub.Rack, ub.Nivel;", connection))
                    {
                        command.Parameters.AddWithValue("@idPedido", idPedido);
                        using (var reader = command.ExecuteReader())
                        {
                            var ubicaciones = new List<UbicacionModel>();
                            while (reader.Read())
                            {
                                try
                                {
                                    var ubicacion = new UbicacionModel
                                    {
                                        Id = Convert.ToInt32(reader["Id"]),
                                        Rack = Convert.ToInt32(reader["Rack"]),
                                        Nivel = Convert.ToInt32(reader["Nivel"]),
                                        Descripcion = Convert.ToString(reader["Descripcion"]) ?? string.Empty,
                                        NoHoja = Convert.ToInt32(reader["NoHoja"]),
                                        TotalUPC = Convert.ToInt32(reader["TotalUPC"]),
                                        LineasCompletadas = Convert.ToInt32(reader["LineasCompletadas"]),
                                        TotalLineas = Convert.ToInt32(reader["TotalLineas"]),
                                        IdUsuarioPreparador = reader.IsDBNull(reader.GetOrdinal("IdUsuario")) ?
                                            string.Empty :
                                            Convert.ToString(reader["IdUsuario"]) ?? string.Empty,
                                        NombrePreparador = reader.IsDBNull(reader.GetOrdinal("NombrePreparador")) ?
                                            string.Empty :
                                            Convert.ToString(reader["NombrePreparador"]).Trim()
                                    };
                                    ubicaciones.Add(ubicacion);
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error al procesar ubicación: {ex.Message}");
                                    continue;
                                }
                            }

                            todasLasUbicaciones = ubicaciones;
                            var hojasDisponibles = ubicaciones
                                .Where(u => !u.EstaEnPreparacion || u.IdUsuarioPreparador == App.IdUsuarioActual.ToString())
                                .Select(u => u.NoHoja)
                                .Distinct()
                                .OrderBy(h => h)
                                .ToList();

                            if (hojasDisponibles.Any())
                            {
                                Device.BeginInvokeOnMainThread(() => {
                                    if (hojaPreseleccionada.HasValue && hojasDisponibles.Contains(hojaPreseleccionada.Value))
                                    {
                                        HojaPicker.SelectedItem = hojas.FirstOrDefault(h => h.NoHoja == hojaPreseleccionada.Value);
                                    }
                                    else
                                    {
                                        HojaPicker.SelectedIndex = 0;
                                    }

                                    var hojaSeleccionada = ((HojaInfo)HojaPicker.SelectedItem).NoHoja;
                                    var ubicacionesFiltradas = todasLasUbicaciones
                                        .Where(u => u.NoHoja == hojaSeleccionada)
                                        .ToList();
                                    UbicacionesCollection.ItemsSource = ubicacionesFiltradas;
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar ubicaciones: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        private void OnHojaSelected(object sender, EventArgs e)
        {
            if (HojaPicker.SelectedItem is HojaInfo hojaSeleccionada)
            {
                var ubicacionesFiltradas = todasLasUbicaciones
                    .Where(u => u.NoHoja == hojaSeleccionada.NoHoja)
                    .ToList();
                UbicacionesCollection.ItemsSource = ubicacionesFiltradas;

                // Actualizar el estado de la hoja seleccionada
                if (ubicacionesFiltradas.Any())
                {
                    var primerUbicacion = ubicacionesFiltradas.First();
                    EstadoLabel.Text = primerUbicacion.EstadoPreparacion;
                    EstadoLabel.TextColor = primerUbicacion.ColorEstado;
                }
            }
        }

        private async void OnUbicacionSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is UbicacionModel ubicacion)
            {
                try
                {
                    LoadingOverlay.IsVisible = true;
                    string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};Allow User Variables=True;";

                    // Variable para almacenar información de preparación pendiente
                    int? pedidoPendiente = null;
                    int? hojaPendiente = null;

                    // Primera conexión para verificar preparaciones en curso por otros usuarios
                    using (var connection = new MySqlConnection(connectionString))
                    {
                        connection.Open();
                        using (var command = new MySqlCommand(@"
                            SELECT u.Usuario, u.Nombres, u.Apellidos
                            FROM HistorialPreparacionPedidos h
                            INNER JOIN usuarios u ON h.IdUsuario = u.Id
                            WHERE h.IdPedido = @IdPedido 
                            AND h.NoHoja = @NoHoja 
                            AND h.FechaHorafinalizo IS NULL
                            AND h.IdUsuario != @IdUsuario", connection))
                        {
                            command.Parameters.AddWithValue("@IdPedido", idPedido);
                            command.Parameters.AddWithValue("@NoHoja", ubicacion.NoHoja);
                            command.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);

                            using (var reader = command.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    string nombrePreparador = $"{reader.GetString("Nombres")} {reader.GetString("Apellidos")}";
                                    await DisplayAlert("Aviso", $"Esta hoja está siendo preparada por: {nombrePreparador}", "OK");
                                    UbicacionesCollection.SelectedItem = null;
                                    return;
                                }
                            }
                        }

                        // Verificar preparaciones pendientes del usuario actual
                        using (var command = new MySqlCommand(@"
                                SELECT h1.IdPedido, h1.NoHoja 
                                FROM HistorialPreparacionPedidos h1
                                WHERE h1.IdUsuario = @IdUsuario 
                                AND h1.FechaHorafinalizo IS NULL
                                AND NOT (h1.IdPedido = @IdPedidoActual AND h1.NoHoja = @NoHojaActual)  -- Excluir la hoja actual
                                AND NOT EXISTS (
                                    SELECT 1 
                                    FROM HistorialPreparacionPedidos h2
                                    WHERE h2.IdPedido = @IdPedidoActual 
                                    AND h2.NoHoja = @NoHojaActual
                                    AND h2.IdUsuario = @IdUsuario
                                    AND h2.FechaHorafinalizo IS NULL
                                )
                                LIMIT 1", connection))
                        {
                            command.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                            command.Parameters.AddWithValue("@IdPedidoActual", idPedido);
                            command.Parameters.AddWithValue("@NoHojaActual", ubicacion.NoHoja);
                            using (var reader = command.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    pedidoPendiente = reader.GetInt32("IdPedido");
                                    hojaPendiente = reader.GetInt32("NoHoja");
                                }
                            }
                        }
                    }

                    // Procesar la lógica de preparación pendiente
                    if (pedidoPendiente.HasValue && hojaPendiente.HasValue)
                    {
                        if (pedidoPendiente.Value == idPedido && hojaPendiente.Value == ubicacion.NoHoja)
                        {
                            await Navigation.PushAsync(new DetallePedidoPendientes(idPedido, ubicacion.Id, ubicacion.NoHoja));
                        }
                        else
                        {
                            bool autorizacion = await DisplayAlert("Aviso",
                                $"Tiene una preparación pendiente del Pedido #{pedidoPendiente}, Hoja {hojaPendiente}. " +
                                "¿Desea solicitar autorización para iniciar una nueva preparación?", "Sí", "No");

                            if (autorizacion)
                            {
                                string usuario = await DisplayPromptAsync("Autorización", "Usuario del supervisor:", keyboard: Keyboard.Default);
                                if (!string.IsNullOrEmpty(usuario))
                                {
                                    string password = await DisplayPromptAsync("Autorización", "Contraseña:", keyboard: Keyboard.Default);
                                    if (!string.IsNullOrEmpty(password))
                                    {
                                        // Nueva conexión para verificar autorización
                                        using (var connection = new MySqlConnection(connectionString))
                                        {
                                            connection.Open();
                                            using (var cmdAuth = new MySqlCommand(@"
                                                SELECT COUNT(*) 
                                                FROM usuarios 
                                                WHERE Usuario = @Usuario 
                                                AND Password = @Password 
                                                AND Activo = 1 
                                                AND IdNivel = 11", connection))
                                            {
                                                cmdAuth.Parameters.AddWithValue("@Usuario", usuario);
                                                cmdAuth.Parameters.AddWithValue("@Password", password);

                                                int result = Convert.ToInt32(cmdAuth.ExecuteScalar());

                                                if (result > 0)
                                                {
                                                    // Nueva conexión para actualizar el historial
                                                    using (var connUpdate = new MySqlConnection(connectionString))
                                                    {
                                                        connUpdate.Open();
                                                        using (var cmdInsert = new MySqlCommand(@"
                                                            UPDATE HistorialPreparacionPedidos 
                                                            SET IdUsuario = @IdUsuario,
                                                                FechaHoraInicio = NOW()
                                                            WHERE IdPedido = @IdPedido 
                                                            AND NoHoja = @NoHoja", connUpdate))
                                                        {
                                                            cmdInsert.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                                                            cmdInsert.Parameters.AddWithValue("@IdPedido", idPedido);
                                                            cmdInsert.Parameters.AddWithValue("@NoHoja", ubicacion.NoHoja);
                                                            cmdInsert.ExecuteNonQuery();
                                                        }
                                                    }

                                                    await Navigation.PushAsync(new DetallePedidoPendientes(idPedido, ubicacion.Id, ubicacion.NoHoja));
                                                }
                                                else
                                                {
                                                    await DisplayAlert("Error", "Credenciales inválidas o usuario sin autorización.", "OK");
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // Si no hay preparación pendiente, registrar nueva preparación
                        using (var connection = new MySqlConnection(connectionString))
                        {
                            connection.Open();
                            using (var command = new MySqlCommand(@"
                                UPDATE HistorialPreparacionPedidos 
                                SET IdUsuario = @IdUsuario,
                                    FechaHoraInicio = NOW()
                                WHERE IdPedido = @IdPedido 
                                AND NoHoja = @NoHoja", connection))
                            {
                                command.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                                command.Parameters.AddWithValue("@IdPedido", idPedido);
                                command.Parameters.AddWithValue("@NoHoja", ubicacion.NoHoja);
                                command.ExecuteNonQuery();
                            }
                        }

                        await Navigation.PushAsync(new DetallePedidoPendientes(idPedido, ubicacion.Id, ubicacion.NoHoja));
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", "Error al procesar la solicitud: " + ex.Message, "OK");
                }
                finally
                {
                    LoadingOverlay.IsVisible = false;
                    UbicacionesCollection.SelectedItem = null;
                }
            }
        }
    }

    public class Grouping<K, T> : List<T>
    {
        public K Key { get; private set; }

        public Grouping(K key, IEnumerable<T> items)
        {
            Key = key;
            AddRange(items);
        }
    }
}