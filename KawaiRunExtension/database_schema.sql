-- KawaiRun Database Schema
-- This creates the database structure for storing user accounts and save data

-- Create database (if using MySQL/MariaDB)
CREATE DATABASE IF NOT EXISTS kawairun_db CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE kawairun_db;

-- Users table - stores account credentials
CREATE TABLE IF NOT EXISTS users (
    user_id INT AUTO_INCREMENT PRIMARY KEY,
    username VARCHAR(50) NOT NULL UNIQUE,
    password_hash VARCHAR(128) NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_login TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    is_active BOOLEAN DEFAULT TRUE,
    INDEX idx_username (username)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Player save data table - stores the serialized game save
CREATE TABLE IF NOT EXISTS player_saves (
    save_id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    save_data MEDIUMBLOB NOT NULL,
    -- Quick access stats (denormalized for performance)
    matches_won INT DEFAULT 0,
    matches_lost INT DEFAULT 0,
    distance_ran BIGINT DEFAULT 0,
    coop_high_score BIGINT DEFAULT 0,
    xp_level INT DEFAULT 1,
    xp_points INT DEFAULT 0,
    coins INT DEFAULT 0,
    blue_coins INT DEFAULT 0,
    mtx_items_count INT DEFAULT 0,
    -- Timestamps
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE,
    INDEX idx_user_id (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Player statistics table (optional - for leaderboards)
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

-- Session tracking (optional - for security)
CREATE TABLE IF NOT EXISTS user_sessions (
    session_id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    login_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    logout_time TIMESTAMP NULL,
    ip_address VARCHAR(45),
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE,
    INDEX idx_user_sessions (user_id, login_time)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Create a database user for the game server (optional but recommended)
-- Replace 'your_password' with a strong password
CREATE USER IF NOT EXISTS 'kawairun_server'@'localhost' IDENTIFIED BY 'KawaiRun2024!';
GRANT SELECT, INSERT, UPDATE ON kawairun_db.* TO 'kawairun_server'@'localhost';
FLUSH PRIVILEGES;




