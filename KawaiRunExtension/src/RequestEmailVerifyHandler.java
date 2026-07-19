import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;
import java.util.Locale;


public class RequestEmailVerifyHandler extends BaseClientRequestHandler {
    private static final int CODE_EXPIRY_MINUTES = 15;

    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        try {
            String username = user.getName();
            if (SecurityUtils.isGuestUser(username) || !SecurityUtils.isValidUsername(username)) {
                respond(user, "error");
                return;
            }

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

            Integer userId = dbManager.getUserId(username);
            if (userId == null) {
                respond(user, "error");
                return;
            }

            DatabaseManager.EmailInfo info = dbManager.getEmailInfo(username);
            if (info != null && info.verified) {
                respond(user, "already");
                return;
            }

            String accountKey = "verify:" + username.toLowerCase(Locale.ROOT);
            String mailKey = "mail:" + email;
            String ipKey = "ip:" + user.getSession().getAddress();
            if (!parentExt.canSendEmailNow(accountKey) || !parentExt.canSendEmailNow(mailKey) || !parentExt.canSendEmailNow(ipKey)) {
                respond(user, "cooldown");
                return;
            }
            if (!parentExt.tryAcquireEmailSend(accountKey) || !parentExt.tryAcquireEmailSend(mailKey) || !parentExt.tryAcquireEmailSend(ipKey)) {
                respond(user, "cooldown");
                return;
            }

            String code = SecurityUtils.generateEmailCode();
            String salt = SecurityUtils.generateCodeSalt();
            String hash = SecurityUtils.hashEmailCode(salt, code);
            if (hash == null || !dbManager.storeEmailCode(userId, "verify", email, salt, hash, CODE_EXPIRY_MINUTES)) {
                respond(user, "error");
                return;
            }

            String body = "Hi " + username + ",\r\n\r\n" +
                "Your Kawairun 2 email verification code is:\r\n\r\n" +
                "    " + code + "\r\n\r\n" +
                "The code expires in " + CODE_EXPIRY_MINUTES + " minutes.\r\n" +
                "If you did not request this, you can safely ignore this email.\r\n";
            parentExt.getEmailService().sendCodeEmail(email, "Kawairun 2 - Verify your email", body);

            respond(user, "sent");
        } catch (Exception e) {
            trace("RequestEmailVerify error: " + e.getMessage());
            respond(user, "error");
        }
    }

    private void respond(User user, String status) {
        ISFSObject response = new SFSObject();
        response.putUtfString("status", status);
        send("EmailVerifyResult", response, user);
    }
}
