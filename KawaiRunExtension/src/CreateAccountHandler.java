import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.nio.charset.StandardCharsets;

public class CreateAccountHandler extends BaseClientRequestHandler {
    private String hashPassword(String plainPassword) {
        try {
            String saltedPassword = "JEDDAHBIKERSSALT2259GIYU2137" + plainPassword;
            MessageDigest md = MessageDigest.getInstance("SHA-256");
            byte[] hash = md.digest(saltedPassword.getBytes(StandardCharsets.UTF_8));

            StringBuilder hexString = new StringBuilder();
            for (byte b : hash) {
                String hex = Integer.toHexString(0xff & b);
                if (hex.length() == 1) hexString.append('0');
                hexString.append(hex);
            }
            return hexString.toString();
        } catch (NoSuchAlgorithmException e) {
            trace("Error hashing password: " + e.getMessage());
            return null;
        }
    }

    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        trace("CreateAccount request from: " + user.getName());

        try {
            String username = params.getUtfString("username");
            String plainPassword = params.getUtfString("password");

            String passwordHash = hashPassword(plainPassword);
            if (passwordHash == null) {
                ISFSObject response = new SFSObject();
                send("DatabaseError", response, user);
                return;
            }

            byte[] saveData = null;
            if (params.containsKey("save")) {
                saveData = params.getByteArray("save");
            }

            KawaiRunExtension parentExt = (KawaiRunExtension) getParentExtension();
            DatabaseManager dbManager = parentExt.getDbManager();

            if (dbManager != null && dbManager.isConnected()) {
                if (dbManager.userExists(username)) {
                    ISFSObject response = new SFSObject();
                    send("AlreadyExists", response, user);
                } else {
                    boolean created = dbManager.createAccount(username, passwordHash, saveData);

                    if (created) {
                        parentExt.markRecentRegistration(username);

                        ISFSObject response = new SFSObject();
                        response.putUtfString("username", username);
                        response.putUtfString("passwordE", plainPassword);
                        send("CreatedUser", response, user);
                    } else {
                        ISFSObject response = new SFSObject();
                        send("DatabaseError", response, user);
                    }
                }
            } else {
                ISFSObject response = new SFSObject();
                send("DatabaseError", response, user);
            }

        } catch (Exception e) {
            trace("CreateAccount error: " + e.getMessage());
            ISFSObject response = new SFSObject();
            send("DatabaseError", response, user);
        }
    }
}
