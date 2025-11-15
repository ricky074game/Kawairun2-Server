import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;

public class AdminCommandHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        String command = params.getUtfString("command");
        if (command == null || command.isEmpty()) return;

        String[] parts = command.trim().split("\\s+");
        String cmd = parts[0].toLowerCase();

        if ("givecoins".equals(cmd)) handleGiveCoins(user, parts);
        else if ("help".equals(cmd)) handleHelp(user);
        else sendResponse(user, "Unknown command: " + cmd + ". Type 'help' for available commands.");
    }

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

        String success = "SUCCESS: Gave " + amount + " blue coins to " + targetUsername;
        sendResponse(adminUser, success);
    }

    private void handleHelp(User user) {
        StringBuilder help = new StringBuilder();
        help.append("Available Admin Commands:\n");
        help.append("- givecoins <username> <amount> : Give blue coins to a player (max 3000)\n");
        help.append("- help : Show this help message\n");
        sendResponse(user, help.toString());
    }

    private void sendResponse(User user, String message) {
        ISFSObject response = new SFSObject();
        response.putUtfString("message", message);
        send("AdminCommandResponse", response, user);
    }
}
