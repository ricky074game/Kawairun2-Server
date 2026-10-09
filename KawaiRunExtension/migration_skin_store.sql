-- Skin store tables (see STORE_CONTRACT.md). Idempotent; the Node web server also runs these at startup.
USE kawairun_db;

CREATE TABLE IF NOT EXISTS skin_items (
  item_id      INT AUTO_INCREMENT PRIMARY KEY,
  creator_id   INT NOT NULL,
  creator_name VARCHAR(50) NOT NULL,          -- display copy of users.username
  name         VARCHAR(40) NOT NULL,
  price        INT NOT NULL DEFAULT 0,        -- coins, 0..100000
  listed       TINYINT NOT NULL DEFAULT 0,    -- 1 = visible in the store
  removed      TINYINT NOT NULL DEFAULT 0,    -- deleted by creator/admin (owners keep wearing it)
  flags        INT NOT NULL DEFAULT 0,        -- skin meta flags byte (CUSTOM_SKINS.md)
  sales        INT NOT NULL DEFAULT 0,        -- paid + free acquisitions by others
  has_preview  TINYINT NOT NULL DEFAULT 0,
  kind         VARCHAR(8) NOT NULL DEFAULT 'skin',  -- 'skin' | 'hat' | 'trail'
  created_at   TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  updated_at   TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  INDEX (listed, removed), INDEX idx_kind_listed (kind, listed, removed), INDEX (creator_id),
  FOREIGN KEY (creator_id) REFERENCES users(user_id) ON DELETE CASCADE
) DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS skin_owned (
  user_id INT NOT NULL, item_id INT NOT NULL,
  price_paid INT NOT NULL DEFAULT 0,
  acquired_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (user_id, item_id),
  FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE,
  FOREIGN KEY (item_id) REFERENCES skin_items(item_id) ON DELETE CASCADE
) DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS skin_equipped (
  user_id INT PRIMARY KEY, item_id INT NULL,  -- NULL = wear the normal costume
  hat_item_id INT NULL,                        -- NULL = normal in-game hat
  trail_item_id INT NULL,                      -- NULL = built-in trail
  FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE
) DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS skin_earnings (
  earning_id INT AUTO_INCREMENT PRIMARY KEY,
  creator_id INT NOT NULL, buyer_id INT NOT NULL, item_id INT NOT NULL,
  amount INT NOT NULL,
  delivered TINYINT NOT NULL DEFAULT 0,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  INDEX (creator_id, delivered)
) DEFAULT CHARSET=utf8mb4;

-- ---------------------------------------------------------------------------
-- Upgrade for databases created BEFORE custom hats/trails (STORE_CONTRACT.md 7/8).
-- The Node server does this automatically at startup; run this by hand if you
-- deploy the Java extension first. Each block only alters when the column/index
-- is missing, so it is safe to re-run (works on MySQL 5.7/8 and MariaDB).
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
