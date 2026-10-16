using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Nfc;
using Android.Nfc.Tech;
using WMS_Movil.Droid;
using WMS_Movil.Servicio;
using Xamarin.Forms;
using Debug = System.Diagnostics.Debug;

[assembly: Dependency(typeof(NfcService))]
namespace WMS_Movil.Droid
{
    // Modo lector de NFC: mientras una pantalla espera tarjetas, la app las recibe
    // directamente (no se abre otra app). MainActivity lo reactiva al volver a primer plano.
    public class NfcService : INfcService
    {
        public static NfcService Actual { get; private set; }

        private const NfcReaderFlags Flags =
            NfcReaderFlags.NfcA | NfcReaderFlags.NfcB | NfcReaderFlags.NfcF | NfcReaderFlags.NfcV;

        private readonly LectorCallback _callback;
        private TaskCompletionSource<TarjetaNfc> _pendiente;
        private bool _escuchando;

        public NfcService()
        {
            Actual = this;
            _callback = new LectorCallback(this);
        }

        private static NfcAdapter Adaptador =>
            NfcAdapter.GetDefaultAdapter(Android.App.Application.Context);

        public bool TieneNfc => Adaptador != null;

        public bool NfcActivado => Adaptador?.IsEnabled ?? false;

        public void AbrirAjustesNfc()
        {
            try
            {
                var intent = new Intent(Android.Provider.Settings.ActionNfcSettings);
                intent.AddFlags(ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(intent);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NfcService: no se pudieron abrir los ajustes de NFC: {ex}");
            }
        }

        public Task<TarjetaNfc> EsperarTarjetaAsync(CancellationToken cancelacion)
        {
            if (!TieneNfc)
                throw new NfcException("Este teléfono no tiene NFC.");

            var tcs = new TaskCompletionSource<TarjetaNfc>(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Exchange(ref _pendiente, tcs)?.TrySetCanceled();
            cancelacion.Register(() =>
            {
                Interlocked.CompareExchange(ref _pendiente, null, tcs);
                tcs.TrySetCanceled();
            });

            _escuchando = true;
            ActivarModoLector(Xamarin.Essentials.Platform.CurrentActivity);
            return tcs.Task;
        }

        public Task EscribirTextoAsync(TarjetaNfc tarjeta, string texto) => Task.Run(() =>
        {
            if (!(tarjeta?.Handle is Tag tag))
                throw new NfcException("No hay tarjeta para grabar. Vuelva a acercarla.");

            var mensaje = new NdefMessage(new[] { NdefRecord.CreateTextRecord("es", texto) });

            try
            {
                var ndef = Ndef.Get(tag);
                if (ndef != null)
                {
                    ndef.Connect();
                    try
                    {
                        if (!ndef.IsWritable)
                            throw new NfcException("La tarjeta está bloqueada contra escritura.");
                        if (ndef.MaxSize < mensaje.ToByteArray().Length)
                            throw new NfcException("La tarjeta no tiene espacio suficiente.");

                        // Reemplaza todo el contenido anterior de la tarjeta
                        ndef.WriteNdefMessage(mensaje);

                        if (LeerTexto(ndef.NdefMessage) != texto)
                            throw new NfcException("No se pudo comprobar lo grabado. Vuelva a acercar la tarjeta.");
                    }
                    finally
                    {
                        ndef.Close();
                    }
                    return;
                }

                // Tarjeta nueva sin formato NDEF: se formatea grabando el mensaje
                var formatable = NdefFormatable.Get(tag);
                if (formatable != null)
                {
                    formatable.Connect();
                    try
                    {
                        formatable.Format(mensaje);
                    }
                    finally
                    {
                        formatable.Close();
                    }
                    return;
                }

                throw new NfcException(MensajeNoCompatible(tag));
            }
            catch (NfcException)
            {
                throw;
            }
            catch (TagLostException)
            {
                throw new NfcException("Se separó la tarjeta antes de terminar. Vuelva a acercarla y manténgala quieta.");
            }
            catch (Exception ex)
            {
                throw new NfcException("No se pudo grabar la tarjeta: " + ex.Message);
            }
        });

        public void Detener()
        {
            _escuchando = false;
            Interlocked.Exchange(ref _pendiente, null)?.TrySetCanceled();

            var activity = Xamarin.Essentials.Platform.CurrentActivity;
            activity?.RunOnUiThread(() =>
            {
                try { Adaptador?.DisableReaderMode(activity); }
                catch (Exception ex) { Debug.WriteLine($"NfcService: DisableReaderMode: {ex.Message}"); }
            });
        }

        // Llamados desde MainActivity: el modo lector solo funciona con la actividad en primer plano.
        public void AlReanudar(Activity activity)
        {
            if (_escuchando) ActivarModoLector(activity);
        }

        public void AlPausar(Activity activity)
        {
            try { Adaptador?.DisableReaderMode(activity); }
            catch (Exception ex) { Debug.WriteLine($"NfcService: DisableReaderMode: {ex.Message}"); }
        }

        private void ActivarModoLector(Activity activity)
        {
            if (activity == null) return;
            activity.RunOnUiThread(() =>
            {
                try { Adaptador?.EnableReaderMode(activity, _callback, Flags, null); }
                catch (Exception ex) { Debug.WriteLine($"NfcService: EnableReaderMode: {ex.Message}"); }
            });
        }

        // Con nadie esperando, la tarjeta se ignora (y tampoco abre otra app).
        private void AlDetectarTarjeta(Tag tag)
        {
            var tcs = Interlocked.Exchange(ref _pendiente, null);
            if (tcs == null) return;

            try
            {
                var ndef = Ndef.Get(tag);
                tcs.TrySetResult(new TarjetaNfc
                {
                    Uid = FormatearUid(tag.GetId()),
                    Texto = LeerTexto(ndef?.CachedNdefMessage),
                    Compatible = ndef != null || NdefFormatable.Get(tag) != null,
                    Tipo = DescribirTipo(tag),
                    Handle = tag
                });
            }
            catch (Exception ex)
            {
                tcs.TrySetException(new NfcException("No se pudo leer la tarjeta: " + ex.Message));
            }
        }

        // Devuelve el primer registro de texto de la tarjeta, o null si no tiene.
        private static string LeerTexto(NdefMessage mensaje)
        {
            var registros = mensaje?.GetRecords();
            if (registros == null) return null;

            foreach (var registro in registros)
            {
                if (registro.ToMimeType() != "text/plain") continue;

                var payload = registro.GetPayload();
                if (payload == null || payload.Length == 0) continue;

                if (registro.Tnf == NdefRecord.TnfWellKnown)
                {
                    // Registro de texto NFC Forum: [estado][idioma][texto]
                    int largoIdioma = payload[0] & 0x3F;
                    var codificacion = (payload[0] & 0x80) != 0 ? Encoding.BigEndianUnicode : Encoding.UTF8;
                    int inicio = 1 + largoIdioma;
                    if (inicio > payload.Length) continue;
                    return codificacion.GetString(payload, inicio, payload.Length - inicio).Trim();
                }

                return Encoding.UTF8.GetString(payload).Trim();
            }
            return null;
        }

        private static string FormatearUid(byte[] id) =>
            id == null || id.Length == 0 ? "" : BitConverter.ToString(id).Replace("-", ":");

        private static string DescribirTipo(Tag tag)
        {
            var techs = tag.GetTechList() ?? new string[0];
            if (techs.Any(t => t.EndsWith("MifareClassic"))) return "MIFARE Classic";
            if (techs.Any(t => t.EndsWith("MifareUltralight"))) return "MIFARE Ultralight / NTAG";
            return string.Join(", ", techs.Select(t => t.Substring(t.LastIndexOf('.') + 1)));
        }

        private static string MensajeNoCompatible(Tag tag)
        {
            var techs = tag.GetTechList() ?? new string[0];
            if (techs.Any(t => t.EndsWith("MifareClassic")) || !techs.Any(t => t.EndsWith("Ndef") || t.EndsWith("NdefFormatable")))
                return $"Este teléfono no puede grabar datos en esta tarjeta ({DescribirTipo(tag)}).\n\n" +
                       "Las tarjetas MIFARE Classic solo se graban en teléfonos con chip NFC de NXP. " +
                       "Pruebe con otro teléfono o use tarjetas NTAG213/215.";
            return $"Tarjeta no compatible ({DescribirTipo(tag)}).";
        }

        private class LectorCallback : Java.Lang.Object, NfcAdapter.IReaderCallback
        {
            private readonly NfcService _servicio;
            public LectorCallback(NfcService servicio) { _servicio = servicio; }
            public void OnTagDiscovered(Tag tag) => _servicio.AlDetectarTarjeta(tag);
        }
    }
}
