import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;


public class RequestPasswordResetHandler extends BaseClientRequestHandler {
    private static final int CODE_EXPIRY_MINUTES = 15;

    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        try {
            String email = SecurityUtils.normalizeEmail(params.getUtfString("email"));
            if (!SecurityUtils.isValidEmail(email)) {
                respond(user, "invalid");
                return;
            }

            KawaiRunExtension parentExt = (KawaiRunExtension) getParentExtension();

            if (parentExt.isEmailShowcaseMode()) {
                respond(user, "sent");
                return;
            }

            DatabaseManager dbManager = parentExt.getDbManager();
            if (dbManager == null || !dbManager.isConnected()) {
                respond(user, "error");
                return;
            }

            String mailKey = "reset:" + email;
            String sessionKey = "resetsess:" + user.getSession().getHashId();
            String ipKey = "ip:" + user.getSession().getAddress();
            if (!parentExt.canSendEmailNow(mailKey) || !parentExt.canSendEmailNow(sessionKey) || !parentExt.canSendEmailNow(ipKey)) {
                respond(user, "cooldown");
                return;
            }
            if (!parentExt.tryAcquireEmailSend(mailKey) || !parentExt.tryAcquireEmailSend(sessionKey) || !parentExt.tryAcquireEmailSend(ipKey)) {
                respond(user, "cooldown");
                return;
            }

            String code = SecurityUtils.generateEmailCode();
            String salt = SecurityUtils.generateCodeSalt();
            String hash = SecurityUtils.hashEmailCode(salt, code);

            String username = dbManager.getUsernameByVerifiedEmail(email);
            if (username != null && hash != null) {
                Integer userId = dbManager.getUserId(username);
                if (userId != null && dbManager.storeEmailCode(userId, "reset", email, salt, hash, CODE_EXPIRY_MINUTES)) {
                    String body = "Hi " + username + ",\r\n\r\n" +
                        "A password reset was requested for your Kawairun 2 account.\r\n\r\n" +
                        "Username: " + username + "\r\n" +
                        "Reset code:\r\n\r\n" +
                        "    " + code + "\r\n\r\n" +
                        "The code expires in " + CODE_EXPIRY_MINUTES + " minutes.\r\n" +
                        "If you did not request this, you can safely ignore this email - your password is unchanged.\r\n";
                    parentExt.getEmailService().sendCodeEmail(email, "Kawairun 2 - Password reset code", body);
                }
            } else {
                // Do comparable DB work when the email is unknown/unverified so response timing
                // does not reveal whether an address belongs to a registered account.
                dbManager.pingTiming();
            }

            respond(user, "sent");
        } catch (Exception e) {
            trace("RequestPasswordReset error: " + e.getMessage());
            respond(user, "error");
        }
    }

    private void respond(User user, String status) {
        ISFSObject response = new SFSObject();
        response.putUtfString("status", status);
        send("PasswordResetResult", response, user);
    }
}
