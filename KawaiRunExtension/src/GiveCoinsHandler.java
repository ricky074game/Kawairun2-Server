import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;

public class GiveCoinsHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        trace("GiveCoins request from: " + user.getName());

        String targetUsername = params.getUtfString("username");
        long amount = params.getLong("amount");

        if (targetUsername == null || targetUsername.isEmpty()) {
            sendError(user, "Username is required");
            return;
        }

        if (amount <= 0) {
            sendError(user, "Amount must be greater than 0");
            return;
        }

        if (amount > 3000) {
            sendError(user, "Amount too high");
            return;
        }

        User targetUser = getApi().getUserByName(targetUsername);

        if (targetUser == null) {
            sendError(user, "User not found or not online");
            return;
        }

        ISFSObject response = new SFSObject();
        response.putLong("coins", amount);
        send("CoinSend", response, targetUser);

        ISFSObject confirmation = new SFSObject();
        confirmation.putUtfString("message", "Successfully gave " + amount + " coins to " + targetUsername);
        confirmation.putBool("success", true);

        send("GiveCoinsResponse", confirmation, user);
    }

    private void sendError(User user, String errorMessage) {
        ISFSObject response = new SFSObject();
        response.putUtfString("message", errorMessage);
        response.putBool("success", false);

        send("GiveCoinsResponse", response, user);
    }
}
