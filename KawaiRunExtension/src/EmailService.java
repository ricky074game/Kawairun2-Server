import com.smartfoxserver.v2.extensions.SFSExtension;
import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import javax.net.ssl.SSLParameters;
import javax.net.ssl.SSLSocket;
import javax.net.ssl.SSLSocketFactory;

public class EmailService {
    private static final int SOCKET_TIMEOUT_MS = 15000;

    private final SFSExtension extension;
    private final ExecutorService executor;

    private final String host;
    private final int port;
    private final String user;
    private final String pass;
    private final String from;
    private final String fromName;

    public EmailService(SFSExtension extension) {
        this.extension = extension;
        this.host = readConfig("kawairun.smtp.host", "KAWAIRUN_SMTP_HOST", null);
        this.port = readIntConfig("kawairun.smtp.port", "KAWAIRUN_SMTP_PORT", 587);
        this.user = readConfig("kawairun.smtp.user", "KAWAIRUN_SMTP_USER", null);
        this.pass = readConfig("kawairun.smtp.pass", "KAWAIRUN_SMTP_PASS", null);
        this.from = readConfig("kawairun.smtp.from", "KAWAIRUN_SMTP_FROM", this.user);
        this.fromName = readConfig("kawairun.smtp.fromName", "KAWAIRUN_SMTP_FROM_NAME", "Kawairun 2");
        this.executor = Executors.newSingleThreadExecutor(runnable -> {
            Thread thread = new Thread(runnable, "KawaiRun-EmailSender");
            thread.setDaemon(true);
            return thread;
        });

        if (isConfigured()) {
            extension.trace("EmailService configured for SMTP host " + host + ":" + port);
        } else {
            extension.trace("EmailService in DEV mode (KAWAIRUN_SMTP_HOST not set) - codes will be traced to this log instead of emailed");
        }
    }

    public boolean isConfigured() {
        return !isBlank(host) && !isBlank(user) && !isBlank(pass) && !isBlank(from);
    }

    public void sendCodeEmail(String toEmail, String subject, String body) {
        if (!SecurityUtils.isValidEmail(toEmail)) {
            extension.trace("EmailService refused to send to invalid address");
            return;
        }

        if (!isConfigured()) {
            boolean logCode = Boolean.parseBoolean(
                readConfig("kawairun.email.devLogCodes", "KAWAIRUN_EMAIL_DEV_LOG_CODES", "false"));
            if (logCode) {
                extension.trace("[DEV EMAIL] To: " + SecurityUtils.maskEmail(toEmail)
                    + " | Subject: " + subject + " | " + body.replace("\r\n", " / "));
            } else {
                extension.trace("[DEV EMAIL] To: " + SecurityUtils.maskEmail(toEmail)
                    + " | Subject: " + subject + " | (code redacted; set KAWAIRUN_EMAIL_DEV_LOG_CODES=true to show)");
            }
            return;
        }

        executor.submit(() -> {
            try {
                sendViaSmtp(toEmail, subject, body);
                extension.trace("Email sent to " + SecurityUtils.maskEmail(toEmail));
            } catch (Exception e) {
                extension.trace("Email send FAILED to " + SecurityUtils.maskEmail(toEmail) + ": " + e.getMessage());
            }
        });
    }

    public void shutdown() {
        executor.shutdown();
        try {
            executor.awaitTermination(5, TimeUnit.SECONDS);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }

    private void sendViaSmtp(String toEmail, String subject, String body) throws IOException {
        Socket socket = null;
        try {
            if (port == 465) {
                SSLSocket sslSocket = (SSLSocket) SSLSocketFactory.getDefault().createSocket();
                sslSocket.connect(new InetSocketAddress(host, port), SOCKET_TIMEOUT_MS);
                sslSocket.setSoTimeout(SOCKET_TIMEOUT_MS);
                enableHostnameVerification(sslSocket);
                sslSocket.startHandshake();
                socket = sslSocket;
                SmtpChannel channel = new SmtpChannel(socket);
                channel.expect(220);
                smtpSession(channel, toEmail, subject, body);
            } else {
                socket = new Socket();
                socket.connect(new InetSocketAddress(host, port), SOCKET_TIMEOUT_MS);
                socket.setSoTimeout(SOCKET_TIMEOUT_MS);
                SmtpChannel channel = new SmtpChannel(socket);
                channel.expect(220);
                channel.command("EHLO kawairun", 250);
                channel.command("STARTTLS", 220);

                SSLSocket sslSocket = (SSLSocket) ((SSLSocketFactory) SSLSocketFactory.getDefault())
                    .createSocket(socket, host, port, true);
                sslSocket.setSoTimeout(SOCKET_TIMEOUT_MS);
                enableHostnameVerification(sslSocket);
                sslSocket.startHandshake();
                socket = sslSocket;
                channel = new SmtpChannel(socket);
                smtpSession(channel, toEmail, subject, body);
            }
        } finally {
            if (socket != null) {
                try {
                    socket.close();
                } catch (IOException ignored) {
                }
            }
        }
    }

    private static void enableHostnameVerification(SSLSocket sslSocket) {
        SSLParameters params = sslSocket.getSSLParameters();
        params.setEndpointIdentificationAlgorithm("HTTPS");
        sslSocket.setSSLParameters(params);
    }

    private void smtpSession(SmtpChannel channel, String toEmail, String subject, String body) throws IOException {
        channel.command("EHLO kawairun", 250);
        channel.command("AUTH LOGIN", 334);
        channel.command(base64(user), 334);
        channel.command(base64(pass), 235);
        channel.command("MAIL FROM:<" + from + ">", 250);
        channel.command("RCPT TO:<" + toEmail + ">", 250);
        channel.command("DATA", 354);
        channel.writeRaw(buildMessage(toEmail, subject, body));
        channel.command(".", 250);
        channel.command("QUIT", 221);
    }

    private String buildMessage(String toEmail, String subject, String body) {
        StringBuilder message = new StringBuilder();
        message.append("From: ").append(sanitizeHeader(fromName)).append(" <").append(from).append(">\r\n");
        message.append("To: <").append(toEmail).append(">\r\n");
        message.append("Subject: ").append(sanitizeHeader(subject)).append("\r\n");
        message.append("MIME-Version: 1.0\r\n");
        message.append("Content-Type: text/plain; charset=UTF-8\r\n");
        message.append("\r\n");

        // Normalize to CRLF and dot-stuff lines that start with '.'
        String normalized = body.replace("\r\n", "\n").replace('\r', '\n');
        for (String line : normalized.split("\n", -1)) {
            if (line.startsWith(".")) {
                message.append('.');
            }
            message.append(line).append("\r\n");
        }
        return message.toString();
    }

    private static String sanitizeHeader(String value) {
        if (value == null) {
            return "";
        }
        StringBuilder cleaned = new StringBuilder(value.length());
        for (int i = 0; i < value.length(); i++) {
            char c = value.charAt(i);
            if (!Character.isISOControl(c)) {
                cleaned.append(c);
            }
        }
        return cleaned.toString();
    }

    private static String base64(String value) {
        return Base64.getEncoder().encodeToString(value.getBytes(StandardCharsets.UTF_8));
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

    private int readIntConfig(String systemProperty, String envVar, int defaultValue) {
        String value = readConfig(systemProperty, envVar, Integer.toString(defaultValue));
        try {
            return Integer.parseInt(value.trim());
        } catch (NumberFormatException e) {
            return defaultValue;
        }
    }

    private static boolean isBlank(String value) {
        return value == null || value.trim().isEmpty();
    }

    private static final class SmtpChannel {
        private final BufferedReader reader;
        private final OutputStream out;

        private SmtpChannel(Socket socket) throws IOException {
            this.reader = new BufferedReader(new InputStreamReader(socket.getInputStream(), StandardCharsets.UTF_8));
            this.out = socket.getOutputStream();
        }

        private void command(String command, int expectedCode) throws IOException {
            out.write((command + "\r\n").getBytes(StandardCharsets.UTF_8));
            out.flush();
            expect(expectedCode);
        }

        private void writeRaw(String data) throws IOException {
            out.write(data.getBytes(StandardCharsets.UTF_8));
            out.flush();
        }

        private void expect(int expectedCode) throws IOException {
            String line;
            do {
                line = reader.readLine();
                if (line == null) {
                    throw new IOException("SMTP connection closed unexpectedly");
                }
            } while (line.length() >= 4 && line.charAt(3) == '-');

            if (line.length() < 3) {
                throw new IOException("Malformed SMTP response: " + line);
            }

            int code;
            try {
                code = Integer.parseInt(line.substring(0, 3));
            } catch (NumberFormatException e) {
                throw new IOException("Malformed SMTP response: " + line);
            }

            if (code != expectedCode) {
                throw new IOException("SMTP error (expected " + expectedCode + "): " + line);
            }
        }
    }
}
