import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.security.SecureRandom;
import java.util.Arrays;
import java.util.Base64;
import java.util.Collections;
import java.util.HashSet;
import java.util.Locale;
import java.util.Set;
import java.util.regex.Pattern;
import org.bouncycastle.crypto.generators.Argon2BytesGenerator;
import org.bouncycastle.crypto.params.Argon2Parameters;

public final class SecurityUtils {
    private static final Pattern USERNAME_PATTERN = Pattern.compile("^[A-Za-z0-9_]{3,24}$");
    private static final Pattern LEGACY_HASH_PATTERN = Pattern.compile("^[a-fA-F0-9]{64}$");
    private static final String MODERN_HASH_PREFIX = "$argon2id$";
    private static final int ARGON2_VERSION = 19;
    private static final int ARGON2_MEMORY_KB = readIntConfig("kawairun.password.argon2.memoryKb", "KAWAIRUN_PASSWORD_ARGON2_MEMORY_KB", 19 * 1024);
    private static final int ARGON2_ITERATIONS = readIntConfig("kawairun.password.argon2.iterations", "KAWAIRUN_PASSWORD_ARGON2_ITERATIONS", 2);
    private static final int ARGON2_PARALLELISM = readIntConfig("kawairun.password.argon2.parallelism", "KAWAIRUN_PASSWORD_ARGON2_PARALLELISM", 1);
    private static final int SALT_LENGTH_BYTES = 16;
    private static final int HASH_LENGTH_BYTES = 32;
    private static final boolean ALLOW_LEGACY_HASH_LOGIN = readBooleanConfig("kawairun.auth.allowLegacyHashLogin", "KAWAIRUN_ALLOW_LEGACY_HASH_LOGIN", false);
    private static final boolean ALLOW_LEGACY_STORED_HASHES = readBooleanConfig("kawairun.auth.allowLegacyStoredHashes", "KAWAIRUN_ALLOW_LEGACY_STORED_HASHES", true);
    private static final byte[] PASSWORD_PEPPER = readSecretBytes("kawairun.password.pepper", "KAWAIRUN_PASSWORD_PEPPER");
    private static final SecureRandom SECURE_RANDOM = new SecureRandom();
    private static final Base64.Encoder BASE64_ENCODER = Base64.getUrlEncoder().withoutPadding();
    private static final Base64.Decoder BASE64_DECODER = Base64.getUrlDecoder();
    private static final Set<String> BLOCKED_PASSWORDS = loadBlockedPasswords();

    private static final int MIN_PASSWORD_LENGTH = 8;
    private static final int MAX_PASSWORD_LENGTH = 128;
    private static final int MAX_SAVE_SIZE_BYTES = 1024 * 1024;
    private static final long MAX_COUNTER_VALUE = Integer.MAX_VALUE;
    private static final long MAX_DISTANCE_VALUE = 10_000_000_000L;

    // Kept only to verify and migrate existing accounts.
    private static final String LEGACY_PASSWORD_SALT = "JEDDAHBIKERSSALT2259GIYU2137";

    private SecurityUtils() {
    }

    public static boolean isGuestUser(String username) {
        if (username == null) {
            return true;
        }

        String normalized = username.trim().toLowerCase(Locale.ROOT);
        return normalized.isEmpty() || normalized.startsWith("guest");
    }

    public static boolean isValidUsername(String username) {
        if (username == null || isGuestUser(username)) {
            return false;
        }

        String normalized = username.trim();
        if ("null".equalsIgnoreCase(normalized) || "system".equalsIgnoreCase(normalized)) {
            return false;
        }

        return USERNAME_PATTERN.matcher(normalized).matches();
    }

    public static boolean isValidPassword(String password) {
        if (password == null || password.length() < MIN_PASSWORD_LENGTH || password.length() > MAX_PASSWORD_LENGTH) {
            return false;
        }

        if (password.trim().isEmpty()) {
            return false;
        }

        for (int i = 0; i < password.length(); i++) {
            if (Character.isISOControl(password.charAt(i))) {
                return false;
            }
        }

        return !BLOCKED_PASSWORDS.contains(password.toLowerCase(Locale.ROOT));
    }

    public static boolean isValidSaveData(byte[] saveData) {
        return saveData != null && saveData.length <= MAX_SAVE_SIZE_BYTES;
    }

    public static boolean isValidCounterStat(long value) {
        return value >= 0 && value <= MAX_COUNTER_VALUE;
    }

    public static boolean isValidDistanceStat(long value) {
        return value >= 0 && value <= MAX_DISTANCE_VALUE;
    }

    public static String hashPasswordForStorage(String plainPassword) {
        return createPasswordHash(plainPassword);
    }

    public static String createPasswordHash(String plainPassword) {
        if (plainPassword == null) {
            return null;
        }

        byte[] salt = new byte[SALT_LENGTH_BYTES];
        SECURE_RANDOM.nextBytes(salt);
        byte[] hash = generateArgon2Hash(plainPassword.toCharArray(), salt, ARGON2_MEMORY_KB, ARGON2_ITERATIONS, ARGON2_PARALLELISM, HASH_LENGTH_BYTES);
        if (hash == null) {
            return null;
        }

        return MODERN_HASH_PREFIX +
            "v=" + ARGON2_VERSION +
            "$m=" + ARGON2_MEMORY_KB + ",t=" + ARGON2_ITERATIONS + ",p=" + ARGON2_PARALLELISM +
            "$" + BASE64_ENCODER.encodeToString(salt) +
            "$" + BASE64_ENCODER.encodeToString(hash);
    }

    public static boolean matchesStoredPassword(String providedSecret, String storedHash) {
        return verifyPassword(providedSecret, storedHash).isMatched();
    }

    public static PasswordCheckResult verifyPassword(String providedSecret, String storedHash) {
        if (providedSecret == null || storedHash == null || storedHash.isEmpty()) {
            return PasswordCheckResult.noMatch();
        }

        if (isModernHash(storedHash)) {
            return verifyModernPassword(providedSecret, storedHash)
                ? PasswordCheckResult.matchNoUpgrade()
                : PasswordCheckResult.noMatch();
        }

        if (!ALLOW_LEGACY_STORED_HASHES) {
            return PasswordCheckResult.noMatch();
        }

        if (!LEGACY_HASH_PATTERN.matcher(storedHash).matches()) {
            return PasswordCheckResult.noMatch();
        }

        boolean providedSecretLooksHashed = LEGACY_HASH_PATTERN.matcher(providedSecret).matches();
        if (providedSecretLooksHashed) {
            if (!ALLOW_LEGACY_HASH_LOGIN) {
                return PasswordCheckResult.noMatch();
            }

            return constantTimeEquals(providedSecret, storedHash)
                ? PasswordCheckResult.matchNoUpgrade()
                : PasswordCheckResult.noMatch();
        }

        String legacyHash = hashLegacyPassword(providedSecret);
        if (legacyHash == null || !constantTimeEquals(legacyHash, storedHash)) {
            return PasswordCheckResult.noMatch();
        }

        String upgradedHash = createPasswordHash(providedSecret);
        return new PasswordCheckResult(true, upgradedHash);
    }

    private static boolean constantTimeEquals(String left, String right) {
        return MessageDigest.isEqual(
            left.getBytes(StandardCharsets.UTF_8),
            right.getBytes(StandardCharsets.UTF_8)
        );
    }

    private static String hashLegacyPassword(String plainPassword) {
        try {
            MessageDigest md = MessageDigest.getInstance("SHA-256");
            byte[] hash = md.digest((LEGACY_PASSWORD_SALT + plainPassword).getBytes(StandardCharsets.UTF_8));
            return toHex(hash);
        } catch (NoSuchAlgorithmException e) {
            return null;
        }
    }

    private static boolean isModernHash(String storedHash) {
        return storedHash.startsWith(MODERN_HASH_PREFIX);
    }

    private static boolean verifyModernPassword(String providedSecret, String storedHash) {
        String[] parts = storedHash.split("\\$");
        if (parts.length != 6 || !"argon2id".equals(parts[1]) || !("v=" + ARGON2_VERSION).equals(parts[2])) {
            return false;
        }

        try {
            Argon2Config config = parseArgon2Config(parts[3]);
            byte[] salt = BASE64_DECODER.decode(parts[4]);
            byte[] expectedHash = BASE64_DECODER.decode(parts[5]);
            byte[] actualHash = generateArgon2Hash(providedSecret.toCharArray(), salt, config.memoryKb, config.iterations, config.parallelism, expectedHash.length);
            return actualHash != null && MessageDigest.isEqual(actualHash, expectedHash);
        } catch (Exception e) {
            return false;
        }
    }

    private static byte[] generateArgon2Hash(char[] password, byte[] salt, int memoryKb, int iterations, int parallelism, int hashLengthBytes) {
        Argon2Parameters.Builder builder = new Argon2Parameters.Builder(Argon2Parameters.ARGON2_id)
            .withVersion(Argon2Parameters.ARGON2_VERSION_13)
            .withMemoryAsKB(memoryKb)
            .withIterations(iterations)
            .withParallelism(parallelism)
            .withSalt(salt);

        if (PASSWORD_PEPPER.length > 0) {
            builder.withSecret(PASSWORD_PEPPER);
        }

        Argon2Parameters parameters = builder.build();
        try {
            Argon2BytesGenerator generator = new Argon2BytesGenerator();
            generator.init(parameters);
            byte[] result = new byte[hashLengthBytes];
            generator.generateBytes(password, result);
            return result;
        } catch (Exception e) {
            return null;
        } finally {
            parameters.clear();
            builder.clear();
            Arrays.fill(password, '\0');
        }
    }

    private static Argon2Config parseArgon2Config(String configPart) {
        int memoryKb = 0;
        int iterations = 0;
        int parallelism = 0;

        for (String part : configPart.split(",")) {
            String[] kv = part.split("=", 2);
            if (kv.length != 2) {
                throw new IllegalArgumentException("Invalid Argon2 config part");
            }

            if ("m".equals(kv[0])) {
                memoryKb = Integer.parseInt(kv[1]);
            } else if ("t".equals(kv[0])) {
                iterations = Integer.parseInt(kv[1]);
            } else if ("p".equals(kv[0])) {
                parallelism = Integer.parseInt(kv[1]);
            }
        }

        if (memoryKb <= 0 || iterations <= 0 || parallelism <= 0) {
            throw new IllegalArgumentException("Invalid Argon2 config");
        }

        return new Argon2Config(memoryKb, iterations, parallelism);
    }

    private static Set<String> loadBlockedPasswords() {
        Set<String> blocked = new HashSet<>(Arrays.asList(
            "12345678",
            "123456789",
            "1234567890",
            "admin123",
            "guest123",
            "kawairun",
            "letmein",
            "password",
            "password1",
            "password123",
            "qwerty123",
            "welcome123"
        ));

        String blocklistPath = readConfig("kawairun.password.blocklistFile", "KAWAIRUN_PASSWORD_BLOCKLIST_FILE", null);
        if (blocklistPath != null && !blocklistPath.trim().isEmpty()) {
            Path path = Paths.get(blocklistPath);
            if (Files.exists(path)) {
                try {
                    for (String line : Files.readAllLines(path, StandardCharsets.UTF_8)) {
                        String value = line.trim().toLowerCase(Locale.ROOT);
                        if (!value.isEmpty() && !value.startsWith("#")) {
                            blocked.add(value);
                        }
                    }
                } catch (IOException ignored) {
                }
            }
        }

        return Collections.unmodifiableSet(blocked);
    }

    private static byte[] readSecretBytes(String systemProperty, String envVar) {
        String value = readConfig(systemProperty, envVar, null);
        return value == null ? new byte[0] : value.getBytes(StandardCharsets.UTF_8);
    }

    private static boolean readBooleanConfig(String systemProperty, String envVar, boolean defaultValue) {
        String value = readConfig(systemProperty, envVar, Boolean.toString(defaultValue));
        return Boolean.parseBoolean(value);
    }

    private static int readIntConfig(String systemProperty, String envVar, int defaultValue) {
        String value = readConfig(systemProperty, envVar, Integer.toString(defaultValue));
        try {
            return Integer.parseInt(value);
        } catch (NumberFormatException e) {
            return defaultValue;
        }
    }

    private static String readConfig(String systemProperty, String envVar, String defaultValue) {
        String value = System.getProperty(systemProperty);
        if (value == null || value.trim().isEmpty()) {
            value = System.getenv(envVar);
        }
        if (value == null || value.trim().isEmpty()) {
            value = defaultValue;
        }
        return value;
    }

    private static String toHex(byte[] data) {
        StringBuilder hexString = new StringBuilder(data.length * 2);
        for (byte b : data) {
            String hex = Integer.toHexString(0xff & b);
            if (hex.length() == 1) {
                hexString.append('0');
            }
            hexString.append(hex);
        }
        return hexString.toString();
    }

    public static final class PasswordCheckResult {
        private final boolean matched;
        private final String upgradedHash;

        private PasswordCheckResult(boolean matched, String upgradedHash) {
            this.matched = matched;
            this.upgradedHash = upgradedHash;
        }

        public boolean isMatched() {
            return matched;
        }

        public String getUpgradedHash() {
            return upgradedHash;
        }

        private static PasswordCheckResult noMatch() {
            return new PasswordCheckResult(false, null);
        }

        private static PasswordCheckResult matchNoUpgrade() {
            return new PasswordCheckResult(true, null);
        }
    }

    private static final class Argon2Config {
        private final int memoryKb;
        private final int iterations;
        private final int parallelism;

        private Argon2Config(int memoryKb, int iterations, int parallelism) {
            this.memoryKb = memoryKb;
            this.iterations = iterations;
            this.parallelism = parallelism;
        }
    }
}
