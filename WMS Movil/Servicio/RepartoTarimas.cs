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
    }
}
