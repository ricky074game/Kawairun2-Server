import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.Room;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.entities.variables.RoomVariable;
import com.smartfoxserver.v2.entities.variables.SFSRoomVariable;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;
import java.util.List;
import java.util.ArrayList;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;

public class AddReadyHandler extends BaseClientRequestHandler {
    private final ScheduledExecutorService scheduler = Executors.newScheduledThreadPool(1);

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
                Object extension = room.getExtension();
                if (extension instanceof GameRoomExtension) {
                    GameRoomExtension gameExt = (GameRoomExtension) extension;
                    gameExt.resetGameState();
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

        ISFSObject response = new SFSObject();
        response.putDoubleArray("randoms", randoms);
        send("AddReady", response, room.getUserList());

        scheduleInitialMovement(room);
    }

    private void scheduleInitialMovement(final Room room) {
        scheduler.schedule(() -> {
            ISFSObject bgResponse = new SFSObject();
            bgResponse.putFloat("x", -100.0f);
            bgResponse.putFloat("speed", 13.0f);

            send("SetNewBackgroundX", bgResponse, room.getUserList());
        }, 3, TimeUnit.SECONDS);
    }
}
