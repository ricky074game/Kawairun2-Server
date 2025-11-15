import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.Room;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;
import com.smartfoxserver.v2.api.CreateRoomSettings;
import java.util.concurrent.atomic.AtomicInteger;

public class CreatePrivateRoomHandler extends BaseClientRequestHandler {
    private static final AtomicInteger privateRoomCounter = new AtomicInteger(0);

    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        trace("CreatePrivateRoom request from: " + user.getName());

        try {
            String roomName = "Private_" + user.getName() + "_" + privateRoomCounter.incrementAndGet();

            CreateRoomSettings settings = new CreateRoomSettings();
            settings.setName(roomName);
            settings.setMaxUsers(2);
            settings.setGroupId("default");
            settings.setGame(true);
            settings.setDynamic(false);

            CreateRoomSettings.RoomExtensionSettings extSettings = new CreateRoomSettings.RoomExtensionSettings(
                    getParentExtension().getName(),
                    "GameRoomExtension"
            );
            settings.setExtension(extSettings);

            Room room = getApi().createRoom(getParentExtension().getParentZone(), settings, user);

            if (room != null) {
                ISFSObject response = new SFSObject();
                response.putUtfString("roomName", roomName);
                send("CreatePrivateRoom", response, user);
            } else {
                trace("Failed to create private room for: " + user.getName());
            }
        } catch (Exception e) {
            trace("CreatePrivateRoom error: " + e.getMessage());
        }
    }
}
