import com.smartfoxserver.v2.core.SFSEventType;
import com.smartfoxserver.v2.core.SFSEventParam;
import com.smartfoxserver.v2.entities.Room;
import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.entities.variables.UserVariable;
import com.smartfoxserver.v2.extensions.SFSExtension;
import java.util.*;

public class GameRoomExtension extends SFSExtension {
    private final Map<String, Boolean> playerDeadStatus = new HashMap<>();
    private float currentSpeed = 20f;
    private boolean gameEnded = false;
    // Track the last sent order so server knows teams like the client (indices 0-1 vs 2-3)
    private List<String> userOrder = new ArrayList<>();

    @Override
    public void init() {
        trace("GameRoomExtension started for: " + getParentRoom().getName());
        addRequestHandler("AddReady", AddReadyHandler.class);
        addRequestHandler("RestartGame", RestartGameHandler.class);
        addRequestHandler("SaveUpdate", SaveUpdateHandler.class);
        addEventHandler(SFSEventType.USER_VARIABLES_UPDATE, new UserVariableUpdateHandler());
        addEventHandler(SFSEventType.USER_JOIN_ROOM, new UserJoinRoomHandler());
        addEventHandler(SFSEventType.USER_LEAVE_ROOM, new UserLeaveRoomHandler());

        // Initialize players alive status
        Room room = getParentRoom();
        if (room != null) {
            for (User user : room.getUserList()) {
                playerDeadStatus.put(user.getName(), false);
            }
        }
    }

    @Override
    public void destroy() {
        trace("GameRoomExtension stopped for: " + getParentRoom().getName());
        super.destroy();
    }

    public void resetGameState() {
        gameEnded = false;
        playerDeadStatus.clear();

        Room room = getParentRoom();
        if (room != null) {
            for (User user : room.getUserList()) {
                playerDeadStatus.put(user.getName(), false);
            }
        }
        currentSpeed = 20f;
    }

    private void endGame(String result) {
        if (gameEnded) return;
        gameEnded = true;

        ISFSObject response = new SFSObject();
        response.putUtfString("result", result);
        response.putFloat("speed", currentSpeed);
        send("Results", response, getParentRoom().getUserList());
        trace("Game ended: " + result);
    }

    private void sendUserOrder() {
        Room room = getParentRoom();
        if (room == null) return;

        List<User> users = room.getUserList();
        int maxUsers = room.getMaxUsers();

        List<String> order = new ArrayList<>();
        for (int i = 0; i < maxUsers; i++) {
            if (i < users.size()) {
                order.add(users.get(i).getName());
            } else {
                order.add("null");
            }
        }
        // Save for server-side team logic
        this.userOrder = order;

        ISFSObject response = new SFSObject();
        response.putUtfStringArray("data", order);
        send("UserOrder", response, users);

        trace("Sent UserOrder: " + order);
    }

    private class UserVariableUpdateHandler extends com.smartfoxserver.v2.extensions.BaseServerEventHandler {
        @Override
        public void handleServerEvent(com.smartfoxserver.v2.core.ISFSEvent event) {
            List<UserVariable> updatedVars = (List<UserVariable>) event.getParameter(SFSEventParam.VARIABLES);
            User user = (User) event.getParameter(SFSEventParam.USER);
            if (updatedVars == null || user == null) return;

            for (UserVariable uv : updatedVars) {
                String name = uv.getName();
                if ("messageString".equals(name)) {
                    ISFSObject msgObj = uv.getSFSObjectValue();
                    if (msgObj != null) {
                        String message = msgObj.getUtfString("message");
                        if ("killPlayer".equals(message)) {
                            playerDeadStatus.put(user.getName(), true);
                        } else if ("respawn".equals(message)) {
                            playerDeadStatus.put(user.getName(), false);
                        }
                    }
                } else if ("deathStatus".equals(name)) {
                    Object val = uv.getValue();
                    if (val != null) {
                        String deathStatus = val.toString();
                        if ("respawnScreenFail".equals(deathStatus)) {
                            // Make sure the failing user is flagged dead
                            playerDeadStatus.put(user.getName(), true);
                            checkWinCondition(user);
                        }
                    }
                }
            }
        }
    }

    private void checkWinCondition(User failedUser) {
        if (gameEnded) return;

        Room room = getParentRoom();
        List<User> users = room.getUserList();
        int maxUsers = room.getMaxUsers();

        if (maxUsers == 2) {
            // 1v1 logic (unchanged)
            User otherUser = null;
            for (User u : users) {
                if (!u.getName().equals(failedUser.getName())) { otherUser = u; break; }
            }
            if (otherUser != null) {
                Boolean otherDead = playerDeadStatus.get(otherUser.getName());
                if (Boolean.TRUE.equals(otherDead)) {
                    endGame("fail");
                } else {
                    ISFSObject winResponse = new SFSObject();
                    winResponse.putUtfString("result", "win");
                    winResponse.putFloat("speed", currentSpeed);
                    winResponse.putBool("byDisconnect", false);
                    send("Results", winResponse, otherUser);

                    ISFSObject loseResponse = new SFSObject();
                    loseResponse.putUtfString("result", "lose");
                    loseResponse.putFloat("speed", currentSpeed);
                    send("Results", loseResponse, failedUser);
                    gameEnded = true;
                }
            } else {
                endGame("fail");
            }
            return;
        }

        if (maxUsers == 4) {
            // Tag team: indices [0,1] vs [2,3]
            // If we don't have a valid order yet, reconstruct from current users
            if (userOrder == null || userOrder.size() < 4) {
                userOrder = new ArrayList<>();
                for (User u : users) userOrder.add(u.getName());
                while (userOrder.size() < 4) userOrder.add("null");
            }

            String a1 = userOrder.get(0);
            String a2 = userOrder.get(1);
            String b1 = userOrder.get(2);
            String b2 = userOrder.get(3);

            boolean a1Dead = isDead(a1);
            boolean a2Dead = isDead(a2);
            boolean b1Dead = isDead(b1);
            boolean b2Dead = isDead(b2);

            boolean teamAAllDead = a1Dead && a2Dead;
            boolean teamBAllDead = b1Dead && b2Dead;

            if (teamAAllDead && teamBAllDead) {
                endGame("fail");
                return;
            }
            if (teamAAllDead) {
                // Team B wins
                sendTeamResult(Arrays.asList(b1, b2), "win", false);
                sendTeamResult(Arrays.asList(a1, a2), "lose", false);
                gameEnded = true;
                return;
            }
            if (teamBAllDead) {
                // Team A wins
                sendTeamResult(Arrays.asList(a1, a2), "win", false);
                sendTeamResult(Arrays.asList(b1, b2), "lose", false);
                gameEnded = true;
                return;
            }
            // Not finished yet; someone failed but teammate still alive
        } else {
            // Unknown mode, fallback
            endGame("fail");
        }
    }

    private boolean isDead(String name) {
        if (name == null || "null".equals(name)) return true; // empty slot counts as dead
        Boolean d = playerDeadStatus.get(name);
        return d != null && d;
    }

    private void sendTeamResult(List<String> names, String result, boolean byDisconnect) {
        ISFSObject res = new SFSObject();
        res.putUtfString("result", result);
        res.putFloat("speed", currentSpeed);
        res.putBool("byDisconnect", byDisconnect);

        // Map names to current User objects present in the room
        List<User> targets = new ArrayList<>();
        for (String n : names) {
            if (n == null || "null".equals(n)) continue;
            User u = getParentRoom().getUserByName(n);
            if (u != null) targets.add(u);
        }
        if (!targets.isEmpty()) send("Results", res, targets);
    }

    private class UserJoinRoomHandler extends com.smartfoxserver.v2.extensions.BaseServerEventHandler {
        @Override
        public void handleServerEvent(com.smartfoxserver.v2.core.ISFSEvent event) {
            User user = (User) event.getParameter(SFSEventParam.USER);
            trace("User joined game room: " + user.getName());
            // Ensure alive when joining
            playerDeadStatus.put(user.getName(), false);
            sendUserOrder();
        }
    }

    private class UserLeaveRoomHandler extends com.smartfoxserver.v2.extensions.BaseServerEventHandler {
        @Override
        public void handleServerEvent(com.smartfoxserver.v2.core.ISFSEvent event) {
            User user = (User) event.getParameter(SFSEventParam.USER);
            trace("User left game room: " + user.getName());
            // Consider a leaving user as dead for win condition
            playerDeadStatus.put(user.getName(), true);
            sendUserOrder();
            checkWinCondition(user);
        }
    }
}
