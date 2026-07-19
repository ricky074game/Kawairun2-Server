USE kawairun_db;

ALTER TABLE users
    ADD COLUMN email VARCHAR(190) NULL DEFAULT NULL,
    ADD COLUMN email_verified BOOLEAN NOT NULL DEFAULT FALSE,
    ADD UNIQUE KEY uniq_email (email);

CREATE TABLE IF NOT EXISTS email_codes (
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
