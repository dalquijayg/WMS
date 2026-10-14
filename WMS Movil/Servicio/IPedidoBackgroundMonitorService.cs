namespace WMS_Movil.Servicio
{
    // Implementado en cada plataforma para mantener el monitoreo de pedidos
    // corriendo aunque la app esté en segundo plano o el teléfono bloqueado.
    public interface IPedidoBackgroundMonitorService
    {
        void Iniciar();
        void Detener();
    }
}
