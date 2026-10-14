using System;

namespace WMS_Movil.Servicio
{
    public class PedidoAsignadoInfo
    {
        public int IdPreparacion { get; set; }
        public int IdPedido { get; set; }
        public int NoHoja { get; set; }
        public string Sucursal { get; set; }
        public string NombreUsuarioAsigno { get; set; }
        public DateTime FechaHoraAsigno { get; set; }
    }
}
