using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Sqlite.LegacyBridge.Protocol;
using Sqlite.LegacyBridge.Protocol.Payloads;

namespace Sqlite.LegacyBridge.Host;

internal static class Program
{
    private const string PipePrefix = @"\\.\pipe\";
    private const string ClientAckLine = "PLU_LEGACY_CLIENT_ACK";

    private static void ReadExact(Stream stream, byte[] buffer, int length)
    {
        var offset = 0;
        while (offset < length)
        {
            var n = stream.Read(buffer, offset, length - offset);
            if (n == 0)
                throw new IOException("Handshake bridge: fluxo fechado antes do ACK do cliente.");
            offset += n;
        }
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
            return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    private static string? ReadUtf8Line(Stream stream)
    {
        using var acc = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            var n = stream.Read(one, 0, 1);
            if (n == 0)
                return acc.Length == 0 ? null : Encoding.UTF8.GetString(acc.ToArray());
            if (one[0] == (byte)'\n')
                return Encoding.UTF8.GetString(acc.ToArray());
            if (one[0] != (byte)'\r')
                acc.WriteByte(one[0]);
        }
    }

    private static void WriteUtf8Line(Stream stream, string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    public static int Main(string[] args)
    {
        string? pipeName = null;
        string? databasePath = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--pipe" && i + 1 < args.Length)
                pipeName = args[++i];
            else if (args[i] == "--database" && i + 1 < args.Length)
                databasePath = args[++i];
        }

        if (string.IsNullOrEmpty(pipeName))
        {
            Console.Error.WriteLine("Uso: Sqlite.LegacyBridge.Host.exe --pipe <nome_curto> [--database <caminho>]");
            Console.Error.WriteLine("Senha: PLU_SQLITE_PASSWORD. Caminho alternativo: PLU_SQLITE_DB.");
            return 2;
        }

        var pipeNameNonNull = pipeName!;

        databasePath ??= Environment.GetEnvironmentVariable("PLU_SQLITE_DB");
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            Console.Error.WriteLine("Defina --database ou PLU_SQLITE_DB.");
            return 2;
        }

        databasePath = Path.GetFullPath(databasePath.Trim());
        var password = Environment.GetEnvironmentVariable("PLU_SQLITE_PASSWORD") ?? "";

        var shortPipe = pipeNameNonNull.StartsWith(PipePrefix, StringComparison.OrdinalIgnoreCase)
            ? pipeNameNonNull.Substring(PipePrefix.Length)
            : pipeNameNonNull;

        try
        {
            using var server = new NamedPipeServerStream(
                shortPipe,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            Console.Error.WriteLine($"[LegacyBridgeHost] à espera do cliente no pipe '{shortPipe}'…");
            server.WaitForConnection();
            Console.Error.WriteLine("[LegacyBridgeHost] cliente conectado.");

            var txCounter = 0;
            var transactions = new Dictionary<int, System.Data.SQLite.SQLiteTransaction>();
            System.Data.SQLite.SQLiteTransaction? currentTx = null;

            // READY em bytes; só depois do ACK em bytes é seguro criar StreamReader no servidor
            // (senão o ctor pode consumir do pipe o que ainda não foi lido pelo cliente).
            var handshake = Encoding.UTF8.GetBytes("PLU_LEGACY_BRIDGE_READY\n");
            server.Write(handshake, 0, handshake.Length);
            server.Flush();

            var ackExpected = Encoding.UTF8.GetBytes(ClientAckLine + "\n");
            var ackBuf = new byte[ackExpected.Length];
            ReadExact(server, ackBuf, ackBuf.Length);
            if (!BytesEqual(ackBuf, ackExpected))
                throw new IOException("Handshake bridge: ACK do cliente inválido.");

            // SQLiteConnection ctor carrega nativos; fazê-lo antes do handshake atrasava o READY indefinidamente.
            using var conn = new System.Data.SQLite.SQLiteConnection(new System.Data.SQLite.SQLiteConnectionStringBuilder
            {
                DataSource = databasePath,
                Password = password,
                Version = 3,
                FailIfMissing = false,
                DefaultTimeout = 30,
                BusyTimeout = 30_000
            }.ToString());

            Console.Error.WriteLine("[LegacyBridgeHost] a abrir SQLite…");
            conn.Open();
            Console.Error.WriteLine("[LegacyBridgeHost] SQLite aberto; à escuta de comandos.");

            string? line;
            while ((line = ReadUtf8Line(server)) != null)
            {
                BridgeResponse response;
                try
                {
                    var env = JsonSerializer.Deserialize<BridgeEnvelope>(line, BridgeJson.Options);
                    if (env == null || env.ProtocolVersion != BridgeProtocolVersion.Current)
                    {
                        response = new BridgeResponse { Id = 0, Ok = false, Error = "invalid_envelope" };
                    }
                    else
                    {
                        response = Handle(env, conn, ref txCounter, transactions, ref currentTx);
                    }
                }
                catch (Exception ex)
                {
                    response = new BridgeResponse { Id = 0, Ok = false, Error = ex.Message };
                }

                WriteUtf8Line(server, JsonSerializer.Serialize(response, BridgeJson.Options));
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static BridgeResponse Handle(
        BridgeEnvelope env,
        System.Data.SQLite.SQLiteConnection conn,
        ref int txCounter,
        Dictionary<int, System.Data.SQLite.SQLiteTransaction> transactions,
        ref System.Data.SQLite.SQLiteTransaction? currentTx)
    {
        var id = env.Id;
        try
        {
            switch (env.Op)
            {
                case BridgeOps.Ping:
                    return new BridgeResponse { Id = id, Ok = true, Result = new BridgeResult { Kind = "pong" } };

                case BridgeOps.ExecuteReader:
                {
                    var payload = env.Payload.HasValue
                        ? JsonSerializer.Deserialize<SqlPayload>(env.Payload.Value.GetRawText(), BridgeJson.Options)
                        : null;
                    if (payload == null || string.IsNullOrEmpty(payload.Sql))
                        return new BridgeResponse { Id = id, Ok = false, Error = "missing_sql" };

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = payload.Sql;
                        cmd.Transaction = currentTx;
                        ParameterBinder.AddParameters(cmd, payload.Parameters);
                        using var r = cmd.ExecuteReader();
                        var cols = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToList();
                        var rows = new List<List<object?>>();
                        while (r.Read())
                        {
                            var row = new List<object?>(r.FieldCount);
                            for (var i = 0; i < r.FieldCount; i++)
                            {
                                if (r.IsDBNull(i))
                                {
                                    row.Add(null);
                                    continue;
                                }

                                var v = r.GetValue(i);
                                row.Add(v switch
                                {
                                    byte[] b => Convert.ToBase64String(b),
                                    DateTime dt => dt.ToString("o"),
                                    _ => v
                                });
                            }
                            rows.Add(row);
                        }

                        return new BridgeResponse
                        {
                            Id = id,
                            Ok = true,
                            Result = new BridgeResult { Kind = "reader", Columns = cols.ToArray(), Rows = rows }
                        };
                    }
                }

                case BridgeOps.ExecuteNonQuery:
                {
                    var payload = env.Payload.HasValue
                        ? JsonSerializer.Deserialize<SqlPayload>(env.Payload.Value.GetRawText(), BridgeJson.Options)
                        : null;
                    if (payload == null || string.IsNullOrEmpty(payload.Sql))
                        return new BridgeResponse { Id = id, Ok = false, Error = "missing_sql" };

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = payload.Sql;
                        cmd.Transaction = currentTx;
                        ParameterBinder.AddParameters(cmd, payload.Parameters);
                        var n = cmd.ExecuteNonQuery();
                        return new BridgeResponse
                        {
                            Id = id,
                            Ok = true,
                            Result = new BridgeResult { Kind = "nonQuery", RowsAffected = n }
                        };
                    }
                }

                case BridgeOps.ExecuteScalar:
                {
                    var payload = env.Payload.HasValue
                        ? JsonSerializer.Deserialize<SqlPayload>(env.Payload.Value.GetRawText(), BridgeJson.Options)
                        : null;
                    if (payload == null || string.IsNullOrEmpty(payload.Sql))
                        return new BridgeResponse { Id = id, Ok = false, Error = "missing_sql" };

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = payload.Sql;
                        cmd.Transaction = currentTx;
                        ParameterBinder.AddParameters(cmd, payload.Parameters);
                        var s = cmd.ExecuteScalar();
                        object? scalar = s == null || s == DBNull.Value ? null : s is byte[] b ? Convert.ToBase64String(b) : s;
                        return new BridgeResponse
                        {
                            Id = id,
                            Ok = true,
                            Result = new BridgeResult { Kind = "scalar", Scalar = scalar }
                        };
                    }
                }

                case BridgeOps.BeginTransaction:
                {
                    if (currentTx != null)
                        return new BridgeResponse { Id = id, Ok = false, Error = "transaction_already_active" };
                    currentTx = conn.BeginTransaction();
                    var tid = ++txCounter;
                    transactions[tid] = currentTx;
                    return new BridgeResponse
                    {
                        Id = id,
                        Ok = true,
                        Result = new BridgeResult { Kind = "transaction", TransactionId = tid }
                    };
                }

                case BridgeOps.Commit:
                {
                    var tp = env.Payload.HasValue
                        ? JsonSerializer.Deserialize<TransactionPayload>(env.Payload.Value.GetRawText(), BridgeJson.Options)
                        : null;
                    if (currentTx == null || tp == null || !transactions.ContainsKey(tp.TransactionId))
                        return new BridgeResponse { Id = id, Ok = false, Error = "no_transaction" };
                    currentTx.Commit();
                    currentTx.Dispose();
                    transactions.Remove(tp.TransactionId);
                    currentTx = null;
                    return new BridgeResponse { Id = id, Ok = true, Result = new BridgeResult { Kind = "committed" } };
                }

                case BridgeOps.Rollback:
                {
                    var tp = env.Payload.HasValue
                        ? JsonSerializer.Deserialize<TransactionPayload>(env.Payload.Value.GetRawText(), BridgeJson.Options)
                        : null;
                    if (currentTx == null || tp == null || !transactions.ContainsKey(tp.TransactionId))
                        return new BridgeResponse { Id = id, Ok = false, Error = "no_transaction" };
                    currentTx.Rollback();
                    currentTx.Dispose();
                    transactions.Remove(tp.TransactionId);
                    currentTx = null;
                    return new BridgeResponse { Id = id, Ok = true, Result = new BridgeResult { Kind = "rolledBack" } };
                }

                case BridgeOps.Close:
                    return new BridgeResponse { Id = id, Ok = true, Result = new BridgeResult { Kind = "closed" } };

                default:
                    return new BridgeResponse { Id = id, Ok = false, Error = "unknown_op:" + env.Op };
            }
        }
        catch (Exception ex)
        {
            return new BridgeResponse { Id = id, Ok = false, Error = ex.Message };
        }
    }
}
