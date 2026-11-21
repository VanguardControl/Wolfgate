using System;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Npgsql;
using Robust.Shared.Configuration;
using Robust.Shared.Network;

namespace Content.Server.Symphony;

/// <summary>
/// The Symphony panel's one-time Discord link tickets, kept in discord_links, a table the panel creates in this
/// server's own PostgreSQL database. It is not part of the EF model, so it is queried on a connection of its own;
/// a whitelist refusal is rare enough that opening one per ticket costs nothing worth a pool, and it keeps the
/// database layer upstream owns untouched.
/// </summary>
public static class SymphonyLinkTickets
{
    private const string LinkedSql =
        "SELECT EXISTS (SELECT 1 FROM discord_links WHERE user_id = @user AND valid = TRUE AND discord_id IS NOT NULL)";

    // A pending ticket is a row the panel has not claimed yet; the panel stops resolving it after four hours.
    private const string PendingTicketSql =
        """
        SELECT one_time_token FROM discord_links
        WHERE user_id = @user AND discord_id IS NULL AND "timestamp" > NOW() - INTERVAL '4 hours'
        ORDER BY "timestamp" DESC LIMIT 1
        """;

    private const string MintTicketSql =
        "INSERT INTO discord_links (user_id, one_time_token) VALUES (@user, @token)";

    // Keyed on the user id: two attempts from one player queue behind each other, and nobody else waits.
    private const string LockSql = "SELECT pg_advisory_xact_lock(hashtextextended(@user, 0))";

    /// <summary>
    /// Whether the player holds a live Discord link, without minting a ticket. Null when this server is not on
    /// PostgreSQL, where the panel has no bridge.
    /// </summary>
    public static async Task<bool?> IsLinkedAsync(IConfigurationManager cfg, NetUserId userId)
    {
        if (!string.Equals(cfg.GetCVar(CCVars.DatabaseEngine), "postgres", StringComparison.OrdinalIgnoreCase))
            return null;

        await using var connection = new NpgsqlConnection(ConnectionString(cfg));
        await connection.OpenAsync();
        await using var linked = new NpgsqlCommand(LinkedSql, connection);
        linked.Parameters.AddWithValue("user", userId.UserId);
        return await linked.ExecuteScalarAsync() is true;
    }

    private static string ConnectionString(IConfigurationManager cfg)
    {
        return new NpgsqlConnectionStringBuilder
        {
            Host = cfg.GetCVar(CCVars.DatabasePgHost),
            Port = cfg.GetCVar(CCVars.DatabasePgPort),
            Database = cfg.GetCVar(CCVars.DatabasePgDatabase),
            Username = cfg.GetCVar(CCVars.DatabasePgUsername),
            Password = cfg.GetCVar(CCVars.DatabasePgPassword),
        }.ConnectionString;
    }

    /// <summary>
    /// Finds or mints the player's ticket. Returns linked when they already hold a live link, and then mints nothing;
    /// otherwise the ticket to hand them. Both false and null when this server is not on PostgreSQL, where the panel
    /// has no bridge.
    /// </summary>
    public static async Task<(bool linked, Guid? ticket)> GetOrMintAsync(IConfigurationManager cfg, NetUserId userId)
    {
        if (!string.Equals(cfg.GetCVar(CCVars.DatabaseEngine), "postgres", StringComparison.OrdinalIgnoreCase))
            return (false, null);

        // The same database the game itself uses, from the same cvars.
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = cfg.GetCVar(CCVars.DatabasePgHost),
            Port = cfg.GetCVar(CCVars.DatabasePgPort),
            Database = cfg.GetCVar(CCVars.DatabasePgDatabase),
            Username = cfg.GetCVar(CCVars.DatabasePgUsername),
            Password = cfg.GetCVar(CCVars.DatabasePgPassword),
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // One attempt per player at a time. A launcher retry racing the first refusal would mint twice, and the
        // panel burns only the token the player was shown. The lock lives with the transaction, so an early
        // return releases it.
        await using var tx = await connection.BeginTransactionAsync();
        await using (var gate = new NpgsqlCommand(LockSql, connection, tx))
        {
            gate.Parameters.AddWithValue("user", userId.UserId.ToString());
            await gate.ExecuteNonQueryAsync();
        }

        // A live link must never be overwritten, so a linked player gets no ticket at all.
        await using (var linked = new NpgsqlCommand(LinkedSql, connection, tx))
        {
            linked.Parameters.AddWithValue("user", userId.UserId);
            if (await linked.ExecuteScalarAsync() is true)
                return (true, null);
        }

        await using (var pending = new NpgsqlCommand(PendingTicketSql, connection, tx))
        {
            pending.Parameters.AddWithValue("user", userId.UserId);
            if (await pending.ExecuteScalarAsync() is Guid existing)
                return (false, existing);
        }

        var ticket = Guid.NewGuid();
        await using (var mint = new NpgsqlCommand(MintTicketSql, connection, tx))
        {
            mint.Parameters.AddWithValue("user", userId.UserId);
            mint.Parameters.AddWithValue("token", ticket);
            await mint.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return (false, ticket);
    }
}
