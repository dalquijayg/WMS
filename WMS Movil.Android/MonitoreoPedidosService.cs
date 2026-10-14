using System;
using System.Linq;
using System.Threading;
using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;
using WMS_Movil.Servicio;
using Debug = System.Diagnostics.Debug;

namespace WMS_Movil.Droid
{
    // Foreground service: mantiene vivo el sondeo de pedidos asignados aunque la
    // app esté en segundo plano o el teléfono bloqueado, y muestra una notificación
    // del sistema (con sonido, vibración y cronómetro) mientras haya un pedido sin iniciar.
    [Service(Enabled = true, Exported = false)]
    public class MonitoreoPedidosService : Service
    {
        private const string CanalMonitoreoId = "wms_monitoreo_activo";
        private const string CanalAlertaId = "wms_alerta_pedido";
        private const int NotifMonitoreoId = 1001;
        private const int NotifAlertaId = 1002;
        private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(10);

        private Timer _timer;
        private bool _alertaVisible;

        public override IBinder OnBind(Intent intent) => null;

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            Debug.WriteLine("MonitoreoPedidosService: OnStartCommand");

            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            {
                // Los canales de notificación (y por lo tanto este servicio) requieren Android 8+.
                Debug.WriteLine("MonitoreoPedidosService: SDK < O, deteniendo servicio.");
                StopSelf();
                return StartCommandResult.NotSticky;
            }

            CrearCanales();
            StartForeground(NotifMonitoreoId, ConstruirNotificacionMonitoreo());
            Debug.WriteLine("MonitoreoPedidosService: foreground iniciado, arrancando timer.");

            _timer?.Dispose();
            _timer = new Timer(_ => VerificarPedidos(), null, TimeSpan.Zero, Intervalo);

            return StartCommandResult.Sticky;
        }

        public override void OnDestroy()
        {
            Debug.WriteLine("MonitoreoPedidosService: OnDestroy");
            _timer?.Dispose();
            _timer = null;

            var notificationManager = (NotificationManager)GetSystemService(NotificationService);
            notificationManager?.Cancel(NotifAlertaId);

            base.OnDestroy();
        }

        private void CrearCanales()
        {
            var notificationManager = (NotificationManager)GetSystemService(NotificationService);

            if (notificationManager.GetNotificationChannel(CanalMonitoreoId) == null)
            {
                var canalMonitoreo = new NotificationChannel(CanalMonitoreoId, "Monitoreo de sesión", NotificationImportance.Low)
                {
                    Description = "Indica que la app está verificando pedidos asignados"
                };
                canalMonitoreo.SetShowBadge(false);
                canalMonitoreo.SetSound(null, null);
                notificationManager.CreateNotificationChannel(canalMonitoreo);
            }

            if (notificationManager.GetNotificationChannel(CanalAlertaId) == null)
            {
                var canalAlerta = new NotificationChannel(CanalAlertaId, "Pedido asignado", NotificationImportance.High)
                {
                    Description = "Avisa cuando tienes un pedido asignado sin iniciar"
                };

                // Usage.Alarm usa el volumen de "Alarma" del teléfono, que NO se silencia
                // con el modo vibrador/silencio (igual que un despertador). El sonido en sí
                // (el archivo) sí puede ser el de notificación normal, son cosas independientes.
                var atributosAudio = new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.Alarm)
                    .SetContentType(AudioContentType.Sonification)
                    .Build();
                canalAlerta.SetSound(RingtoneManager.GetDefaultUri(RingtoneType.Notification), atributosAudio);

                canalAlerta.EnableVibration(true);
                canalAlerta.SetVibrationPattern(new long[] { 0, 500, 250, 500 });
                canalAlerta.EnableLights(true);
                notificationManager.CreateNotificationChannel(canalAlerta);
                Debug.WriteLine("MonitoreoPedidosService: canal de alerta creado con sonido y vibración.");
            }
        }

        private PendingIntent ObtenerPendingIntentAbrirApp(int requestCode)
        {
            var intent = PackageManager.GetLaunchIntentForPackage(PackageName);
            intent?.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);

            return PendingIntent.GetActivity(this, requestCode, intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        }

        private Notification ConstruirNotificacionMonitoreo()
        {
            return new Notification.Builder(this, CanalMonitoreoId)
                .SetContentTitle("WMS Movil")
                .SetContentText("Sesión activa: verificando pedidos asignados")
                .SetSmallIcon(Resource.Mipmap.Portada2)
                .SetOngoing(true)
                .SetContentIntent(ObtenerPendingIntentAbrirApp(0))
                .Build();
        }

        private void VerificarPedidos()
        {
            try
            {
                var pendientes = PedidoMonitorHelper.ObtenerPedidosAsignadosSinIniciar(App.IdUsuarioActual);
                Debug.WriteLine($"MonitoreoPedidosService: usuario {App.IdUsuarioActual}, {pendientes.Count} pendiente(s) sin iniciar.");
                var notificationManager = (NotificationManager)GetSystemService(NotificationService);

                if (pendientes.Count == 0)
                {
                    if (_alertaVisible)
                    {
                        notificationManager.Cancel(NotifAlertaId);
                        _alertaVisible = false;
                    }
                    return;
                }

                var primero = pendientes.First();
                long fechaBaseMillis = new DateTimeOffset(DateTime.SpecifyKind(primero.FechaHoraAsigno, DateTimeKind.Local))
                    .ToUnixTimeMilliseconds();

                string titulo = pendientes.Count == 1
                    ? $"Pedido #{primero.IdPedido} - Hoja {primero.NoHoja} asignado"
                    : $"Tienes {pendientes.Count} pedidos asignados sin iniciar";

                string texto = string.IsNullOrEmpty(primero.NombreUsuarioAsigno)
                    ? $"Sucursal: {primero.Sucursal}"
                    : $"Asignado por {primero.NombreUsuarioAsigno} · {primero.Sucursal}";

                var notificacion = new Notification.Builder(this, CanalAlertaId)
                    .SetContentTitle(titulo)
                    .SetContentText(texto)
                    .SetSmallIcon(Resource.Mipmap.Portada2)
                    .SetCategory(Notification.CategoryAlarm)
                    .SetVisibility(NotificationVisibility.Public)
                    .SetAutoCancel(false)
                    .SetContentIntent(ObtenerPendingIntentAbrirApp(1))
                    .SetUsesChronometer(true)
                    .SetWhen(fechaBaseMillis)
                    .Build();

                notificationManager.Notify(NotifAlertaId, notificacion);
                _alertaVisible = true;
                Debug.WriteLine($"MonitoreoPedidosService: notificación de alerta publicada para pedido #{primero.IdPedido}.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MonitoreoPedidosService: error en monitoreo de pedidos: {ex}");
            }
        }
    }
}
