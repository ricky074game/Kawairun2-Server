import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;

public class AdminCommandHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        KawaiRunExtension parentExt = (KawaiRunExtension) getParentExtension();
        if (!parentExt.isAdminUser(user)) {
            trace("Blocked AdminCommand attempt from non-admin user: " + user.getName());
            sendResponse(user, "Unauthorized");
            return;
        }

        String command = params.getUtfString("command");
        if (command == null || command.isEmpty()) return;

        String[] parts = command.trim().split("\\s+");
        String cmd = parts[0].toLowerCase();

        switch (cmd) {
            case "givecoins": handleGiveCoins(user, parts); break;
            case "give":      handleGive(user, parts); break;
            case "mute":      handleMute(user, parts, true); break;
            case "unmute":    handleMute(user, parts, false); break;
            case "ban":       handleBan(user, parts, true); break;
            case "unban":     handleBan(user, parts, false); break;
            case "clear":     handleClear(user, parts); break;
            case "help":      handleHelp(user); break;
            default:          sendResponse(user, "Unknown command: " + cmd + ". Type 'help' for available commands.");
        }
    }

    // givecoins <username> <amount> -> blue coins to an online player (legacy)
    private void handleGiveCoins(User adminUser, String[] parts) {
        if (parts.length < 3) { sendResponse(adminUser, "Usage: givecoins <username> <amount>"); return; }

        String targetUsername = parts[1];
        int amount;
        try { amount = Integer.parseInt(parts[2]); } catch (NumberFormatException e) { sendResponse(adminUser, "Invalid amount: " + parts[2]); return; }

        if (amount <= 0) { sendResponse(adminUser, "Amount must be positive (got " + amount + ")"); return; }
        if (amount > 3000) { sendResponse(adminUser, "Amount too high (max 3000)"); return; }

        User targetUser = getApi().getUserByName(targetUsername);
        if (targetUser == null) { sendResponse(adminUser, "User '" + targetUsername + "' not found or not online"); return; }

        ISFSObject coinResponse = new SFSObject();
        coinResponse.putLong("coins", amount);
        send("CoinSend", coinResponse, targetUser);

        sendResponse(adminUser, "SUCCESS: Gave " + amount + " blue coins to " + targetUsername);
    }

    // give <username> <coins> <blueCoins> -> grants both currencies to an online player
    private void handleGive(User adminUser, String[] parts) {
        if (parts.length < 4) { sendResponse(adminUser, "Usage: give <username> <coins> <blueCoins>"); return; }

        String targetUsername = parts[1];
        long coins;
        long blueCoins;
        try { coins = Long.parseLong(parts[2]); } catch (NumberFormatException e) { sendResponse(adminUser, "Invalid coins amount: " + parts[2]); return; }
        try { blueCoins = Long.parseLong(parts[3]); } catch (NumberFormatException e) { sendResponse(adminUser, "Invalid blue coins amount: " + parts[3]); return; }

        if (coins < 0 || blueCoins < 0) { sendResponse(adminUser, "Amounts cannot be negative"); return; }
        if (coins == 0 && blueCoins == 0) { sendResponse(adminUser, "Nothing to give (both amounts are 0)"); return; }
        // Blue coins are anti-cheat capped client-side at 3000 per grant.
        if (blueCoins > 3000) { sendResponse(adminUser, "Blue coins too high (max 3000 per grant)"); return; }
        if (coins > 1000000) { sendResponse(adminUser, "Coins too high (max 1000000 per grant)"); return; }

        User targetUser = getApi().getUserByName(targetUsername);
        if (targetUser == null) { sendResponse(adminUser, "User '" + targetUsername + "' not found or not online"); return; }

        ISFSObject giveResponse = new SFSObject();
        giveResponse.putLong("coins", coins);
        giveResponse.putLong("blueCoins", blueCoins);
        send("AdminGive", giveResponse, targetUser);

        sendResponse(adminUser, "SUCCESS: Gave " + coins + " coins and " + blueCoins + " blue coins to " + targetUsername);
    }

    // mute/unmute <username> -> blocks the player's chat (in-memory, online session)
    private void handleMute(User adminUser, String[] parts, boolean mute) {
        if (parts.length < 2) { sendResponse(adminUser, "Usage: " + (mute ? "mute" : "unmute") + " <username>"); return; }

        String targetUsername = parts[1];
        KawaiRunExtension parentExt = (KawaiRunExtension) getParentExtension();
        boolean changed = mute ? parentExt.muteUser(targetUsername) : parentExt.unmuteUser(targetUsername);

        User targetUser = getApi().getUserByName(targetUsername);
        if (targetUser != null) {
            ISFSObject muteResponse = new SFSObject();
            muteResponse.putBool("muted", mute);
            send("AdminMute", muteResponse, targetUser);
        }

        String verb = mute ? "Muted" : "Unmuted";
        String already = mute ? " (was already muted)" : " (was not muted)";
        String onlineNote = targetUser == null ? " [offline - will apply if they reconnect this session]" : "";
        sendResponse(adminUser, "SUCCESS: " + verb + " " + targetUsername + (changed ? "" : already) + onlineNote);
    }

    // ban/unban <username> -> toggles users.is_active in the DB; bans kick the live session
    private void handleBan(User adminUser, String[] parts, boolean ban) {
        if (parts.length < 2) { sendResponse(adminUser, "Usage: " + (ban ? "ban" : "unban") + " <username>"); return; }

        String targetUsername = parts[1];
        KawaiRunExtension parentExt = (KawaiRunExtension) getParentExtension();
        DatabaseManager db = parentExt.getDbManager();
        if (db == null) { sendResponse(adminUser, "Database unavailable - cannot " + (ban ? "ban" : "unban")); return; }

        boolean ok = db.setUserActive(targetUsername, !ban);
        if (!ok) { sendResponse(adminUser, "User '" + targetUsername + "' not found in database"); return; }

        String kicked = "";
        if (ban) {
            User targetUser = getApi().getUserByName(targetUsername);
            if (targetUser != null) {
                try {
                    getApi().disconnectUser(targetUser);
                    kicked = " (kicked from current session)";
                } catch (Exception e) {
                    trace("Error disconnecting banned user " + targetUsername + ": " + e.getMessage());
                }
            }
        }

        sendResponse(adminUser, "SUCCESS: " + (ban ? "Banned " : "Unbanned ") + targetUsername + kicked);
    }

    // clear <username> -> wipes the player's save except level + stats (enforced on the online client)
    private void handleClear(User adminUser, String[] parts) {
        if (parts.length < 2) { sendResponse(adminUser, "Usage: clear <username>"); return; }

        String targetUsername = parts[1];
        User targetUser = getApi().getUserByName(targetUsername);
        if (targetUser == null) { sendResponse(adminUser, "User '" + targetUsername + "' not found or not online (clear requires the target online)"); return; }

        send("AdminClear", new SFSObject(), targetUser);
        sendResponse(adminUser, "SUCCESS: Cleared " + targetUsername + " (kept level + stats)");
    }

    private void handleHelp(User user) {
        StringBuilder help = new StringBuilder();
        help.append("Available Admin Commands:\n");
        help.append("- give <username> <coins> <blueCoins> : Grant both currencies (online; blue max 3000)\n");
        help.append("- givecoins <username> <amount> : Grant blue coins (online; max 3000)\n");
        help.append("- mute <username> / unmute <username> : Block/allow a player's chat\n");
        help.append("- ban <username> / unban <username> : Block/allow login (kicks on ban)\n");
        help.append("- clear <username> : Wipe a player's save except level + stats (online)\n");
        help.append("- help : Show this help message\n");
        sendResponse(user, help.toString());
    }

    private void sendResponse(User user, String message) {
        ISFSObject response = new SFSObject();
        response.putUtfString("message", message);
        send("AdminCommandResponse", response, user);
    }
}
