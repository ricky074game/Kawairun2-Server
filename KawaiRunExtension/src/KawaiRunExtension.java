import com.smartfoxserver.v2.core.SFSEventType;
import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.extensions.SFSExtension;
import com.smartfoxserver.v2.api.CreateRoomSettings;
import com.smartfoxserver.v2.entities.Room;
import java.util.Arrays;
import java.util.Locale;
import java.util.Objects;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

public class KawaiRunExtension extends SFSExtension {
    private static final int MAX_FAILED_LOGIN_ATTEMPTS = 5;
    private static final long FAILED_LOGIN_WINDOW_MS = 10 * 60 * 1000L;
    private static final long FAILED_LOGIN_LOCKOUT_MS = 15 * 60 * 1000L;

    private DatabaseManager dbManager;
    private MatchmakingManager matchmakingManager;

    private static final ConcurrentHashMap<String, RecentRegistrationInfo> recentRegistrations = new ConcurrentHashMap<>();
    private static final long REGISTRATION_GRACE_PERIOD = 2000; // 2 seconds
    private final ConcurrentHashMap<String, FailedLoginState> failedLoginAttempts = new ConcurrentHashMap<>();
    private final Set<String> adminUsers = ConcurrentHashMap.newKeySet();
    private final Set<String> welcomeCoinsClaimedUsers = ConcurrentHashMap.newKeySet();
    private final Set<String> mutedUsers = ConcurrentHashMap.newKeySet();

    @Override
    public void init() {
        trace("Kawai Run Java Extension Started");

        loadAdminUsers();

        dbManager = new DatabaseManager(this);
        if (dbManager.connect()) {
            trace("Database initialized");
        } else {
            trace("Database not available - running without persistence");
        }

        matchmakingManager = new MatchmakingManager(this);

        addEventHandler(SFSEventType.USER_LOGIN, LoginHandler.class);
        addEventHandler(SFSEventType.USER_LEAVE_ROOM, RoomCleanupHandler.class);

        addRequestHandler("Authenticate", AuthenticateHandler.class);
        addRequestHandler("CreateAccount", CreateAccountHandler.class);
        addRequestHandler("SaveRequest", SaveRequestHandler.class);
        addRequestHandler("SaveUpdate", SaveUpdateHandler.class);

        addRequestHandler("FindPartner", FindPartnerHandler.class);
        addRequestHandler("FindPartnerTagTeam", FindPartnerTagTeamHandler.class);
        addRequestHandler("StopFindPartner", StopFindPartnerHandler.class);

        addRequestHandler("CreatePrivateRoom", CreatePrivateRoomHandler.class);
        addRequestHandler("AddReady", AddReadyHandler.class);
        addRequestHandler("RestartGame", RestartGameHandler.class);

        addRequestHandler("AskStats", AskStatsHandler.class);
        addRequestHandler("CheckForCoins", CheckForCoinsHandler.class);
        addRequestHandler("GiveCoins", GiveCoinsHandler.class);
        addRequestHandler("AdminCommand", AdminCommandHandler.class);

        addRequestHandler("DONTKICKMEOUT", DontKickMeOutHandler.class);
        
        createStaticRoomIfMissing("SignUp");
        createStaticRoomIfMissing("LoggedIn");
        createStaticRoomIfMissing("Matchmaking");
        createStaticRoomIfMissing("The Lobby");

        try {
            getParentZone().setMaxUserVariablesAllowed(30);
            trace("Successfully set maximum user variables allowed to 30.");
        } catch (Exception e) {
            trace("Error setting max user variables: " + e.getMessage());
        }

    }

    private void createStaticRoomIfMissing(String roomName) {
        try {
            if (getParentZone().getRoomByName(roomName) == null) {
                CreateRoomSettings settings = new CreateRoomSettings();
                settings.setName(roomName);
                settings.setMaxUsers(200);
                settings.setGroupId("default");
                settings.setDynamic(false); // Make it static so it stays active
                getApi().createRoom(getParentZone(), settings, null);
                trace("Programmatically created static room: " + roomName);
            } else {
                trace("Static room already exists: " + roomName);
            }
        } catch (Exception e) {
            trace("Error creating static room '" + roomName + "': " + e.getMessage());
        }
    }

    @Override
    public void destroy() {
        trace("Kawai Run Java Extension Stopping");
        if (dbManager != null) dbManager.disconnect();
        super.destroy();
        trace("Kawai Run Java Extension Stopped");
    }

    public DatabaseManager getDbManager() { return dbManager; }
    public MatchmakingManager getMatchmakingManager() { return matchmakingManager; }

    public void markRecentRegistration(String username, String plainPassword) {
        recentRegistrations.put(
            username.toLowerCase(Locale.ROOT),
            new RecentRegistrationInfo(System.currentTimeMillis(), plainPassword)
        );
    }

    public boolean isRecentRegistration(String username) {
        RecentRegistrationInfo info = recentRegistrations.get(username.toLowerCase(Locale.ROOT));
        if (info == null) return false;
        long age = System.currentTimeMillis() - info.timestamp;
        if (age > REGISTRATION_GRACE_PERIOD) {
            recentRegistrations.remove(username.toLowerCase(Locale.ROOT));
            return false;
        }
        return true;
    }

    public String getRecentRegistrationPassword(String username) {
        RecentRegistrationInfo info = recentRegistrations.get(username.toLowerCase(Locale.ROOT));
        if (info == null) {
            return null;
        }

        long age = System.currentTimeMillis() - info.timestamp;
        if (age > REGISTRATION_GRACE_PERIOD) {
            recentRegistrations.remove(username.toLowerCase(Locale.ROOT));
            return null;
        }

        return info.plainPassword;
    }

    public void clearRecentRegistration(String username) {
        recentRegistrations.remove(username.toLowerCase(Locale.ROOT));
    }

    public boolean isAdminUser(User user) {
        if (user == null || user.getName() == null) {
            return false;
        }

        return adminUsers.contains(user.getName().toLowerCase(Locale.ROOT));
    }

    public boolean claimWelcomeCoins(User user) {
        if (user == null || !SecurityUtils.isValidUsername(user.getName())) {
            return false;
        }

        return welcomeCoinsClaimedUsers.add(user.getName().toLowerCase(Locale.ROOT));
    }

    public boolean isLoginBlocked(String username) {
        if (username == null) {
            return false;
        }

        FailedLoginState state = failedLoginAttempts.get(username.toLowerCase(Locale.ROOT));
        if (state == null) {
            return false;
        }

        long now = System.currentTimeMillis();
        if (state.lockedUntil > now) {
            return true;
        }

        if (now - state.windowStartedAt > FAILED_LOGIN_WINDOW_MS) {
            failedLoginAttempts.remove(username.toLowerCase(Locale.ROOT), state);
        }

        return false;
    }

    public void recordFailedLoginAttempt(String username) {
        if (username == null) {
            return;
        }

        String normalized = username.toLowerCase(Locale.ROOT);
        long now = System.currentTimeMillis();

        failedLoginAttempts.compute(normalized, (key, state) -> {
            if (state == null || now - state.windowStartedAt > FAILED_LOGIN_WINDOW_MS) {
                return new FailedLoginState(1, now, 0L);
            }

            int attempts = state.attempts + 1;
            long lockedUntil = attempts >= MAX_FAILED_LOGIN_ATTEMPTS ? now + FAILED_LOGIN_LOCKOUT_MS : state.lockedUntil;
            return new FailedLoginState(attempts, state.windowStartedAt, lockedUntil);
        });
    }

    public void clearFailedLoginAttempts(String username) {
        if (username == null) {
            return;
        }

        failedLoginAttempts.remove(username.toLowerCase(Locale.ROOT));
    }

    public boolean muteUser(String username) {
        if (username == null || username.isEmpty()) return false;
        return mutedUsers.add(username.toLowerCase(Locale.ROOT));
    }

    public boolean unmuteUser(String username) {
        if (username == null || username.isEmpty()) return false;
        return mutedUsers.remove(username.toLowerCase(Locale.ROOT));
    }

    public boolean isMuted(String username) {
        if (username == null || username.isEmpty()) return false;
        return mutedUsers.contains(username.toLowerCase(Locale.ROOT));
    }

    public String giveCoins(String username, int amount) {
        if (username == null || username.isEmpty()) return "ERROR: Username is required";
        if (amount <= 0) return "ERROR: Amount must be positive";
        if (amount > 3000) return "ERROR: Amount too high";

        com.smartfoxserver.v2.entities.User targetUser = getApi().getUserByName(username);
        if (targetUser == null) return "ERROR: User not found";

        com.smartfoxserver.v2.entities.data.ISFSObject response = new com.smartfoxserver.v2.entities.data.SFSObject();
        response.putLong("coins", amount);
        send("CoinSend", response, targetUser);

        return "SUCCESS: Gave " + amount + " blue coins to " + username;
    }

    private void loadAdminUsers() {
        String configuredAdmins = System.getProperty("kawairun.admin.users");
        if (configuredAdmins == null || configuredAdmins.trim().isEmpty()) {
            configuredAdmins = System.getenv("KAWAIRUN_ADMIN_USERS");
        }

        if (configuredAdmins == null || configuredAdmins.trim().isEmpty()) {
            trace("No admin allowlist configured. Admin-only commands are disabled.");
            return;
        }

        Arrays.stream(configuredAdmins.split(","))
            .map(String::trim)
            .filter(value -> !value.isEmpty())
            .map(value -> value.toLowerCase(Locale.ROOT))
            .forEach(adminUsers::add);

        trace("Loaded " + adminUsers.size() + " admin user(s) from allowlist.");
    }

    private static class FailedLoginState {
        private final int attempts;
        private final long windowStartedAt;
        private final long lockedUntil;

        private FailedLoginState(int attempts, long windowStartedAt, long lockedUntil) {
            this.attempts = attempts;
            this.windowStartedAt = windowStartedAt;
            this.lockedUntil = lockedUntil;
        }
    }

    private static class RecentRegistrationInfo {
        private final long timestamp;
        private final String plainPassword;

        private RecentRegistrationInfo(long timestamp, String plainPassword) {
            this.timestamp = timestamp;
            this.plainPassword = Objects.requireNonNullElse(plainPassword, "");
        }
    }
}
