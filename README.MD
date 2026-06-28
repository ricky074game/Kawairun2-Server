# Kawairun 2 - Server Reimplementation

This project is a complete reimplementation of the Kawairun 2 game infrastructure, including a custom game launcher, SmartFoxServer extension, and web server to restore functionality of the original Flash-based game.

[Discord Server (Play with us here!)](https://discord.gg/sp3QTrckG5)

Kawairun 2 is a multiplayer Flash game that has been restored through three main components:

1. **KawaiRun2Launcher** - A Windows launcher application that runs the game
2. **KawaiRunExtension** - A Java-based SmartFoxServer 2X extension for multiplayer functionality
3. **Server** - A Node.js web server for hosting game assets and serving configuration files

The game is accessible at **kawairun2.puzzlebest.tech**

---

## Building the Project

### 1. KawaiRun2Launcher

**Prerequisites:**
- .NET 8.0 SDK or later
- Windows for building with the bundled `build.ps1`

**Build Steps:**

```powershell
cd KawaiRun2Launcher
.\build.ps1
```

This produces launcher packages for:
- `win-x86`
- `win-x64`
- `osx-x64`

Published folders are written to:
```
KawaiRun2Launcher\publish\
```

Update ZIPs for the server are written to:
```
Server\launcher\
```

The launcher now:
- starts the bundled Flash projector with `game.swf`
- checks `Server/update.txt` for updates before launching
- auto-installs critical updates and prompts for optional ones
- renames the Windows Flash window to `KawaiRun 2`
- supports optional Discord Rich Presence via `launcher_config.json`

**Discord presence setup:**
- Open `KawaiRun2Launcher\launcher_config.json`
- Set `discord.enabled` to `true`
- Fill in `discord.applicationId` with your Discord application client ID
- Optionally set `largeImageKey` and `largeImageText`

**macOS Flash projector note:**
- The repository includes the standalone mac projector package used for the `osx-x64` build.
- Modern macOS versions may still block or refuse older Flash projector builds depending on OS security and architecture support.
---

### 2. KawaiRunExtension (SmartFoxServer)

**Prerequisites:**
- Java Development Kit (JDK) 11
- SmartFoxServer 2X installed (for dependency JARs)

**Build Steps:**

```powershell
cd KawaiRunExtension
.\build.ps1
```

Or manually with javac:
```powershell
javac -cp "C:\path\to\SmartFoxServer\lib\*" -d out\build src\*.java
jar cvfm out\jar\KawaiRunExtension.jar src\META-INF\MANIFEST.MF -C out\build .
```

**Installation:**
1. Copy `KawaiRunExtension.jar` to your SmartFoxServer extensions directory:
   ```
   SmartFoxServer_2X\SFS2X\extensions\KawaiRunExtension\
   ```

2. Set up the MySQL/MariaDB database:
   ```bash
   mysql -u root -p < database_schema.sql
   ```

3. Configure `DatabaseManager.java` with your database credentials (if needed)

4. Restart SmartFoxServer 2X

---

### 3. Server (Web Server)

**Prerequisites:**
- Node.js (v14 or later)

**Setup & Run:**

```bash
cd Server
npm install
node web_server.js
```

Or use the npm start script:
```bash
npm start
```

**Configuration:**
- Default port: `3000` (configurable via `PORT` environment variable)
- Default host: `0.0.0.0` (configurable via `HOST` environment variable)

**Example with custom port:**
```bash
$env:PORT=8080; node web_server.js
```

---

## Database Setup

The project uses MySQL/MariaDB for persistence. Run the schema file to create the required tables:

```sql
mysql -u root -p < KawaiRunExtension/database_schema.sql
```

**Tables created:**
- `users` - User accounts (username, password hash, login tracking)
- `player_saves` - Serialized game save data with quick-access stats
- `player_stats` - Additional statistics for leaderboards
- `user_sessions` - Session tracking for security

**Default credentials** (change in production):
- Database: `kawairun_db`
- User: `kawairun_server`
- Password: `KawaiRun2024!`
---

## Running the Complete System

1. **Start the database:**
   ```bash
   # Ensure MySQL/MariaDB is running
   systemctl start mysql  # Linux
   # or
   net start MySQL  # Windows
   ```

2. **Start SmartFoxServer 2X:**
   - Ensure the KawaiRunExtension is installed
   - Launch SmartFoxServer from its installation directory
   - Verify the extension loads without errors in the console

3. **Start the web server:**
   ```bash
   cd Server
   node web_server.js
   ```

4. **Distribute the launcher:**
   - Build KawaiRun2Launcher.exe
   - Host it on the web server or distribute directly
   - Players download and run the launcher to play

5. **Update server IP (if needed):**
   - Edit `Server/currentServer.txt` with the SmartFoxServer IP
   - The game client will fetch this on startup

---

## Configuration Files

### currentServer.txt
Contains the IP address of the SmartFoxServer:
```
3.143.25.120
```

### update.txt
Controls launcher updates:
```txt
version=1.0.0
critical=false
win_x86_url=https://kawairun2.puzzlebest.tech/launcher/win-x86.zip
win_x64_url=https://kawairun2.puzzlebest.tech/launcher/win-x64.zip
osx_x64_url=https://kawairun2.puzzlebest.tech/launcher/osx-x64.zip
```

If `critical=true`, the launcher will force the update and close the game if the update cannot be installed.

### crossdomain.xml
Allows Flash to make cross-domain requests:
```xml
<?xml version="1.0"?>
<cross-domain-policy>
  <allow-access-from domain="*" secure="false"/>
  <site-control permitted-cross-domain-policies="all"/>
</cross-domain-policy>
```

---

## Troubleshooting

**Launcher doesn't start:**
- Check that the platform package includes its Flash projector and `game.swf`
- On Windows, confirm `flash.exe` is next to `KawaiRun2Launcher.exe`
- On macOS, confirm `Flash Player.app` is next to the launcher binary

**Launcher won't update:**
- Verify `Server/update.txt` is reachable
- Verify the files in `Server/launcher/` match the URLs in `update.txt`
- Critical updates intentionally stop the game when download or install fails

**Can't connect to server:**
- Verify SmartFoxServer is running
- Check `currentServer.txt` has the correct IP
- Ensure port 9933 (default SFS port) is open

**Database connection errors:**
- Verify MySQL/MariaDB is running
- Check credentials in `DatabaseManager.java`
- Ensure the database schema has been imported

**Build script fails:**
- For KawaiRunExtension: Verify Java JDK is installed and SmartFoxServer lib path is correct
- For KawaiRun2Launcher: Ensure .NET 8.0 SDK is installed

---


