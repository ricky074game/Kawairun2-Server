# Kawairun 2 Revival

A community revival of the multiplayer running game Kawairun 2. Play at **[kawairun2.puzzlebest.tech](https://kawairun2.puzzlebest.tech)** or join the [Discord](https://discord.gg/sp3QTrckG5).

## Projects

| Folder | What it is |
|---|---|
| `KawaiRun2Launcher` | Desktop launcher for Windows, macOS and Linux. Plays the HTML5 or Flash version, handles updates and controllers. |
| `KawaiRun2Android` | Android app. |
| `KawaiRunExtension` | SmartFoxServer 2X extension for multiplayer, accounts and saves. |

## Building

**Launcher** (.NET 8 SDK): run `KawaiRun2Launcher\build.ps1`.

**Android** (JDK 17+, Android SDK, Gradle 8.14): run `gradle assembleRelease` in `KawaiRun2Android`.

**Extension** (JDK 11): run `KawaiRunExtension\build.ps1`, then copy the jar to your SmartFoxServer extensions folder. Create the database with `KawaiRunExtension/database_schema.sql`.

## License

See [LICENSE](LICENSE).
