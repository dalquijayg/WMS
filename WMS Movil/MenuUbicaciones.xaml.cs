using System;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace WMS_Movil
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MenuUbicaciones : ContentPage
    {
        public MenuUbicaciones()
        {
            InitializeComponent();
        }

        private async void OnVolverClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        private async void OnRegistrarProductoClicked(object sender, EventArgs e)
        {
            try
            {
                await Navigation.PushAsync(new RegistrarInventarioUbicacion());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al abrir la pantalla: " + ex.Message, "OK");
            }
        }

        private async void OnConsultarProductoClicked(object sender, EventArgs e)
        {
            try
            {
                await Navigation.PushAsync(new ConsultarProducto());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", "Error al abrir la pantalla: " + ex.Message, "OK");
            }
        }
    }
}
