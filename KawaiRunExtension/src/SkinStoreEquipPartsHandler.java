import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;

public class SkinStoreEquipPartsHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        int[] ids = new int[SkinStoreSupport.PARTS.length];
        for (int i = 0; i < ids.length; i++) {
            String p = SkinStoreSupport.PARTS[i];
            ids[i] = params.containsKey(p) ? params.getInt(p) : 0;
            if (ids[i] < 0) { respond(user, false, ids, "not_found"); return; }
        }

        try {
            Integer userId = SkinStoreSupport.resolveUserId(getParentExtension(), user);
            if (userId == null) { respond(user, false, ids, "guest"); return; }

            try (Connection c = SkinStoreSupport.open(getParentExtension())) {
                for (int id : ids) {
                    if (id == 0) continue;
                    try (PreparedStatement st = c.prepareStatement(
                            "SELECT si.kind FROM skin_owned so JOIN skin_items si ON si.item_id = so.item_id " +
                            "WHERE so.user_id = ? AND so.item_id = ?")) {
                        st.setInt(1, userId);
                        st.setInt(2, id);
                        try (ResultSet rs = st.executeQuery()) {
                            if (!rs.next()) { respond(user, false, ids, "not_owned"); return; }
                            if (!"skin".equals(SkinStoreSupport.normalizeKind(rs.getString("kind")))) {
                                respond(user, false, ids, "bad_kind");
                                return;
                            }
                        }
                    }
                }

                StringBuilder cols = new StringBuilder("user_id, item_id");
                StringBuilder vals = new StringBuilder("?, NULL");
                StringBuilder upd = new StringBuilder("item_id = NULL");
                for (String p : SkinStoreSupport.PARTS) {
                    String col = SkinStoreSupport.partColumn(p);
                    cols.append(", ").append(col);
                    vals.append(", ?");
                    upd.append(", ").append(col).append(" = VALUES(").append(col).append(")");
                }
                try (PreparedStatement st = c.prepareStatement(
                        "INSERT INTO skin_equipped (" + cols + ") VALUES (" + vals + ") ON DUPLICATE KEY UPDATE " + upd)) {
                    st.setInt(1, userId);
                    for (int i = 0; i < ids.length; i++) {
                        if (ids[i] == 0) st.setNull(2 + i, java.sql.Types.INTEGER);
                        else st.setInt(2 + i, ids[i]);
                    }
                    st.executeUpdate();
                }
                respond(user, true, ids, "");
            }
        } catch (SQLException e) {
            trace("SkinStoreEquipParts failed for " + user.getName() + ": " + e.getMessage());
            respond(user, false, ids, "server");
        }
    }

    private void respond(User user, boolean ok, int[] ids, String error) {
        ISFSObject out = new SFSObject();
        out.putBool("ok", ok);
        for (int i = 0; i < ids.length; i++) out.putInt(SkinStoreSupport.PARTS[i], ids[i]);
        out.putUtfString("error", error);
        send("SkinStoreEquipPartsResult", out, user);
    }
}
