import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.BaseClientRequestHandler;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;

public class SkinStoreBuyHandler extends BaseClientRequestHandler {
    @Override
    public void handleClientRequest(User user, ISFSObject params) {
        int id = params.containsKey("id") ? params.getInt("id") : 0;
        int clientPrice = params.containsKey("price") ? params.getInt("price") : -1;
        if (id <= 0) { respond(user, false, id, 0, "not_found"); return; }

        Integer buyerId;
        try {
            buyerId = SkinStoreSupport.resolveUserId(getParentExtension(), user);
        } catch (SQLException e) {
            trace("SkinStoreBuy user lookup failed: " + e.getMessage());
            respond(user, false, id, 0, "server");
            return;
        }
        if (buyerId == null) { respond(user, false, id, 0, "guest"); return; }

        if (!SkinStoreSupport.allowBuy(user.getName())) {
            trace("SkinStoreBuy rate limited: " + user.getName());
            respond(user, false, id, 0, "server");
            return;
        }

        String error = null;
        int price = 0;
        int creatorId = 0;
        String kind = "skin";
        try (Connection c = SkinStoreSupport.open(getParentExtension())) {
            c.setAutoCommit(false);
            try {
                boolean found = false;
                boolean buyable = false;
                try (PreparedStatement st = c.prepareStatement(
                        "SELECT si.creator_id, si.price, si.kind, si.listed, si.removed, si.approval, u.is_active AS creator_active " +
                        "FROM skin_items si JOIN users u ON u.user_id = si.creator_id WHERE si.item_id = ? FOR UPDATE")) {
                    st.setInt(1, id);
                    try (ResultSet rs = st.executeQuery()) {
                        if (rs.next()) {
                            found = true;
                            creatorId = rs.getInt("creator_id");
                            price = rs.getInt("price");
                            kind = SkinStoreSupport.normalizeKind(rs.getString("kind"));
                            buyable = rs.getInt("listed") == 1 && rs.getInt("removed") == 0 && rs.getInt("approval") == 1 && rs.getBoolean("creator_active");
                        }
                    }
                }

                if (found && creatorId == buyerId.intValue()) {
                    error = "own_item";
                } else if (!found || !buyable) {
                    error = "not_found";
                } else {
                    try (PreparedStatement st = c.prepareStatement(
                            "SELECT 1 FROM skin_owned WHERE user_id = ? AND item_id = ?")) {
                        st.setInt(1, buyerId);
                        st.setInt(2, id);
                        try (ResultSet rs = st.executeQuery()) {
                            if (rs.next()) error = "owned";
                        }
                    }
                    if (error == null && clientPrice != price) error = "price_changed";
                }

                if (error != null) {
                    c.rollback();
                } else {
                    try (PreparedStatement st = c.prepareStatement(
                            "INSERT INTO skin_owned (user_id, item_id, price_paid) VALUES (?, ?, ?)")) {
                        st.setInt(1, buyerId);
                        st.setInt(2, id);
                        st.setInt(3, price);
                        st.executeUpdate();
                    }
                    try (PreparedStatement st = c.prepareStatement(
                            "UPDATE skin_items SET sales = sales + 1 WHERE item_id = ?")) {
                        st.setInt(1, id);
                        st.executeUpdate();
                    }
                    if (price > 0) {
                        try (PreparedStatement st = c.prepareStatement(
                                "INSERT INTO skin_earnings (creator_id, buyer_id, item_id, amount) VALUES (?, ?, ?, ?)")) {
                            st.setInt(1, creatorId);
                            st.setInt(2, buyerId);
                            st.setInt(3, id);
                            st.setInt(4, price * 70 / 100);
                            st.executeUpdate();
                        }
                    }
                    String col = SkinStoreSupport.slotColumn(kind);
                    try (PreparedStatement st = c.prepareStatement(
                            "INSERT INTO skin_equipped (user_id, " + col + ") VALUES (?, ?) ON DUPLICATE KEY UPDATE " + col + " = VALUES(" + col + ")")) {
                        st.setInt(1, buyerId);
                        st.setInt(2, id);
                        st.executeUpdate();
                    }
                    c.commit();
                }
            } catch (SQLException | RuntimeException e) {
                try { c.rollback(); } catch (SQLException ignored) {}
                throw e;
            }
        } catch (SQLException e) {
            if (e.getErrorCode() == 1062) {
                error = "owned";
            } else {
                trace("SkinStoreBuy failed for " + user.getName() + " item " + id + ": " + e.getMessage());
                error = "server";
            }
        }

        if (error != null) {
            respond(user, false, id, 0, error);
            return;
        }

        respond(user, true, id, price, "");

        if (price > 0) {
            deliverToCreatorIfOnline(creatorId);
        }
    }

    private void deliverToCreatorIfOnline(int creatorId) {
        try {
            String creatorName = null;
            try (Connection c = SkinStoreSupport.open(getParentExtension());
                 PreparedStatement st = c.prepareStatement("SELECT username FROM users WHERE user_id = ?")) {
                st.setInt(1, creatorId);
                try (ResultSet rs = st.executeQuery()) {
                    if (rs.next()) creatorName = rs.getString("username");
                }
            }
            if (creatorName == null) return;
            User creator = getApi().getUserByName(creatorName);
            if (creator == null) return;
            SkinStoreSupport.deliverEarnings(getParentExtension(), creator, creatorId);
        } catch (SQLException e) {
            trace("SkinStoreBuy earnings delivery failed (creator " + creatorId + "): " + e.getMessage());
        }
    }

    private void respond(User user, boolean ok, int id, int price, String error) {
        ISFSObject out = new SFSObject();
        out.putBool("ok", ok);
        out.putInt("id", id);
        out.putInt("price", price);
        out.putUtfString("error", error);
        send("SkinStoreBuyResult", out, user);
    }
}
