using System;
using System.Collections.Generic;
using MySqlConnector;

namespace WMS_Movil.Servicio
{
    // Tarima de embalaje (TarimasEmbalaje): destino físico que sale a la sucursal.
    // Se identifica como "L{NoLote}-T{NoTarimaEmbalada}" porque el correlativo se
    // reinicia en cada lote de la sucursal.
    public class TarimaEmbalaje
    {
        public int IdTarimaEmbalada { get; set; }
        public int IdLote { get; set; }
        public int NoLote { get; set; }
        public int NoTarimaEmbalada { get; set; }
        public int IdSucursal { get; set; }
        public int Estado { get; set; }
        public int? IdInventario { get; set; }
        public int CantidadSkus { get; set; }
        public int CantidadFardos { get; set; }
        public string NombreUsuarioCreo { get; set; }
        public DateTime? FechaSalida { get; set; }

        public string Codigo => $"L{NoLote}-T{NoTarimaEmbalada}";
    }

    // Producto de una tarima de preparación que se rechequea y se pone en una tarima de embalaje.
    public class ProductoAEmbalar
    {
        public int IdTarima { get; set; }          // tarima de preparación (origen)
        public int IdPedido { get; set; }
        public int NoHoja { get; set; }
        public string Upc { get; set; }
        public string UpcUnidad { get; set; }
        public int CantidadConfirmada { get; set; } // fardos; se mueve aunque se encuentre otra cantidad
    }

    // La tarima de embalaje ya no está en Estado 1 (alguien la cerró).
    public class TarimaEmbalajeCerradaException : Exception
    {
        public string Codigo { get; }
        public TarimaEmbalajeCerradaException(string codigo)
            : base($"La tarima {codigo} ya fue cerrada (embalada).") { Codigo = codigo; }
    }

    // Rechequeo + embalaje:
    // - Lotes por sucursal (LotesEmbalaje): el correlativo de tarimas se reinicia en cada lote.
    //   Un lote recibe tarimas nuevas mientras está en bodega (1); pasa a salida (2) cuando
    //   se carga al camión la primera de sus tarimas. Sus tarimas en Estado 1 se siguen llenando.
    // - Inventario: la tarima de embalaje adopta el inventario de la primera tarima de preparación
    //   que embala en ella (si ese inventario no es ya de otra tarima de embalaje; si lo es, se crea
    //   uno nuevo). Lo que llega de otras tarimas se mueve a ese inventario con la cantidad
    //   confirmada; el inventario de origen que queda vacío se anula (Estado 4).
    public static class EmbalajeTarimas
    {
        public const int EstadoEmbalando = 1;
        public const int EstadoEmbalada = 2;

        private const int LoteEnBodega = 1;
        private const int LoteEnSalida = 2;

        private const int InventarioAbierto = 0;
        private const int InventarioAnulado = 4;

        private const string SqlTarima = @"
            SELECT te.IdTarimaEmbalada, te.IdLote, l.NoLote, te.NoTarimaEmbalada, te.IdSucursal,
                   te.Estado, te.IdInventario, COALESCE(te.CantidadSkus, 0) AS CantidadSkus,
                   COALESCE(te.CantidadFardos, 0) AS CantidadFardos,
                   COALESCE(te.NombreUsuarioCreo, '') AS NombreUsuarioCreo, te.FechaSalida
            FROM TarimasEmbalaje te
            INNER JOIN LotesEmbalaje l ON l.IdLote = te.IdLote";

        private static TarimaEmbalaje LeerFila(MySqlDataReader r) => new TarimaEmbalaje
        {
            IdTarimaEmbalada = Convert.ToInt32(r["IdTarimaEmbalada"]),
            IdLote = Convert.ToInt32(r["IdLote"]),
            NoLote = Convert.ToInt32(r["NoLote"]),
            NoTarimaEmbalada = Convert.ToInt32(r["NoTarimaEmbalada"]),
            IdSucursal = Convert.ToInt32(r["IdSucursal"]),
            Estado = Convert.ToInt32(r["Estado"]),
            IdInventario = r.IsDBNull(r.GetOrdinal("IdInventario")) || Convert.ToInt32(r["IdInventario"]) <= 0
                ? (int?)null : Convert.ToInt32(r["IdInventario"]),
            CantidadSkus = Convert.ToInt32(r["CantidadSkus"]),
            CantidadFardos = Convert.ToInt32(r["CantidadFardos"]),
            NombreUsuarioCreo = Convert.ToString(r["NombreUsuarioCreo"]),
            FechaSalida = r.IsDBNull(r.GetOrdinal("FechaSalida")) ? (DateTime?)null : r.GetDateTime("FechaSalida")
        };

        public static TarimaEmbalaje Leer(MySqlConnection conn, MySqlTransaction tr, int idTarimaEmbalada, bool bloquear = false)
        {
            using (var cmd = new MySqlCommand(
                SqlTarima + " WHERE te.IdTarimaEmbalada = @Id" + (bloquear ? " FOR UPDATE" : ""), conn, tr))
            {
                cmd.Parameters.AddWithValue("@Id", idTarimaEmbalada);
                using (var r = cmd.ExecuteReader())
                    return r.Read() ? LeerFila(r) : null;
            }
        }

        // Tarimas de la sucursal que aún se están embalando (Estado 1), de cualquier lote.
        public static List<TarimaEmbalaje> ListarAbiertas(MySqlConnection conn, int idSucursal)
        {
            var lista = new List<TarimaEmbalaje>();
            using (var cmd = new MySqlCommand(
                SqlTarima + " WHERE te.IdSucursal = @IdSucursal AND te.Estado = @Estado ORDER BY l.NoLote, te.NoTarimaEmbalada", conn))
            {
                cmd.Parameters.AddWithValue("@IdSucursal", idSucursal);
                cmd.Parameters.AddWithValue("@Estado", EstadoEmbalando);
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        lista.Add(LeerFila(r));
            }
            return lista;
        }

        public static int? LeerSucursalPedido(MySqlConnection conn, MySqlTransaction tr, int idPedido)
        {
            using (var cmd = new MySqlCommand(
                "SELECT IdSucursal FROM pedidostienda_bodega WHERE IdPedidos = @IdPedido", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                var res = cmd.ExecuteScalar();
                return res == null || res == DBNull.Value ? (int?)null : Convert.ToInt32(res);
            }
        }

        // ─────────────────────────────────────────
        // CREAR TARIMA (lote en bodega de la sucursal; si no hay, se abre uno nuevo)
        // ─────────────────────────────────────────

        public static TarimaEmbalaje CrearTarima(MySqlConnection conn, MySqlTransaction tr,
            int idSucursal, int idUsuario, string nombreUsuario)
        {
            // Lote bloqueado hasta el commit: dos usuarios no toman el mismo número
            int? idLote = null;
            using (var cmd = new MySqlCommand(@"
                SELECT IdLote FROM LotesEmbalaje
                WHERE IdSucursal = @IdSucursal AND Estado = @EnBodega
                ORDER BY IdLote DESC LIMIT 1 FOR UPDATE", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdSucursal", idSucursal);
                cmd.Parameters.AddWithValue("@EnBodega", LoteEnBodega);
                var res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value) idLote = Convert.ToInt32(res);
            }

            if (!idLote.HasValue)
            {
                int noLote;
                using (var cmd = new MySqlCommand(
                    "SELECT COALESCE(MAX(NoLote), 0) + 1 FROM LotesEmbalaje WHERE IdSucursal = @IdSucursal FOR UPDATE", conn, tr))
                {
                    cmd.Parameters.AddWithValue("@IdSucursal", idSucursal);
                    noLote = Convert.ToInt32(cmd.ExecuteScalar());
                }

                using (var cmd = new MySqlCommand(@"
                    INSERT INTO LotesEmbalaje (IdSucursal, NoLote, Estado, FechaApertura)
                    VALUES (@IdSucursal, @NoLote, @EnBodega, NOW());
                    SELECT LAST_INSERT_ID();", conn, tr))
                {
                    cmd.Parameters.AddWithValue("@IdSucursal", idSucursal);
                    cmd.Parameters.AddWithValue("@NoLote", noLote);
                    cmd.Parameters.AddWithValue("@EnBodega", LoteEnBodega);
                    idLote = Convert.ToInt32(cmd.ExecuteScalar());
                }
            }

            // Cuenta también las tarimas ya embaladas del lote para no repetir número
            int noTarima;
            using (var cmd = new MySqlCommand(
                "SELECT COALESCE(MAX(NoTarimaEmbalada), 0) + 1 FROM TarimasEmbalaje WHERE IdLote = @IdLote FOR UPDATE", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdLote", idLote.Value);
                noTarima = Convert.ToInt32(cmd.ExecuteScalar());
            }

            int idTarima;
            using (var cmd = new MySqlCommand(@"
                INSERT INTO TarimasEmbalaje (
                    IdLote, IdSucursal, NoTarimaEmbalada, Estado,
                    IdUsuarioCreo, NombreUsuarioCreo, FechaCreacion, CantidadSkus, CantidadFardos
                ) VALUES (
                    @IdLote, @IdSucursal, @NoTarima, @Embalando,
                    @IdUsuario, @NombreUsuario, NOW(), 0, 0
                );
                SELECT LAST_INSERT_ID();", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdLote", idLote.Value);
                cmd.Parameters.AddWithValue("@IdSucursal", idSucursal);
                cmd.Parameters.AddWithValue("@NoTarima", noTarima);
                cmd.Parameters.AddWithValue("@Embalando", EstadoEmbalando);
                cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                cmd.Parameters.AddWithValue("@NombreUsuario", nombreUsuario ?? "");
                idTarima = Convert.ToInt32(cmd.ExecuteScalar());
            }

            return Leer(conn, tr, idTarima);
        }

        // ─────────────────────────────────────────
        // EMBALAR UN PRODUCTO (misma transacción que guarda el rechequeo)
        // ─────────────────────────────────────────

        // Lanza TarimaEmbalajeCerradaException si la tarima ya no está en Estado 1.
        public static void EmbalarProducto(MySqlConnection conn, MySqlTransaction tr,
            int idTarimaEmbalada, ProductoAEmbalar producto, int idUsuario)
        {
            var destino = Leer(conn, tr, idTarimaEmbalada, bloquear: true)
                ?? throw new Exception("La tarima de embalaje ya no existe.");
            if (destino.Estado != EstadoEmbalando)
                throw new TarimaEmbalajeCerradaException(destino.Codigo);

            int? invOrigen = RepartoTarimas.LeerInventarioTarima(conn, tr, producto.IdTarima);
            int? invDestino = destino.IdInventario;

            if (invOrigen.HasValue)
            {
                ValidarInventarioAbierto(conn, tr, invOrigen.Value);

                if (!invDestino.HasValue)
                    invDestino = AsignarInventario(conn, tr, destino, invOrigen.Value, producto.IdTarima);
                else
                    ValidarInventarioAbierto(conn, tr, invDestino.Value);

                if (invDestino.Value != invOrigen.Value)
                {
                    MoverProducto(conn, tr, invOrigen.Value, invDestino.Value,
                        producto.Upc, producto.UpcUnidad, producto.CantidadConfirmada);
                    AnularSiQuedoVacio(conn, tr, invOrigen.Value);
                }

                using (var cmd = new MySqlCommand(@"
                    UPDATE detalleinventarios SET IdTarimaEmbalada = @IdTarimaEmbalada
                    WHERE IdInventarios = @IdInventario
                      AND (Upc IN (@Codigo1, @Codigo2) OR DUN14 IN (@Codigo1, @Codigo2))", conn, tr))
                {
                    cmd.Parameters.AddWithValue("@IdTarimaEmbalada", idTarimaEmbalada);
                    cmd.Parameters.AddWithValue("@IdInventario", invDestino.Value);
                    cmd.Parameters.AddWithValue("@Codigo1", producto.Upc);
                    cmd.Parameters.AddWithValue("@Codigo2", string.IsNullOrEmpty(producto.UpcUnidad) ? producto.Upc : producto.UpcUnidad);
                    cmd.ExecuteNonQuery();
                }
            }

            // Un producto repartido en varias tarimas es una sola fila: queda con la última tarima de
            // embalaje; el detalle completo está en DetalleTarimaEmbalada.
            using (var cmd = new MySqlCommand(@"
                UPDATE detallepedidostienda_bodega SET IdTarimaEmbalada = @IdTarimaEmbalada
                WHERE IdConsolidado = @IdPedido AND UPC = @Upc AND NoHoja = @NoHoja", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdTarimaEmbalada", idTarimaEmbalada);
                cmd.Parameters.AddWithValue("@IdPedido", producto.IdPedido);
                cmd.Parameters.AddWithValue("@Upc", producto.Upc);
                cmd.Parameters.AddWithValue("@NoHoja", producto.NoHoja);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new MySqlCommand(@"
                INSERT INTO DetalleTarimaEmbalada (
                    IdTarimaEmbalada, IdTarima, IdPedido, NoHoja, Upc, Cantidad,
                    IdInventarioOrigen, IdInventarioDestino, IdUsuario, FechaHora
                ) VALUES (
                    @IdTarimaEmbalada, @IdTarima, @IdPedido, @NoHoja, @Upc, @Cantidad,
                    @InvOrigen, @InvDestino, @IdUsuario, NOW()
                )", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdTarimaEmbalada", idTarimaEmbalada);
                cmd.Parameters.AddWithValue("@IdTarima", producto.IdTarima);
                cmd.Parameters.AddWithValue("@IdPedido", producto.IdPedido);
                cmd.Parameters.AddWithValue("@NoHoja", producto.NoHoja);
                cmd.Parameters.AddWithValue("@Upc", producto.Upc);
                cmd.Parameters.AddWithValue("@Cantidad", producto.CantidadConfirmada);
                cmd.Parameters.AddWithValue("@InvOrigen", (object)invOrigen ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@InvDestino", (object)invDestino ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new MySqlCommand(@"
                UPDATE TarimasEmbalaje te
                SET te.CantidadSkus = (SELECT COUNT(DISTINCT d.IdPedido, d.NoHoja, d.Upc)
                                       FROM DetalleTarimaEmbalada d WHERE d.IdTarimaEmbalada = te.IdTarimaEmbalada),
                    te.CantidadFardos = (SELECT COALESCE(SUM(d.Cantidad), 0)
                                         FROM DetalleTarimaEmbalada d WHERE d.IdTarimaEmbalada = te.IdTarimaEmbalada)
                WHERE te.IdTarimaEmbalada = @IdTarimaEmbalada", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdTarimaEmbalada", idTarimaEmbalada);
                cmd.ExecuteNonQuery();
            }
        }

        // La tarima de embalaje adopta el inventario de la tarima de preparación si ese inventario
        // aún no es de otra tarima de embalaje; si ya lo es, recibe uno nuevo con el mismo encabezado.
        private static int AsignarInventario(MySqlConnection conn, MySqlTransaction tr,
            TarimaEmbalaje destino, int invOrigen, int idTarimaOrigen)
        {
            bool tieneDueno;
            using (var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM TarimasEmbalaje WHERE IdInventario = @IdInventario", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdInventario", invOrigen);
                tieneDueno = Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }

            int idInventario = tieneDueno ? CrearInventarioCopia(conn, tr, invOrigen) : invOrigen;

            using (var cmd = new MySqlCommand(@"
                UPDATE TarimasEmbalaje
                SET IdInventario = @IdInventario,
                    IdTarimaOrigenInventario = @IdTarimaOrigen
                WHERE IdTarimaEmbalada = @IdTarimaEmbalada", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdInventario", idInventario);
                cmd.Parameters.AddWithValue("@IdTarimaOrigen", tieneDueno ? (object)DBNull.Value : idTarimaOrigen);
                cmd.Parameters.AddWithValue("@IdTarimaEmbalada", destino.IdTarimaEmbalada);
                cmd.ExecuteNonQuery();
            }
            destino.IdInventario = idInventario;
            return idInventario;
        }

        private static int CrearInventarioCopia(MySqlConnection conn, MySqlTransaction tr, int idInventarioBase)
        {
            using (var cmd = new MySqlCommand(@"
                INSERT INTO inventarios (
                    IdUsuarios, Fecha, FechaHoraI, Operacion, Estado,
                    IdSucursales, Sucursal, Usuario, IdRazon, NombreRazon, IdDepartamentos
                )
                SELECT IdUsuarios, CURDATE(), NOW(), 1, 0,
                       IdSucursales, Sucursal, Usuario, IdRazon, NombreRazon, IdDepartamentos
                FROM inventarios WHERE idInventarios = @IdBase;
                SELECT LAST_INSERT_ID();", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdBase", idInventarioBase);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        // Bloquea el inventario y comprueba que siga abierto (Estado 0).
        private static void ValidarInventarioAbierto(MySqlConnection conn, MySqlTransaction tr, int idInventario)
        {
            using (var cmd = new MySqlCommand(
                "SELECT COALESCE(Estado, 0) FROM inventarios WHERE idInventarios = @Id FOR UPDATE", conn, tr))
            {
                cmd.Parameters.AddWithValue("@Id", idInventario);
                var res = cmd.ExecuteScalar();
                if (res == null || res == DBNull.Value)
                    throw new Exception($"El inventario #{idInventario} no existe.");
                if (Convert.ToInt32(res) != InventarioAbierto)
                    throw new Exception($"El inventario #{idInventario} ya no está abierto. No se pueden mover productos.");
            }
        }

        // Pasa la cantidad confirmada del producto del inventario origen al destino. El inventario
        // guarda unidades cuando el renglón es de paquete (DUN14 + UnidadesFardo) y fardos cuando no.
        private static void MoverProducto(MySqlConnection conn, MySqlTransaction tr,
            int invOrigen, int invDestino, string upc, string upcUnidad, int fardos)
        {
            if (fardos <= 0) return;

            string codigo2 = string.IsNullOrEmpty(upcUnidad) ? upc : upcUnidad;
            string upcFila, dun14Fila;
            decimal cantidadFila;
            int unidadesFardo;

            using (var cmd = new MySqlCommand(@"
                SELECT Upc, DUN14, Cantidad, COALESCE(UnidadesFardo, 0) AS UnidadesFardo
                FROM detalleinventarios
                WHERE IdInventarios = @IdInventario
                  AND (Upc IN (@Codigo1, @Codigo2) OR DUN14 IN (@Codigo1, @Codigo2))
                LIMIT 1 FOR UPDATE", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdInventario", invOrigen);
                cmd.Parameters.AddWithValue("@Codigo1", upc);
                cmd.Parameters.AddWithValue("@Codigo2", codigo2);
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return; // ya no está en el inventario de origen
                    upcFila = Convert.ToString(r["Upc"]);
                    dun14Fila = r.IsDBNull(r.GetOrdinal("DUN14")) ? null : Convert.ToString(r["DUN14"]);
                    cantidadFila = Convert.ToDecimal(r["Cantidad"]);
                    unidadesFardo = Convert.ToInt32(r["UnidadesFardo"]);
                }
            }

            decimal mover = Math.Min(fardos * (unidadesFardo > 0 ? unidadesFardo : 1), cantidadFila);
            if (mover <= 0) return;

            // Primero sumar en destino (el INSERT copia los datos del renglón origen)
            int filas;
            using (var cmd = new MySqlCommand(@"
                UPDATE detalleinventarios SET Cantidad = Cantidad + @Mover
                WHERE IdInventarios = @Destino AND Upc = @Upc AND DUN14 <=> @DUN14
                LIMIT 1", conn, tr))
            {
                cmd.Parameters.AddWithValue("@Mover", mover);
                cmd.Parameters.AddWithValue("@Destino", invDestino);
                cmd.Parameters.AddWithValue("@Upc", upcFila);
                cmd.Parameters.AddWithValue("@DUN14", (object)dun14Fila ?? DBNull.Value);
                filas = cmd.ExecuteNonQuery();
            }
            if (filas == 0)
            {
                using (var cmd = new MySqlCommand(@"
                    INSERT INTO detalleinventarios (
                        IdInventarios, DUN14, Upc, Cantidad, UnidadesFardo,
                        Descripcion, IdProveedor, IdDepartamento, Costo, IdRazonSocial
                    )
                    SELECT @Destino, DUN14, Upc, @Mover, UnidadesFardo,
                           Descripcion, IdProveedor, IdDepartamento, Costo, IdRazonSocial
                    FROM detalleinventarios
                    WHERE IdInventarios = @Origen AND Upc = @Upc AND DUN14 <=> @DUN14
                    LIMIT 1", conn, tr))
                {
                    cmd.Parameters.AddWithValue("@Destino", invDestino);
                    cmd.Parameters.AddWithValue("@Mover", mover);
                    cmd.Parameters.AddWithValue("@Origen", invOrigen);
                    cmd.Parameters.AddWithValue("@Upc", upcFila);
                    cmd.Parameters.AddWithValue("@DUN14", (object)dun14Fila ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }

            // Luego restar en origen (si queda en cero, se elimina el renglón)
            string sqlOrigen = cantidadFila - mover <= 0
                ? @"DELETE FROM detalleinventarios
                    WHERE IdInventarios = @Origen AND Upc = @Upc AND DUN14 <=> @DUN14 LIMIT 1"
                : @"UPDATE detalleinventarios SET Cantidad = Cantidad - @Mover
                    WHERE IdInventarios = @Origen AND Upc = @Upc AND DUN14 <=> @DUN14 LIMIT 1";
            using (var cmd = new MySqlCommand(sqlOrigen, conn, tr))
            {
                cmd.Parameters.AddWithValue("@Mover", mover);
                cmd.Parameters.AddWithValue("@Origen", invOrigen);
                cmd.Parameters.AddWithValue("@Upc", upcFila);
                cmd.Parameters.AddWithValue("@DUN14", (object)dun14Fila ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        // El inventario de una tarima de preparación que quedó sin renglones se anula (Estado 4).
        // Nunca se anula el inventario de una tarima de embalaje.
        private static void AnularSiQuedoVacio(MySqlConnection conn, MySqlTransaction tr, int idInventario)
        {
            using (var cmd = new MySqlCommand(@"
                UPDATE inventarios SET Estado = @Anulado
                WHERE idInventarios = @Id AND COALESCE(Estado, 0) = @Abierto
                  AND NOT EXISTS (SELECT 1 FROM detalleinventarios WHERE IdInventarios = @Id)
                  AND NOT EXISTS (SELECT 1 FROM TarimasEmbalaje WHERE IdInventario = @Id)", conn, tr))
            {
                cmd.Parameters.AddWithValue("@Anulado", InventarioAnulado);
                cmd.Parameters.AddWithValue("@Abierto", InventarioAbierto);
                cmd.Parameters.AddWithValue("@Id", idInventario);
                cmd.ExecuteNonQuery();
            }
        }

        // ─────────────────────────────────────────
        // CERRAR Y SALIDA
        // ─────────────────────────────────────────

        // Estado 1 → 2. Ya no se pueden meter ni quitar productos.
        public static void CerrarTarima(MySqlConnection conn, MySqlTransaction tr, int idTarimaEmbalada, int idUsuario)
        {
            var tarima = Leer(conn, tr, idTarimaEmbalada, bloquear: true)
                ?? throw new Exception("La tarima de embalaje ya no existe.");
            if (tarima.Estado != EstadoEmbalando)
                throw new TarimaEmbalajeCerradaException(tarima.Codigo);
            if (tarima.CantidadSkus == 0)
                throw new Exception($"La tarima {tarima.Codigo} no tiene productos; no se puede cerrar.");

            using (var cmd = new MySqlCommand(@"
                UPDATE TarimasEmbalaje
                SET Estado = @Embalada, IdUsuarioCierre = @IdUsuario, FechaCierre = NOW()
                WHERE IdTarimaEmbalada = @Id", conn, tr))
            {
                cmd.Parameters.AddWithValue("@Embalada", EstadoEmbalada);
                cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                cmd.Parameters.AddWithValue("@Id", idTarimaEmbalada);
                cmd.ExecuteNonQuery();
            }
        }

        // Tarima embalada (Estado 2) que se sube al camión. Su lote pasa a salida (2): la próxima
        // tarima de la sucursal abre un lote nuevo y el correlativo empieza otra vez en 1.
        public static TarimaEmbalaje RegistrarSalida(MySqlConnection conn, MySqlTransaction tr, int idTarimaEmbalada, int idUsuario)
        {
            var tarima = Leer(conn, tr, idTarimaEmbalada, bloquear: true)
                ?? throw new Exception($"No existe la tarima de embalaje Id {idTarimaEmbalada}.");
            if (tarima.Estado != EstadoEmbalada)
                throw new Exception($"La tarima {tarima.Codigo} aún se está embalando. Debe cerrarse antes de darle salida.");
            if (tarima.FechaSalida.HasValue)
                throw new Exception($"La tarima {tarima.Codigo} ya tiene salida registrada ({tarima.FechaSalida.Value:dd/MM/yyyy HH:mm}).");

            using (var cmd = new MySqlCommand(@"
                UPDATE TarimasEmbalaje SET FechaSalida = NOW(), IdUsuarioSalida = @IdUsuario
                WHERE IdTarimaEmbalada = @Id", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
                cmd.Parameters.AddWithValue("@Id", idTarimaEmbalada);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new MySqlCommand(@"
                UPDATE LotesEmbalaje SET Estado = @EnSalida, FechaSalida = COALESCE(FechaSalida, NOW())
                WHERE IdLote = @IdLote", conn, tr))
            {
                cmd.Parameters.AddWithValue("@EnSalida", LoteEnSalida);
                cmd.Parameters.AddWithValue("@IdLote", tarima.IdLote);
                cmd.ExecuteNonQuery();
            }

            return Leer(conn, tr, idTarimaEmbalada);
        }
    }
}
