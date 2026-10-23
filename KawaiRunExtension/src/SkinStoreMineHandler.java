import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSArray;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSArray;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;

public class SkinStoreMineHandler extends BaseClientRequestHandler {
    private static final int MAX_ITEMS = 500;

    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        try {
            Integer userId = SkinStoreSupport.resolveUserId(getParentExtension(), user);
            if (userId == null) { respond(user, false, 0, 0, 0, new SFSArray(), "guest"); return; }

            int equippedId = 0;
            int equippedHatId = 0;
            int equippedTrailId = 0;
            int[] parts = new int[SkinStoreSupport.PARTS.length];
            ISFSArray items = new SFSArray();
            try (Connection c = SkinStoreSupport.open(getParentExtension())) {
                try (PreparedStatement st = c.prepareStatement("SELECT * FROM skin_equipped WHERE user_id = ?")) {
                    st.setInt(1, userId);
                    try (ResultSet rs = st.executeQuery()) {
                        if (rs.next()) {
                            equippedId = rs.getInt("item_id");
                            equippedHatId = rs.getInt("hat_item_id");
                            equippedTrailId = rs.getInt("trail_item_id");
                            for (int i = 0; i < parts.length; i++) {
                                try { parts[i] = rs.getInt(SkinStoreSupport.partColumn(SkinStoreSupport.PARTS[i])); }
                                catch (SQLException noColumn) { parts[i] = 0; }
                            }
                        }
                    }
                }
                try (PreparedStatement st = c.prepareStatement(
                        "SELECT si.item_id, si.name, si.creator_name, si.price, si.kind FROM skin_owned so " +
                        "JOIN skin_items si ON si.item_id = so.item_id WHERE so.user_id = ? " +
                        "ORDER BY so.sort_pos ASC, so.acquired_at DESC, si.item_id DESC LIMIT " + MAX_ITEMS)) {
                    st.setInt(1, userId);
                    try (ResultSet rs = st.executeQuery()) {
                        while (rs.next()) {
                            ISFSObject item = new SFSObject();
                            item.putInt("id", rs.getInt("item_id"));
                            item.putUtfString("name", rs.getString("name"));
                            item.putUtfString("creator", rs.getString("creator_name"));
                            item.putInt("price", rs.getInt("price"));
                            item.putUtfString("kind", SkinStoreSupport.normalizeKind(rs.getString("kind")));
                            items.addSFSObject(item);
                        }
                    }
                }
            }
            respond(user, true, equippedId, equippedHatId, equippedTrailId, items, "");
            sendParts(user, parts);
        } catch (SQLException e) {
            trace("SkinStoreMine failed for " + user.getName() + ": " + e.getMessage());
            respond(user, false, 0, 0, 0, new SFSArray(), "server");
        }
    }

    private void sendParts(User user, int[] parts) {
        ISFSObject out = new SFSObject();
        out.putBool("ok", true);
        for (int i = 0; i < parts.length; i++) out.putInt(SkinStoreSupport.PARTS[i], parts[i]);
        out.putUtfString("error", "");
        send("SkinStoreEquipPartsResult", out, user);
    }

    private void respond(User user, boolean ok, int equippedId, int equippedHatId, int equippedTrailId, ISFSArray items, String error) {
        ISFSObject out = new SFSObject();
        out.putBool("ok", ok);
        out.putInt("equippedId", equippedId);
        out.putInt("equippedHatId", equippedHatId);
        out.putInt("equippedTrailId", equippedTrailId);
        out.putSFSArray("items", items);
        out.putUtfString("error", error);
        send("SkinStoreMineResult", out, user);
    }
}
