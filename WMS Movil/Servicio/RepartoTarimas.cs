using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace WMS_Movil.Servicio
{
    public class ParteTarima
    {
        public int NoTarima { get; set; }
        public int Cantidad { get; set; }
    }

    // Fila de detallepedidostienda_bodega de un producto repartido en varias tarimas.
    public class ProductoRepartido
    {
        public string Upc { get; set; }
        public string UpcProducto { get; set; }
        public string Descripcion { get; set; }
        public int NoHoja { get; set; }
        public int CantidadSolicitada { get; set; }
        public int CantConfirmada { get; set; }
        public int EstadoPreparacion { get; set; }
        public string Observaciones { get; set; }
        public List<ParteTarima> Partes { get; set; } = new List<ParteTarima>();

        public int CantidadEn(int noTarima) =>
            Partes.Where(p => p.NoTarima == noTarima).Sum(p => p.Cantidad);

        public int TotalRepartido => Partes.Sum(p => p.Cantidad);

        // Texto para el usuario: cuánto lleva esta tarima, dónde está el resto y el total.
        public string ResumenPara(int noTarima)
        {
            var resto = Partes.Where(p => p.NoTarima != noTarima)
                              .Select(p => $"Tarima {p.NoTarima} → {p.Cantidad}");
            return $"📦 En esta tarima: {CantidadEn(noTarima)} fardos\n" +
                   $"Resto: {string.Join(", ", resto)}\n" +
                   $"Total: {TotalRepartido} fardos (solicitado {CantidadSolicitada})";
        }
    }

    // Productos repartidos en varias tarimas.
    // En detallepedidostienda_bodega quedan con NoTarima = 0 y el reparto se guarda al
    // inicio de Observaciones con formato fijo, p.ej.:
    //   "[TARIMAS:4=102;5=102;6=96] Repartido en varias tarimas - <observación original>"
    // El sistema solo lee la etiqueta entre corchetes; el resto es texto para las personas.
    public static class RepartoTarimas
    {
        public const string Prefijo = "[TARIMAS:";
        private const string TextoRepartido = "Repartido en varias tarimas";
        private static readonly Regex EtiquetaRegex = new Regex(@"^\[TARIMAS:([^\]]*)\]");

        public static bool TieneReparto(string observaciones) =>
            observaciones != null && observaciones.StartsWith(Prefijo, StringComparison.Ordinal);

        public static List<ParteTarima> Leer(string observaciones)
        {
            var partes = new List<ParteTarima>();
            if (!TieneReparto(observaciones)) return partes;

            var match = EtiquetaRegex.Match(observaciones);
            if (!match.Success) return partes;

            foreach (var item in match.Groups[1].Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = item.Split('=');
                if (kv.Length == 2
                    && int.TryParse(kv[0].Trim(), out int noTarima)
                    && int.TryParse(kv[1].Trim(), out int cantidad)
                    && noTarima > 0 && cantidad > 0)
                {
                    partes.Add(new ParteTarima { NoTarima = noTarima, Cantidad = cantidad });
                }
            }
            return partes;
        }

        public static string Etiqueta(IEnumerable<ParteTarima> partes) =>
            Prefijo + string.Join(";", partes.OrderBy(p => p.NoTarima).Select(p => $"{p.NoTarima}={p.Cantidad}")) + "]";

        public static string ArmarObservaciones(IEnumerable<ParteTarima> partes, string observacionOriginal)
        {
            string texto = $"{Etiqueta(partes)} {TextoRepartido}";
            if (!string.IsNullOrWhiteSpace(observacionOriginal))
                texto += " - " + observacionOriginal.Trim();
            return texto;
        }

        // La observación que tenía el producto antes de repartirlo (sin etiqueta ni texto fijo).
        public static string ObservacionOriginal(string observaciones)
        {
            if (!TieneReparto(observaciones)) return observaciones ?? string.Empty;

            string resto = EtiquetaRegex.Replace(observaciones, "", 1).Trim();
            if (resto.StartsWith(TextoRepartido, StringComparison.Ordinal))
                resto = resto.Substring(TextoRepartido.Length).Trim();
            if (resto.StartsWith("-", StringComparison.Ordinal))
                resto = resto.Substring(1).Trim();
            return resto;
        }

        // Cambia solo la etiqueta y conserva el resto del texto.
        public static string ReemplazarPartes(string observaciones, IEnumerable<ParteTarima> partes) =>
            TieneReparto(observaciones)
                ? EtiquetaRegex.Replace(observaciones, Etiqueta(partes), 1)
                : ArmarObservaciones(partes, observaciones);

        // Mueve lo que había en la tarima origen a la destino (sumando si la destino ya tenía).
        public static List<ParteTarima> Trasladar(IEnumerable<ParteTarima> partes, int noTarimaOrigen, int noTarimaDestino)
        {
            var resultado = partes.Select(p => new ParteTarima
            {
                NoTarima = p.NoTarima == noTarimaOrigen ? noTarimaDestino : p.NoTarima,
                Cantidad = p.Cantidad
            });
            return resultado
                .GroupBy(p => p.NoTarima)
                .Select(g => new ParteTarima { NoTarima = g.Key, Cantidad = g.Sum(p => p.Cantidad) })
                .OrderBy(p => p.NoTarima)
                .ToList();
        }

        // Igual que la renumeración de TarimasInventario al eliminar una tarima vacía.
        public static List<ParteTarima> Renumerar(IEnumerable<ParteTarima> partes, int noTarimaEliminada) =>
            partes.Select(p => new ParteTarima
            {
                NoTarima = p.NoTarima > noTarimaEliminada ? p.NoTarima - 1 : p.NoTarima,
                Cantidad = p.Cantidad
            }).ToList();

        // ─────────────────────────────────────────
        // BASE DE DATOS
        // ─────────────────────────────────────────

        public static List<ProductoRepartido> ObtenerRepartidos(MySqlConnection conn, MySqlTransaction tr, int idPedido)
        {
            var lista = new List<ProductoRepartido>();
            using (var cmd = new MySqlCommand(@"
                SELECT UPC,
                       COALESCE(UPCProducto, '') AS UPCProducto,
                       COALESCE(Descripcion, UPC) AS Descripcion,
                       NoHoja,
                       COALESCE(Cantidad, 0) AS Cantidad,
                       COALESCE(CantConfirmada, 0) AS CantConfirmada,
                       COALESCE(EstadoPreparacionproducto, 0) AS EstadoPreparacionproducto,
                       Observaciones
                FROM detallepedidostienda_bodega
                WHERE IdConsolidado = @IdPedido
                  AND NoTarima = 0
                  AND CantConfirmada > 0
                  AND Observaciones LIKE '[TARIMAS:%'", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        string obs = r.GetString("Observaciones");
                        var partes = Leer(obs);
                        if (partes.Count == 0) continue;

                        lista.Add(new ProductoRepartido
                        {
                            Upc = r.GetString("UPC"),
                            UpcProducto = Convert.ToString(r["UPCProducto"]),
                            Descripcion = Convert.ToString(r["Descripcion"]),
                            NoHoja = Convert.ToInt32(r["NoHoja"]),
                            CantidadSolicitada = Convert.ToInt32(r["Cantidad"]),
                            CantConfirmada = Convert.ToInt32(r["CantConfirmada"]),
                            EstadoPreparacion = Convert.ToInt32(r["EstadoPreparacionproducto"]),
                            Observaciones = obs,
                            Partes = partes
                        });
                    }
                }
            }
            return lista;
        }

        public static void GuardarPartes(MySqlConnection conn, MySqlTransaction tr, int idPedido,
            ProductoRepartido producto, IEnumerable<ParteTarima> partes)
        {
            using (var cmd = new MySqlCommand(@"
                UPDATE detallepedidostienda_bodega
                SET Observaciones = @Observaciones
                WHERE IdConsolidado = @IdPedido AND UPC = @UPC AND NoHoja = @NoHoja
                  AND NoTarima = 0 AND Observaciones LIKE '[TARIMAS:%'", conn, tr))
            {
                cmd.Parameters.AddWithValue("@Observaciones", ReemplazarPartes(producto.Observaciones, partes));
                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                cmd.Parameters.AddWithValue("@UPC", producto.Upc);
                cmd.Parameters.AddWithValue("@NoHoja", producto.NoHoja);
                cmd.ExecuteNonQuery();
            }
        }

        // Llamar después de eliminar una tarima y renumerar TarimasInventario.
        public static void RenumerarTrasEliminar(MySqlConnection conn, MySqlTransaction tr, int idPedido, int noTarimaEliminada)
        {
            foreach (var p in ObtenerRepartidos(conn, tr, idPedido))
            {
                if (p.Partes.Any(x => x.NoTarima > noTarimaEliminada))
                    GuardarPartes(conn, tr, idPedido, p, Renumerar(p.Partes, noTarimaEliminada));
            }
        }

        // Cantidad de productos en la tarima: filas normales + repartidos que tienen parte en ella.
        public static int ContarProductosEnTarima(MySqlConnection conn, MySqlTransaction tr, int idPedido, int noTarima)
        {
            int normales;
            using (var cmd = new MySqlCommand(@"
                SELECT COUNT(*) FROM detallepedidostienda_bodega
                WHERE IdConsolidado = @IdPedido AND NoTarima = @NoTarima", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                cmd.Parameters.AddWithValue("@NoTarima", noTarima);
                normales = Convert.ToInt32(cmd.ExecuteScalar());
            }
            return normales + ObtenerRepartidos(conn, tr, idPedido).Count(p => p.CantidadEn(noTarima) > 0);
        }

        // SKUs y fardos de la tarima: filas normales + partes de productos repartidos.
        public static (int Skus, int Fardos) ContarSkusYFardos(MySqlConnection conn, MySqlTransaction tr, int idPedido, int noTarima)
        {
            int skus = 0, fardos = 0;
            using (var cmd = new MySqlCommand(@"
                SELECT COUNT(*) AS Skus, COALESCE(SUM(CantConfirmada), 0) AS Fardos
                FROM detallepedidostienda_bodega
                WHERE IdConsolidado = @IdPedido AND NoTarima = @NoTarima", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                cmd.Parameters.AddWithValue("@NoTarima", noTarima);
                using (var r = cmd.ExecuteReader())
                {
                    if (r.Read())
                    {
                        skus = Convert.ToInt32(r["Skus"]);
                        fardos = Convert.ToInt32(r["Fardos"]);
                    }
                }
            }

            foreach (var p in ObtenerRepartidos(conn, tr, idPedido))
            {
                int cantidad = p.CantidadEn(noTarima);
                if (cantidad <= 0) continue;
                skus++;
                fardos += cantidad;
            }
            return (skus, fardos);
        }

        public static void RecalcularTarima(MySqlConnection conn, MySqlTransaction tr, int idTarima, int idPedido, int noTarima)
        {
            var (skus, fardos) = ContarSkusYFardos(conn, tr, idPedido, noTarima);

            using (var cmd = new MySqlCommand(@"
                UPDATE TarimasInventario
                SET CantidadSkus = @Skus, CantidadFardos = @Fardos
                WHERE IdTarima = @IdTarima", conn, tr))
            {
                cmd.Parameters.AddWithValue("@Skus", skus);
                cmd.Parameters.AddWithValue("@Fardos", fardos);
                cmd.Parameters.AddWithValue("@IdTarima", idTarima);
                cmd.ExecuteNonQuery();
            }
        }

        // ─────────────────────────────────────────
        // ENTREGA Y ELIMINACIÓN DE TARIMAS
        // ─────────────────────────────────────────

        public enum ResultadoEntrega { NoExiste, Cerrada, Eliminada }

        // Una tarima con productos se cierra; una vacía se elimina y se renumeran las del pedido.
        public static ResultadoEntrega EntregarTarima(MySqlConnection conn, MySqlTransaction tr, int idTarima)
        {
            int idPedido = 0, noTarima = 0;
            using (var cmd = new MySqlCommand(@"
                SELECT IdPedido, NoTarima FROM TarimasInventario
                WHERE IdTarima = @IdTarima FOR UPDATE", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdTarima", idTarima);
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return ResultadoEntrega.NoExiste;
                    idPedido = Convert.ToInt32(r["IdPedido"]);
                    noTarima = Convert.ToInt32(r["NoTarima"]);
                }
            }

            if (ContarProductosEnTarima(conn, tr, idPedido, noTarima) == 0)
            {
                EliminarTarimaVacia(conn, tr, idTarima, idPedido, noTarima);
                return ResultadoEntrega.Eliminada;
            }

            var (skus, fardos) = ContarSkusYFardos(conn, tr, idPedido, noTarima);
            using (var cmd = new MySqlCommand(@"
                UPDATE TarimasInventario
                SET FechaFinalizacion = COALESCE(FechaFinalizacion, NOW()),
                    CantidadFardos = @Fardos, CantidadSkus = @Skus
                WHERE IdTarima = @IdTarima", conn, tr))
            {
                cmd.Parameters.AddWithValue("@Fardos", fardos);
                cmd.Parameters.AddWithValue("@Skus", skus);
                cmd.Parameters.AddWithValue("@IdTarima", idTarima);
                cmd.ExecuteNonQuery();
            }
            return ResultadoEntrega.Cerrada;
        }

        // Elimina la tarima y renumera las posteriores del pedido (tarimas, detalle y etiquetas de reparto).
        public static void EliminarTarimaVacia(MySqlConnection conn, MySqlTransaction tr, int idTarima, int idPedido, int noTarima)
        {
            using (var cmd = new MySqlCommand(
                "DELETE FROM TarimasInventario WHERE IdTarima = @IdTarima", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdTarima", idTarima);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new MySqlCommand(@"
                UPDATE TarimasInventario
                SET NoTarima = NoTarima - 1
                WHERE IdPedido = @IdPedido AND NoTarima > @NoTarima
                ORDER BY NoTarima ASC", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                cmd.Parameters.AddWithValue("@NoTarima", noTarima);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new MySqlCommand(@"
                UPDATE detallepedidostienda_bodega
                SET NoTarima = NoTarima - 1
                WHERE IdConsolidado = @IdPedido AND NoTarima > @NoTarima
                ORDER BY NoTarima ASC", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdPedido", idPedido);
                cmd.Parameters.AddWithValue("@NoTarima", noTarima);
                cmd.ExecuteNonQuery();
            }

            RenumerarTrasEliminar(conn, tr, idPedido, noTarima);
        }

        // ─────────────────────────────────────────
        // INVENTARIO POR TARIMA
        // ─────────────────────────────────────────

        public static int? LeerInventarioTarima(MySqlConnection conn, MySqlTransaction tr, int idTarima)
        {
            using (var cmd = new MySqlCommand(
                "SELECT IdInventario FROM TarimasInventario WHERE IdTarima = @IdTarima", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdTarima", idTarima);
                var res = cmd.ExecuteScalar();
                if (res == null || res == DBNull.Value) return null;
                int id = Convert.ToInt32(res);
                return id > 0 ? id : (int?)null;
            }
        }

        // Inventario de la tarima y su estado (0 = abierto). Ambos null si la tarima aún no tiene inventario.
        // Con bloquear = true se bloquea el inventario hasta el fin de la transacción.
        public static (int? IdInventario, int? Estado) LeerEstadoInventarioTarima(
            MySqlConnection conn, MySqlTransaction tr, int idTarima, bool bloquear = false)
        {
            using (var cmd = new MySqlCommand($@"
                SELECT i.idInventarios, COALESCE(i.Estado, 0) AS Estado
                FROM TarimasInventario ti
                INNER JOIN inventarios i ON i.idInventarios = ti.IdInventario
                WHERE ti.IdTarima = @IdTarima
                {(bloquear ? "FOR UPDATE" : "")}", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdTarima", idTarima);
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return (null, null);
                    return (Convert.ToInt32(r["idInventarios"]), Convert.ToInt32(r["Estado"]));
                }
            }
        }

        // Lanza excepción si el inventario de la tarima ya no está en estado 0 (ya fue confirmado).
        public static void ValidarInventarioAbierto(MySqlConnection conn, MySqlTransaction tr, int idTarima, int noTarima)
        {
            var (idInventario, estado) = LeerEstadoInventarioTarima(conn, tr, idTarima, bloquear: true);
            if (estado.HasValue && estado.Value != 0)
                throw new Exception(
                    $"El inventario #{idInventario} de la Tarima #{noTarima} ya está confirmado. No se puede trasladar.");
        }

        // Crea un inventario con el mismo encabezado que otro y lo asigna a la tarima.
        private static int CrearInventarioCopia(MySqlConnection conn, MySqlTransaction tr, int idInventarioBase, int idTarima)
        {
            int idNuevo;
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
                idNuevo = Convert.ToInt32(cmd.ExecuteScalar());
            }

            using (var cmd = new MySqlCommand(
                "UPDATE TarimasInventario SET IdInventario = @IdInventario WHERE IdTarima = @IdTarima", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdInventario", idNuevo);
                cmd.Parameters.AddWithValue("@IdTarima", idTarima);
                cmd.ExecuteNonQuery();
            }
            return idNuevo;
        }

        // Pasa lo que un producto ocupa en el inventario de la tarima origen al de la tarima destino.
        // "fardos" es lo que se traslada; el inventario guarda unidades cuando el renglón es de
        // paquete (DUN14 + UnidadesFardo) y fardos cuando no.
        public static void MoverProductoEntreInventarios(MySqlConnection conn, MySqlTransaction tr,
            int idTarimaOrigen, int idTarimaDestino, string upc, string upcProducto, decimal fardos)
        {
            if (fardos <= 0) return;

            int? invOrigen = LeerInventarioTarima(conn, tr, idTarimaOrigen);
            if (!invOrigen.HasValue) return; // tarima sin inventario (datos anteriores)

            int? invDestino = LeerInventarioTarima(conn, tr, idTarimaDestino);
            if (invDestino == invOrigen) return; // mismo inventario (datos anteriores, por hoja)

            string codigo2 = string.IsNullOrEmpty(upcProducto) ? upc : upcProducto;
            string upcFila = null, dun14Fila = null;
            decimal cantidadFila = 0;
            int unidadesFardo = 0;

            using (var cmd = new MySqlCommand(@"
                SELECT Upc, DUN14, Cantidad, COALESCE(UnidadesFardo, 0) AS UnidadesFardo
                FROM detalleinventarios
                WHERE IdInventarios = @IdInventario
                  AND (Upc IN (@Codigo1, @Codigo2) OR DUN14 IN (@Codigo1, @Codigo2))
                LIMIT 1 FOR UPDATE", conn, tr))
            {
                cmd.Parameters.AddWithValue("@IdInventario", invOrigen.Value);
                cmd.Parameters.AddWithValue("@Codigo1", upc);
                cmd.Parameters.AddWithValue("@Codigo2", codigo2);
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return;
                    upcFila = Convert.ToString(r["Upc"]);
                    dun14Fila = r.IsDBNull(r.GetOrdinal("DUN14")) ? null : Convert.ToString(r["DUN14"]);
                    cantidadFila = Convert.ToDecimal(r["Cantidad"]);
                    unidadesFardo = Convert.ToInt32(r["UnidadesFardo"]);
                }
            }

            decimal mover = Math.Min(fardos * (unidadesFardo > 0 ? unidadesFardo : 1), cantidadFila);
            if (mover <= 0) return;

            if (!invDestino.HasValue)
                invDestino = CrearInventarioCopia(conn, tr, invOrigen.Value, idTarimaDestino);

            // Primero sumar en destino (el INSERT copia los datos del renglón origen)
            int filas;
            using (var cmd = new MySqlCommand(@"
                UPDATE detalleinventarios SET Cantidad = Cantidad + @Mover
                WHERE IdInventarios = @Destino AND Upc = @Upc AND DUN14 <=> @DUN14
                LIMIT 1", conn, tr))
            {
                cmd.Parameters.AddWithValue("@Mover", mover);
                cmd.Parameters.AddWithValue("@Destino", invDestino.Value);
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
                    cmd.Parameters.AddWithValue("@Destino", invDestino.Value);
                    cmd.Parameters.AddWithValue("@Mover", mover);
                    cmd.Parameters.AddWithValue("@Origen", invOrigen.Value);
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
                cmd.Parameters.AddWithValue("@Origen", invOrigen.Value);
                cmd.Parameters.AddWithValue("@Upc", upcFila);
                cmd.Parameters.AddWithValue("@DUN14", (object)dun14Fila ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
