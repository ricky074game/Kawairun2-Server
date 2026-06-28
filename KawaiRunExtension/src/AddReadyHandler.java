import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.Room;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.entities.variables.RoomVariable;
import com.smartfoxserver.v2.entities.variables.SFSRoomVariable;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;
import java.util.List;
import java.util.ArrayList;

public class AddReadyHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        Room room = user.getLastJoinedRoom();

        if (room == null) {
            trace("AddReady error: User not in a room");
            return;
        }

        sendUserOrder(room);

        RoomVariable readyVar = room.getVariable("readyCount");
        int readyCount = (readyVar != null) ? readyVar.getIntValue() : 0;
        readyCount++;

        List<RoomVariable> vars = new ArrayList<>();
        vars.add(new SFSRoomVariable("readyCount", readyCount));
        getApi().setRoomVariables(null, room, vars);

        if (readyCount >= room.getUserList().size()) {
            try {
                trace("All players ready in room " + room.getName() + ". Starting match sync.");
                Object extension = room.getExtension();
                if (extension instanceof GameRoomExtension) {
                    GameRoomExtension gameExt = (GameRoomExtension) extension;
                    gameExt.resetGameState();
                    gameExt.startGameSync(); // Start periodic position broadcasts!
                }
            } catch (Exception e) {
                trace("RestartGame error: " + e.getMessage());
            }

            startMatch(room);

            vars.clear();
            vars.add(new SFSRoomVariable("readyCount", 0));
            getApi().setRoomVariables(null, room, vars);
        }
    }

    private void sendUserOrder(Room room) {
        List<User> users = room.getUserList();
        int maxUsers = room.getMaxUsers();

        List<String> userOrder = new ArrayList<>();
        for (int i = 0; i < maxUsers; i++) {
            if (i < users.size()) {
                userOrder.add(users.get(i).getName());
            } else {
                userOrder.add("null");
            }
        }

        ISFSObject response = new SFSObject();
        response.putUtfStringArray("data", userOrder);
        send("UserOrder", response, users);

        trace("Sent UserOrder: " + userOrder);
    }

    private void startMatch(Room room) {
        List<Double> randoms = new ArrayList<>();
        for (int i = 0; i < 100; i++) randoms.add(Math.random());

        trace("Sending AddReady for room " + room.getName() + " to " + room.getUserList().size() + " players");
        ISFSObject response = new SFSObject();
        response.putDoubleArray("randoms", randoms);
        send("AddReady", response, room.getUserList());
    }
}
