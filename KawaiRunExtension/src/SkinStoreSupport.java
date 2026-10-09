import com.smartfoxserver.v2.entities.User;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.entities.data.SFSObject;
import com.smartfoxserver.v2.extensions.SFSExtension;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.util.ArrayList;
import java.util.Locale;
import java.util.concurrent.ConcurrentHashMap;

public final class SkinStoreSupport {
    private static final int BUY_LIMIT_PER_WINDOW = 20;
    private static final long BUY_WINDOW_MS = 60_000L;
    private static final ConcurrentHashMap<String, long[]> BUY_RATE = new ConcurrentHashMap<>();

    private SkinStoreSupport() {}

    public static Integer resolveUserId(SFSExtension ext, User user) throws SQLException {
        String name = user.getName();
        if (SecurityUtils.isGuestUser(name) || !SecurityUtils.isValidUsername(name)) return null;
        try (Connection c = open(ext);
             PreparedStatement st = c.prepareStatement("SELECT user_id FROM users WHERE username = ? AND is_active = TRUE")) {
            st.setString(1, name);
            try (ResultSet rs = st.executeQuery()) {
                return rs.next() ? Integer.valueOf(rs.getInt("user_id")) : null;
            }
        }
    }

    public static boolean isValidKind(String kind) {
        return "skin".equals(kind) || "hat".equals(kind) || "trail".equals(kind);
    }

    public static String normalizeKind(String kind) {
        return isValidKind(kind) ? kind : "skin";
    }

    public static final String[] PARTS = {"head", "body", "arm", "leg"};

    public static String partColumn(String part) {
        if ("head".equals(part)) return "head_item_id";
        if ("body".equals(part)) return "body_item_id";
        if ("arm".equals(part)) return "arm_item_id";
        return "leg_item_id";
    }

    public static String clearPartsSql() {
        StringBuilder sb = new StringBuilder();
        for (String p : PARTS) sb.append(", ").append(partColumn(p)).append(" = NULL");
        return sb.toString();
    }

    public static String slotColumn(String kind) {
        if ("hat".equals(kind)) return "hat_item_id";
        if ("trail".equals(kind)) return "trail_item_id";
        return "item_id";
    }

    public static Connection open(SFSExtension ext) throws SQLException {
        DatabaseManager db = ((KawaiRunExtension) ext).getDbManager();
        if (db == null) throw new SQLException("no database manager");
        return db.openDedicatedConnection();
    }

    public static boolean allowBuy(String username) {
        String key = username.toLowerCase(Locale.ROOT);
        long now = System.currentTimeMillis();
        boolean[] ok = new boolean[1];
        BUY_RATE.compute(key, (k, v) -> {
            if (v == null || now - v[1] >= BUY_WINDOW_MS) {
                ok[0] = true;
                return new long[] { 1, now };
            }
            if (v[0] >= BUY_LIMIT_PER_WINDOW) {
                ok[0] = false;
                return v;
            }
            v[0]++;
            ok[0] = true;
            return v;
        });
        if (BUY_RATE.size() > 5000) {
            BUY_RATE.entrySet().removeIf(e -> now - e.getValue()[1] >= BUY_WINDOW_MS);
        }
        return ok[0];
    }

    public static long[] claimEarnings(SFSExtension ext, int creatorId) throws SQLException {
        try (Connection c = open(ext)) {
            c.setAutoCommit(false);
            try {
                long coins = 0;
                int count = 0;
                ArrayList<Integer> ids = new ArrayList<>();
                try (PreparedStatement st = c.prepareStatement(
                        "SELECT earning_id, amount FROM skin_earnings WHERE creator_id = ? AND delivered = 0 FOR UPDATE")) {
                    st.setInt(1, creatorId);
                    try (ResultSet rs = st.executeQuery()) {
                        while (rs.next()) {
                            ids.add(rs.getInt("earning_id"));
                            coins += rs.getInt("amount");
                            count++;
                        }
                    }
                }
                if (count > 0) {
                    try (PreparedStatement st = c.prepareStatement(
                            "UPDATE skin_earnings SET delivered = 1 WHERE earning_id = ? AND delivered = 0")) {
                        for (int id : ids) {
                            st.setInt(1, id);
                            st.addBatch();
                        }
                        st.executeBatch();
                    }
                }
                c.commit();
                return new long[] { coins, count };
            } catch (SQLException | RuntimeException e) {
                try { c.rollback(); } catch (SQLException ignored) {}
                throw e;
            }
        }
    }

    public static void deliverEarnings(SFSExtension ext, User user, int userId) throws SQLException {
        long[] r = claimEarnings(ext, userId);
        if (r[0] > 0) {
            ISFSObject out = new SFSObject();
            out.putInt("coins", (int) Math.min(r[0], Integer.MAX_VALUE));
            out.putInt("count", (int) r[1]);
            ext.send("SkinEarnings", out, user);
        }
    }
}
