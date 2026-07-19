import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;

public class AuthenticateHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        trace("--- 'Authenticate' request received from user: " + user.getName() + " ---");

        // Re-apply a server-side mute if this user was muted earlier this session.
        KawaiRunExtension ext = (KawaiRunExtension) getParentExtension();
        if (ext.isMuted(user.getName())) {
            ISFSObject muteResponse = new SFSObject();
            muteResponse.putBool("muted", true);
            send("AdminMute", muteResponse, user);
        }

        if (params.containsKey("EKey")) {
            trace("Authenticate request included an EKey payload");
        }

        String username = user.getName();
        boolean isGuest = SecurityUtils.isGuestUser(username);

        if (isGuest) {
            trace("--- Guest authentication acknowledged for: " + username + " ---");
            return;
        }
        KawaiRunExtension parentExt = (KawaiRunExtension) getParentExtension();
        DatabaseManager dbManager = parentExt.getDbManager();

        if (dbManager != null && dbManager.isConnected()) {
            if (dbManager.userExists(username)) {
                trace("--- User " + username + " verified in database (existing account) ---");

                DatabaseManager.EmailInfo emailInfo = dbManager.getEmailInfo(username);
                ISFSObject emailStatus = new SFSObject();
                boolean verified = emailInfo != null && emailInfo.verified;
                emailStatus.putBool("verified", verified);
                emailStatus.putUtfString("masked", verified ? SecurityUtils.maskEmail(emailInfo.email) : "");
                send("EmailStatus", emailStatus, user);
            } else {
                trace("--- User " + username + " could not be verified in database ---");
            }
        } else {
            trace("Authentication check skipped because database is unavailable");
        }

        trace("--- Authentication acknowledged for: " + user.getName() + " ---");
    }
}

