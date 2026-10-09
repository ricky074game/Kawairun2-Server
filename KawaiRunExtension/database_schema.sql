CREATE DATABASE IF NOT EXISTS kawairun_db CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE kawairun_db;

CREATE TABLE IF NOT EXISTS users (
    user_id INT AUTO_INCREMENT PRIMARY KEY,
    username VARCHAR(50) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    email VARCHAR(190) NULL DEFAULT NULL,
    email_verified BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    password_updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_login TIMESTAMP NULL DEFAULT NULL,
    is_active BOOLEAN DEFAULT TRUE,
    INDEX idx_username (username),
    UNIQUE KEY uniq_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-CREATE TABLE IF NOT EXISTS email_codes (
    code_id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    purpose VARCHAR(10) NOT NULL,
    email VARCHAR(190) NOT NULL,
    code_salt VARCHAR(32) NOT NULL,
    code_hash VARCHAR(128) NOT NULL,
    attempts INT NOT NULL DEFAULT 0,
    expires_at DATETIME NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uniq_user_purpose (user_id, purpose),
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS player_saves (
    save_id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    save_data MEDIUMBLOB NOT NULL,
    matches_won INT DEFAULT 0,
    matches_lost INT DEFAULT 0,
    distance_ran BIGINT DEFAULT 0,
    coop_high_score BIGINT DEFAULT 0,
    xp_level INT DEFAULT 1,
    xp_points INT DEFAULT 0,
    coins INT DEFAULT 0,
    blue_coins INT DEFAULT 0,
    mtx_items_count INT DEFAULT 0,
    daily_matches_won INT NOT NULL DEFAULT 0,
    daily_matches_lost INT NOT NULL DEFAULT 0,
    daily_distance_ran BIGINT NOT NULL DEFAULT 0,
    daily_high_score BIGINT NOT NULL DEFAULT 0,
    current_day DATE NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE,
    INDEX idx_user_id (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS player_stats (
    stat_id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    total_playtime_minutes INT DEFAULT 0,
    tasks_completed INT DEFAULT 0,
    current_day DATE,
    global_spins_left INT DEFAULT 3,
    reports_left INT DEFAULT 3,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE,
    UNIQUE KEY unique_user_stats (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS user_sessions (
    session_id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    login_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    logout_time TIMESTAMP NULL,
    ip_address VARCHAR(45),
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE,
    INDEX idx_user_sessions (user_id, login_time)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;





-- Skin store (see STORE_CONTRACT.md section 1)
CREATE TABLE IF NOT EXISTS skin_items (
    item_id      INT AUTO_INCREMENT PRIMARY KEY,
    creator_id   INT NOT NULL,
    creator_name VARCHAR(50) NOT NULL,
    name         VARCHAR(40) NOT NULL,
    price        INT NOT NULL DEFAULT 0,
    listed       TINYINT NOT NULL DEFAULT 0,
    removed      TINYINT NOT NULL DEFAULT 0,
    flags        INT NOT NULL DEFAULT 0,
    sales        INT NOT NULL DEFAULT 0,
    has_preview  TINYINT NOT NULL DEFAULT 0,
    kind         VARCHAR(8) NOT NULL DEFAULT 'skin',
    created_at   TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at   TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX (listed, removed),
    INDEX idx_kind_listed (kind, listed, removed),
    INDEX (creator_id),
    FOREIGN KEY (creator_id) REFERENCES users(user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS skin_owned (
    user_id     INT NOT NULL,
    item_id     INT NOT NULL,
    price_paid  INT NOT NULL DEFAULT 0,
    acquired_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (user_id, item_id),
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE,
    FOREIGN KEY (item_id) REFERENCES skin_items(item_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS skin_equipped (
    user_id INT PRIMARY KEY,
    item_id INT NULL,
    hat_item_id INT NULL,
    trail_item_id INT NULL,
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS skin_earnings (
    earning_id INT AUTO_INCREMENT PRIMARY KEY,
    creator_id INT NOT NULL,
    buyer_id   INT NOT NULL,
    item_id    INT NOT NULL,
    amount     INT NOT NULL,
    delivered  TINYINT NOT NULL DEFAULT 0,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    INDEX (creator_id, delivered)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Upgrade for existing databases (custom hats/trails; idempotent, MySQL 5.7/8 + MariaDB).
DROP PROCEDURE IF EXISTS kr_skin_store_upgrade;
DELIMITER //
CREATE PROCEDURE kr_skin_store_upgrade()
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'skin_items' AND COLUMN_NAME = 'kind') THEN
    ALTER TABLE skin_items ADD COLUMN kind VARCHAR(8) NOT NULL DEFAULT 'skin';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'skin_equipped' AND COLUMN_NAME = 'hat_item_id') THEN
    ALTER TABLE skin_equipped ADD COLUMN hat_item_id INT NULL;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'skin_equipped' AND COLUMN_NAME = 'trail_item_id') THEN
    ALTER TABLE skin_equipped ADD COLUMN trail_item_id INT NULL;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'skin_items' AND INDEX_NAME = 'idx_kind_listed') THEN
    ALTER TABLE skin_items ADD INDEX idx_kind_listed (kind, listed, removed);
  END IF;
END//
DELIMITER ;
CALL kr_skin_store_upgrade();
DROP PROCEDURE kr_skin_store_upgrade;
