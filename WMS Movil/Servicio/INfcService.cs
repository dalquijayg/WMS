using System;
using System.Threading;
using System.Threading.Tasks;

namespace WMS_Movil.Servicio
{
    // Tarjeta NFC detectada. Texto es el registro de texto grabado (el IdTarima),
    // null si la tarjeta está vacía. Handle es el tag nativo para escribir en ella.
    // Compatible = false si este teléfono no puede leer/grabar datos en ella
    // (p. ej. MIFARE Classic en un teléfono sin chip NFC de NXP); el UID sí se lee.
    public class TarjetaNfc
    {
        public string Uid { get; set; }
        public string Texto { get; set; }
        public bool Compatible { get; set; }
        public string Tipo { get; set; }
        public object Handle { get; set; }
    }

    // Error de lectura/escritura con un mensaje listo para mostrar al usuario.
    public class NfcException : Exception
    {
        public NfcException(string mensaje) : base(mensaje) { }
    }

    // Implementado en Android con el modo lector de NFC: mientras se espera una
    // tarjeta, la app la recibe directamente y ninguna otra app se abre.
    public interface INfcService
    {
        bool TieneNfc { get; }
        bool NfcActivado { get; }
        void AbrirAjustesNfc();

        // Espera a que se acerque una tarjeta y devuelve su UID y contenido.
        Task<TarjetaNfc> EsperarTarjetaAsync(CancellationToken cancelacion);

        // Borra lo que tenga la tarjeta, graba el texto y lo vuelve a leer para comprobarlo.
        // La tarjeta debe seguir junto al teléfono.
        Task EscribirTextoAsync(TarjetaNfc tarjeta, string texto);

        // Apaga el modo lector y cancela la espera en curso.
        void Detener();
    }
}
