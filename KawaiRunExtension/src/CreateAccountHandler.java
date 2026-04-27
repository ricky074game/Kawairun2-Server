import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;

public class CreateAccountHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        trace("CreateAccount request from: " + user.getName());

        try {
            String username = params.getUtfString("username");
            String plainPassword = params.getUtfString("password");

            if (!SecurityUtils.isValidUsername(username) || !SecurityUtils.isValidPassword(plainPassword)) {
                trace("Rejected account creation for invalid username/password format");
                ISFSObject response = new SFSObject();
                send("DatabaseError", response, user);
                return;
            }

            String passwordHash = SecurityUtils.hashPasswordForStorage(plainPassword);
            if (passwordHash == null) {
                ISFSObject response = new SFSObject();
                send("DatabaseError", response, user);
                return;
            }

            byte[] saveData = new byte[0];
            if (params.containsKey("save") && params.getByteArray("save") != null) {
                saveData = params.getByteArray("save");
            }

            if (!SecurityUtils.isValidSaveData(saveData)) {
                trace("Rejected oversized save payload during account creation for: " + username);
                ISFSObject response = new SFSObject();
                send("DatabaseError", response, user);
                return;
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
