# JITAI Wizard -- Control Station

_The researcher-facing desktop application for running intervention studies with the JITAI Wizard wearable system._
_Project for the Bachelor of Science in the University of Siegen_

---

> **Maintenance note:** This file is the authoritative reference for the ControlStation project. If you change a screen, an endpoint call, a database table, a UI control, or a build target, update the relevant section here before merging. If you believe something new is to be added to the contents, go for it.

---

## Table of Contents

1. [Overview](#overview)
2. [System Architecture](#system-architecture)
3. [Requirements](#requirements)
4. [Building the Project](#building-the-project)
5. [Application Layout](#application-layout)
6. [Session Management](#session-management)
7. [Device Discovery and Connection](#device-discovery-and-connection)
8. [Participant Panel](#participant-panel)
9. [Intervention Controls](#intervention-controls)
10. [Trivia Editor](#trivia-editor)
11. [Data Storage](#data-storage)
12. [CSV Export](#csv-export)
13. [Communication Protocol Reference](#communication-protocol-reference)
14. [Known Limitations and Notes](#known-limitations-and-notes)

---

## Overview

ControlStation is the researcher-side desktop application for the JITAI Wizard platform. It runs on Windows and allows a researcher to observe participants during a study, trigger interventions on demand, and collect biometric data from up to two participants simultaneously.

The application connects to Android phones running the JITAI Wizard companion app. Each phone acts as a bridge to a Wear OS watch worn by a participant. ControlStation does not communicate with the watches directly -- all watch control flows through the phone.

For documentation on the Android phone and watch apps, see the JITAI-WizardAndroid repository (same GitHub account, BWPlayer).

---

## System Architecture

```
+------------------+           UDP 5280 (discovery)           +-------------------+
|  ControlStation  | <---------------------------------------> |   Android Phone   |
|  Windows / .NET  |           HTTP 8080 (control)            |   mobile module   |
|  Avalonia UI     | <---------------------------------------> |   Ktor server     |
+------------------+           WS 8080/stream (data)          +-------------------+
                                                                        |
                                                               Wearable Data Layer
                                                              (Google Play Services)
                                                                        |
                                                              +-----------------+
                                                              |  Wear OS Watch  |
                                                              |  wear module    |
                                                              |  Sensor service |
                                                              +-----------------+
```

ControlStation discovers phones by listening for UDP beacon broadcasts. Once connected to a phone, it sends intervention commands over HTTP and receives a continuous stream of watch sensor data over a WebSocket. All received data is stored in a local SQLite database and can be exported to CSV.

ControlStation never initiates Bluetooth or Wearable Data Layer communication. That is handled entirely by the Android phone app.

---

## Requirements

### Hardware

- A Windows PC connected to the same local Wi-Fi network as the participant's Android phone.
- One Android phone and Wear OS watch per participant (up to two participants supported simultaneously).

### Software

| Tool | Notes |
|------|-------|
| Visual Studio 2022 or later | Recommended for building and running the project. |
| .NET 10.0 SDK | Required; download from the Microsoft .NET site |
| Avalonia extension for VS | Optional but improves XAML preview support |

Instead of Visual Studio, you may also also VSCode with the C# and Avalonia extension.

The project targets `net10.0` and uses Windows-specific packaging features. Building on macOS or Linux is theoretically possible with the .NET SDK but has not been tested.

---

## Building the Project

1. Clone the repository:

```
git clone https://github.com/BWPlayer/JITAI-WizardControlStation.git
```

2. Open `OcdWizard.ControlStation.sln` in Visual Studio 2022.

3. Restore NuGet packages (Visual Studio does this automatically on first open).

4. Set `OcdWizard.ControlStation` as the startup project if it is not already.

5. Build and run with F5 or the "Start" button.

The database file is created automatically on first launch at `%APPDATA%\OcdWizard\data.db`. No manual database setup is required.

### Key Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Avalonia | 11.2.7 | Cross-platform UI framework |
| CommunityToolkit.Mvvm | 8.4.0 | MVVM base classes and source generators |
| Microsoft.Data.Sqlite | 9.0.5 | SQLite database driver |
| Dapper | 2.1.35 | Lightweight SQL query mapping |
| System.Text.Json | 9.0.5 | JSON serialization for HTTP and WebSocket |

---

## Application Layout

The main window has three regions:

**Top bar** -- Session controls. Contains a session name text field, a Start Session button (green), a Stop Session button (red), and a status message line. The Start button is disabled while a session is active; the Stop button is disabled when no session is running.

**Main content area** -- Two side-by-side participant panels (Participant 1 on the left, Participant 2 on the right). Each panel is independent and controls one device.

**Bottom toolbar** -- Export and utility actions. Contains an Export CSV button and a Manage Trivia button.

---

## Session Management

Before triggering any interventions or connecting to devices, a session must be started.

1. Enter a name in the Session Name field (default: "New Session").
2. Click "Start Session." A GUID is generated for the session and stored in the database along with the session name and start timestamp.
3. The two participant panels become active.
4. When the study is complete, click "Stop Session." The end timestamp is recorded in the database.

Session data (sensor readings, interventions, responses) is associated with the current session ID throughout its lifetime. Stopping a session does not disconnect devices or discard buffered data.

---

## Device Discovery and Connection

Each participant panel handles its own device independently.

### Scanning

Click "Scan" in a participant panel. ControlStation opens a UDP listener on port 5280 and waits 3 seconds for beacon packets from JITAI Wizard phones on the local network. Each discovered device appears in the dropdown list as "Device Name (IP:Port)".

The phone broadcasts a beacon every 5 seconds automatically whenever the JITAI Wizard companion app is running. If no devices appear, confirm that:

- The phone and the PC are on the same Wi-Fi subnet.
- The JITAI Wizard companion app is open and running on the phone.
- No firewall is blocking UDP port 5280 on the PC.

### Connecting

Select a discovered device from the dropdown, or type an address manually into the IP field in the format `XXX.XXX.X.XXX` or `XXX.XXX.X.XX:8080`. Click "Connect."

ControlStation sends a GET request to `/status` on the device to verify it is reachable. On success, the status indicator turns green and a WebSocket connection is opened to `/stream` to begin receiving sensor data.

If the WebSocket drops for any reason, it automatically attempts to reconnect with a 3-second delay between attempts.

### Disconnecting

Click "Disconnect" to close the HTTP and WebSocket connections for that participant. The session remains active and the other participant is unaffected.

---

## Participant Panel

Each participant panel shows the following information once connected:

### Biometric Display

| Field | Description |
|-------|-------------|
| Heart Rate | Current BPM from the watch, displayed as a progress bar (0 to 200) and a numeric readout |
| Delta X Accel | Change in X-axis acceleration between the two most recent sensor snapshots (orange bar) |
| Delta Y Accel | Change in Y-axis acceleration (cyan bar) |
| Delta Z Accel | Change in Z-axis acceleration (green bar) |

Acceleration deltas give a quick visual indication of movement without requiring the researcher to interpret raw values. The bars show absolute magnitude.

### Event Log

A scrollable log at the bottom of each panel records timestamped events for that participant during the session. Events include connection state changes, triggered interventions, and received responses. The log is display-only and is not persisted; it resets when the application is restarted.

---

## Intervention Controls

The intervention controls sit in the middle section of each participant panel.

### Game Type

Selects the micro-game to attach to the intervention. Options:

| Value | Description |
|-------|-------------|
| None | No game; the watch shows only the intervention message |
| STAND_STILL | Participant must hold the watch still for 10 seconds |
| TRIVIA | Multiple-choice question (see Trivia section below) |
| SIMON_SAYS | Color sequence memory game, 4 rounds |
| LOCK_PICKING | Three-stage precision game (hold still, swipe circle, tap timing) |

### Notification Type

Selects how the watch alerts the participant when the intervention arrives. Options:

| Value | Description |
|-------|-------------|
| VIBRATION1 | Single short vibration pulse |
| VIBRATION2 | Single long vibration pulse |
| SOUND | Notification tone |
| VIBRATION_SOUND | Vibration and sound together |

### Sending an Intervention

Click "TRIGGER OCD DISTRACTION" to send the intervention to the connected device. The phone receives the request via HTTP POST to `/trigger`, forwards it to the watch over the Wearable Data Layer, and the watch displays it immediately.

The intervention is logged to the database with a timestamp and the selected game and notification types.

### Stopping an Intervention

Click "Stop" to cancel any currently active intervention on the watch. This sends an intervention request with the type set to "CANCEL."

---

## Trivia Editor

Click "Manage Trivia" in the bottom toolbar to open the Trivia Editor dialog.

The left panel shows all saved trivia questions as a list. The right panel shows the edit form for whichever question is selected.

Each question has:
- A question text field (multi-line)
- Four answer fields (A, B, C, D)
- A radio button group to select which answer is correct

**Add** creates a new blank question and selects it for editing.

**Remove** deletes the selected question from the list.

**Save Locally** writes the current question list to the database. Nothing is sent to any device yet.

**Push to Devices** saves to the database and then sends the full question list to all currently connected devices via HTTP POST to `/trivia`. The phone stores the list and uses it when a TRIVIA intervention is triggered.

When a TRIVIA intervention is triggered from the participant panel, there is also an inline trivia form that appears in the panel itself. This allows sending a single one-off question without going through the Trivia Editor.

---

## Data Storage

All session data is stored in a SQLite database at:

```
%APPDATA%\OcdWizard\data.db
```

The database is created automatically on first launch. The schema contains the following tables:

### sessions

| Column | Type | Description |
|--------|------|-------------|
| id | TEXT | Session GUID (primary key) |
| started_at | INTEGER | Unix timestamp in milliseconds |
| ended_at | INTEGER | Unix timestamp in milliseconds, null if session is still active |
| notes | TEXT | Session name as entered by the researcher |

### participants

| Column | Type | Description |
|--------|------|-------------|
| id | TEXT | Participant GUID (primary key) |
| session_id | TEXT | Foreign key to sessions |
| label | TEXT | "P1" or "P2" |
| device_ip | TEXT | IP address of the connected phone |

### watch_data

| Column | Type | Description |
|--------|------|-------------|
| id | INTEGER | Auto-incremented primary key |
| participant_id | TEXT | Foreign key to participants |
| timestamp | INTEGER | Unix timestamp in milliseconds |
| heart_rate | REAL | BPM reading from the watch |
| accel_x | REAL | X-axis accelerometer value |
| accel_y | REAL | Y-axis accelerometer value |
| accel_z | REAL | Z-axis accelerometer value |
| step_count | INTEGER | Cumulative step count |

### interventions

| Column | Type | Description |
|--------|------|-------------|
| id | TEXT | Intervention GUID (primary key) |
| participant_id | TEXT | Foreign key to participants |
| sent_at | INTEGER | Unix timestamp in milliseconds |
| game_type | TEXT | Game type string or null |
| notification_type | TEXT | Notification type string |
| response | TEXT | Participant's response, null until received |
| reaction_time_ms | INTEGER | Time from send to response in milliseconds, null until received |

### trivia_questions

| Column | Type | Description |
|--------|------|-------------|
| id | INTEGER | Auto-incremented primary key |
| question | TEXT | Question text |
| answer_a | TEXT | Answer option A |
| answer_b | TEXT | Answer option B |
| answer_c | TEXT | Answer option C |
| answer_d | TEXT | Answer option D |
| correct_index | INTEGER | Index of the correct answer (0=A, 1=B, 2=C, 3=D) |

---

## CSV Export

Click "Export CSV" in the bottom toolbar. A save dialog opens. Choose a file path and confirm.

The export contains two sections in the same file:

**WATCH DATA** -- all sensor snapshots for the current session, with columns: participant_id, timestamp, heart_rate, accel_x, accel_y, accel_z, step_count.

**INTERVENTIONS** -- all interventions sent during the current session, with columns: id, participant_id, sent_at, game_type, notification_type, response, reaction_time_ms.

The export uses the session that was most recently active. If multiple sessions exist in the database, only the data associated with the last active session is exported.

May be updated in the Future to include more data. 

---

## Communication Protocol Reference

### HTTP Endpoints Called by ControlStation

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | /status | Verify the device is reachable and retrieve session info |
| POST | /trigger | Send an InterventionRequest to the phone (forwarded to watch) |
| POST | /trivia | Push the full trivia question list to the phone |

All HTTP requests use a 10-second timeout. The base URL is `http://{device-ip}:8080`.

### WebSocket

ControlStation connects to `ws://{device-ip}:8080/stream` after a successful HTTP connection. The phone sends `WatchDataBatch` JSON objects over this stream as batches arrive from the watch (approximately once per second). ControlStation inserts each batch into the `watch_data` table.

The WebSocket reconnects automatically with a 3-second delay if the connection drops.

### UDP Discovery

ControlStation listens on UDP port 5280 for 3 seconds when Scan is clicked. Phones broadcast to this port every 5 seconds. The expected payload format is:

```json
{"app": "JITAIWizard", "port": 8080, "name": "<device model>"}
```

Packets where `app` is not `"JITAIWizard"` are silently ignored.

### InterventionRequest JSON Format

Sent as the body of POST /trigger:

```json
{
  "notificationType": "VIBRATION1",
  "message": "Stop!",
  "durationMs": 30000,
  "gameType": "TRIVIA",
  "phoneTaskType": null,
  "triviaQuestion": {
    "id": 0,
    "question": "What is 2 + 2?",
    "answers": ["3", "4", "5", "6"],
    "correctIndex": 1
  }
}
```

`gameType` and `triviaQuestion` are null when no game is selected. `phoneTaskType` is reserved for future use.

---

## Known Limitations and Notes

- The application supports exactly two participants simultaneously. This is a design decision for the current study scope, not a hard technical limit.
- The CSV export does not allow selecting a specific session by name. It exports the data for the session ID that was most recently started in the current application run. If you need data from a previous session, query the SQLite database directly.
- Sensor data stored in `watch_data` includes only the fields that ControlStation's data model defines (heart rate, three acceleration axes, step count). The watch collects additional sensors (gyroscope, rotation vector, barometer, light) that are streamed to the phone but not currently persisted by ControlStation.
- The HTTP server on the phone runs without authentication. Do not use this system on untrusted networks. For realsies.
- Intervention responses (what the participant chose or whether they completed a game) are received by the phone from the watch and available via the phone's HTTP API, but ControlStation does not currently poll for them automatically. Response data in the database is only populated if the phone pushes the response as part of the watch data stream.
- Currently there is a 90% Failed Trigger chance due to a few comm failures. 
