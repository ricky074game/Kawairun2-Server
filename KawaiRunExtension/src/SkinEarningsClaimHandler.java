import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;
import java.sql.SQLException;

public class SkinEarningsClaimHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        try {
            Integer userId = SkinStoreSupport.resolveUserId(getParentExtension(), user);
            if (userId == null) return;
            SkinStoreSupport.deliverEarnings(getParentExtension(), user, userId);
        } catch (SQLException e) {
            trace("SkinEarningsClaim failed for " + user.getName() + ": " + e.getMessage());
        }
    }
}
