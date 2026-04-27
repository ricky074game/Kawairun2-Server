import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;

public class SaveUpdateHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        trace("SaveUpdate from: " + user.getName());

        try {
            if (!SecurityUtils.isValidUsername(user.getName())) {
                trace("Blocked SaveUpdate for guest or invalid user: " + user.getName());
                return;
            }

            byte[] saveData = null;
            if (params.containsKey("save") && params.getByteArray("save") != null) {
                saveData = params.getByteArray("save");
            }

            long wins = params.getLong("Wins");
            long lost = params.getLong("Lost");
            long totalDistance = params.getLong("TotalDistance");
            long distance = params.getLong("Distance");
            long mtxItems = params.getLong("mtxitems");

            if (!SecurityUtils.isValidSaveData(saveData)) {
                trace("Blocked SaveUpdate with invalid save payload size for: " + user.getName());
                return;
            }

            if (!SecurityUtils.isValidCounterStat(wins) ||
                !SecurityUtils.isValidCounterStat(lost) ||
                !SecurityUtils.isValidCounterStat(mtxItems) ||
                !SecurityUtils.isValidDistanceStat(totalDistance) ||
                !SecurityUtils.isValidDistanceStat(distance)) {
                trace("Blocked SaveUpdate with invalid stat values for: " + user.getName());
                return;
            }

            KawaiRunExtension parentExt = (KawaiRunExtension) getParentExtension();
            DatabaseManager dbManager = parentExt.getDbManager();

            if (dbManager != null && dbManager.isConnected() && saveData != null) {
                boolean updated = dbManager.updateSaveData(user.getName(), saveData, wins, lost, distance, totalDistance, mtxItems);
                if (updated) trace("Save data updated for: " + user.getName());
                else trace("Save update failed for: " + user.getName());
            } else {
                trace("Save not persisted: database unavailable or no save data");
            }

        } catch (Exception e) {
            trace("SaveUpdate error: " + e.getMessage());
        }
    }
}
