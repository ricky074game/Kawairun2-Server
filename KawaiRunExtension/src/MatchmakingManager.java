import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.Room;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.SFSExtension;
import com.smartfoxserver.v2.api.CreateRoomSettings;
import java.util.concurrent.ConcurrentLinkedQueue;
import java.util.concurrent.atomic.AtomicInteger;

public class MatchmakingManager {
    private final SFSExtension extension;
    private final ConcurrentLinkedQueue<User> versusQueue = new ConcurrentLinkedQueue<>();
    private final AtomicInteger roomCounter = new AtomicInteger(0);

    public MatchmakingManager(SFSExtension extension) {
        this.extension = extension;
    }

    public void addToVersusQueue(User user) {
        if (versusQueue.contains(user)) {
            extension.trace(user.getName() + " already in versus queue");
            return;
        }
        versusQueue.add(user);
        extension.trace(user.getName() + " added to versus queue (" + versusQueue.size() + ")");
        tryMatchVersus();
    }

    public void removeFromQueues(User user) {
        boolean removed = versusQueue.remove(user);
        if (removed) extension.trace(user.getName() + " removed from versus queue");
    }

    private void tryMatchVersus() {
        if (versusQueue.size() >= 2) {
            User player1 = versusQueue.poll();
            User player2 = versusQueue.poll();

            if (player1 != null && player2 != null) {
                if (!player1.isConnected() || !player2.isConnected()) {
                    extension.trace("Player disconnected during match creation, requeueing...");
                    if (player1.isConnected()) versusQueue.add(player1);
                    if (player2.isConnected()) versusQueue.add(player2);
                    return;
                }

                String roomName = "Versus_" + roomCounter.incrementAndGet();
                Room room = createGameRoom(roomName, 2, false);

                if (room != null) {
                    notifyMatch(player1, roomName, false);
                    notifyMatch(player2, roomName, false);
                    extension.trace("Match created: " + roomName + " for " + player1.getName() + " vs " + player2.getName());
                } else {
                    extension.trace("Failed to create versus room, requeueing players");
                    versusQueue.add(player1);
                    versusQueue.add(player2);
                }
            }
        }
    }

    private Room createGameRoom(String roomName, int maxUsers, boolean isTagTeam) {
        try {
            CreateRoomSettings settings = new CreateRoomSettings();
            settings.setName(roomName);
            settings.setMaxUsers(maxUsers);
            settings.setGroupId("default");
            settings.setGame(true);
            settings.setDynamic(false);

            CreateRoomSettings.RoomExtensionSettings extSettings = new CreateRoomSettings.RoomExtensionSettings(
                    extension.getName(),
                    "GameRoomExtension"
            );
            settings.setExtension(extSettings);

            Room room = extension.getApi().createRoom(extension.getParentZone(), settings, null);
            if (room != null) extension.trace("Game room created: " + roomName);
            return room;
        } catch (Exception e) {
            extension.trace("Error creating game room: " + e.getMessage());
            return null;
        }
    }

    private void notifyMatch(User user, String roomName, boolean isTagTeam) {
        ISFSObject response = new SFSObject();
        response.putUtfString("roomName", roomName);
        response.putBool("isTagTeam", isTagTeam);
        extension.send("FindPartner", response, user);
    }

    public int getVersusQueueSize() { return versusQueue.size(); }
}
