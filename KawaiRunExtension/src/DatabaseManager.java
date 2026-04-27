import com.smartfoxserver.v2.extensions.SFSExtension;
import java.sql.*;

public class DatabaseManager {
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
            Class.forName(dbDriver);
            connection = DriverManager.getConnection(dbUrl, dbUser, dbPassword);
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
        try {
            if (connection != null && !connection.isClosed()) {
                connection.close();
                extension.trace("Database disconnected");
            }
        } catch (SQLException e) {
            extension.trace("Error closing database: " + e.getMessage());
        }
    }

    public boolean userExists(String username) {
        if (!isConnected()) {
            return false;
        }

        String query = "SELECT user_id FROM users WHERE username = ?";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setString(1, username);
            ResultSet rs = stmt.executeQuery();
            return rs.next();
        } catch (SQLException e) {
            extension.trace("Error checking user existence: " + e.getMessage());
            return false;
        }
    }

    public boolean createAccount(String username, String passwordHash, byte[] saveData) {
        if (!isConnected()) {
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
            extension.trace("Error creating account: " + e.getMessage());
            return false;
        } finally {
            try { connection.setAutoCommit(true); } catch (SQLException e) { extension.trace("Error resetting auto-commit: " + e.getMessage()); }
        }
    }

    public boolean verifyLogin(String username, String passwordHash) {
        if (!isConnected()) {
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
            extension.trace("Error verifying login: " + e.getMessage());
            return false;
        }
    }

    public byte[] getSaveData(String username) {
        if (!isConnected()) {
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
            extension.trace("Error retrieving save data: " + e.getMessage());
            return null;
        }
    }

    public boolean updateSaveData(String username, byte[] saveData, long wins, long losses, long distance, long totalDistance, long mtxItems) {
        if (!isConnected()) {
            return false;
        }

        String query = "UPDATE player_saves ps " +
                      "JOIN users u ON ps.user_id = u.user_id " +
                      "SET ps.save_data = ?, ps.matches_won = ?, ps.matches_lost = ?, " +
                      "ps.distance_ran = ?, ps.coop_high_score = ?, ps.mtx_items_count = ? " +
                      "WHERE u.username = ?";

        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setBytes(1, saveData);
            stmt.setLong(2, wins);
            stmt.setLong(3, losses);
            stmt.setLong(4, distance);
            stmt.setLong(5, totalDistance);
            stmt.setLong(6, mtxItems);
            stmt.setString(7, username);
            int rowsAffected = stmt.executeUpdate();
            return rowsAffected > 0;
        } catch (SQLException e) {
            extension.trace("Error updating save data: " + e.getMessage());
            return false;
        }
    }

    private void updateLastLogin(String username) {
        String query = "UPDATE users SET last_login = CURRENT_TIMESTAMP WHERE username = ?";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setString(1, username);
            stmt.executeUpdate();
        } catch (SQLException e) {
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
            extension.trace("Error upgrading password hash: " + e.getMessage());
        }
    }

    public boolean isConnected() {
        try { return connection != null && !connection.isClosed(); } catch (SQLException e) { return false; }
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
}
