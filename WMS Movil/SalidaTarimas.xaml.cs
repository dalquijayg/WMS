using MySqlConnector;
using System;
using System.Linq;
using System.Threading.Tasks;
using WMS_Movil.Servicio;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    // Salida de tarimas embaladas: al escanear la hoja impresa (trae el IdTarimaEmbalada) la
    // tarima queda con FechaSalida y su lote pasa a salida, así el correlativo de la sucursal
    // se reinicia en la próxima tarima. Ver Servicio/EmbalajeTarimas.RegistrarSalida.
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class SalidaTarimas : ContentPage
    {
        private bool _procesando;
        private int _registradas;

        public SalidaTarimas()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Device.BeginInvokeOnMainThread(() => CodigoEntry.Focus());
        }

        private string BuildConnectionString() =>
            $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

        private async void OnBackClicked(object sender, EventArgs e) => await Navigation.PopAsync();

        private async void OnCodigoCompleted(object sender, EventArgs e) => await ProcesarCodigoAsync();

        private async void OnRegistrarClicked(object sender, EventArgs e) => await ProcesarCodigoAsync();

        private async Task ProcesarCodigoAsync()
        {
            if (_procesando) return;

            // La hoja puede traer el Id con texto alrededor: se toman solo los dígitos
            string digitos = new string((CodigoEntry.Text ?? "").Where(char.IsDigit).ToArray());
            if (!int.TryParse(digitos, out int idTarimaEmbalada) || idTarimaEmbalada <= 0)
            {
                MostrarResultado(false, "Código inválido", "Escanea el código de la hoja de salida de la tarima.");
                LimpiarYEnfocar();
                return;
            }

            _procesando = true;
            LoadingOverlay.IsVisible = true;
            try
            {
                var tarima = await Task.Run(() =>
                {
                    using (var connection = new MySqlConnection(BuildConnectionString()))
                    {
                        connection.Open();
                        using (var transaction = connection.BeginTransaction())
                        {
                            try
                            {
                                var t = EmbalajeTarimas.RegistrarSalida(connection, transaction,
                                    idTarimaEmbalada, App.IdUsuarioActual);
                                transaction.Commit();
                                return t;
                            }
                            catch
                            {
                                transaction.Rollback();
                                throw;
                            }
                        }
                    }
                });

                string detalle = $"{tarima.CantidadSkus} SKU  •  {tarima.CantidadFardos} fardos";
                MostrarResultado(true, $"Tarima {tarima.Codigo} en salida", detalle);
                AgregarRegistrada(tarima.Codigo, detalle);
                try { Vibration.Vibrate(150); } catch { }
            }
            catch (Exception ex)
            {
                MostrarResultado(false, "No se registró la salida", ex.Message);
                try { Vibration.Vibrate(400); } catch { }
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
                _procesando = false;
                LimpiarYEnfocar();
            }
        }

        private void MostrarResultado(bool ok, string titulo, string detalle)
        {
            ResultadoFrame.BackgroundColor = Color.FromHex(ok ? "#DCFCE7" : "#FEE2E2");
            ResultadoFrame.BorderColor = Color.FromHex(ok ? "#86EFAC" : "#FCA5A5");
            ResultadoIconoLabel.Text = ok ? "✅" : "⛔";
            ResultadoTituloLabel.Text = titulo;
            ResultadoTituloLabel.TextColor = Color.FromHex(ok ? "#166534" : "#B91C1C");
            ResultadoDetalleLabel.Text = detalle;
            ResultadoDetalleLabel.TextColor = Color.FromHex(ok ? "#15803D" : "#B91C1C");
            ResultadoFrame.IsVisible = true;
        }

        private void AgregarRegistrada(string codigo, string detalle)
        {
            _registradas++;
            TotalRegistradasLabel.Text = _registradas.ToString();
            RegistradasLayout.Children.Insert(0, new Frame
            {
                BackgroundColor = Color.White,
                CornerRadius = 12,
                Padding = new Thickness(12, 10),
                HasShadow = false,
                Content = new StackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label { Text = $"🚚 Tarima {codigo}", FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Color.FromHex("#1E2235") },
                        new Label { Text = $"{detalle}  •  {DateTime.Now:HH:mm}", FontSize = 11, TextColor = Color.FromHex("#6B7280") }
                    }
                }
            });
        }

        private void LimpiarYEnfocar()
        {
            CodigoEntry.Text = string.Empty;
            Device.BeginInvokeOnMainThread(() => CodigoEntry.Focus());
        }
    }
}
