import com.smartfoxserver.v2.extensions.SFSExtension;
import java.sql.*;

public class DatabaseManager {
    private static final int CONNECTION_VALIDATION_TIMEOUT_SECONDS = 2;

    private final SFSExtension extension;
    private Connection connection;

    private final String dbDriver;
    private final String dbUrl;
    private final String dbUser;
    private final String dbPassword;

    public DatabaseManager(SFSExtension extension) {
        this.extension = extension;
        this.dbDriver = readConfig("kawairun.db.driver", "KAWAIRUN_DB_DRIVER", "com.mysql.cj.jdbc.Driver");
        this.dbUrl = readConfig("kawairun.db.url", "KAWAIRUN_DB_URL", null);
        this.dbUser = readConfig("kawairun.db.user", "KAWAIRUN_DB_USER", null);
        this.dbPassword = readConfig("kawairun.db.password", "KAWAIRUN_DB_PASSWORD", null);
    }

    public boolean connect() {
        if (isBlank(dbUrl) || isBlank(dbUser) || isBlank(dbPassword)) {
            extension.trace("Database configuration missing. Set KAWAIRUN_DB_URL, KAWAIRUN_DB_USER, and KAWAIRUN_DB_PASSWORD.");
            return false;
        }

        try {
            closeSilently();
            Class.forName(dbDriver);
            String connectionUrl = buildConnectionUrl(dbUrl);
            connection = DriverManager.getConnection(connectionUrl, dbUser, dbPassword);
            extension.trace("Database connected");
            return true;
        } catch (ClassNotFoundException e) {
            extension.trace("MySQL JDBC Driver not found: " + e.getMessage());
            return false;
        } catch (SQLException e) {
            extension.trace("Database connection failed: " + e.getMessage());
            return false;
        }
    }

    public void disconnect() {
        closeSilently();
    }

    public synchronized boolean ensureConnected() {
        if (hasUsableConnection()) {
            return true;
        }

        extension.trace("Database connection unavailable - attempting reconnect");
        return connect();
    }

    private synchronized boolean hasUsableConnection() {
        try {
            if (connection == null || connection.isClosed()) {
                return false;
            }

            return connection.isValid(CONNECTION_VALIDATION_TIMEOUT_SECONDS);
        } catch (SQLException e) {
            return false;
        }
    }

    public boolean userExists(String username) {
        if (!ensureConnected()) {
            return false;
        }

        String query = "SELECT user_id FROM users WHERE username = ?";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setString(1, username);
            ResultSet rs = stmt.executeQuery();
            return rs.next();
        } catch (SQLException e) {
            handleConnectionException("checking user existence", e);
            extension.trace("Error checking user existence: " + e.getMessage());
            return false;
        }
    }

    public boolean createAccount(String username, String passwordHash, byte[] saveData) {
        if (!ensureConnected()) {
            return false;
        }

        String insertUser = "INSERT INTO users (username, password_hash) VALUES (?, ?)";
        String insertSave = "INSERT INTO player_saves (user_id, save_data) VALUES (?, ?)";

        try {
            connection.setAutoCommit(false);
            int userId;
            try (PreparedStatement stmt = connection.prepareStatement(insertUser, Statement.RETURN_GENERATED_KEYS)) {
                stmt.setString(1, username);
                stmt.setString(2, passwordHash);
                stmt.executeUpdate();
                ResultSet rs = stmt.getGeneratedKeys();
                if (rs.next()) userId = rs.getInt(1);
                else throw new SQLException("Failed to get user_id");
            }

            try (PreparedStatement stmt = connection.prepareStatement(insertSave)) {
                stmt.setInt(1, userId);
                stmt.setBytes(2, saveData);
                stmt.executeUpdate();
            }

            connection.commit();
            extension.trace("Account created: " + username + " (ID: " + userId + ")");
            return true;
        } catch (SQLException e) {
            try { connection.rollback(); } catch (SQLException ex) { extension.trace("Rollback failed: " + ex.getMessage()); }
            handleConnectionException("creating account", e);
            extension.trace("Error creating account: " + e.getMessage());
            return false;
        } finally {
            try { connection.setAutoCommit(true); } catch (SQLException e) { extension.trace("Error resetting auto-commit: " + e.getMessage()); }
        }
    }

    public boolean verifyLogin(String username, String passwordHash) {
        if (!ensureConnected()) {
            return false;
        }

        String query = "SELECT user_id, password_hash FROM users WHERE username = ? AND is_active = TRUE";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setString(1, username);
            ResultSet rs = stmt.executeQuery();

            if (rs.next()) {
                String storedHash = rs.getString("password_hash");
                SecurityUtils.PasswordCheckResult passwordResult = SecurityUtils.verifyPassword(passwordHash, storedHash);
                if (passwordResult.isMatched()) {
                    if (passwordResult.getUpgradedHash() != null) {
                        updatePasswordHash(username, passwordResult.getUpgradedHash());
                    }
                    updateLastLogin(username);
                    return true;
                }
            }
            return false;
        } catch (SQLException e) {
            handleConnectionException("verifying login", e);
            extension.trace("Error verifying login: " + e.getMessage());
            return false;
        }
    }

    public byte[] getSaveData(String username) {
        if (!ensureConnected()) {
            return null;
        }

        String query = "SELECT ps.save_data FROM player_saves ps " +
                      "JOIN users u ON ps.user_id = u.user_id " +
                      "WHERE u.username = ?";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setString(1, username);
            ResultSet rs = stmt.executeQuery();
            if (rs.next()) return rs.getBytes("save_data");
            return null;
        } catch (SQLException e) {
            handleConnectionException("retrieving save data", e);
            extension.trace("Error retrieving save data: " + e.getMessage());
            return null;
        }
    }

    public boolean updateSaveData(String username, byte[] saveData, long wins, long losses, long distance, long totalDistance, long mtxItems) {
        if (!ensureConnected()) {
            return false;
        }

        String query = "UPDATE player_saves ps " +
                      "JOIN users u ON ps.user_id = u.user_id " +
                      "SET " +
                      "ps.daily_matches_won = IF(ps.current_day = CURRENT_DATE(), ps.daily_matches_won, 0) + GREATEST(0, ? - ps.matches_won), " +
                      "ps.daily_matches_lost = IF(ps.current_day = CURRENT_DATE(), ps.daily_matches_lost, 0) + GREATEST(0, ? - ps.matches_lost), " +
                      "ps.daily_distance_ran = IF(ps.current_day = CURRENT_DATE(), ps.daily_distance_ran, 0) + GREATEST(0, ? - ps.distance_ran), " +
                      "ps.daily_high_score = IF(ps.current_day = CURRENT_DATE(), GREATEST(ps.daily_high_score, GREATEST(0, ? - ps.coop_high_score)), GREATEST(0, ? - ps.coop_high_score)), " +
                      "ps.current_day = CURRENT_DATE(), " +
                      "ps.save_data = ?, ps.matches_won = ?, ps.matches_lost = ?, " +
                      "ps.distance_ran = ?, ps.coop_high_score = ?, ps.mtx_items_count = ? " +
                      "WHERE u.username = ?";

        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setLong(1, wins);
            stmt.setLong(2, losses);
            stmt.setLong(3, distance);
            stmt.setLong(4, totalDistance);
            stmt.setLong(5, totalDistance);
            stmt.setBytes(6, saveData);
            stmt.setLong(7, wins);
            stmt.setLong(8, losses);
            stmt.setLong(9, distance);
            stmt.setLong(10, totalDistance);
            stmt.setLong(11, mtxItems);
            stmt.setString(12, username);
            int rowsAffected = stmt.executeUpdate();
            return rowsAffected > 0;
        } catch (SQLException e) {
            handleConnectionException("updating save data", e);
            extension.trace("Error updating save data: " + e.getMessage());
            return false;
        }
    }

    public boolean setUserActive(String username, boolean active) {
        if (!ensureConnected()) {
            return false;
        }

        String query = "UPDATE users SET is_active = ? WHERE username = ?";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setBoolean(1, active);
            stmt.setString(2, username);
            int rowsAffected = stmt.executeUpdate();
            return rowsAffected > 0;
        } catch (SQLException e) {
            handleConnectionException("updating user active flag", e);
            extension.trace("Error updating user active flag: " + e.getMessage());
            return false;
        }
    }

    private void updateLastLogin(String username) {
        String query = "UPDATE users SET last_login = CURRENT_TIMESTAMP WHERE username = ?";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setString(1, username);
            stmt.executeUpdate();
        } catch (SQLException e) {
            handleConnectionException("updating last login", e);
            extension.trace("Error updating last login: " + e.getMessage());
        }
    }

    private void updatePasswordHash(String username, String passwordHash) {
        String query = "UPDATE users SET password_hash = ?, password_updated_at = CURRENT_TIMESTAMP WHERE username = ?";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setString(1, passwordHash);
            stmt.setString(2, username);
            stmt.executeUpdate();
            extension.trace("Upgraded password hash for user: " + username);
        } catch (SQLException e) {
            handleConnectionException("upgrading password hash", e);
            extension.trace("Error upgrading password hash: " + e.getMessage());
        }
    }

    public Integer getUserId(String username) {
        if (!ensureConnected()) {
            return null;
        }

        String query = "SELECT user_id FROM users WHERE username = ? AND is_active = TRUE";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setString(1, username);
            ResultSet rs = stmt.executeQuery();
            if (rs.next()) {
                return rs.getInt("user_id");
            }
            return null;
        } catch (SQLException e) {
            handleConnectionException("looking up user id", e);
            extension.trace("Error looking up user id: " + e.getMessage());
            return null;
        }
    }

    public EmailInfo getEmailInfo(String username) {
        if (!ensureConnected()) {
            return null;
        }

        String query = "SELECT email, email_verified FROM users WHERE username = ?";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setString(1, username);
            ResultSet rs = stmt.executeQuery();
            if (rs.next()) {
                return new EmailInfo(rs.getString("email"), rs.getBoolean("email_verified"));
            }
            return null;
        } catch (SQLException e) {
            handleConnectionException("reading email status", e);
            extension.trace("Error reading email status: " + e.getMessage());
            return null;
        }
    }

    public String getUsernameByVerifiedEmail(String email) {
        if (!ensureConnected()) {
            return null;
        }

        String query = "SELECT username FROM users WHERE email = ? AND email_verified = TRUE AND is_active = TRUE";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setString(1, email);
            ResultSet rs = stmt.executeQuery();
            if (rs.next()) {
                return rs.getString("username");
            }
            return null;
        } catch (SQLException e) {
            handleConnectionException("looking up email owner", e);
            extension.trace("Error looking up email owner: " + e.getMessage());
            return null;
        }
    }

    public boolean storeEmailCode(int userId, String purpose, String email, String salt, String hash, int expiryMinutes) {
        if (!ensureConnected()) {
            return false;
        }

        String query = "INSERT INTO email_codes (user_id, purpose, email, code_salt, code_hash, attempts, expires_at) " +
                       "VALUES (?, ?, ?, ?, ?, 0, DATE_ADD(NOW(), INTERVAL ? MINUTE)) " +
                       "ON DUPLICATE KEY UPDATE email = VALUES(email), code_salt = VALUES(code_salt), " +
                       "code_hash = VALUES(code_hash), attempts = 0, expires_at = VALUES(expires_at), created_at = NOW()";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setInt(1, userId);
            stmt.setString(2, purpose);
            stmt.setString(3, email);
            stmt.setString(4, salt);
            stmt.setString(5, hash);
            stmt.setInt(6, expiryMinutes);
            stmt.executeUpdate();
            return true;
        } catch (SQLException e) {
            handleConnectionException("storing email code", e);
            extension.trace("Error storing email code: " + e.getMessage());
            return false;
        }
    }

    public EmailCodeRow getEmailCode(int userId, String purpose) {
        if (!ensureConnected()) {
            return null;
        }

        String query = "SELECT email, code_salt, code_hash, attempts, (expires_at < NOW()) AS expired " +
                       "FROM email_codes WHERE user_id = ? AND purpose = ?";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setInt(1, userId);
            stmt.setString(2, purpose);
            ResultSet rs = stmt.executeQuery();
            if (rs.next()) {
                return new EmailCodeRow(
                    rs.getString("email"),
                    rs.getString("code_salt"),
                    rs.getString("code_hash"),
                    rs.getInt("attempts"),
                    rs.getBoolean("expired")
                );
            }
            return null;
        } catch (SQLException e) {
            handleConnectionException("reading email code", e);
            extension.trace("Error reading email code: " + e.getMessage());
            return null;
        }
    }

    public boolean incrementEmailCodeAttempts(int userId, String purpose) {
        if (!ensureConnected()) {
            return false;
        }

        String query = "UPDATE email_codes SET attempts = attempts + 1 WHERE user_id = ? AND purpose = ?";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setInt(1, userId);
            stmt.setString(2, purpose);
            return stmt.executeUpdate() > 0;
        } catch (SQLException e) {
            handleConnectionException("incrementing code attempts", e);
            extension.trace("Error incrementing code attempts: " + e.getMessage());
            return false;
        }
    }

    public boolean tryConsumeEmailCodeAttempt(int userId, String purpose, int maxAttempts) {
        if (!ensureConnected()) {
            return false;
        }

        String query = "UPDATE email_codes SET attempts = attempts + 1 " +
                       "WHERE user_id = ? AND purpose = ? AND attempts < ?";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setInt(1, userId);
            stmt.setString(2, purpose);
            stmt.setInt(3, maxAttempts);
            return stmt.executeUpdate() > 0;
        } catch (SQLException e) {
            handleConnectionException("consuming code attempt", e);
            extension.trace("Error consuming code attempt: " + e.getMessage());
            return false;
        }
    }

    public void pingTiming() {
        if (!ensureConnected()) {
            return;
        }
        try (PreparedStatement stmt = connection.prepareStatement("SELECT 1")) {
            stmt.executeQuery().close();
            stmt.executeQuery().close();
        } catch (SQLException e) {
            handleConnectionException("timing ping", e);
        }
    }

    public void deleteEmailCode(int userId, String purpose) {
        if (!ensureConnected()) {
            return;
        }

        String query = "DELETE FROM email_codes WHERE user_id = ? AND purpose = ?";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setInt(1, userId);
            stmt.setString(2, purpose);
            stmt.executeUpdate();
        } catch (SQLException e) {
            handleConnectionException("deleting email code", e);
            extension.trace("Error deleting email code: " + e.getMessage());
        }
    }

    public int setVerifiedEmail(int userId, String email) {
        if (!ensureConnected()) {
            return 0;
        }

        String query = "UPDATE users SET email = ?, email_verified = TRUE WHERE user_id = ?";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setString(1, email);
            stmt.setInt(2, userId);
            return stmt.executeUpdate() > 0 ? 1 : 0;
        } catch (SQLIntegrityConstraintViolationException e) {
            return -1;
        } catch (SQLException e) {
            if ("23000".equals(e.getSQLState())) {
                return -1;
            }
            handleConnectionException("setting verified email", e);
            extension.trace("Error setting verified email: " + e.getMessage());
            return 0;
        }
    }

    public boolean updatePasswordForUser(String username, String passwordHash) {
        if (!ensureConnected()) {
            return false;
        }

        String query = "UPDATE users SET password_hash = ?, password_updated_at = CURRENT_TIMESTAMP WHERE username = ? AND is_active = TRUE";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setString(1, passwordHash);
            stmt.setString(2, username);
            return stmt.executeUpdate() > 0;
        } catch (SQLException e) {
            handleConnectionException("resetting password", e);
            extension.trace("Error resetting password: " + e.getMessage());
            return false;
        }
    }

    public boolean isConnected() {
        return ensureConnected();
    }

    public static final class EmailInfo {
        public final String email;
        public final boolean verified;

        private EmailInfo(String email, boolean verified) {
            this.email = email;
            this.verified = verified;
        }
    }

    public static final class EmailCodeRow {
        public final String email;
        public final String salt;
        public final String hash;
        public final int attempts;
        public final boolean expired;

        private EmailCodeRow(String email, String salt, String hash, int attempts, boolean expired) {
            this.email = email;
            this.salt = salt;
            this.hash = hash;
            this.attempts = attempts;
            this.expired = expired;
        }
    }

    private synchronized void closeSilently() {
        try {
            if (connection != null && !connection.isClosed()) {
                connection.close();
                extension.trace("Database disconnected");
            }
        } catch (SQLException e) {
            extension.trace("Error closing database: " + e.getMessage());
        } finally {
            connection = null;
        }
    }

    private void handleConnectionException(String action, SQLException e) {
        if (isConnectionException(e)) {
            extension.trace("Database connection lost while " + action + ": " + e.getMessage());
            closeSilently();
        }
    }

    private boolean isConnectionException(SQLException e) {
        if (e instanceof SQLRecoverableException ||
            e instanceof SQLTransientConnectionException ||
            e instanceof SQLNonTransientConnectionException) {
            return true;
        }

        String sqlState = e.getSQLState();
        return sqlState != null && sqlState.startsWith("08");
    }

    private String readConfig(String systemProperty, String envVar, String defaultValue) {
        String value = System.getProperty(systemProperty);
        if (isBlank(value)) {
            value = System.getenv(envVar);
        }
        if (isBlank(value)) {
            value = defaultValue;
        }
        return value;
    }

    private boolean isBlank(String value) {
        return value == null || value.trim().isEmpty();
    }

    private String buildConnectionUrl(String baseUrl) {
        String normalizedUrl = baseUrl.trim();

        if (normalizedUrl.contains("allowPublicKeyRetrieval=")) {
            return normalizedUrl;
        }

        StringBuilder builder = new StringBuilder(normalizedUrl);
        builder.append(normalizedUrl.contains("?") ? "&" : "?");
        builder.append("allowPublicKeyRetrieval=true");
        builder.append("&useSSL=false");
        builder.append("&serverTimezone=UTC");
        return builder.toString();
    }
}
