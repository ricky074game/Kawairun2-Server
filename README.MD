# Kawairun 2 - Server Reimplementation

This project is a complete reimplementation of the Kawairun 2 game infrastructure, including a custom game launcher, SmartFoxServer extension, and web server to restore functionality of the original Flash-based game.

Kawairun 2 is a multiplayer Flash game that has been restored through three main components:

1. **KawaiRun2Launcher** - A Windows launcher application that runs the game
2. **KawaiRunExtension** - A Java-based SmartFoxServer 2X extension for multiplayer functionality
3. **Server** - A Node.js web server for hosting game assets and serving configuration files

The game is accessible at **puzzlebest.tech**

---

## Building the Project

### 1. KawaiRun2Launcher (Windows)

**Prerequisites:**
- .NET 8.0 SDK or later
- Windows operating system

**Build Steps:**

```powershell
cd KawaiRun2Launcher
dotnet publish -c Release -r win-x64 --self-contained true
```

The compiled executable will be located at:
```
KawaiRun2Launcher\bin\Release\net8.0-windows\win-x64\publish\KawaiRun2Launcher.exe
```
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
- Ensure .NET 8.0 Runtime is installed
- Check that `flash.exe` and `game.swf` are embedded in the project

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

