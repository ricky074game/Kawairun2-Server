import com.smartfoxserver.v2.extensions.SFSExtension;
import java.sql.*;

public class DatabaseManager {
    private final SFSExtension extension;
    private Connection connection;

    private static final String DB_DRIVER = "com.mysql.cj.jdbc.Driver";
    private static final String DB_URL = "jdbc:mysql://localhost:3306/kawairun_db?useSSL=false&serverTimezone=UTC&allowPublicKeyRetrieval=true";
    private static final String DB_USER = "kawairun_server";
    private static final String DB_PASSWORD = "KawaiRun2024!"; // Change this!

    public DatabaseManager(SFSExtension extension) { this.extension = extension; }

    public boolean connect() {
        try {
            Class.forName(DB_DRIVER);
            connection = DriverManager.getConnection(DB_URL, DB_USER, DB_PASSWORD);
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
        String query = "SELECT user_id, password_hash FROM users WHERE username = ? AND is_active = TRUE";
        try (PreparedStatement stmt = connection.prepareStatement(query)) {
            stmt.setString(1, username);
            ResultSet rs = stmt.executeQuery();

            if (rs.next()) {
                String storedHash = rs.getString("password_hash");
                if (storedHash.equals(passwordHash)) {
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

    public boolean isConnected() {
        try { return connection != null && !connection.isClosed(); } catch (SQLException e) { return false; }
    }
}
