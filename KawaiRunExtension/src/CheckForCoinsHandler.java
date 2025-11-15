import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;

public class CheckForCoinsHandler extends BaseClientRequestHandler {
    // Give a one-time award that's accepted by the client (client treats >3000 as hack)
    private static final long ONE_TIME_COINS = 3000L;
    private static final String CLAIM_PROP = "blueCoinsClaimed";

    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        trace("CheckForCoins request from: " + user.getName());

        boolean alreadyClaimed = false;
        Object prop = user.getProperty(CLAIM_PROP);
        if (prop instanceof Boolean) {
            alreadyClaimed = ((Boolean) prop).booleanValue();
        }

        long coinsToGive = 0L;
        if (!alreadyClaimed) {
            coinsToGive = ONE_TIME_COINS;
            user.setProperty(CLAIM_PROP, Boolean.TRUE);
            trace("Granted " + coinsToGive + " blue coins to: " + user.getName());
        } else {
            trace("User " + user.getName() + " already claimed blue coins.");
        }

        ISFSObject response = new SFSObject();
        response.putLong("coins", coinsToGive);
        send("CoinSend", response, user);
    }
}