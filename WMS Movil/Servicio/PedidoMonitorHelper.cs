using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using MySqlConnector;
using Xamarin.Essentials;

namespace WMS_Movil.Servicio
{
    // Consulta compartida (usada tanto por el monitoreo en primer plano como por el
    // servicio en segundo plano) de los pedidos asignados a un usuario que aún no
    // ha iniciado a preparar.
    public static class PedidoMonitorHelper
    {
        // Si FechaHoraAsigno viene NULL en la BD (p.ej. una asignación hecha a mano
        // sin llenar esa columna), se recuerda la primera hora en que ESTE dispositivo
        // la detectó, para que el cronómetro no se reinicie en cada consulta.
        private static readonly ConcurrentDictionary<int, DateTime> FechaAsignoRespaldo = new ConcurrentDictionary<int, DateTime>();

        public static List<PedidoAsignadoInfo> ObtenerPedidosAsignadosSinIniciar(int idUsuario)
        {
            var resultado = new List<PedidoAsignadoInfo>();
            if (idUsuario <= 0) return resultado;

            string connectionString = $"Server={Preferences.Get("ip_address", "")};Database={Preferences.Get("selected_database", "")};User ID={Preferences.Get("username", "")};Password={Preferences.Get("password", "")};";

            using (var connection = new MySqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand(@"
                    SELECT Idpreparo, IdPedido, NoHoja, Sucursal, NombreUsuarioAsigno, FechaHoraAsigno
                    FROM PreparacionPedidos
                    WHERE IdUsuario = @IdUsuario
                      AND FechaHoraInicio IS NULL
                      AND FechaHorafinalizo IS NULL
                    ORDER BY FechaHoraAsigno ASC", connection))
                {
                    command.Parameters.AddWithValue("@IdUsuario", idUsuario);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int idPreparo = reader.GetInt32("Idpreparo");
                            DateTime fechaAsigno;

                            if (!reader.IsDBNull(reader.GetOrdinal("FechaHoraAsigno")))
                            {
                                fechaAsigno = reader.GetDateTime("FechaHoraAsigno");
                            }
                            else if (!FechaAsignoRespaldo.TryGetValue(idPreparo, out fechaAsigno))
                            {
                                fechaAsigno = DateTime.Now;
                                FechaAsignoRespaldo[idPreparo] = fechaAsigno;
                                Debug.WriteLine($"PedidoMonitorHelper: Idpreparo {idPreparo} no tiene FechaHoraAsigno en la BD; usando hora local de primera detección como respaldo.");
                            }

                            resultado.Add(new PedidoAsignadoInfo
                            {
                                IdPreparacion = idPreparo,
                                IdPedido = reader.GetInt32("IdPedido"),
                                NoHoja = reader.GetInt32("NoHoja"),
                                Sucursal = reader.IsDBNull(reader.GetOrdinal("Sucursal")) ? "" : reader.GetString("Sucursal"),
                                NombreUsuarioAsigno = reader.IsDBNull(reader.GetOrdinal("NombreUsuarioAsigno")) ? "" : reader.GetString("NombreUsuarioAsigno"),
                                FechaHoraAsigno = fechaAsigno
                            });
                        }
                    }
                }
            }

            return resultado;
        }
    }
}
