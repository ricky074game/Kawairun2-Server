import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;

/**
 * Handler for the "DONTKICKMEOUT" command.
 * This is sent by the client to keep the connection alive and prevent idle timeouts.
 */
public class DontKickMeOutHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        // keepalive ping - no action required
    }
}
