using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using System;
using Xamarin.Forms;

namespace WMS_Movil.Droid
{
    [Activity(Label = "WMS_Movil", Icon = "@mipmap/Portada2", Theme = "@style/MainTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation)]
    public class MainActivity : global::Xamarin.Forms.Platform.Android.FormsAppCompatActivity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Window.SetFlags(WindowManagerFlags.Fullscreen, WindowManagerFlags.Fullscreen);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            global::Xamarin.Forms.Forms.Init(this, savedInstanceState);
            var filter = new IntentFilter(BluetoothDevice.ActionAclDisconnected);
            filter.AddAction(BluetoothDevice.ActionAclConnected);
            RegisterReceiver(new BluetoothReceiver(), filter);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu &&
                CheckSelfPermission("android.permission.POST_NOTIFICATIONS") != Permission.Granted)
            {
                RequestPermissions(new[] { "android.permission.POST_NOTIFICATIONS" }, 200);
            }

            LoadApplication(new App());
        }

        // El modo lector NFC solo funciona con la actividad en primer plano
        protected override void OnResume()
        {
            base.OnResume();
            WMS_Movil.Droid.NfcService.Actual?.AlReanudar(this);
        }

        protected override void OnPause()
        {
            WMS_Movil.Droid.NfcService.Actual?.AlPausar(this);
            base.OnPause();
        }
        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }
        public class BluetoothReceiver : BroadcastReceiver
        {
            public override void OnReceive(Context context, Intent intent)
            {
                var action = intent.Action;

                if (action == BluetoothDevice.ActionAclDisconnected)
                {
                    // Manejar la desconexión sin cerrar la aplicación
                    Device.BeginInvokeOnMainThread(() => {
                        // Mostrar mensaje al usuario
                        App.Current.MainPage.DisplayAlert("Dispositivo desconectado",
                            "El escáner Bluetooth se ha desconectado. Por favor, reconéctelo para continuar.", "OK");
                    });
                }
                else if (action == BluetoothDevice.ActionAclConnected)
                {
                    // Manejar la reconexión
                    Device.BeginInvokeOnMainThread(() => {
                        // Informar al usuario
                        App.Current.MainPage.DisplayAlert("Dispositivo conectado",
                            "El escáner Bluetooth se ha conectado nuevamente.", "OK");
                    });
                }
            }
        }
    }
}