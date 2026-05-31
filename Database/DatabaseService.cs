using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.Database;

public class DatabaseService
{
    private const int CurrentSchemaVersion = 2;
    private readonly string _connectionString;

    public DatabaseService(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
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
        await conn.ExecuteAsync($"PRAGMA user_version = {CurrentSchemaVersion}");
    }

    private static async Task AddColumnIfMissing(SqliteConnection conn, string table, string column, string type)
    {
        var rows = await conn.QueryAsync<dynamic>($"PRAGMA table_info({table})");
        foreach (var r in rows)
            if ((string)r.name == column) return;
        await conn.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {type}");
    }

    public async Task InsertSessionAsync(string id, long startedAt, string? notes = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "INSERT OR IGNORE INTO sessions (id, started_at, notes) VALUES (@id, @startedAt, @notes)",
            new { id, startedAt, notes });
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
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        foreach (var s in batch.Snapshots)
        {
            await conn.ExecuteAsync(
                "INSERT INTO watch_data (participant_id, timestamp, heart_rate, accel_x, accel_y, accel_z, step_count, " +
                "gyro_x, gyro_y, gyro_z, rot_w, rot_x, rot_y, rot_z, baro_hpa, light_lux) " +
                "VALUES (@participantId, @timestamp, @heartRate, @accelX, @accelY, @accelZ, @stepCount, " +
                "@gyroX, @gyroY, @gyroZ, @rotW, @rotX, @rotY, @rotZ, @baro, @light)",
                new
                {
                    participantId,
                    s.Timestamp,
                    s.HeartRate,
                    s.AccelX, s.AccelY, s.AccelZ,
                    s.StepCount,
                    s.GyroX, s.GyroY, s.GyroZ,
                    rotW = s.RotationW, rotX = s.RotationX, rotY = s.RotationY, rotZ = s.RotationZ,
                    baro = s.Barometer, light = s.Light
                },
                tx);
        }
        await tx.CommitAsync();
    }

    public async Task InsertInterventionAsync(string id, string participantId, long sentAt, string? gameType, string notificationType)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "INSERT OR IGNORE INTO interventions (id, participant_id, sent_at, game_type, notification_type) " +
            "VALUES (@id, @participantId, @sentAt, @gameType, @notificationType)",
            new { id, participantId, sentAt, gameType, notificationType });
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
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "INSERT OR REPLACE INTO experiment_runs (id, experiment_id, participant_id, session_id, started_at) " +
            "VALUES (@runId, @experimentId, @participantId, @sessionId, @startedAt)",
            new { runId, experimentId, participantId, sessionId, startedAt });
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
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "INSERT OR REPLACE INTO distraction_runs (id, experiment_run_id, event_id, intervention_id, started_at) " +
            "VALUES (@distractionId, @runId, @eventId, @interventionId, @startedAt)",
            new { distractionId, runId, eventId, interventionId, startedAt });
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
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "INSERT INTO session_logs (run_id, ts, source, level, kind, payload_json) " +
            "VALUES (@runId, @ts, @source, @level, @kind, @payloadJson)",
            new { runId = entry.RunId, ts = entry.Ts, source = entry.Source, level = entry.Level, kind = entry.Kind, payloadJson = entry.PayloadJson });
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

    public async Task ExportToCsvAsync(string sessionId, string outputPath)
    {
        using var conn = new SqliteConnection(_connectionString);
        var sb = new StringBuilder();

        sb.AppendLine("=== WATCH DATA ===");
        sb.AppendLine("participant_id,participant_key,timestamp,heart_rate,accel_x,accel_y,accel_z,gyro_x,gyro_y,gyro_z,rot_w,rot_x,rot_y,rot_z,baro_hpa,light_lux,step_count");
        var watchRows = await conn.QueryAsync<dynamic>(
            "SELECT wd.*, p.participant_key FROM watch_data wd " +
            "JOIN participants p ON p.id = wd.participant_id WHERE p.session_id = @sessionId " +
            "ORDER BY wd.timestamp", new { sessionId });
        foreach (var r in watchRows)
            sb.AppendLine($"{r.participant_id},{r.participant_key},{r.timestamp},{r.heart_rate},{r.accel_x},{r.accel_y},{r.accel_z},{r.gyro_x},{r.gyro_y},{r.gyro_z},{r.rot_w},{r.rot_x},{r.rot_y},{r.rot_z},{r.baro_hpa},{r.light_lux},{r.step_count}");

        sb.AppendLine();
        sb.AppendLine("=== INTERVENTIONS ===");
        sb.AppendLine("id,participant_id,sent_at,game_type,notification_type,response,reaction_time_ms");
        var intRows = await conn.QueryAsync<dynamic>(
            "SELECT i.* FROM interventions i " +
            "JOIN participants p ON p.id = i.participant_id WHERE p.session_id = @sessionId " +
            "ORDER BY i.sent_at", new { sessionId });
        foreach (var r in intRows)
            sb.AppendLine($"{r.id},{r.participant_id},{r.sent_at},{r.game_type},{r.notification_type},{r.response},{r.reaction_time_ms}");

        sb.AppendLine();
        sb.AppendLine("=== EXPERIMENT RUNS ===");
        sb.AppendLine("run_key,experiment_id,participant_id,started_at,ended_at");
        var runRows = await conn.QueryAsync<dynamic>(
            "SELECT er.* FROM experiment_runs er WHERE er.session_id = @sessionId ORDER BY er.started_at", new { sessionId });
        foreach (var r in runRows)
            sb.AppendLine($"{r.id},{r.experiment_id},{r.participant_id},{r.started_at},{r.ended_at}");

        sb.AppendLine();
        sb.AppendLine("=== DISTRACTION RUNS ===");
        sb.AppendLine("distraction_key,run_key,event_id,intervention_id,started_at,ended_at,response,reaction_time_ms");
        var distRows = await conn.QueryAsync<dynamic>(
            "SELECT dr.* FROM distraction_runs dr " +
            "JOIN experiment_runs er ON er.id = dr.experiment_run_id WHERE er.session_id = @sessionId " +
            "ORDER BY dr.started_at", new { sessionId });
        foreach (var r in distRows)
            sb.AppendLine($"{r.id},{r.experiment_run_id},{r.event_id},{r.intervention_id},{r.started_at},{r.ended_at},{r.response},{r.reaction_time_ms}");

        await File.WriteAllTextAsync(outputPath, sb.ToString());
    }

    private const string EmbeddedSchema = """
        CREATE TABLE IF NOT EXISTS sessions (id TEXT PRIMARY KEY, started_at INTEGER NOT NULL, ended_at INTEGER, notes TEXT);
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
