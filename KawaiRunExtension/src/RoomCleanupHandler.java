import com.smartfoxserver.v2.core.ISFSEvent;
import com.smartfoxserver.v2.core.SFSEventParam;
import com.smartfoxserver.v2.entities.Room;
import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.exceptions.SFSException;
import com.smartfoxserver.v2.extensions.BaseServerEventHandler;

public class RoomCleanupHandler extends BaseServerEventHandler {
    @Override
    public void handleServerEvent(ISFSEvent event) throws SFSException {
        Room room = (Room) event.getParameter(SFSEventParam.ROOM);
        User user = (User) event.getParameter(SFSEventParam.USER);

        if (room != null && room.isGame()) {
            String roomName = room.getName();

            if (roomName.startsWith("Versus_") || roomName.startsWith("TagTeam_")) {
                trace("User " + user.getName() + " left room: " + roomName + ", users remaining: " + room.getUserList().size());

                if (room.isEmpty()) {
                    try {
                        room.setExtension(null);
                        getApi().removeRoom(room);
                        trace("Room " + roomName + " removed");
                    } catch (Exception e) {
                        trace("RoomCleanup error: " + e.getMessage());
                    }
                }
            }
        }
    }
}
