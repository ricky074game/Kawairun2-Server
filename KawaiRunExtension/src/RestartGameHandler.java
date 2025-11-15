import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.Room;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.entities.variables.RoomVariable;
import com.smartfoxserver.v2.entities.variables.SFSRoomVariable;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;
import java.util.List;
import java.util.ArrayList;

public class RestartGameHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        Room room = user.getLastJoinedRoom();

        if (room == null) {
            trace("RestartGame error: User not in a room");
            return;
        }

        trace("RestartGame request from: " + user.getName() + " in room: " + room.getName());

        RoomVariable restartVar = room.getVariable("restartCount");
        int restartCount = (restartVar != null) ? restartVar.getIntValue() : 0;
        restartCount++;

        List<RoomVariable> vars = new ArrayList<>();
        vars.add(new SFSRoomVariable("restartCount", restartCount));
        getApi().setRoomVariables(null, room, vars);

        if (restartCount >= room.getUserList().size()) {
            restartMatch(room);
            vars.clear();
            vars.add(new SFSRoomVariable("restartCount", 0));
            getApi().setRoomVariables(null, room, vars);
        }
    }

    private void restartMatch(Room room) {
        try {
            Object extension = room.getExtension();
            if (extension instanceof GameRoomExtension) {
                GameRoomExtension gameExt = (GameRoomExtension) extension;
                gameExt.resetGameState();
            } else {
                trace("RestartGame: room extension not GameRoomExtension");
            }
        } catch (Exception e) {
            trace("RestartMatch error: " + e.getMessage());
        }

        ISFSObject response = new SFSObject();
        response.putUtfString("roomName", room.getName());

        send("RestartRoom", response, room.getUserList());
    }
}
