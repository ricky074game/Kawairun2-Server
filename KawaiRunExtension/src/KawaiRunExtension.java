import com.smartfoxserver.v2.core.SFSEventType;
import com.smartfoxserver.v2.extensions.SFSExtension;
import java.util.concurrent.ConcurrentHashMap;

public class KawaiRunExtension extends SFSExtension {

    private DatabaseManager dbManager;
    private MatchmakingManager matchmakingManager;

    private static final ConcurrentHashMap<String, Long> recentRegistrations = new ConcurrentHashMap<>();
    private static final long REGISTRATION_GRACE_PERIOD = 2000; // 2 seconds

    @Override
    public void init() {
        trace("Kawai Run Java Extension Started");

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

    public void markRecentRegistration(String username) {
        recentRegistrations.put(username.toLowerCase(), System.currentTimeMillis());
    }

    public boolean isRecentRegistration(String username) {
        Long timestamp = recentRegistrations.get(username.toLowerCase());
        if (timestamp == null) return false;
        long age = System.currentTimeMillis() - timestamp;
        if (age > REGISTRATION_GRACE_PERIOD) {
            recentRegistrations.remove(username.toLowerCase());
            return false;
        }
        return true;
    }

    public void clearRecentRegistration(String username) {
        recentRegistrations.remove(username.toLowerCase());
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
}
