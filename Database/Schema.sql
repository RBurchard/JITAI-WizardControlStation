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
    device_ip TEXT
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
