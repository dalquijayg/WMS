using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using WMS_Movil.Reportes;
using WMS_Movil.Servicio;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public class HojaPendienteModel
    {
        public int IdPreparacion { get; set; }
        public int IdPedido { get; set; }
        public int NoHoja { get; set; }
        public double PorcentajeProgreso { get; set; }
        public string NombreEmpresa { get; set; }
        public int TotalSkus { get; set; }
        public int TotalFardos { get; set; }
        public string ColorProgreso => PorcentajeProgreso >= 75 ? "#48BB78" :
                                      (PorcentajeProgreso >= 50 ? "#4C51BF" :
                                      (PorcentajeProgreso >= 25 ? "#ECC94B" : "#E53E3E"));
        public double ProgresoWidth => 750 * (PorcentajeProgreso / 100);
    }

    public partial class Home : ContentPage
    {
        private string nombreCompleto;
        private bool _isRefreshing;

        public bool IsRefreshing
        {
            get { return _isRefreshing; }
            set
            {
                _isRefreshing = value;
                OnPropertyChanged();
            }
        }

        public string ResumenPendientes { get; private set; }
        public List<HojaPendienteModel> HojasPendientes { get; private set; }

        private PedidoAsignadoInfo _pedidoBanner;
        private bool _bannerTimerActivo;
        private int _bannerTickCount;

        public Home(string nombreCompleto)
        {
            InitializeComponent();
            this.nombreCompleto = nombreCompleto;
            UserNameLabel.Text = nombreCompleto;
            DateLabel.Text = DateTime.Now.ToString("dddd, d 'de' MMMM 'de' yyyy");
            HojasPendientes = new List<HojaPendienteModel>();

            RefreshView.Refreshing += RefreshView_Refreshing;
            BindingContext = this;
            LoadingOverlay.IsVisible = true;

            PrepFrame.Opacity = 0;
            StatsGrid.Opacity = 0;
            ButtonsStack.Opacity = 0;

            Task.Run(async () =>
            {
                try
                {
                    await Device.InvokeOnMainThreadAsync(() =>
                    {
                        CargarHojasPendientes();
                    });
                }
                finally
                {
                    await Device.InvokeOnMainThreadAsync(async () =>
                    {
                        LoadingOverlay.IsVisible = false;
                        await AnimarElementos();
                    });
                }
            });
        }

        private async Task AnimarElementos()
        {
            await PrepFrame.FadeTo(1, 400, Easing.CubicOut);
            await StatsGrid.FadeTo(1, 400, Easing.CubicOut);
            await ButtonsStack.FadeTo(1, 400, Easing.CubicOut);
            await ButtonsStack.ScaleTo(1.05, 150, Easing.SpringOut);
            await ButtonsStack.ScaleTo(1, 100, Easing.SpringOut);
        }

        private async void RefreshView_Refreshing(object sender, EventArgs e)
        {
            try
            {
                CargarHojasPendientes();
                DateLabel.Text = DateTime.Now.ToString("dddd, d 'de' MMMM 'de' yyyy");
            }
            finally
            {
                await Task.Delay(500);
                RefreshView.IsRefreshing = false;
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            CargarHojasPendientes();
            ActualizarBannerPedidoAsignado();
            IniciarTimerBanner();

            // Tarjetas NFC que quedaron sin configurar (la app se cerró a medias)
            _ = ConfigurarTarjetaNfcPage.ProcesarPendientesAsync(Navigation);

            // Mostrar Mis Pedidos para todos los usuarios
            MisPedidosFrame.IsVisible = true;
            MisChequeosFrame.IsVisible = true;
            UbicacionesFrame.IsVisible = true;
            SalidaFrame.IsVisible = true;
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _bannerTimerActivo = false;
        }

        private void IniciarTimerBanner()
        {
            if (_bannerTimerActivo) return;
            _bannerTimerActivo = true;
            _bannerTickCount = 0;

            Device.StartTimer(TimeSpan.FromSeconds(1), () =>
            {
                if (!_bannerTimerActivo) return false;

                _bannerTickCount++;
                if (_bannerTickCount % 10 == 0)
                {
                    // Recarga automática cada 10s: refresca la lista y el banner de asignado.
                    CargarHojasPendientes();
                    ActualizarBannerPedidoAsignado();
                }
                else
                {
                    ActualizarTextoTiempoBanner();
                }

                return true;
            });
        }

        private void ActualizarBannerPedidoAsignado()
        {
            try
            {
                var pendientes = PedidoMonitorHelper.ObtenerPedidosAsignadosSinIniciar(App.IdUsuarioActual);
                _pedidoBanner = pendientes.FirstOrDefault();

                if (_pedidoBanner == null)
                {
                    BannerPedidoAsignado.IsVisible = false;
                    PrepFrame.Margin = new Thickness(16, -30, 16, 0);
                    return;
                }

                BannerTituloLabel.Text = $"Pedido #{_pedidoBanner.IdPedido} - Hoja {_pedidoBanner.NoHoja} asignado";
                BannerAsignoLabel.Text = string.IsNullOrEmpty(_pedidoBanner.NombreUsuarioAsigno)
                    ? _pedidoBanner.Sucursal
                    : $"Asignado por {_pedidoBanner.NombreUsuarioAsigno} · {_pedidoBanner.Sucursal}";

                BannerPedidoAsignado.IsVisible = true;
                PrepFrame.Margin = new Thickness(16, 10, 16, 0);
                ActualizarTextoTiempoBanner();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al actualizar banner de pedido asignado: {ex.Message}");
            }
        }

        private void ActualizarTextoTiempoBanner()
        {
            if (_pedidoBanner == null) return;

            var transcurrido = DateTime.Now - _pedidoBanner.FechaHoraAsigno;
            if (transcurrido < TimeSpan.Zero) transcurrido = TimeSpan.Zero;

            BannerTiempoLabel.Text = transcurrido.TotalHours >= 1
                ? $"{(int)transcurrido.TotalHours}h {transcurrido.Minutes}m"
                : $"{transcurrido.Minutes}m {transcurrido.Seconds}s";
        }

        private async void OnBannerPedidoAsignadoTapped(object sender, EventArgs e)
        {
            if (_pedidoBanner == null) return;

            try
            {
                await Navigation.PushAsync(new DetallePedidoPendientes(_pedidoBanner.IdPedido, _pedidoBanner.NoHoja));
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al abrir el pedido: " + ex.Message, "OK");
            }
        }

        private async void OnMisPedidosClicked(object sender, EventArgs e)
        {
            try
            {
                await Navigation.PushAsync(new MisPedidos());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al abrir Mis Pedidos: " + ex.Message, "OK");
            }
        }

        private async void CargarHojasPendientes()
        {
            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new MySqlCommand(@"
                SELECT
                    hp.Idpreparo,
                    hp.IdPedido,
                    hp.NoHoja,
                    hp.FechaHoraInicio,
                    SUM(CASE WHEN dpb.EstadoPreparacionproducto NOT IN(0,4,3) THEN 1 ELSE 0 END) * 100.0 / COUNT(*) AS Progreso,
                    pb.NombreEmpresa,
                    COUNT(*) AS TotalSkus,
                    COALESCE(SUM(dpb.Cantidad), 0) AS TotalFardos
                FROM PreparacionPedidos hp
                JOIN detallepedidostienda_bodega dpb ON hp.IdPedido = dpb.IdConsolidado AND hp.NoHoja = dpb.NoHoja
                LEFT JOIN pedidostienda_bodega pb ON hp.IdPedido = pb.IdPedidos
                WHERE hp.IdUsuario = @IdUsuario AND hp.FechaHorafinalizo IS NULL
                GROUP BY hp.Idpreparo, hp.IdPedido, hp.NoHoja, hp.FechaHoraInicio, pb.NombreEmpresa", connection))
                    {
                        command.Parameters.AddWithValue("@IdUsuario", App.IdUsuarioActual);
                        using (var reader = command.ExecuteReader())
                        {
                            var hojasList = new List<HojaPendienteModel>();
                            var pedidosUnicos = new HashSet<int>();

                            while (reader.Read())
                            {
                                var hoja = new HojaPendienteModel
                                {
                                    IdPreparacion = reader.GetInt32("Idpreparo"),
                                    IdPedido = reader.GetInt32("IdPedido"),
                                    NoHoja = reader.GetInt32("NoHoja"),
                                    PorcentajeProgreso = reader.GetDouble("Progreso"),
                                    NombreEmpresa = reader.IsDBNull(reader.GetOrdinal("NombreEmpresa")) ? "" : reader.GetString("NombreEmpresa"),
                                    TotalSkus = reader.GetInt32("TotalSkus"),
                                    TotalFardos = reader.IsDBNull(reader.GetOrdinal("TotalFardos")) ? 0 : reader.GetInt32("TotalFardos")
                                };
                                hojasList.Add(hoja);
                                pedidosUnicos.Add(hoja.IdPedido);
                            }

                            if (hojasList.Any())
                            {
                                HojasPendientes = hojasList;

                                if (!HojasPendientesLayout.IsVisible)
                                {
                                    HojasPendientesLayout.Opacity = 0;
                                    HojasPendientesLayout.IsVisible = true;
                                    await HojasPendientesLayout.FadeTo(1, 300);
                                }

                                NoHojasPendientesLabel.IsVisible = false;

                                ResumenPendientes = $"Tienes {hojasList.Count} hoja{(hojasList.Count > 1 ? "s" : "")} pendiente{(hojasList.Count > 1 ? "s" : "")} de {pedidosUnicos.Count} pedido{(pedidosUnicos.Count > 1 ? "s" : "")}";

                                IndicatorView.Count = hojasList.Count;
                                HojasCarousel.PositionChanged += (s, e) => {
                                    IndicatorView.Position = e.CurrentPosition;
                                };
                            }
                            else
                            {
                                HojasPendientes.Clear();
                                HojasPendientesLayout.IsVisible = false;

                                if (!NoHojasPendientesLabel.IsVisible)
                                {
                                    NoHojasPendientesLabel.Opacity = 0;
                                    NoHojasPendientesLabel.IsVisible = true;
                                    await NoHojasPendientesLabel.FadeTo(1, 300);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar hojas pendientes: " + ex.Message, "OK");
            }

            OnPropertyChanged(nameof(HojasPendientes));
            OnPropertyChanged(nameof(ResumenPendientes));
        }

        private async void OnRefreshTapped(object sender, EventArgs e)
        {
            var image = sender as Image;
            if (image != null)
            {
                await image.RotateTo(360, 500, Easing.CubicInOut);
                image.Rotation = 0;
            }
            CargarHojasPendientes();
        }

        private async Task AnimarBoton(View view)
        {
            if (view == null) return;
            await view.ScaleTo(0.95, 100, Easing.CubicOut);
            await view.ScaleTo(1, 100, Easing.CubicIn);
        }

        private async void OnHojaTapped(object sender, EventArgs e)
        {
            var frame = sender as Frame;
            if (frame != null)
            {
                await frame.ScaleTo(0.95, 100, Easing.CubicOut);
                await frame.ScaleTo(1, 100, Easing.CubicIn);
            }

            try
            {
                LoadingOverlay.IsVisible = true;

                if (frame != null && frame.BindingContext is HojaPendienteModel hoja)
                {
                    string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

                    using (var connection = new MySqlConnection(connectionString))
                    {
                        connection.Open();

                        using (var checkCommand = new MySqlCommand(@"SELECT FechaHoraInicio 
                                                            FROM PreparacionPedidos 
                                                            WHERE Idpreparo = @idPreparacion", connection))
                        {
                            checkCommand.Parameters.AddWithValue("@idPreparacion", hoja.IdPreparacion);
                            var fechaInicio = checkCommand.ExecuteScalar();

                            if (fechaInicio == null || fechaInicio == DBNull.Value)
                            {
                                using (var updateCommand = new MySqlCommand(@"UPDATE PreparacionPedidos 
                                                                     SET FechaHoraInicio = NOW() 
                                                                     WHERE Idpreparo = @idPreparacion", connection))
                                {
                                    updateCommand.Parameters.AddWithValue("@idPreparacion", hoja.IdPreparacion);
                                    updateCommand.ExecuteNonQuery();
                                }
                            }
                        }
                    }

                    await Navigation.PushAsync(new DetallePedidoPendientes(hoja.IdPedido, hoja.NoHoja));
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al abrir el detalle: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Cerrar Sesión", "¿Está seguro que desea cerrar la sesión?", "Sí", "No");

            if (confirm)
            {
                try
                {
                    // Limpiar dispositivo en la BD
                    string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

                    using (var connection = new MySqlConnection(connectionString))
                    {
                        connection.Open();
                        using (var cmd = new MySqlCommand(
                            "UPDATE usuarios SET DispositivoActivo = NULL WHERE Id = @id",
                            connection))
                        {
                            cmd.Parameters.AddWithValue("@id", App.IdUsuarioActual);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                catch { }

                // Limpiar sesión local
                Preferences.Set("SessionActive", false);
                App.IdUsuarioActual = 0;
                App.NombreUsuarioActual = "";
                App.NivelUsuarioActual = 0;
                App.CurrentPageName = "";
                App.DetenerMonitoreoPedidos();

                Application.Current.MainPage = new NavigationPage(new Ingreso());
            }
        }

        private async void OnUbicacionesClicked(object sender, EventArgs e)
        {
            try
            {
                await Navigation.PushAsync(new MenuUbicaciones());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al abrir Ubicaciones: " + ex.Message, "OK");
            }
        }

        private async void OnMisChequeosClicked(object sender, EventArgs e)
        {
            try
            {
                await Navigation.PushAsync(new ListRechequeo());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al abrir Rechequeo: " + ex.Message, "OK");
            }
        }
        private async void OnSalidaClicked(object sender, EventArgs e)
        {
            try
            {
                await Navigation.PushAsync(new SalidaTarimas());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al abrir Salida de tarimas: " + ex.Message, "OK");
            }
        }

        protected override void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            base.OnPropertyChanged(propertyName);
        }
    }
}