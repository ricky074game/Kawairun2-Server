import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;

public class SaveRequestHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        trace("SaveRequest from: " + user.getName());

        try {
            KawaiRunExtension parentExt = (KawaiRunExtension) getParentExtension();
            DatabaseManager dbManager = parentExt.getDbManager();

            if (dbManager != null && dbManager.isConnected()) {
                byte[] saveData = dbManager.getSaveData(user.getName());

                if (saveData != null && saveData.length > 0) {
                    ISFSObject response = new SFSObject();
                    response.putByteArray("save", saveData);
                    send("SaveRecieve", response, user);
                    trace("Save data sent to: " + user.getName());
                } else {
                    trace("No save data for: " + user.getName());
                }
            } else {
                trace("Database not available for SaveRequest");
            }

        } catch (Exception e) {
            trace("SaveRequest error: " + e.getMessage());
        }
    }
}
