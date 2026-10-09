import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;

public class SkinStoreEquipHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        int id = params.containsKey("id") ? params.getInt("id") : 0;
        if (id < 0) { respond(user, false, id, "skin", "not_found"); return; }
        String reqKind = params.containsKey("kind") ? params.getUtfString("kind") : "skin";
        if (reqKind == null || reqKind.isEmpty()) reqKind = "skin";
        if (!SkinStoreSupport.isValidKind(reqKind)) { respond(user, false, id, "skin", "bad_kind"); return; }

        try {
            Integer userId = SkinStoreSupport.resolveUserId(getParentExtension(), user);
            if (userId == null) { respond(user, false, id, reqKind, "guest"); return; }

            try (Connection c = SkinStoreSupport.open(getParentExtension())) {
                if (id == 0) {
                    String col = SkinStoreSupport.slotColumn(reqKind);
                    String clear = "skin".equals(reqKind) ? SkinStoreSupport.clearPartsSql() : "";
                    try (PreparedStatement st = c.prepareStatement(
                            "INSERT INTO skin_equipped (user_id, " + col + ") VALUES (?, NULL) ON DUPLICATE KEY UPDATE " + col + " = NULL" + clear)) {
                        st.setInt(1, userId);
                        st.executeUpdate();
                    }
                    respond(user, true, 0, reqKind, "");
                    return;
                }

                String kind = null;
                try (PreparedStatement st = c.prepareStatement(
                        "SELECT si.kind FROM skin_owned so JOIN skin_items si ON si.item_id = so.item_id " +
                        "WHERE so.user_id = ? AND so.item_id = ?")) {
                    st.setInt(1, userId);
                    st.setInt(2, id);
                    try (ResultSet rs = st.executeQuery()) {
                        if (rs.next()) kind = SkinStoreSupport.normalizeKind(rs.getString("kind"));
                    }
                }
                if (kind == null) { respond(user, false, id, reqKind, "not_owned"); return; }

                String col = SkinStoreSupport.slotColumn(kind);
                String clear = "skin".equals(kind) ? SkinStoreSupport.clearPartsSql() : "";
                try (PreparedStatement st = c.prepareStatement(
                        "INSERT INTO skin_equipped (user_id, " + col + ") VALUES (?, ?) ON DUPLICATE KEY UPDATE " + col + " = VALUES(" + col + ")" + clear)) {
                    st.setInt(1, userId);
                    st.setInt(2, id);
                    st.executeUpdate();
                }
                respond(user, true, id, kind, "");
            }
        } catch (SQLException e) {
            trace("SkinStoreEquip failed for " + user.getName() + ": " + e.getMessage());
            respond(user, false, id, reqKind, "server");
        }
    }

    private void respond(User user, boolean ok, int id, String kind, String error) {
        ISFSObject out = new SFSObject();
        out.putBool("ok", ok);
        out.putInt("id", id);
        out.putUtfString("kind", kind);
        out.putUtfString("error", error);
        send("SkinStoreEquipResult", out, user);
    }
}
