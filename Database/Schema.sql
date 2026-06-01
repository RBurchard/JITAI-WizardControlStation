CREATE TABLE IF NOT EXISTS sessions (
    id TEXT PRIMARY KEY,
    started_at INTEGER NOT NULL,
    ended_at INTEGER,
    notes TEXT
);

CREATE TABLE IF NOT EXISTS participants (
    id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    label TEXT NOT NULL,
    device_ip TEXT,
    participant_key TEXT
);

CREATE TABLE IF NOT EXISTS watch_data (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    participant_id TEXT NOT NULL,
    timestamp INTEGER NOT NULL,
    heart_rate REAL,
    accel_x REAL,
    accel_y REAL,
    accel_z REAL,
    step_count INTEGER
);

CREATE TABLE IF NOT EXISTS events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    participant_id TEXT NOT NULL,
    timestamp INTEGER NOT NULL,
    event_type TEXT NOT NULL,
    value TEXT,
    reaction_time_ms INTEGER
);

CREATE TABLE IF NOT EXISTS interventions (
    id TEXT PRIMARY KEY,
    participant_id TEXT NOT NULL,
    sent_at INTEGER NOT NULL,
    game_type TEXT,
    notification_type TEXT,
    response TEXT,
    reaction_time_ms INTEGER
);

CREATE TABLE IF NOT EXISTS trivia_questions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    question TEXT NOT NULL,
    answer_a TEXT,
    answer_b TEXT,
    answer_c TEXT,
    answer_d TEXT,
    correct_index INTEGER
);

CREATE TABLE IF NOT EXISTS experiments (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    json TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    updated_at INTEGER NOT NULL,
    source TEXT NOT NULL DEFAULT 'pc'
);

CREATE TABLE IF NOT EXISTS experiment_runs (
    id TEXT PRIMARY KEY,
    experiment_id TEXT NOT NULL,
    participant_id TEXT,
    session_id TEXT,
    started_at INTEGER NOT NULL,
    ended_at INTEGER
);

CREATE TABLE IF NOT EXISTS distraction_runs (
    id TEXT PRIMARY KEY,
    experiment_run_id TEXT NOT NULL,
    event_id TEXT,
    intervention_id TEXT,
    started_at INTEGER NOT NULL,
    ended_at INTEGER,
    response TEXT,
    reaction_time_ms INTEGER
);

CREATE TABLE IF NOT EXISTS session_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_id TEXT,
    ts INTEGER NOT NULL,
    source TEXT NOT NULL,
    level TEXT NOT NULL DEFAULT 'INFO',
    kind TEXT NOT NULL,
    payload_json TEXT
);

CREATE INDEX IF NOT EXISTS idx_session_logs_run ON session_logs(run_id, id);
CREATE INDEX IF NOT EXISTS idx_runs_participant ON experiment_runs(participant_id);
CREATE INDEX IF NOT EXISTS idx_distraction_runs_parent ON distraction_runs(experiment_run_id);
