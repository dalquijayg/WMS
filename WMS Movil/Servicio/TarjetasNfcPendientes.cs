using System.Collections.Generic;
using Newtonsoft.Json;
using Xamarin.Essentials;

namespace WMS_Movil.Servicio
{
    // Tarima cuya tarjeta NFC falta configurar. YaEntregada = false: la tarima se
    // entrega en la BD hasta que se grabe la tarjeta (y se puede cancelar si
    // PermiteCancelar). YaEntregada = true: ya está cerrada, solo falta la tarjeta.
    public class PendienteNfc
    {
        public int IdTarima { get; set; }
        public int NoTarima { get; set; }
        public int IdPedido { get; set; }
        public int NoHoja { get; set; }
        public bool YaEntregada { get; set; }
        public bool PermiteCancelar { get; set; }
    }

    // Se guardan en el teléfono para volver a pedir la tarjeta si se cierra la app a medias.
    public static class TarjetasNfcPendientes
    {
        private const string Clave = "nfc_tarjetas_pendientes";

        public static List<PendienteNfc> Leer()
        {
            try
            {
                return JsonConvert.DeserializeObject<List<PendienteNfc>>(Preferences.Get(Clave, ""))
                       ?? new List<PendienteNfc>();
            }
            catch
            {
                return new List<PendienteNfc>();
            }
        }

        public static void Agregar(PendienteNfc pendiente)
        {
            var lista = Leer();
            lista.RemoveAll(p => p.IdTarima == pendiente.IdTarima);
            lista.Add(pendiente);
            Guardar(lista);
        }

        public static void Quitar(int idTarima)
        {
            var lista = Leer();
            lista.RemoveAll(p => p.IdTarima == idTarima);
            Guardar(lista);
        }

        private static void Guardar(List<PendienteNfc> lista)
        {
            if (lista.Count == 0)
                Preferences.Remove(Clave);
            else
                Preferences.Set(Clave, JsonConvert.SerializeObject(lista));
        }
    }
}
