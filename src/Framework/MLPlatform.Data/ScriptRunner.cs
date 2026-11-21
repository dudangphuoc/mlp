using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace MLPlatform.Data;

/// <summary>
/// Applies migrations <c>db/&lt;schema&gt;/yyyyMMddHHmm_slug.sql</c> in file-name order (20/02 §5.5).
/// One transaction per file with <c>lock_timeout 5s</c>. <c>platform.migrations</c> stores a checksum of the SQL with comments and
/// whitespace removed ("v2:…"), so editing comments is allowed but changing an applied statement fails.
/// Legacy checksums (raw file bytes) are upgraded to v2 automatically while the file is still byte-identical.
/// </summary>
public sealed class ScriptRunner(NpgsqlDataSource dataSource, TextWriter log)
{
    public sealed record Script(string Id, string Path, string Sql, string Checksum, string LegacyChecksum);

    /// <summary>SQL without comments (outside quoted strings / dollar quotes) and with whitespace collapsed.</summary>
    public static string Normalize(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        var i = 0;
        string? dollar = null;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (dollar is not null)
            {
                if (string.CompareOrdinal(sql, i, dollar, 0, dollar.Length) == 0) { sb.Append(dollar); i += dollar.Length; dollar = null; }
                else { sb.Append(c); i++; }
                continue;
            }
            if (c == '\'')
            {
                var j = i + 1;
                while (j < sql.Length && !(sql[j] == '\'' && (j + 1 >= sql.Length || sql[j + 1] != '\''))) j += sql[j] == '\'' ? 2 : 1;
                sb.Append(sql, i, Math.Min(j + 1, sql.Length) - i); i = j + 1; continue;
            }
            if (c == '$')
            {
                var j = i + 1;
                while (j < sql.Length && (char.IsLetterOrDigit(sql[j]) || sql[j] == '_')) j++;
                if (j < sql.Length && sql[j] == '$') { dollar = sql[i..(j + 1)]; sb.Append(dollar); i = j + 1; continue; }
            }
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-') { while (i < sql.Length && sql[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? sql.Length : end + 2; continue;
            }
            sb.Append(char.IsWhiteSpace(c) ? ' ' : c); i++;
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static string Hash(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    public static IReadOnlyList<Script> Discover(string dbRoot) =>
        Directory.EnumerateFiles(dbRoot, "*.sql", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{System.IO.Path.DirectorySeparatorChar}down{System.IO.Path.DirectorySeparatorChar}"))
            .Select(p =>
            {
                var sql = File.ReadAllText(p);
                var schema = new DirectoryInfo(System.IO.Path.GetDirectoryName(p)!).Name;
                var id = schema + "/" + System.IO.Path.GetFileNameWithoutExtension(p);
                return new Script(id, p, sql, "v2:" + Hash(Normalize(sql)), Hash(sql));
            })
            .OrderBy(s => s.Id[(s.Id.IndexOf('/') + 1)..], StringComparer.Ordinal)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

    public async Task<int> ApplyAsync(IReadOnlyList<Script> scripts, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await Exec(conn, null, """
            CREATE SCHEMA IF NOT EXISTS platform;
            CREATE TABLE IF NOT EXISTS platform.migrations (
                id text PRIMARY KEY, checksum text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now());
            """, ct);
        var applied = new Dictionary<string, string>();
        await using (var cmd = new NpgsqlCommand("SELECT id, checksum FROM platform.migrations", conn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) applied[r.GetString(0)] = r.GetString(1);

        var count = 0;
        foreach (var s in scripts)
        {
            if (applied.TryGetValue(s.Id, out var sum))
            {
                if (sum == s.Checksum) continue;
                if (sum == s.LegacyChecksum)
                {
                    await using var up = new NpgsqlCommand("UPDATE platform.migrations SET checksum = @c WHERE id = @id", conn);
                    up.Parameters.AddWithValue("c", s.Checksum);
                    up.Parameters.AddWithValue("id", s.Id);
                    await up.ExecuteNonQueryAsync(ct);
                    continue;
                }
                throw new InvalidOperationException($"Migration {s.Id} was already applied but its SQL has changed (checksum mismatch).");
            }
            await using var tx = await conn.BeginTransactionAsync(ct);
            await Exec(conn, tx, "SET LOCAL lock_timeout = '5s'", ct);
            await Exec(conn, tx, s.Sql, ct);
            await using (var ins = new NpgsqlCommand("INSERT INTO platform.migrations (id, checksum) VALUES (@id, @c)", conn, tx))
            {
                ins.Parameters.AddWithValue("id", s.Id);
                ins.Parameters.AddWithValue("c", s.Checksum);
                await ins.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            log.WriteLine($"applied {s.Id}");
            count++;
        }
        return count;
    }

    private static async Task Exec(NpgsqlConnection c, NpgsqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, c, tx);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
