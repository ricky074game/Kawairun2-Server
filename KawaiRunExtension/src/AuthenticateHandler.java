import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;


public class AuthenticateHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        trace("--- 'Authenticate' request received from user: " + user.getName() + " ---");

        String eKey = params.containsKey("EKey") ? params.getUtfString("EKey") : "none";
        trace("EKey: " + eKey);

        String username = user.getName();
        boolean isGuest = username == null || username.isEmpty() || username.toLowerCase().startsWith("guest");

        if (isGuest) {
            trace("--- Guest authentication acknowledged for: " + username + " ---");
            return;
        }
        KawaiRunExtension parentExt = (KawaiRunExtension) getParentExtension();
        DatabaseManager dbManager = parentExt.getDbManager();

        if (dbManager != null && dbManager.isConnected()) {
            if (dbManager.userExists(username)) {
                trace("--- User " + username + " verified in database (existing account) ---");
            } else {
                trace("--- User " + username + " not in database (may be new registration) ---");
            }
        } else {
            trace("!!! WARNING: Database not available - allowing authentication without verification !!!");
        }

        trace("--- Authentication acknowledged for: " + user.getName() + " ---");
    }
}

