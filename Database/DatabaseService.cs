using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.Database;

public class DatabaseService
{
    private const int CurrentSchemaVersion = 3;
    private readonly string _connectionString;

    // Serializes the high-frequency watch_data writes so concurrent batches never
    // collide on the SQLite file (which previously corrupted pooled connections and
    // caused every insert to fail with a nested-transaction InvalidOperationException).
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    // Diagnostics surfaced in the CSV export header so data-pipeline faults are visible.
    public volatile string? LastWatchInsertError;
    public long WatchInsertSuccessCount;
    public volatile string? LastWriteError;

    public DatabaseService(string dbPath)
    {
        // Default Timeout adds a busy-retry window so writers wait instead of failing.
        _connectionString = $"Data Source={dbPath};Default Timeout=30";
    }

    public async Task InitializeAsync()
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "Database", "Schema.sql");
        string sql;
        if (File.Exists(schemaPath))
            sql = await File.ReadAllTextAsync(schemaPath);
        else
            sql = EmbeddedSchema;

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        // WAL lets a writer proceed alongside readers and is persistent in the db file.
        await conn.ExecuteAsync("PRAGMA journal_mode=WAL;");
        await conn.ExecuteAsync(sql);
        await RunMigrationsAsync(conn);
    }

    private static async Task RunMigrationsAsync(SqliteConnection conn)
    {
        var version = await conn.ExecuteScalarAsync<long>("PRAGMA user_version");
        if (version < 1)
        {
            await AddColumnIfMissing(conn, "watch_data", "gyro_x", "REAL");
            await AddColumnIfMissing(conn, "watch_data", "gyro_y", "REAL");
            await AddColumnIfMissing(conn, "watch_data", "gyro_z", "REAL");
            await AddColumnIfMissing(conn, "watch_data", "rot_w", "REAL");
            await AddColumnIfMissing(conn, "watch_data", "rot_x", "REAL");
            await AddColumnIfMissing(conn, "watch_data", "rot_y", "REAL");
            await AddColumnIfMissing(conn, "watch_data", "rot_z", "REAL");
            await AddColumnIfMissing(conn, "watch_data", "baro_hpa", "REAL");
            await AddColumnIfMissing(conn, "watch_data", "light_lux", "REAL");
        }
        if (version < 2)
        {
            await AddColumnIfMissing(conn, "participants", "participant_key", "TEXT");
            await conn.ExecuteAsync("UPDATE participants SET participant_key = id WHERE participant_key IS NULL");
        }
        if (version < 3)
        {
            // `notes` previously doubled as the session name; give the name its own column
            // and free `notes` for real research notes. Preserve existing names.
            await AddColumnIfMissing(conn, "sessions", "name", "TEXT");
            await conn.ExecuteAsync("UPDATE sessions SET name = notes WHERE name IS NULL");
        }
        await conn.ExecuteAsync($"PRAGMA user_version = {CurrentSchemaVersion}");
    }

    private static async Task AddColumnIfMissing(SqliteConnection conn, string table, string column, string type)
    {
        var rows = await conn.QueryAsync<dynamic>($"PRAGMA table_info({table})");
        foreach (var r in rows)
            if ((string)r.name == column) return;
        await conn.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {type}");
    }

    public async Task InsertSessionAsync(string id, long startedAt, string? name = null, string? notes = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "INSERT OR IGNORE INTO sessions (id, started_at, name, notes) VALUES (@id, @startedAt, @name, @notes)",
            new { id, startedAt, name, notes });
    }

    public async Task UpdateSessionNotesAsync(string id, string? notes)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "UPDATE sessions SET notes = @notes WHERE id = @id",
            new { id, notes });
    }

    public async Task EndSessionAsync(string id, long endedAt)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "UPDATE sessions SET ended_at = @endedAt WHERE id = @id",
            new { id, endedAt });
    }

    public async Task UpsertParticipantAsync(string id, string sessionId, string label, string? deviceIp, string? participantKey = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "INSERT OR REPLACE INTO participants (id, session_id, label, device_ip, participant_key) " +
            "VALUES (@id, @sessionId, @label, @deviceIp, COALESCE(@participantKey, @id))",
            new { id, sessionId, label, deviceIp, participantKey });
    }

    public async Task InsertWatchDataBatchAsync(string participantId, WatchDataBatch batch)
    {
        if (batch.Snapshots == null || batch.Snapshots.Count == 0) return;

        // One writer at a time on this hot path — avoids concurrent SQLite locking.
        await _writeGate.WaitAsync();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();

            // Explicit parameters (bypass Dapper inference, which bound nothing here)
            // built once and reused per row — the documented Sqlite bulk-insert pattern.
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO watch_data (participant_id, timestamp, heart_rate, accel_x, accel_y, accel_z, step_count, " +
                "gyro_x, gyro_y, gyro_z, rot_w, rot_x, rot_y, rot_z, baro_hpa, light_lux) " +
                "VALUES ($pid, $ts, $hr, $ax, $ay, $az, $steps, $gx, $gy, $gz, $rw, $rx, $ry, $rz, $baro, $light)";
            SqliteParameter P(string name)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = name;
                cmd.Parameters.Add(p);
                return p;
            }
            var pPid = P("$pid"); var pTs = P("$ts"); var pHr = P("$hr");
            var pAx = P("$ax"); var pAy = P("$ay"); var pAz = P("$az");
            var pSteps = P("$steps");
            var pGx = P("$gx"); var pGy = P("$gy"); var pGz = P("$gz");
            var pRw = P("$rw"); var pRx = P("$rx"); var pRy = P("$ry"); var pRz = P("$rz");
            var pBaro = P("$baro"); var pLight = P("$light");

            pPid.Value = participantId;
            foreach (var s in batch.Snapshots)
            {
                pTs.Value = s.Timestamp;
                pHr.Value = s.HeartRate;
                pAx.Value = s.AccelX; pAy.Value = s.AccelY; pAz.Value = s.AccelZ;
                pSteps.Value = s.StepCount;
                pGx.Value = s.GyroX; pGy.Value = s.GyroY; pGz.Value = s.GyroZ;
                pRw.Value = s.RotationW; pRx.Value = s.RotationX; pRy.Value = s.RotationY; pRz.Value = s.RotationZ;
                pBaro.Value = s.Barometer; pLight.Value = s.Light;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            WatchInsertSuccessCount += batch.Snapshots.Count;
            LastWatchInsertError = null;
        }
        catch (Exception ex)
        {
            // Capture rather than rethrow: the export diagnostic surfaces this so a
            // failing pipeline is visible without spamming first-chance exceptions.
            LastWatchInsertError = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task InsertInterventionAsync(string id, string participantId, long sentAt, string? gameType, string notificationType)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.ExecuteAsync(
                "INSERT OR IGNORE INTO interventions (id, participant_id, sent_at, game_type, notification_type) " +
                "VALUES (@id, @participantId, @sentAt, @gameType, @notificationType)",
                new { id, participantId, sentAt, gameType, notificationType });
        }
        catch (Exception ex) { LastWriteError = $"InsertIntervention: {ex.GetType().Name}: {ex.Message}"; }
    }

    public async Task UpdateInterventionResponseAsync(string id, string response, long reactionTimeMs)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "UPDATE interventions SET response = @response, reaction_time_ms = @reactionTimeMs WHERE id = @id",
            new { id, response, reactionTimeMs });
    }

    public async Task UpsertExperimentAsync(string id, string name, string json, long version, long updatedAt, string source)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "INSERT INTO experiments (id, name, json, version, updated_at, source) " +
            "VALUES (@id, @name, @json, @version, @updatedAt, @source) " +
            "ON CONFLICT(id) DO UPDATE SET name=@name, json=@json, version=@version, updated_at=@updatedAt, source=@source",
            new { id, name, json, version, updatedAt, source });
    }

    public async Task<(string Json, long Version, long UpdatedAt)?> LoadExperimentAsync(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT json, version, updated_at FROM experiments WHERE id = @id", new { id });
        if (row == null) return null;
        return ((string)row.json, (long)row.version, (long)row.updated_at);
    }

    public async Task<(string Id, string Json, long Version, long UpdatedAt)?> LoadLatestExperimentAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT id, json, version, updated_at FROM experiments ORDER BY updated_at DESC LIMIT 1");
        if (row == null) return null;
        return ((string)row.id, (string)row.json, (long)row.version, (long)row.updated_at);
    }

    public async Task OpenRunAsync(string runId, string experimentId, string? participantId, string? sessionId, long startedAt)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.ExecuteAsync(
                "INSERT OR REPLACE INTO experiment_runs (id, experiment_id, participant_id, session_id, started_at) " +
                "VALUES (@runId, @experimentId, @participantId, @sessionId, @startedAt)",
                new { runId, experimentId, participantId, sessionId, startedAt });
        }
        catch (Exception ex) { LastWriteError = $"OpenRun: {ex.GetType().Name}: {ex.Message}"; }
    }

    public async Task CloseRunAsync(string runId, long endedAt)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "UPDATE experiment_runs SET ended_at = @endedAt WHERE id = @runId",
            new { runId, endedAt });
    }

    public async Task OpenDistractionAsync(string distractionId, string runId, string? eventId, string? interventionId, long startedAt)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.ExecuteAsync(
                "INSERT OR REPLACE INTO distraction_runs (id, experiment_run_id, event_id, intervention_id, started_at) " +
                "VALUES (@distractionId, @runId, @eventId, @interventionId, @startedAt)",
                new { distractionId, runId, eventId, interventionId, startedAt });
        }
        catch (Exception ex) { LastWriteError = $"OpenDistraction: {ex.GetType().Name}: {ex.Message}"; }
    }

    public async Task CloseDistractionAsync(string distractionId, long endedAt, string? response, long? reactionTimeMs)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "UPDATE distraction_runs SET ended_at = @endedAt, response = @response, reaction_time_ms = @reactionTimeMs WHERE id = @distractionId",
            new { distractionId, endedAt, response, reactionTimeMs });
    }

    public async Task InsertLogAsync(SessionLogEntryDto entry)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.ExecuteAsync(
                "INSERT INTO session_logs (run_id, ts, source, level, kind, payload_json) " +
                "VALUES (@runId, @ts, @source, @level, @kind, @payloadJson)",
                new { runId = entry.RunId, ts = entry.Ts, source = entry.Source, level = entry.Level, kind = entry.Kind, payloadJson = entry.PayloadJson });
        }
        catch (Exception ex) { LastWriteError = $"InsertLog: {ex.GetType().Name}: {ex.Message}"; }
    }

    public async Task<List<SessionLogEntryDto>> QueryLogsAsync(string? runId, long sinceId, int limit)
    {
        using var conn = new SqliteConnection(_connectionString);
        var sql = runId != null
            ? "SELECT id, run_id, ts, source, level, kind, payload_json FROM session_logs WHERE run_id = @runId AND id > @sinceId ORDER BY id ASC LIMIT @limit"
            : "SELECT id, run_id, ts, source, level, kind, payload_json FROM session_logs WHERE id > @sinceId ORDER BY id ASC LIMIT @limit";
        var rows = await conn.QueryAsync<dynamic>(sql, new { runId, sinceId, limit });
        var result = new List<SessionLogEntryDto>();
        foreach (var r in rows)
        {
            result.Add(new SessionLogEntryDto(
                Id: (long)r.id,
                Ts: (long)r.ts,
                Source: (string)r.source,
                Kind: (string)r.kind,
                RunId: r.run_id as string,
                Level: (string?)r.level ?? "INFO",
                PayloadJson: (string?)r.payload_json ?? ""));
        }
        return result;
    }

    public async Task SaveTriviaQuestionsAsync(IEnumerable<TriviaQuestion> questions)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync("DELETE FROM trivia_questions", transaction: tx);
        foreach (var q in questions)
        {
            var answers = q.Answers;
            await conn.ExecuteAsync(
                "INSERT INTO trivia_questions (question, answer_a, answer_b, answer_c, answer_d, correct_index) " +
                "VALUES (@question, @a, @b, @c, @d, @correctIndex)",
                new
                {
                    question = q.Question,
                    a = answers.Count > 0 ? answers[0] : null,
                    b = answers.Count > 1 ? answers[1] : null,
                    c = answers.Count > 2 ? answers[2] : null,
                    d = answers.Count > 3 ? answers[3] : null,
                    correctIndex = q.CorrectIndex
                }, tx);
        }
        await tx.CommitAsync();
    }

    public async Task<List<TriviaQuestion>> LoadTriviaQuestionsAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        var rows = await conn.QueryAsync<dynamic>(
            "SELECT id, question, answer_a, answer_b, answer_c, answer_d, correct_index FROM trivia_questions");
        var result = new List<TriviaQuestion>();
        foreach (var r in rows)
        {
            result.Add(new TriviaQuestion
            {
                Id = (int)r.id,
                Question = r.question,
                Answers = [(string)(r.answer_a ?? ""), (string)(r.answer_b ?? ""), (string)(r.answer_c ?? ""), (string)(r.answer_d ?? "")],
                CorrectIndex = (int)r.correct_index
            });
        }
        return result;
    }

    public async Task<List<SessionSummary>> LoadAllSessionsAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        var rows = await conn.QueryAsync<dynamic>(
            "SELECT s.id, s.started_at, s.ended_at, s.name, s.notes, " +
            "GROUP_CONCAT(DISTINCT p.participant_key) AS participants, " +
            "GROUP_CONCAT(DISTINCT ex.name) AS experiments, " +
            "COUNT(DISTINCT wd.id) AS watch_data_count " +
            "FROM sessions s " +
            "LEFT JOIN participants p ON p.session_id = s.id " +
            "LEFT JOIN experiment_runs er ON er.session_id = s.id " +
            "LEFT JOIN experiments ex ON ex.id = er.experiment_id " +
            "LEFT JOIN watch_data wd ON wd.participant_id = p.id " +
            "GROUP BY s.id ORDER BY s.started_at DESC");
        var result = new List<SessionSummary>();
        foreach (var r in rows)
        {
            result.Add(new SessionSummary(
                Id: (string)r.id,
                StartedAt: (long)r.started_at,
                EndedAt: r.ended_at as long?,
                Name: (string?)r.name ?? (string?)r.notes ?? "(unnamed)",
                Participants: (string?)r.participants ?? "",
                Experiments: (string?)r.experiments ?? "",
                WatchDataCount: (long)r.watch_data_count));
        }
        return result;
    }

    public async Task ExportToCsvAsync(string sessionId, string outputPath)
    {
        using var conn = new SqliteConnection(_connectionString);
        var sb = new StringBuilder();

        // Session metadata header
        var session = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT s.*, GROUP_CONCAT(DISTINCT p.participant_key) AS pkeys " +
            "FROM sessions s LEFT JOIN participants p ON p.session_id = s.id " +
            "WHERE s.id = @sessionId GROUP BY s.id", new { sessionId });
        if (session != null)
        {
            var start = DateTimeOffset.FromUnixTimeMilliseconds((long)session.started_at).ToLocalTime();
            var endMs = session.ended_at as long?;
            var end = endMs.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(endMs.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                : "active";
            sb.AppendLine("=== SESSION ===");
            sb.AppendLine($"id,{session.id}");
            sb.AppendLine($"name,{EscapeCsv((string?)session.name ?? (string?)session.notes ?? "(unnamed)")}");
            sb.AppendLine($"notes,{EscapeCsv((string?)session.notes ?? "")}");
            sb.AppendLine($"started_at,{start:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"ended_at,{end}");
            sb.AppendLine($"participants,{session.pkeys ?? ""}");
            sb.AppendLine();
        }

        // Diagnostic counts — zero counts indicate a data-linkage problem, not an empty session.
        var totalWd = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM watch_data wd JOIN participants p ON p.id = wd.participant_id WHERE p.session_id = @sessionId",
            new { sessionId });
        var totalParticipants = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM participants WHERE session_id = @sessionId",
            new { sessionId });
        var totalWdAll = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM watch_data");
        var totalRunsAll = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM experiment_runs");
        var totalRunsSession = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM experiment_runs WHERE session_id = @sessionId", new { sessionId });
        var totalLogsAll = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM session_logs");
        var totalIntAll = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM interventions");
        sb.AppendLine($"# diagnostic: participants_in_session={totalParticipants}, watch_data_for_session={totalWd}, watch_data_total_in_db={totalWdAll}");
        sb.AppendLine($"# diagnostic: watch_insert_success_count={WatchInsertSuccessCount}, last_watch_insert_error={LastWatchInsertError ?? "none"}");
        sb.AppendLine($"# diagnostic: experiment_runs_for_session={totalRunsSession}, experiment_runs_total={totalRunsAll}, session_logs_total={totalLogsAll}, interventions_total={totalIntAll}");
        sb.AppendLine($"# diagnostic: last_write_error={LastWriteError ?? "none"}");
        sb.AppendLine();

        sb.AppendLine("=== WATCH DATA ===");
        sb.AppendLine("participant_label,participant_key,timestamp,heart_rate,accel_x,accel_y,accel_z,gyro_x,gyro_y,gyro_z,rot_w,rot_x,rot_y,rot_z,baro_hpa,light_lux,step_count");
        var watchRows = await conn.QueryAsync<dynamic>(
            "SELECT wd.*, p.participant_key, p.label FROM watch_data wd " +
            "JOIN participants p ON p.id = wd.participant_id WHERE p.session_id = @sessionId " +
            "ORDER BY wd.timestamp", new { sessionId });
        foreach (var r in watchRows)
            sb.AppendLine($"{r.label},{r.participant_key},{r.timestamp},{r.heart_rate},{r.accel_x},{r.accel_y},{r.accel_z},{r.gyro_x},{r.gyro_y},{r.gyro_z},{r.rot_w},{r.rot_x},{r.rot_y},{r.rot_z},{r.baro_hpa},{r.light_lux},{r.step_count}");

        sb.AppendLine();
        sb.AppendLine("=== SESSION LOGS ===");
        sb.AppendLine("id,run_id,ts,source,level,kind,payload_json");
        var logRows = await conn.QueryAsync<dynamic>(
            "SELECT sl.* FROM session_logs sl " +
            "JOIN experiment_runs er ON er.id = sl.run_id WHERE er.session_id = @sessionId " +
            "ORDER BY sl.id", new { sessionId });
        foreach (var r in logRows)
            sb.AppendLine($"{r.id},{r.run_id},{r.ts},{r.source},{r.level},{r.kind},{EscapeCsv((string?)r.payload_json ?? "")}");

        sb.AppendLine();
        sb.AppendLine("=== INTERVENTIONS ===");
        sb.AppendLine("id,participant_label,participant_key,sent_at,game_type,notification_type,response,reaction_time_ms");
        var intRows = await conn.QueryAsync<dynamic>(
            "SELECT i.*, p.label, p.participant_key FROM interventions i " +
            "JOIN participants p ON p.id = i.participant_id WHERE p.session_id = @sessionId " +
            "ORDER BY i.sent_at", new { sessionId });
        foreach (var r in intRows)
            sb.AppendLine($"{r.id},{r.label},{r.participant_key},{r.sent_at},{r.game_type},{r.notification_type},{r.response},{r.reaction_time_ms}");

        sb.AppendLine();
        sb.AppendLine("=== EXPERIMENT RUNS ===");
        sb.AppendLine("run_id,experiment_name,participant_label,started_at,ended_at");
        var runRows = await conn.QueryAsync<dynamic>(
            "SELECT er.*, ex.name AS experiment_name, p.label AS participant_label " +
            "FROM experiment_runs er " +
            "LEFT JOIN experiments ex ON ex.id = er.experiment_id " +
            "LEFT JOIN participants p ON p.id = er.participant_id " +
            "WHERE er.session_id = @sessionId ORDER BY er.started_at", new { sessionId });
        foreach (var r in runRows)
            sb.AppendLine($"{r.id},{r.experiment_name},{r.participant_label},{r.started_at},{r.ended_at}");

        sb.AppendLine();
        sb.AppendLine("=== DISTRACTION RUNS ===");
        sb.AppendLine("distraction_id,run_id,event_id,intervention_id,started_at,ended_at,response,reaction_time_ms");
        var distRows = await conn.QueryAsync<dynamic>(
            "SELECT dr.* FROM distraction_runs dr " +
            "JOIN experiment_runs er ON er.id = dr.experiment_run_id WHERE er.session_id = @sessionId " +
            "ORDER BY dr.started_at", new { sessionId });
        foreach (var r in distRows)
            sb.AppendLine($"{r.id},{r.experiment_run_id},{r.event_id},{r.intervention_id},{r.started_at},{r.ended_at},{r.response},{r.reaction_time_ms}");

        await File.WriteAllTextAsync(outputPath, sb.ToString());
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private const string EmbeddedSchema = """
        CREATE TABLE IF NOT EXISTS sessions (id TEXT PRIMARY KEY, started_at INTEGER NOT NULL, ended_at INTEGER, name TEXT, notes TEXT);
        CREATE TABLE IF NOT EXISTS participants (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, label TEXT NOT NULL, device_ip TEXT, participant_key TEXT);
        CREATE TABLE IF NOT EXISTS watch_data (id INTEGER PRIMARY KEY AUTOINCREMENT, participant_id TEXT NOT NULL, timestamp INTEGER NOT NULL, heart_rate REAL, accel_x REAL, accel_y REAL, accel_z REAL, step_count INTEGER, gyro_x REAL, gyro_y REAL, gyro_z REAL, rot_w REAL, rot_x REAL, rot_y REAL, rot_z REAL, baro_hpa REAL, light_lux REAL);
        CREATE TABLE IF NOT EXISTS events (id INTEGER PRIMARY KEY AUTOINCREMENT, participant_id TEXT NOT NULL, timestamp INTEGER NOT NULL, event_type TEXT NOT NULL, value TEXT, reaction_time_ms INTEGER);
        CREATE TABLE IF NOT EXISTS interventions (id TEXT PRIMARY KEY, participant_id TEXT NOT NULL, sent_at INTEGER NOT NULL, game_type TEXT, notification_type TEXT, response TEXT, reaction_time_ms INTEGER);
        CREATE TABLE IF NOT EXISTS trivia_questions (id INTEGER PRIMARY KEY AUTOINCREMENT, question TEXT NOT NULL, answer_a TEXT, answer_b TEXT, answer_c TEXT, answer_d TEXT, correct_index INTEGER);
        CREATE TABLE IF NOT EXISTS experiments (id TEXT PRIMARY KEY, name TEXT NOT NULL, json TEXT NOT NULL, version INTEGER NOT NULL DEFAULT 1, updated_at INTEGER NOT NULL, source TEXT NOT NULL DEFAULT 'pc');
        CREATE TABLE IF NOT EXISTS experiment_runs (id TEXT PRIMARY KEY, experiment_id TEXT NOT NULL, participant_id TEXT, session_id TEXT, started_at INTEGER NOT NULL, ended_at INTEGER);
        CREATE TABLE IF NOT EXISTS distraction_runs (id TEXT PRIMARY KEY, experiment_run_id TEXT NOT NULL, event_id TEXT, intervention_id TEXT, started_at INTEGER NOT NULL, ended_at INTEGER, response TEXT, reaction_time_ms INTEGER);
        CREATE TABLE IF NOT EXISTS session_logs (id INTEGER PRIMARY KEY AUTOINCREMENT, run_id TEXT, ts INTEGER NOT NULL, source TEXT NOT NULL, level TEXT NOT NULL DEFAULT 'INFO', kind TEXT NOT NULL, payload_json TEXT);
        """;
}
