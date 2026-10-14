using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using WMS_Movil.Servicio;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ListadoTarimas : ContentPage
    {
        private readonly int idPedido;
        private int? tarimaEnProceso = null;

        public class TarimaItem
        {
            public int NumeroTarima { get; set; }
            public int TotalProductos { get; set; }
            public int ProductosVerificados { get; set; }
            public int TotalFardos { get; set; }
            public int? IdInventario { get; set; }
            public int? IdUsuarioChequeo { get; set; }
            public string NombreUsuarioChequeo { get; set; }
            public string NombrePreparador { get; set; }
            public string TextoInventario => IdInventario.HasValue ? $"Inventario #{IdInventario}" : "Sin inventario";
            public bool TienePreparador => !string.IsNullOrEmpty(NombrePreparador);
            public string TextoPreparador => TienePreparador ? $"🧑‍🏭 Preparó: {NombrePreparador}" : "";
            public bool EstaCompleta => TotalProductos > 0 && TotalProductos == ProductosVerificados;
            public bool EstaBloqueada { get; set; }
            public bool EsLaTarimaActiva { get; set; }

            // Propiedades para UI
            public string EstadoTexto
            {
                get
                {
                    if (EstaCompleta)
                        return "✓ Completada";
                    else if (EstaBloqueada)
                        return "🔒 Bloqueada por otro usuario";
                    else if (EsLaTarimaActiva)
                        return "🔄 En proceso";
                    else
                        return "📋 Disponible";
                }
            }

            public Color ColorEstado
            {
                get
                {
                    if (EstaCompleta)
                        return Color.FromHex("#48BB78");
                    else if (EstaBloqueada)
                        return Color.FromHex("#E53E3E");
                    else if (EsLaTarimaActiva)
                        return Color.FromHex("#4299E1");
                    else
                        return Color.FromHex("#718096");
                }
            }

            public Color ColorBorde
            {
                get
                {
                    if (EstaCompleta)
                        return Color.FromHex("#48BB78");
                    else if (EstaBloqueada)
                        return Color.FromHex("#E53E3E");
                    else if (EsLaTarimaActiva)
                        return Color.FromHex("#4299E1");
                    else
                        return Color.FromHex("#E2E8F0");
                }
            }

            public string IconoEstado
            {
                get
                {
                    if (EstaCompleta)
                        return "✓";
                    else if (EstaBloqueada)
                        return "🔒";
                    else if (EsLaTarimaActiva)
                        return "🔄";
                    else
                        return "📦";
                }
            }

            public bool TieneUsuarioAsignado => !string.IsNullOrEmpty(NombreUsuarioChequeo);
            public string TextoUsuario => TieneUsuarioAsignado ? $"👤 Asignado a: {NombreUsuarioChequeo}" : "";

            // Barra de progreso (mismo criterio visual que las hojas pendientes en Home)
            public double PorcentajeProgreso => TotalProductos > 0 ? (double)ProductosVerificados / TotalProductos * 100 : 0;
            public string ColorProgreso => PorcentajeProgreso >= 75 ? "#48BB78" :
                                          (PorcentajeProgreso >= 50 ? "#4C51BF" :
                                          (PorcentajeProgreso >= 25 ? "#ECC94B" : "#E53E3E"));
            public double ProgresoWidth => 750 * (PorcentajeProgreso / 100);
        }

        public ListadoTarimas(int idPedido)
        {
            InitializeComponent();
            this.idPedido = idPedido;
            PedidoLabel.Text = $"Pedido #{idPedido}";
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            CargarInfoPedido();
            CargarTarimas();
        }

        private async void CargarInfoPedido()
        {
            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                string nombreEmpresa = "";
                string nombreDepartamento = "";

                await System.Threading.Tasks.Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(connectionString))
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

        private async void CargarTarimas()
        {
            LoadingOverlay.IsVisible = true;

            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    // Verificar si el usuario tiene una tarima en proceso
                    using (var cmdVerificar = new MySqlCommand(@"
                SELECT t.NoTarima
                FROM TarimasInventario t
                WHERE t.IdPedido = @IdPedido 
                AND t.IdUsuarioChequeo = @IdUsuario
                AND t.FechaHoraFin IS NULL
                AND t.EstadoChequeo = 0
                LIMIT 1", connection))
                    {
                        cmdVerificar.Parameters.AddWithValue("@IdPedido", idPedido);
                        cmdVerificar.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);

                        var resultado = cmdVerificar.ExecuteScalar();
                        if (resultado != null && resultado != DBNull.Value)
                        {
                            tarimaEnProceso = Convert.ToInt32(resultado);

                            // Mostrar mensaje
                            MensajeActivoFrame.IsVisible = true;
                            MensajeActivoLabel.Text = $"⚠ Tiene la Tarima #{tarimaEnProceso} en proceso";
                        }
                        else
                        {
                            MensajeActivoFrame.IsVisible = false;
                        }
                    }

                    // Productos repartidos en varias tarimas (NoTarima = 0 en el detalle):
                    // se suman a cada tarima donde tienen una parte.
                    var repartidos = RepartoTarimas.ObtenerRepartidos(connection, null, idPedido);

                    // Cargar TODAS las tarimas que salieron de este pedido (sin importar quién las rechequea).
                    // LEFT JOIN para no perder las tarimas que solo tienen productos repartidos.
                    using (var command = new MySqlCommand(@"
                SELECT
                    t.NoTarima,
                    t.IdInventario,
                    COUNT(d.UPC) as TotalProductos,
                    COALESCE(SUM(CASE WHEN d.EstadoPreparacionproducto = 5 THEN 1 ELSE 0 END), 0) as ProductosVerificados,
                    COALESCE(SUM(d.CantConfirmada), 0) as TotalFardos,
                    t.IdUsuarioChequeo,
                    u.NombreCompleto AS NombreUsuarioChequeo,
                    t.NombreUsuario AS NombrePreparador,
                    t.FechaHoraFin,
                    t.EstadoChequeo
                FROM TarimasInventario t
                LEFT JOIN detallepedidostienda_bodega d
                    ON t.IdPedido = d.IdConsolidado
                    AND t.NoTarima = d.NoTarima
                LEFT JOIN usuarios u ON t.IdUsuarioChequeo = u.Id
                WHERE t.IdPedido = @IdPedido
                GROUP BY t.NoTarima, t.IdInventario, t.IdUsuarioChequeo, u.NombreCompleto, t.NombreUsuario, t.FechaHoraFin, t.EstadoChequeo
                ORDER BY t.NoTarima", connection))
                    {
                        command.Parameters.AddWithValue("@IdPedido", idPedido);

                        var tarimas = new List<TarimaItem>();

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int numeroTarima = reader.GetInt32("NoTarima");
                                int totalProductos = Convert.ToInt32(reader["TotalProductos"]);
                                int productosVerificados = Convert.ToInt32(reader["ProductosVerificados"]);
                                int fardosRepartidos = 0;

                                foreach (var rep in repartidos)
                                {
                                    int cantidad = rep.CantidadEn(numeroTarima);
                                    if (cantidad <= 0) continue;
                                    totalProductos++;
                                    fardosRepartidos += cantidad;
                                    if (rep.EstadoPreparacion == 5) productosVerificados++;
                                }

                                // Tarimas sin productos (p.ej. la activa recién abierta) no se listan
                                if (totalProductos == 0)
                                {
                                    continue;
                                }

                                bool estaCompleta = totalProductos == productosVerificados;

                                // No agregar tarimas completadas a la lista
                                if (estaCompleta)
                                {
                                    continue;
                                }

                                // IdUsuarioChequeo y FechaHoraFin quedan en NULL hasta que alguien
                                // empieza el rechequeo de esa tarima (se llenan en DetalleHojarechequeo).
                                int? idUsuarioChequeo = reader.IsDBNull(reader.GetOrdinal("IdUsuarioChequeo"))
                                    ? (int?)null
                                    : reader.GetInt32("IdUsuarioChequeo");
                                bool rechequeoSinTerminar = reader.IsDBNull(reader.GetOrdinal("FechaHoraFin"));

                                // Bloqueada solo si OTRO usuario ya la tiene en proceso (sin terminar)
                                bool bloqueadaPorOtroUsuario = idUsuarioChequeo.HasValue
                                    && idUsuarioChequeo.Value != App.IdUsuarioActual
                                    && rechequeoSinTerminar;

                                int? idInventario = reader.IsDBNull(reader.GetOrdinal("IdInventario"))
                                    ? (int?)null
                                    : Convert.ToInt32(reader["IdInventario"]);
                                int totalFardos = (reader.IsDBNull(reader.GetOrdinal("TotalFardos"))
                                    ? 0
                                    : Convert.ToInt32(reader["TotalFardos"])) + fardosRepartidos;

                                var tarima = new TarimaItem
                                {
                                    NumeroTarima = numeroTarima,
                                    TotalProductos = totalProductos,
                                    ProductosVerificados = productosVerificados,
                                    TotalFardos = totalFardos,
                                    IdInventario = idInventario,
                                    IdUsuarioChequeo = idUsuarioChequeo,
                                    NombreUsuarioChequeo = reader.IsDBNull(reader.GetOrdinal("NombreUsuarioChequeo"))
                                        ? null
                                        : reader.GetString("NombreUsuarioChequeo"),
                                    NombrePreparador = reader.IsDBNull(reader.GetOrdinal("NombrePreparador"))
                                        ? null
                                        : reader.GetString("NombrePreparador"),
                                    EsLaTarimaActiva = tarimaEnProceso.HasValue && tarimaEnProceso.Value == numeroTarima,
                                    EstaBloqueada = bloqueadaPorOtroUsuario
                                };

                                tarimas.Add(tarima);
                            }
                        }

                        Device.BeginInvokeOnMainThread(() =>
                        {
                            TarimasCollection.ItemsSource = tarimas;
                            bool sinTarimas = tarimas.Count == 0;
                            TarimasCollection.IsVisible = !sinTarimas;
                            SinTarimasFrame.IsVisible = sinTarimas;
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar tarimas: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        private async void OnTarimaSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is TarimaItem tarimaSeleccionada)
            {
                try
                {
                    // Si la tarima está completada
                    if (tarimaSeleccionada.EstaCompleta)
                    {
                        await DisplayAlert("Tarima Completada",
                            $"La Tarima #{tarimaSeleccionada.NumeroTarima} ya ha sido verificada completamente.",
                            "OK");
                        TarimasCollection.SelectedItem = null;
                        return;
                    }

                    // Si la tarima está bloqueada por otro usuario
                    if (tarimaSeleccionada.EstaBloqueada)
                    {
                        await DisplayAlert("Tarima Bloqueada",
                            $"La Tarima #{tarimaSeleccionada.NumeroTarima} está siendo procesada por {tarimaSeleccionada.NombreUsuarioChequeo}.",
                            "OK");
                        TarimasCollection.SelectedItem = null;
                        return;
                    }

                    // Si tiene una tarima en proceso y NO es la que está seleccionando
                    if (tarimaEnProceso.HasValue && tarimaEnProceso.Value != tarimaSeleccionada.NumeroTarima)
                    {
                        await DisplayAlert("Tarima en Proceso",
                            $"Debe completar la Tarima #{tarimaEnProceso} antes de iniciar otra.",
                            "OK");
                        TarimasCollection.SelectedItem = null;
                        return;
                    }

                    // Si pasa todas las validaciones, navegar a DetalleHojarechequeo
                    LoadingOverlay.IsVisible = true;
                    await Navigation.PushAsync(new DetalleHojarechequeo(idPedido, tarimaSeleccionada.NumeroTarima));
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", "Error al procesar la tarima: " + ex.Message, "OK");
                }
                finally
                {
                    TarimasCollection.SelectedItem = null;
                    LoadingOverlay.IsVisible = false;
                }
            }
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}