using MySqlConnector;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public class HojaPendienteModel
    {
        public int IdPedido { get; set; }
        public int NoHoja { get; set; }
        public double PorcentajeProgreso { get; set; }
        public string ColorProgreso => PorcentajeProgreso >= 75 ? "#48BB78" :
                                      (PorcentajeProgreso >= 50 ? "#4C51BF" :
                                      (PorcentajeProgreso >= 25 ? "#ECC94B" : "#E53E3E"));
        public double ProgresoWidth => 750 * (PorcentajeProgreso / 100);
    }

    public partial class Home : ContentPage, INotifyPropertyChanged
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

        public ICommand RefreshCommand { get; private set; }
        public string ResumenPendientes { get; private set; }
        public HojaPendienteModel HojaActual { get; private set; }
        public List<HojaPendienteModel> HojasPendientes { get; private set; }

        public Home(string nombreCompleto)
        {
            InitializeComponent();
            this.nombreCompleto = nombreCompleto;
            UserNameLabel.Text = nombreCompleto;
            DateLabel.Text = DateTime.Now.ToString("dddd, d 'de' MMMM 'de' yyyy");
            HojasPendientes = new List<HojaPendienteModel>();

            // Configurar visibilidad de botones según nivel de usuario
            ConfigurarPermisosUsuario();

            // Asignar el manejador de eventos Refreshing al RefreshView
            RefreshView.Refreshing += RefreshView_Refreshing;

            // Establecer el contexto de binding
            BindingContext = this;
            LoadingOverlay.IsVisible = true;

            // Preparar animaciones de entrada
            PrepFrame.Opacity = 0;
            StatsGrid.Opacity = 0;
            ButtonsStack.Opacity = 0;

            // Cargar los datos de forma asíncrona
            Task.Run(async () =>
            {
                try
                {
                    await Device.InvokeOnMainThreadAsync(() =>
                    {
                        CargarHojasPendientes();
                        ConteoDatospedidos();
                    });
                }
                finally
                {
                    // Ocultar el LoadingOverlay cuando termine y animar la entrada de elementos
                    await Device.InvokeOnMainThreadAsync(async () =>
                    {
                        LoadingOverlay.IsVisible = false;

                        // Animar la entrada de los elementos
                        await AnimarElementos();
                    });
                }
            });
        }

        private async Task AnimarElementos()
        {
            // Animar la entrada de los frames uno tras otro
            await PrepFrame.FadeTo(1, 400, Easing.CubicOut);
            await StatsGrid.FadeTo(1, 400, Easing.CubicOut);
            await ButtonsStack.FadeTo(1, 400, Easing.CubicOut);

            // Animar un pequeño rebote en los botones
            await ButtonsStack.ScaleTo(1.05, 150, Easing.SpringOut);
            await ButtonsStack.ScaleTo(1, 100, Easing.SpringOut);
        }

        private void ConfigurarPermisosUsuario()
        {
            // Configurar visibilidad de botones según el nivel del usuario
            switch (App.NivelUsuarioActual)
            {
                case 3:
                    // Nivel 3: Solo puede ver pedidos pendientes
                    BtnPendientes.IsVisible = true;
                    BtnRechequeo.IsVisible = false;
                    break;
                case 4:
                    // Nivel 4: Solo puede ver pedidos en rechequeo
                    BtnPendientes.IsVisible = false;
                    BtnRechequeo.IsVisible = true;
                    break;
                default:
                    // Otros niveles: mostrar ambos botones (en caso de que haya más niveles con todos los permisos)
                    BtnPendientes.IsVisible = true;
                    BtnRechequeo.IsVisible = true;
                    break;
            }
        }

        private async void RefreshView_Refreshing(object sender, EventArgs e)
        {
            try
            {
                // Realizar las operaciones de actualización
                CargarHojasPendientes();
                ConteoDatospedidos();

                // Actualizar la fecha
                DateLabel.Text = DateTime.Now.ToString("dddd, d 'de' MMMM 'de' yyyy");
            }
            finally
            {
                // Importante: Establecer IsRefreshing directamente en el control
                await Task.Delay(500); // Pequeño retardo para asegurar que la UI se actualice
                RefreshView.IsRefreshing = false;
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            ConteoDatospedidos();
        }

        private async void ConteoDatospedidos()
        {
            try
            {
                string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    // Contar pedidos pendientes
                    using (var command = new MySqlCommand("SELECT COUNT(*) FROM pedidostienda_bodega WHERE Estado = 4", connection))
                    {
                        var pendientes = command.ExecuteScalar();

                        // Animar el cambio del número
                        await AnimarCambioNumero(PendientesLabel, pendientes.ToString());
                    }

                    // Contar pedidos en rechequeo
                    using (var command = new MySqlCommand("SELECT COUNT(*) FROM pedidostienda_bodega WHERE Estado = 6", connection))
                    {
                        var rechequeo = command.ExecuteScalar();

                        // Animar el cambio del número
                        await AnimarCambioNumero(RechequeoLabel, rechequeo.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar datos: " + ex.Message, "OK");
            }
        }

        private async Task AnimarCambioNumero(Label label, string nuevoValor)
        {
            // Si el valor es diferente, animar el cambio
            if (label.Text != nuevoValor)
            {
                await label.ScaleTo(1.2, 150, Easing.SpringOut);
                label.Text = nuevoValor;
                await label.ScaleTo(1, 150, Easing.SpringIn);
            }
            else
            {
                // Si es el mismo valor, simplemente asignarlo
                label.Text = nuevoValor;
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
                        hp.IdPedido,
                        hp.NoHoja,
                        SUM(CASE WHEN dpb.EstadoPreparacionproducto NOT IN(0,4) THEN 1 ELSE 0 END) * 100.0 / COUNT(*) AS Progreso
                    FROM HistorialPreparacionPedidos hp
                    JOIN detallepedidostienda_bodega dpb ON hp.IdPedido = dpb.IdConsolidado AND hp.NoHoja = dpb.NoHoja
                    WHERE hp.IdUsuario = @IdUsuario AND hp.FechaHorafinalizo IS NULL
                    GROUP BY hp.IdPedido, hp.NoHoja", connection))
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
                                    IdPedido = reader.GetInt32("IdPedido"),
                                    NoHoja = reader.GetInt32("NoHoja"),
                                    PorcentajeProgreso = reader.GetDouble("Progreso")
                                };
                                hojasList.Add(hoja);
                                pedidosUnicos.Add(hoja.IdPedido);
                            }

                            if (hojasList.Any())
                            {
                                HojasPendientes = hojasList;

                                // Animar cambio de visibilidad si es necesario
                                if (!HojasPendientesLayout.IsVisible)
                                {
                                    HojasPendientesLayout.Opacity = 0;
                                    HojasPendientesLayout.IsVisible = true;
                                    await HojasPendientesLayout.FadeTo(1, 300);
                                }

                                NoHojasPendientesLabel.IsVisible = false;

                                ResumenPendientes = $"Tienes {hojasList.Count} hoja{(hojasList.Count > 1 ? "s" : "")} pendiente{(hojasList.Count > 1 ? "s" : "")} de {pedidosUnicos.Count} pedido{(pedidosUnicos.Count > 1 ? "s" : "")}";

                                // Configurar el indicador
                                IndicatorView.Count = hojasList.Count;
                                HojasCarousel.PositionChanged += (s, e) => {
                                    IndicatorView.Position = e.CurrentPosition;
                                };
                            }
                            else
                            {
                                HojasPendientes.Clear();
                                HojasPendientesLayout.IsVisible = false;

                                // Animar aparición del mensaje si es necesario
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
            // Animar rotación del icono de refresh
            var image = sender as Image;
            await image.RotateTo(360, 500, Easing.CubicInOut);
            image.Rotation = 0;

            // Refrescar datos
            CargarHojasPendientes();
            ConteoDatospedidos();
        }

        private async void OnPendientesClicked(object sender, EventArgs e)
        {
            await AnimarBoton(sender as Button);

            try
            {
                LoadingOverlay.IsVisible = true;
                await Navigation.PushAsync(new ListPendientes());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar pedidos pendientes: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        private async void OnRechequeoClicked(object sender, EventArgs e)
        {
            await AnimarBoton(sender as Button);

            try
            {
                LoadingOverlay.IsVisible = true;
                await Navigation.PushAsync(new ListRechequeo());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al cargar pedidos pendientes: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
            }
        }

        private async Task AnimarBoton(Button button)
        {
            // Animar presionado de botón
            await button.ScaleTo(0.95, 100, Easing.CubicOut);
            await button.ScaleTo(1, 100, Easing.CubicIn);
        }

        private async void OnHojaTapped(object sender, EventArgs e)
        {
            var frame = sender as Frame;
            if (frame != null)
            {
                // Animar tap en frame
                await frame.ScaleTo(0.95, 100, Easing.CubicOut);
                await frame.ScaleTo(1, 100, Easing.CubicIn);
            }

            try
            {
                LoadingOverlay.IsVisible = true;
                if (frame != null && frame.BindingContext is HojaPendienteModel hoja)
                {
                    // Navegar a la página de detalle
                    await Navigation.PushAsync(new DetalleHojaunbicaciones(hoja.IdPedido, hoja.NoHoja));
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al abrir el detalle: " + ex.Message, "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;  // Ocultar el overlay
            }
        }

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            var button = sender as Button;
            if (button != null)
            {
                await button.ScaleTo(0.95, 100, Easing.CubicOut);
                await button.ScaleTo(1, 100, Easing.CubicIn);
            }

            bool confirmar = await DisplayAlert("Cerrar Sesión", "¿Está seguro que desea cerrar sesión?", "Sí", "No");
            if (confirmar)
            {
                // Mostrar animación de carga
                LoadingOverlay.IsVisible = true;

                // Limpiar datos de usuario
                App.IdUsuarioActual = 0;
                App.NivelUsuarioActual = 0;
                App.NombreUsuarioActual = null;

                // Redirigir a la página de login
                await Task.Delay(500);
                Application.Current.MainPage = new NavigationPage(new Ingreso());
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}