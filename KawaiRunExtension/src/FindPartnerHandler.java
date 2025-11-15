import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;

public class FindPartnerHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        trace("FindPartner request from: " + user.getName());

        try {
            KawaiRunExtension parentExt = (KawaiRunExtension) getParentExtension();
            MatchmakingManager matchmaking = parentExt.getMatchmakingManager();
            matchmaking.addToVersusQueue(user);
        } catch (Exception e) {
            trace("FindPartner error: " + e.getMessage());
        }
    }
}
