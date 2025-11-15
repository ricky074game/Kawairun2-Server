import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.Room;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;
import java.util.List;

public class AskStatsHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        try {
            int totalUsers = getParentExtension().getParentZone().getUserList().size();
            int versusCount = 0;
            int tagTeamCount = 0;

            List<Room> rooms = getParentExtension().getParentZone().getRoomList();
            for (Room room : rooms) {
                if (room.isGame()) {
                    String roomName = room.getName().toLowerCase();
                    int playerCount = room.getUserList().size();

                    if (roomName.contains("versus") || roomName.contains("duel")) {
                        versusCount += playerCount;
                    } else if (roomName.contains("tag") || roomName.contains("team")) {
                        tagTeamCount += playerCount;
                    }
                }
            }

            ISFSObject response = new SFSObject();
            response.putInt("users", totalUsers);
            response.putInt("versus", versusCount);
            response.putInt("tagteam", tagTeamCount);
            send("Stats", response, user);
        } catch (Exception e) {
            ISFSObject response = new SFSObject();
            response.putInt("users", 0);
            response.putInt("versus", 0);
            response.putInt("tagteam", 0);
            send("Stats", response, user);
        }
    }
}
