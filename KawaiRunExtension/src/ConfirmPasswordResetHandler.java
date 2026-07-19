import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;


public class ConfirmPasswordResetHandler extends BaseClientRequestHandler {
    private static final int MAX_CODE_ATTEMPTS = 5;

    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        try {
            String email = SecurityUtils.normalizeEmail(params.getUtfString("email"));
            String code = params.getUtfString("code");
            String newPassword = params.getUtfString("newPassword");

            if (!SecurityUtils.isValidEmail(email) || !SecurityUtils.isValidEmailCodeFormat(code)) {
                respond(user, "badcode");
                return;
            }

            if (!SecurityUtils.isValidPassword(newPassword)) {
                respond(user, "badpass");
                return;
            }

            KawaiRunExtension parentExt = (KawaiRunExtension) getParentExtension();

            if (parentExt.isEmailShowcaseMode()) {
                respond(user, "ok");
                return;
            }

            DatabaseManager dbManager = parentExt.getDbManager();
            if (dbManager == null || !dbManager.isConnected()) {
                respond(user, "error");
                return;
            }

            String username = dbManager.getUsernameByVerifiedEmail(email);
            Integer userId = username == null ? null : dbManager.getUserId(username);
            if (userId == null) {
                respond(user, "badcode");
                return;
            }

            DatabaseManager.EmailCodeRow row = dbManager.getEmailCode(userId, "reset");
            if (row == null || !email.equals(row.email)) {
                respond(user, "badcode");
                return;
            }

            if (row.expired) {
                dbManager.deleteEmailCode(userId, "reset");
                respond(user, "expired");
                return;
            }

            // Atomically charge one guess; false means the row is already at the attempt cap.
            if (!dbManager.tryConsumeEmailCodeAttempt(userId, "reset", MAX_CODE_ATTEMPTS)) {
                dbManager.deleteEmailCode(userId, "reset");
                respond(user, "toomany");
                return;
            }

            if (!SecurityUtils.matchesEmailCode(row.salt, code, row.hash)) {
                respond(user, "badcode");
                return;
            }

            String newHash = SecurityUtils.createPasswordHash(newPassword);
            if (newHash == null || !dbManager.updatePasswordForUser(username, newHash)) {
                respond(user, "error");
                return;
            }

            dbManager.deleteEmailCode(userId, "reset");
            parentExt.clearFailedLoginAttempts(username);
            trace("Password reset completed for user: " + username);

            // Kick any currently-connected session of this account so a stolen
            // session does not survive the password change.
            User activeUser = getApi().getUserByName(username);
            if (activeUser != null && activeUser != user) {
                getApi().disconnectUser(activeUser);
            }

            respond(user, "ok");
        } catch (Exception e) {
            trace("ConfirmPasswordReset error: " + e.getMessage());
            respond(user, "error");
        }
    }

    private void respond(User user, String status) {
        ISFSObject response = new SFSObject();
        response.putUtfString("status", status);
        send("PasswordResetResult", response, user);
    }
}
