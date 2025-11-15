import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.Room;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;
import com.smartfoxserver.v2.api.CreateRoomSettings;
import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;

public class FindPartnerTagTeamHandler extends BaseClientRequestHandler {
    private static final AtomicInteger roomCounter = new AtomicInteger(0);

    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        trace("--- FindPartnerTagTeam request from: " + user.getName() + " ---");

        try {
            // Check if user is already in a tag team game room
            Room currentRoom = user.getLastJoinedRoom();
            if (currentRoom != null && currentRoom.getName().startsWith("TagTeam_") && currentRoom.isGame()) {
                trace("User " + user.getName() + " already in tag team room: " + currentRoom.getName());
                // Send them confirmation again in case they didn't receive it
                notifyJoinedRoom(user, currentRoom.getName());
                return;
            }

            // Look for an existing TagTeam room with space
            Room availableRoom = findAvailableTagTeamRoom();

            if (availableRoom != null) {
                // Join existing room
                trace("Joining " + user.getName() + " to existing room: " + availableRoom.getName() +
                      " (" + availableRoom.getUserList().size() + "/4 players)");
                getApi().joinRoom(user, availableRoom);
                notifyJoinedRoom(user, availableRoom.getName());
            } else {
                // Create new TagTeam lobby room
                String roomName = "TagTeam_" + roomCounter.incrementAndGet();
                Room newRoom = createTagTeamLobbyRoom(roomName);

                if (newRoom != null) {
                    trace("Created new tag team room: " + roomName + " for " + user.getName());
                    getApi().joinRoom(user, newRoom);
                    notifyJoinedRoom(user, roomName);
                } else {
                    trace("Failed to create tag team room for: " + user.getName());
                }
            }
        } catch (Exception e) {
            trace("FindPartnerTagTeam error: " + e.getMessage());
            e.printStackTrace();
        }
    }

    private Room findAvailableTagTeamRoom() {
        List<Room> rooms = getParentExtension().getParentZone().getRoomListFromGroup("default");

        for (Room room : rooms) {
            if (room.getName().startsWith("TagTeam_") &&
                room.isGame() &&
                !room.isFull() &&
                room.getUserList().size() < 4) {

                // Check if game hasn't started yet (no readyCount or readyCount is 0)
                try {
                    Object readyVar = room.getVariable("readyCount");
                    if (readyVar == null) {
                        return room; // Room exists but game hasn't started
                    }
                } catch (Exception e) {
                    // No ready variable, room is available
                    return room;
                }
            }
        }
        return null;
    }

    private Room createTagTeamLobbyRoom(String roomName) {
        try {
            CreateRoomSettings settings = new CreateRoomSettings();
            settings.setName(roomName);
            settings.setMaxUsers(4);
            settings.setGroupId("default");
            settings.setGame(true);
            settings.setDynamic(true); // Allow dynamic join/leave

            CreateRoomSettings.RoomExtensionSettings extSettings =
                new CreateRoomSettings.RoomExtensionSettings(
                    getParentExtension().getName(),
                    "GameRoomExtension"
                );
            settings.setExtension(extSettings);

            Room room = getApi().createRoom(
                getParentExtension().getParentZone(),
                settings,
                null
            );

            if (room != null) {
                trace("Tag team lobby room created: " + roomName);
            }
            return room;
        } catch (Exception e) {
            trace("Error creating tag team lobby room: " + e.getMessage());
            return null;
        }
    }

    private void notifyJoinedRoom(User user, String roomName) {
        ISFSObject response = new SFSObject();
        response.putUtfString("roomName", roomName);
        response.putBool("isTagTeam", true);
        send("FindPartner", response, user);
        trace("Notified " + user.getName() + " to join room: " + roomName);
    }
}
