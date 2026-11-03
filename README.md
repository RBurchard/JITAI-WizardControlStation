<h1 align="center">JITAI Wizard · Control Station</h1>

<p align="center">
  <img src="Assets/odi_icon.png" alt="Odi, the JITAI Wizard mascot" width="180" />
</p>

<p align="center">
  <i>The researcher's desktop cockpit for running intervention studies with the JITAI Wizard wearable system.</i><br/>
  <i>Project for the Bachelor of Science at the University of Siegen.</i>
</p>

<p align="center">
  <a href="https://github.com/RBurchard/JITAI-WizardControlStation/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/RBurchard/JITAI-WizardControlStation?label=release&color=1565C0"></a>
  <img alt="Windows 10/11 x64" src="https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-0078D4?logo=windows&logoColor=white">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white">
  <img alt="Avalonia UI" src="https://img.shields.io/badge/Avalonia-11.2-8B44AC">
  <img alt="SQLite" src="https://img.shields.io/badge/SQLite-databank-003B57?logo=sqlite&logoColor=white">
  <img alt="Languages" src="https://img.shields.io/badge/UI-EN%20%7C%20DE%20%7C%20NL-42A5F5">
</p>

<p align="center">
  <b>Latest release:</b> <a href="https://github.com/RBurchard/JITAI-WizardControlStation/releases/latest">v2.1.1</a> &middot; Windows x64, .NET runtime bundled &middot;
  pairs with the <a href="https://github.com/RBurchard/JITAI-WizardAndroid">Android + Wear OS apps</a>
</p>

> [!NOTE]
> **Maintenance:** this file is the authoritative reference for the Control Station. If you change a screen, an endpoint call, a database table, a UI control or a build target, update the matching section here before merging. If something new belongs in here, add it.

---

## Table of Contents

1. [Overview](#overview)
2. [Download & Install](#download--install)
3. [Release History](#release-history)
4. [System Architecture](#system-architecture)
5. [Requirements](#requirements)
6. [Building from Source](#building-from-source)
7. [Running a Session, Step by Step](#running-a-session-step-by-step)
8. [Application Layout](#application-layout)
9. [Session Management](#session-management)
10. [Device Discovery and Connection](#device-discovery-and-connection)
11. [Participant Panel](#participant-panel)
12. [Experiment Editor and Runner](#experiment-editor-and-runner)
13. [Intervention Controls](#intervention-controls)
14. [Trivia Editor](#trivia-editor)
15. [Replay Mode and Master Notes](#replay-mode-and-master-notes)
16. [Data Storage](#data-storage)
17. [CSV Export](#csv-export)
18. [Communication Protocol Reference](#communication-protocol-reference)
19. [Known Limitations and Notes](#known-limitations-and-notes)

---

## Overview

The Control Station is the researcher-side desktop application of the JITAI Wizard platform. On Windows it lets a researcher design and run experiments, watch participants live, trigger interventions on demand, record biometric and motion data from **one or two participants at once**, and afterwards replay and annotate every recording.

It connects to Android phones running the JITAI Wizard companion app. Each phone is the bridge to a Wear OS watch worn by a participant. The Control Station never talks to a watch directly. Everything goes through the phone.

For the phone and watch apps, see the [JITAI-WizardAndroid repository](https://github.com/RBurchard/JITAI-WizardAndroid).

---

## Download & Install

> [!TIP]
> Just want to run it? Grab the [**latest release**](https://github.com/RBurchard/JITAI-WizardControlStation/releases/latest). The .NET 10 runtime is bundled, so nothing else needs installing.

1. Download **`JITAI-WizardControlStation-v2.1.1-win-x64.zip`**.
2. Extract it anywhere (for example `C:\JITAI-Wizard`). Keep all files together: the `.exe` needs the DLLs and the `Assets` and `Database` folders beside it.
3. Run **`OcdWizard.ControlStation.exe`**.
4. The build is unsigned, so Windows SmartScreen may warn on first launch. Click **More info → Run anyway**.

The database is created automatically at `%APPDATA%\OcdWizard\data.db` on first run, and a guided tour starts on first launch.

**Requires Windows 10/11 x64.** The companion **Android and Wear OS apps** are required too: [JITAI-WizardAndroid releases](https://github.com/RBurchard/JITAI-WizardAndroid/releases/latest). Install the same version on all three devices.

Prefer to build it yourself? See [Building from Source](#building-from-source).

---

## Release History

Both repositories are released together under the same version number.

| Version | Highlights |
|---------|-----------|
| **v2.1.1** | First 2.x release on `main`. No Control Station code changes since 2.1.0; it pairs with the Android 2.1.1 fix to the phone's schedule editor. |
| v2.1.0-beta | Replay fixed on decimal-comma PCs (CSV numbers now always use a decimal point), seeking backwards in Replay fixed, notes saved when a session ends, German experiment variants, **Wake watch** button. Used for the follow-up test. |
| v2.0.0-beta | English/German/Dutch UI, named sessions, scientific viewer (Replay + Master Notes), mini-game difficulty on the experiment, `MINIGAME SETTINGS` export section. |
| v1.2.0 | Fix for experiment trackers fighting each other. Used for the first study run. |
| v1.1.0-beta, v1.0.0-beta | First public builds. |

Full notes for each version are on the [Releases page](https://github.com/RBurchard/JITAI-WizardControlStation/releases).

---

## System Architecture

```
+------------------+           UDP 5280 (discovery)           +-------------------+
|  Control Station | <--------------------------------------> |   Android Phone   |
|  Windows / .NET  |           HTTP 8080 (control)            |   mobile module   |
|  Avalonia UI     | <--------------------------------------> |   Ktor server     |
+------------------+     WS 8080 /stream (data), /events      +-------------------+
        |                                                               |
   SQLite databank                                             Wearable Data Layer
 %APPDATA%\OcdWizard                                          (Google Play Services)
                                                                        |
                                                              +-----------------+
                                                              |  Wear OS Watch  |
                                                              |  wear module    |
                                                              |  Sensor service |
                                                              +-----------------+
```

The Control Station finds phones by listening for UDP beacons, or over USB debugging via `adb`. Once connected, it sends commands over HTTP and receives two WebSocket streams: the watch's sensor data (`/stream`) and the experiment's events and logs (`/events`). Everything received is stored in a local SQLite database, from which it can be exported to CSV or replayed.

Bluetooth and the Wearable Data Layer are handled entirely by the phone app.

---

## Requirements

### Hardware

- A Windows 10/11 x64 PC on the same local Wi-Fi network as the participant's phone, or with the phone attached by USB.
- One Android phone and one Wear OS watch per participant (up to two participants at once).

### Software (to build from source)

| Tool | Notes |
|------|-------|
| .NET 10 SDK | Required. The project targets `net10.0`. |
| IDE | Visual Studio with .NET 10 support, or VS Code with the C# Dev Kit and Avalonia extensions. Neither is needed for a command-line build. |
| Android platform-tools (`adb`) | Optional; only for discovering phones over USB. |

Building on macOS or Linux may work with the .NET SDK but has not been tested; the published release is Windows x64 only.

---

## Building from Source

1. Clone the repository:

   ```bash
   git clone https://github.com/RBurchard/JITAI-WizardControlStation.git
   cd JITAI-WizardControlStation
   ```

2. Run it:

   ```bash
   dotnet run --project OcdWizard.ControlStation.csproj
   ```

   Or open `OcdWizard.ControlStation.sln` in your IDE and start `OcdWizard.ControlStation` (F5). NuGet packages restore automatically.

3. To produce the same self-contained build as a release:

   ```bash
   dotnet publish OcdWizard.ControlStation.csproj -c Release -r win-x64 --self-contained true -o publish
   ```

The database is created automatically on first launch at `%APPDATA%\OcdWizard\data.db`. No manual database setup is needed.

### Key Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Avalonia (+ Desktop, Fluent theme, DataGrid, Inter font) | 11.2.7 | Cross-platform UI framework |
| CommunityToolkit.Mvvm | 8.4.0 | MVVM base classes and source generators |
| Microsoft.Data.Sqlite | 9.0.5 | SQLite driver |
| Dapper | 2.1.35 | Lightweight SQL mapping |
| System.Text.Json | 9.0.5 | JSON for HTTP and WebSocket traffic |

---

## Running a Session, Step by Step

1. **Phone:** open the JITAI Wizard app and check that the watch shows as connected.
2. **Connect:** in a participant panel press **Scan**, pick the phone, then **Connect**. While fitting the watch, press **Wake watch (5 min)** to see live heart rate and motion.
3. **Participant:** enter the participant name or ID (it syncs with the phone).
4. **Experiment:** in the **Experiment Editor**, **Import** one of the bundled experiments from `Assets` (English or German) or build your own, then **Push** it to the phone.
5. **Session:** enter a session name in the top bar (e.g. the participant code) and press **Start Session**.
6. **Run:** in the **Experiment Runner**, press **Start**, or **Auto** to let the phone step through the schedule on its own. Fire extra interventions by hand from **Send Intervention** whenever needed.
7. **Finish:** press **Stop Session**, or let the session close itself when the experiment finishes; you are then asked to name it.
8. **Afterwards:** **Menu → Export CSV**, or switch to **Replay Mode** to replay, annotate and summarize the recording.

The **Tutorial** in the menu walks through all of this on screen.

---

## Application Layout

The main window has three regions.

**Top bar**

- **Experiment Mode / Replay Mode** toggle: live recording or playback of past sessions (see [Replay Mode and Master Notes](#replay-mode-and-master-notes)).
- **Session name** field (e.g. `Participant_01`).
- **1 Watch / 2 Watches** toggle: a single full-width participant panel, or two side by side with a draggable splitter.
- **Start Session** / **Stop Session**: only the one that applies is shown.
- **☰ Menu**: Export CSV, Trivia, Tutorial, Settings (interface language: system default, English, German, Dutch).
- A status line with the latest message.

**Session notes bar:** free-text notes for the running session, with a **Save** button. They are also saved automatically when the session ends.

**Main area:** one or two [participant panels](#participant-panel) in Experiment Mode, or the Replay view in Replay Mode.

A **guided tour** (Tutorial) runs on first launch and can be replayed from the menu at any time.

---

## Session Management

A session must be running before data is recorded.

1. Enter a session name (default: "New Session").
2. Press **Start Session**. A GUID is generated and stored with the name and start time.
3. When the study is done, press **Stop Session**. The end time is recorded, and whatever is in the notes box is saved with it. When an experiment run finishes, the session also ends on its own and a dialog asks you to name it.

The notes box and its Save button are disabled once the session has ended, and cleared when the next one starts. Sensor readings, interventions and responses are tied to the current session ID. Stopping a session does not disconnect devices or discard buffered data.

---

## Device Discovery and Connection

Each participant panel handles its own device.

### Scanning

Press **Scan**. The Control Station listens on UDP port 5280 for 3 seconds for beacons from JITAI Wizard phones and, if `adb` is available, also lists phones attached over **USB debugging** (it forwards a local port to the phone's port 8080). Each device appears in the dropdown as "Device Name (IP:Port)".

`adb` is looked up on `PATH`, under `ANDROID_HOME` / the Android SDK in `%LOCALAPPDATA%\Android\Sdk\platform-tools`, and in a `tools\adb` folder next to the executable.

The phone broadcasts a beacon every 5 seconds while the app runs. If nothing shows up, check that:

- the phone and the PC are on the same Wi-Fi subnet,
- the JITAI Wizard app is open on the phone,
- no firewall blocks UDP port 5280 on the PC.

### Connecting

Pick a discovered device, or type an address into the IP field (`192.168.1.42` or `192.168.1.42:8080`), then press **Connect**.

The Control Station calls `GET /status` to check that the device is reachable. On success the indicator turns green, the participant name is reconciled with the phone (if none is set here, the phone's is adopted), and the `/stream` and `/events` WebSockets open. If a WebSocket drops, it reconnects every 3 seconds.

### Waking the Watch

Between runs the watch drops into a low-power profile after five quiet minutes: one sensor snapshot a minute, no motion sensors, heart rate only when the OS measures it anyway. Live sensors are therefore sparse until a run starts.

**Wake watch (5 min)** (enabled once connected) sends `POST /watch/wake` to the phone, which relays it to the watch. The watch then collects at full rate for five minutes and idles again on its own, unless a run starts in the meantime, in which case the run keeps it awake for as long as it lasts. Use it while fitting the watch to check heart rate and motion before pressing Start.

### Disconnecting

**Disconnect** closes the HTTP and WebSocket connections for that participant. The session stays active and the other participant is unaffected.

---

## Participant Panel

Each panel is a stack of collapsible sections. Any section can be **popped out into its own window** and docked back, which helps on a second monitor.

| Section | Contents |
|---------|----------|
| 📡 **Connection** | Scan, device list, IP field, Connect/Disconnect, Wake watch, participant name/ID (synced with the phone). |
| 🖐 **Motion Tracker** | A live 3D model of the wrist with its orientation, plus the reconstructed 3D velocity. Axis mapping and flip options with a default preset `(Y, -X, Z)`; **Reset Pose** and **Reset Vel.** buttons. A small heart beats in time with the participant's pulse. |
| 💓 **Live Sensors** | Heart rate as a bar (0 to 200) with a numeric readout, and per-axis accelerometer deltas between the last two snapshots (X orange, Y cyan, Z green) for a quick sense of movement. |
| ▶ **Experiment Runner** | Drives the schedule on the phone. See [below](#experiment-editor-and-runner). |
| ✏ **Experiment Editor** | Designs the schedule. See [below](#experiment-editor-and-runner). |
| 📣 **Send Intervention** | Manual interventions. See [Intervention Controls](#intervention-controls). |
| 📋 **Synced Logs** | The phone's structured session log as it arrives over `/events` (or the `/logs` polling fallback). |
| 🔔 **Event Log** | A timestamped log of this panel's own events: connection changes, triggered interventions, responses. Display only; it resets when the app restarts. |

---

## Experiment Editor and Runner

### Editor

An experiment is an ordered list of **events** plus a set of **triggers** and the **watch mini-game** settings. It is the same JSON format the phone uses.

- **Events:** each has a duration, a kind and an optional intervention. Kinds: **Timed**, **Random Wait** (random time between a min and a max), **Wait for Action** (a response with a matching action ID), **Wait for Trigger** (a trigger fires) and **Wait for Previous** (any response to the intervention). The intervention sets the message, the display type (Text, Timer, Yes/No), the notification, and either a micro-game or a phone task (selecting one clears the other).
- **Triggers:** **RapidMovement** (acceleration threshold in g), **RepeatingMovement** (frequency band in Hz), **HeartRate** (BPM threshold, above or below) and **Manual**. Each can be switched on or off.
- **Watch mini-games:** Simon speed (1 to 5), Simon rounds (2 to 8), Lock difficulty (1 to 5), and what a lock-picking mistake costs (Stun, Push back, Restart stage). These travel with the experiment and reach the watch when it is pushed, so a researcher never has to set difficulty on the wrist as well.
- **Import / Export** read and write experiment JSON files. **Pull / Push** fetch the phone's current experiment or replace it (with ETag conflict detection).

**Bundled experiments** sit in the `Assets` folder: `default_experiment.json`, `experiment_distraction_combo.json`, `experiment_alt_microgames.json` and `experiment_standstill_only.json`, each with a German variant (`*_de.json`). All eight pin the mini-games to medium (Simon 3/5 with 4 rounds, Lock 3/5, a mistake stuns). Only the text people read is translated; event and intervention IDs match, so English and German runs line up in the logs.

### Runner

**Start**, **Auto** (the phone steps through the schedule by itself), **Pause**, **Stop**, **Prev** and **Next**, sent as `POST /experiment/control`. The runner shows the event that is **NOW RUNNING**, a live countdown and the run ID.

---

## Intervention Controls

The **Send Intervention** section fires a one-off intervention outside the schedule.

### Game Type

| Value | Description |
|-------|-------------|
| None | No game; the watch shows only the message |
| `STAND_STILL` | Hold the watch still for 10 seconds |
| `TRIVIA` | Multiple-choice question (an inline form appears to write one) |
| `SIMON_SAYS` | Sequence memory game |
| `LOCK_PICKING` | Three-stage heist (hold still, dodge the beams, crack the safe) |

### Notification Type

| Value | Description |
|-------|-------------|
| `VIBRATION1` | Single short vibration |
| `VIBRATION2` | Single long vibration |
| `SOUND` | Notification tone |
| `VIBRATION_SOUND` | Vibration and tone together |

The Experiment Editor offers the full set, including the long and repeating ("annoying") variants.

### Sending and Stopping

**📣 Trigger Intervention** sends it to the connected phone (`POST /trigger`), which forwards it to the watch. It is logged in the database with a timestamp and the chosen game and notification. The **✕** button next to it cancels any running intervention on the watch (an intervention with type `CANCEL`).

---

## Trivia Editor

**Menu → Trivia** opens the Trivia Question Editor. The list on the left shows the saved questions; the form on the right edits the selected one: question text, four answers (A to D) and the correct answer.

- **+ Add** creates a blank question. **Remove** deletes the selected one.
- **Save Locally** writes the list to the database and sends nothing.
- **Push to Devices** saves and then sends the full list to every connected phone (`POST /trivia`). The phone uses it for TRIVIA interventions.

For a single one-off question, the Send Intervention section has its own inline trivia form.

---

## Replay Mode and Master Notes

Switch the top-bar toggle to **Replay Mode**. It has two tabs.

### ▶ Player

- **Open ▾** loads a recording **from the databank** or **imports a CSV file** (a Control Station export or a phone-side export). The source is shown as DATABANK or FILE, and if a CSV had damaged rows the number skipped is shown next to the file name.
- A **timeline** with every marker: run start and end, interventions sent, responses (with reaction time), distractions and notes. Click to seek, scroll to zoom, right-drag to pan.
- Playback at 0.25× to 8×, steps of ±100 ms and ±1 s, single-sample steps, and a `mm:ss.mmm` time entry.
- **Sensors @ playhead** and the 3D motion view replay the recording exactly as it looked live, including after seeking backwards.
- An overlay shows the running experiment and the current event (best effort). **Attach Experiment JSON** shows that experiment's events as a reference list beside the recording.
- **Add Marker** (with recently used names) and an editable **Notes** box. **Save** writes notes and markers back, either overwriting the loaded file or as a new copy; markers go into a `REPLAY MARKERS` section.

### 🗒 Master Notes

The research summary table: **one row per recording**, with participant, recording, date/time, experiment, variant source, wash duration, whether the intervention was noticed, whether washing was interrupted, alert type, mini-game and verbatim notes.

- **Add ▾** rows from the databank, from CSV files, or blank. Each row can be duplicated, removed or **opened** in the player.
- **Export ▾** as an Excel workbook (`.xlsx`) or a CSV file.
- The table is stored in `%APPDATA%\OcdWizard\master_notes.json`.

---

## Data Storage

Everything lives in `%APPDATA%\OcdWizard\`:

| File | Contents |
|------|----------|
| `data.db` | The SQLite databank (tables below) |
| `master_notes.json` | The Master Notes table |
| `replay_settings.json` | Replay view preferences |
| `language_settings.json` | Interface language |
| `tutorial_seen.flag` | Marks the first-launch tour as done |

The database is created on first launch, and older databases are upgraded in place (missing columns are added).

<details>
<summary><b>Database schema</b> (click to expand)</summary>

### sessions

| Column | Type | Description |
|--------|------|-------------|
| id | TEXT | Session GUID (primary key) |
| started_at | INTEGER | Unix time in ms |
| ended_at | INTEGER | Unix time in ms; null while the session is running |
| name | TEXT | Session name |
| notes | TEXT | Researcher's session notes |

### participants

| Column | Type | Description |
|--------|------|-------------|
| id | TEXT | Participant GUID (primary key) |
| session_id | TEXT | Foreign key to `sessions` |
| label | TEXT | `P1` or `P2` |
| device_ip | TEXT | IP address of the connected phone |
| participant_key | TEXT | Researcher-assigned name or ID (defaults to the GUID) |

### watch_data

One row per sensor snapshot streamed from the watch.

| Column | Type | Description |
|--------|------|-------------|
| id | INTEGER | Auto-increment primary key |
| participant_id | TEXT | Foreign key to `participants` |
| timestamp | INTEGER | Unix time in ms |
| heart_rate | REAL | BPM |
| accel_x, accel_y, accel_z | REAL | Accelerometer (m/s²) |
| gyro_x, gyro_y, gyro_z | REAL | Gyroscope (rad/s) |
| rot_w, rot_x, rot_y, rot_z | REAL | Rotation quaternion |
| baro_hpa | REAL | Barometric pressure (hPa) |
| light_lux | REAL | Ambient light (lux) |
| step_count | INTEGER | Cumulative step count |

### interventions

| Column | Type | Description |
|--------|------|-------------|
| id | TEXT | Intervention GUID (primary key) |
| participant_id | TEXT | Foreign key to `participants` |
| sent_at | INTEGER | Unix time in ms |
| game_type | TEXT | Game type or null |
| notification_type | TEXT | Notification type |
| response | TEXT | Participant's response; null until received |
| reaction_time_ms | INTEGER | Send-to-response time; null until received |

### experiment_runs

| Column | Type | Description |
|--------|------|-------------|
| id | TEXT | Run GUID (primary key) |
| experiment_id | TEXT | ID of the experiment |
| participant_id | TEXT | Foreign key to `participants` |
| session_id | TEXT | Foreign key to `sessions` |
| started_at | INTEGER | Unix time in ms |
| ended_at | INTEGER | Unix time in ms; null while running |

### distraction_runs

| Column | Type | Description |
|--------|------|-------------|
| id | TEXT | Distraction GUID (primary key) |
| experiment_run_id | TEXT | Foreign key to `experiment_runs` |
| event_id | TEXT | The experiment event that triggered it |
| intervention_id | TEXT | The intervention delivered |
| started_at | INTEGER | Unix time in ms |
| ended_at | INTEGER | Unix time in ms; null until dismissed |
| response | TEXT | Participant response |
| reaction_time_ms | INTEGER | Delivery-to-response time |

### session_logs

The structured event log streamed from the phone during runs.

| Column | Type | Description |
|--------|------|-------------|
| id | INTEGER | Auto-increment primary key |
| run_id | TEXT | Foreign key to `experiment_runs` |
| ts | INTEGER | Unix time in ms |
| source | TEXT | Origin, e.g. `experiment`, `engine`, `trigger` |
| level | TEXT | `INFO`, `WARN` or `ERROR` |
| kind | TEXT | Event type |
| payload_json | TEXT | Event-specific JSON |

### experiments

| Column | Type | Description |
|--------|------|-------------|
| id | TEXT | Experiment GUID (primary key) |
| name | TEXT | Experiment name |
| json | TEXT | The full experiment JSON |
| version | INTEGER | Incremented on each save |
| updated_at | INTEGER | Unix time in ms of the last save |
| source | TEXT | `pc` if saved here, `phone` if synced from a device |

### trivia_questions

| Column | Type | Description |
|--------|------|-------------|
| id | INTEGER | Auto-increment primary key |
| question | TEXT | Question text |
| answer_a … answer_d | TEXT | Answer options |
| correct_index | INTEGER | Correct answer (0 = A … 3 = D) |

### events

A legacy table. It is still created but no longer written to.

</details>

---

## CSV Export

**Menu → Export CSV** opens a session picker listing every session in the databank with its date, name, duration, participants, experiments and number of sensor samples. Pick one, press **Export Selected**, and choose where to save it. (If the only session in the databank is the one running, the picker is skipped.)

Numbers are always written with a decimal point, whatever the PC's regional settings. Files exported by builds before 2.1.0 on a decimal-comma PC have damaged sensor rows; Replay Mode skips those rows and shows how many, and the session can simply be exported again from the databank.

The file contains these sections, separated by blank lines:

| Section | Contents |
|---------|----------|
| `=== SESSION ===` | id, name, notes, start and end time, participant identifiers, plus `# diagnostic` lines with row counts (zero counts point at a data-linkage problem, not an empty session) |
| `=== MINIGAME SETTINGS ===` | `scope,ts,origin,simon_difficulty,simon_rounds,lock_difficulty,lock_penalty`: a `schedule` row (what the experiment specified) and a `change` row for every adjustment during the run |
| `=== WATCH DATA ===` | One row per snapshot: `participant_label,participant_key,timestamp,heart_rate,accel_x,accel_y,accel_z,gyro_x,gyro_y,gyro_z,rot_w,rot_x,rot_y,rot_z,baro_hpa,light_lux,step_count` |
| `=== SESSION LOGS ===` | `id,run_id,ts,source,level,kind,payload_json` |
| `=== INTERVENTIONS ===` | `id,participant_label,participant_key,sent_at,game_type,notification_type,response,reaction_time_ms` |
| `=== EXPERIMENT RUNS ===` | `run_id,experiment_name,participant_label,started_at,ended_at` |
| `=== DISTRACTION RUNS ===` | `distraction_id,run_id,event_id,intervention_id,started_at,ended_at,response,reaction_time_ms` |
| `=== REPLAY MARKERS ===` | `ts,label`. Only in files saved from Replay Mode |

The phone app can write an equivalent file on its own; see the [Android README](https://github.com/RBurchard/JITAI-WizardAndroid#phone-side-csv-export-11-with-the-control-station).

---

## Communication Protocol Reference

### HTTP Endpoints Called by the Control Station

All requests use a 10-second timeout. The base URL is `http://{device-ip}:8080` (or the forwarded local port for USB devices).

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/status` | Check the device is reachable; watch connection state, session ID, uptime, last BPM |
| GET | `/data` | Session ID, participant label, last reaction time, last BPM, last action |
| GET | `/participant` | The participant info stored on the phone |
| POST | `/participant` | Set participant info (id, sessionId, label, deviceIp) |
| GET | `/experiment` | Pull the current experiment (with an `X-Experiment-Etag` header) |
| PUT | `/experiment` | Push an experiment; 409 if the ETag is stale |
| POST | `/experiment/control` | Run command: `start`, `auto`, `pause`, `stop`, `next`, `prev` |
| POST | `/trigger` | Send an `InterventionRequest` (forwarded to the watch) |
| POST | `/trigger/fire` | Fire a manual trigger by trigger ID |
| POST | `/watch/wake` | Hold the watch at full sensor rate for `{"minutes": n}` without a run |
| GET | `/logs?since={id}&limit={n}` | Poll session log entries after a given ID (fallback when `/events` is down) |
| GET | `/trivia` | Pull the trivia question bank |
| POST | `/trivia` | Push the full trivia question list |

### WebSocket Connections

| Path | Direction | Purpose |
|------|-----------|---------|
| `/stream` | Phone → Control Station | `WatchDataBatch` JSON about every 100 ms during a session (about once a minute while the watch idles); each batch holds all snapshots since the last one. Stored in `watch_data`. |
| `/events` | Phone → Control Station | `WsEnvelope` JSON for experiment state changes, log entries, trigger fires, run start/stop and intervention start/finish. Fills `session_logs`, `experiment_runs` and `distraction_runs`. |

Both reconnect automatically every 3 seconds if the connection drops.

### UDP Discovery

When **Scan** is pressed, the Control Station listens on UDP port 5280 for 3 seconds. Phones broadcast there every 5 seconds:

```json
{"app": "JITAIWizard", "port": 8080, "name": "<device model>"}
```

Packets whose `app` is not `"JITAIWizard"` are ignored.

### InterventionRequest JSON

The body of `POST /trigger`:

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

`gameType` and `triviaQuestion` are null when no game is selected. `phoneTaskType` (`DRAW`, `CAMERA`) is carried through but not acted on by the phone yet.

---

## Known Limitations and Notes

- The application supports exactly two participants at once. This is a design decision for the current study scope, not a hard technical limit.
- The phone's HTTP server runs **without authentication or encryption**. Do not use this system on untrusted networks. For realsies.
- Intervention responses (what the participant chose, or whether they finished a game) are received by the phone from the watch. The Control Station gets them over the `/events` WebSocket as `intervention-finished` envelopes and writes them to `distraction_runs`.
- Batches that arrive while the Control Station is not connected are not stored in its databank. Use the phone-side CSV export as a backup if the connection is unreliable.
- The release build is unsigned, so SmartScreen warns on first launch.
