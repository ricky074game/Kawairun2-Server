import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;

public class StopFindPartnerHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        trace("StopFindPartner request from: " + user.getName());

        try {
            KawaiRunExtension parentExt = (KawaiRunExtension) getParentExtension();
            MatchmakingManager matchmaking = parentExt.getMatchmakingManager();
            matchmaking.removeFromQueues(user);
        } catch (Exception e) {
            trace("StopFindPartner error: " + e.getMessage());
        }
    }
}
