import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;


public class ConfirmEmailVerifyHandler extends BaseClientRequestHandler {
    private static final int MAX_CODE_ATTEMPTS = 5;

    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        try {
            String username = user.getName();
            if (SecurityUtils.isGuestUser(username) || !SecurityUtils.isValidUsername(username)) {
                respond(user, "error");
                return;
            }

            String code = params.getUtfString("code");
            if (!SecurityUtils.isValidEmailCodeFormat(code)) {
                respond(user, "badcode");
                return;
            }

            KawaiRunExtension parentExt = (KawaiRunExtension) getParentExtension();

            if (parentExt.isEmailShowcaseMode()) {
                respond(user, "verified");
                ISFSObject showcaseStatus = new SFSObject();
                showcaseStatus.putBool("verified", true);
                showcaseStatus.putUtfString("masked", "***@***");
                send("EmailStatus", showcaseStatus, user);
                return;
            }

            DatabaseManager dbManager = parentExt.getDbManager();
            if (dbManager == null || !dbManager.isConnected()) {
                respond(user, "error");
                return;
            }

            Integer userId = dbManager.getUserId(username);
            if (userId == null) {
                respond(user, "error");
                return;
            }

            DatabaseManager.EmailCodeRow row = dbManager.getEmailCode(userId, "verify");
            if (row == null) {
                respond(user, "badcode");
                return;
            }

            if (row.expired) {
                dbManager.deleteEmailCode(userId, "verify");
                respond(user, "expired");
                return;
            }

            // Atomically charge one guess; false means the row is already at the attempt cap.
            if (!dbManager.tryConsumeEmailCodeAttempt(userId, "verify", MAX_CODE_ATTEMPTS)) {
                dbManager.deleteEmailCode(userId, "verify");
                respond(user, "toomany");
                return;
            }

            if (!SecurityUtils.matchesEmailCode(row.salt, code, row.hash)) {
                respond(user, "badcode");
                return;
            }

            int result = dbManager.setVerifiedEmail(userId, row.email);
            if (result == -1) {
                dbManager.deleteEmailCode(userId, "verify");
                respond(user, "inuse");
                return;
            }
            if (result != 1) {
                respond(user, "error");
                return;
            }

            dbManager.deleteEmailCode(userId, "verify");
            trace("Email verified for user: " + username);
            respond(user, "verified");

            ISFSObject status = new SFSObject();
            status.putBool("verified", true);
            status.putUtfString("masked", SecurityUtils.maskEmail(row.email));
            send("EmailStatus", status, user);
        } catch (Exception e) {
            trace("ConfirmEmailVerify error: " + e.getMessage());
            respond(user, "error");
        }
    }

    private void respond(User user, String status) {
        ISFSObject response = new SFSObject();
        response.putUtfString("status", status);
        send("EmailVerifyResult", response, user);
    }
}
