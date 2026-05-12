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

    public async Task UpsertParticipantAsync(string id, string sessionId, string label, string? deviceIp)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.ExecuteAsync(
            "INSERT OR REPLACE INTO participants (id, session_id, label, device_ip) VALUES (@id, @sessionId, @label, @deviceIp)",
            new { id, sessionId, label, deviceIp });
    }

    public async Task InsertWatchDataBatchAsync(string participantId, WatchDataBatch batch)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        foreach (var s in batch.Snapshots)
        {
            await conn.ExecuteAsync(
                "INSERT INTO watch_data (participant_id, timestamp, heart_rate, accel_x, accel_y, accel_z, step_count) " +
                "VALUES (@participantId, @timestamp, @heartRate, @accelX, @accelY, @accelZ, @stepCount)",
                new { participantId, s.Timestamp, s.HeartRate, s.AccelX, s.AccelY, s.AccelZ, s.StepCount },
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
        sb.AppendLine("participant_id,timestamp,heart_rate,accel_x,accel_y,accel_z,step_count");
        var watchRows = await conn.QueryAsync<dynamic>(
            "SELECT wd.* FROM watch_data wd " +
            "JOIN participants p ON p.id = wd.participant_id WHERE p.session_id = @sessionId " +
            "ORDER BY wd.timestamp", new { sessionId });
        foreach (var r in watchRows)
            sb.AppendLine($"{r.participant_id},{r.timestamp},{r.heart_rate},{r.accel_x},{r.accel_y},{r.accel_z},{r.step_count}");

        sb.AppendLine();
        sb.AppendLine("=== INTERVENTIONS ===");
        sb.AppendLine("id,participant_id,sent_at,game_type,notification_type,response,reaction_time_ms");
        var intRows = await conn.QueryAsync<dynamic>(
            "SELECT i.* FROM interventions i " +
            "JOIN participants p ON p.id = i.participant_id WHERE p.session_id = @sessionId " +
            "ORDER BY i.sent_at", new { sessionId });
        foreach (var r in intRows)
            sb.AppendLine($"{r.id},{r.participant_id},{r.sent_at},{r.game_type},{r.notification_type},{r.response},{r.reaction_time_ms}");

        await File.WriteAllTextAsync(outputPath, sb.ToString());
    }

    private const string EmbeddedSchema = """
        CREATE TABLE IF NOT EXISTS sessions (id TEXT PRIMARY KEY, started_at INTEGER NOT NULL, ended_at INTEGER, notes TEXT);
        CREATE TABLE IF NOT EXISTS participants (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, label TEXT NOT NULL, device_ip TEXT);
        CREATE TABLE IF NOT EXISTS watch_data (id INTEGER PRIMARY KEY AUTOINCREMENT, participant_id TEXT NOT NULL, timestamp INTEGER NOT NULL, heart_rate REAL, accel_x REAL, accel_y REAL, accel_z REAL, step_count INTEGER);
        CREATE TABLE IF NOT EXISTS events (id INTEGER PRIMARY KEY AUTOINCREMENT, participant_id TEXT NOT NULL, timestamp INTEGER NOT NULL, event_type TEXT NOT NULL, value TEXT, reaction_time_ms INTEGER);
        CREATE TABLE IF NOT EXISTS interventions (id TEXT PRIMARY KEY, participant_id TEXT NOT NULL, sent_at INTEGER NOT NULL, game_type TEXT, notification_type TEXT, response TEXT, reaction_time_ms INTEGER);
        CREATE TABLE IF NOT EXISTS trivia_questions (id INTEGER PRIMARY KEY AUTOINCREMENT, question TEXT NOT NULL, answer_a TEXT, answer_b TEXT, answer_c TEXT, answer_d TEXT, correct_index INTEGER);
        """;
}
