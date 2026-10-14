using System;
using Android.Content;
using Android.OS;
using WMS_Movil.Droid;
using WMS_Movil.Servicio;
using Xamarin.Forms;
using Debug = System.Diagnostics.Debug;

[assembly: Dependency(typeof(PedidoBackgroundMonitorService))]
namespace WMS_Movil.Droid
{
    public class PedidoBackgroundMonitorService : IPedidoBackgroundMonitorService
    {
        public void Iniciar()
        {
            try
            {
                var context = Android.App.Application.Context;
                var intent = new Intent(context, typeof(MonitoreoPedidosService));

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    context.StartForegroundService(intent);
                }
                else
                {
                    context.StartService(intent);
                }

                Debug.WriteLine("PedidoBackgroundMonitorService: solicitud de inicio enviada.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PedidoBackgroundMonitorService: error al iniciar el servicio: {ex}");
            }
        }

        public void Detener()
        {
            try
            {
                var context = Android.App.Application.Context;
                context.StopService(new Intent(context, typeof(MonitoreoPedidosService)));
                Debug.WriteLine("PedidoBackgroundMonitorService: solicitud de detención enviada.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PedidoBackgroundMonitorService: error al detener el servicio: {ex}");
            }
        }
    }
}
