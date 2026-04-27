import com.smartfoxserver.bitswarm.sessions.ISession;
import com.smartfoxserver.v2.core.ISFSEvent;
import com.smartfoxserver.v2.core.SFSEventParam;
import com.smartfoxserver.v2.entities.data.ISFSObject;
import com.smartfoxserver.v2.exceptions.SFSLoginException;
import com.smartfoxserver.v2.extensions.BaseServerEventHandler;

public class LoginHandler extends BaseServerEventHandler {
    @Override
    public void handleServerEvent(ISFSEvent event) throws SFSLoginException {
        ISession session = (ISession) event.getParameter(SFSEventParam.SESSION);
        String userName = (String) event.getParameter(SFSEventParam.LOGIN_NAME);
        String password = (String) event.getParameter(SFSEventParam.LOGIN_PASSWORD);
        ISFSObject loginParams = (ISFSObject) event.getParameter(SFSEventParam.LOGIN_IN_DATA);
        String zoneName = getParentExtension().getParentZone().getName();

        trace("Login attempt for user: " + userName + " in Zone: " + zoneName);

        // Check if credentials are in loginParams (EPass field)
        String actualPassword = password;
        if (loginParams != null && loginParams.containsKey("EPass")) {
            actualPassword = loginParams.getUtfString("EPass");
            trace("Using password from EPass parameter");
        }

        // Get database manager
        KawaiRunExtension parentExt = (KawaiRunExtension) getParentExtension();
        DatabaseManager dbManager = parentExt.getDbManager();

        // Check if this is a guest login (empty username/password or guest username)
        boolean isGuest = SecurityUtils.isGuestUser(userName);

        if (isGuest) {
            // Allow guest login without database check
            trace("Guest/initial login allowed");
            getApi().login(session, userName, password, zoneName, loginParams);
        } else {
            if (!SecurityUtils.isValidUsername(userName)) {
                trace("Rejected login for invalid username format");
                throw new SFSLoginException("Invalid username or password");
            }

            if (parentExt.isLoginBlocked(userName)) {
                trace("Rejected login for rate-limited user: " + userName);
                throw new SFSLoginException("Too many login attempts. Please try again later.");
            }

            // For registered users, check if they exist in database
            if (dbManager != null && dbManager.isConnected()) {
                if (dbManager.userExists(userName)) {
                    // User exists - verify password
                    // Check if this is a recent registration (auto-login flow)
                    if (parentExt.isRecentRegistration(userName)) {
                        trace("Recent registration detected for: " + userName + " - handling auto-login");

                        // Check if this session already completed a login to prevent duplicate
                        Object loginDone = session.getProperty("LOGIN_COMPLETED");
                        if (loginDone != null) {
                            trace("Session already has completed login - skipping duplicate login call");
                            return;
                        }

                        // Delay to ensure logout completes
                        try {
                            Thread.sleep(500); // 500ms delay
                        } catch (InterruptedException e) {
                            // Ignore interruption
                        }

                        // Verify password
                        if (dbManager.verifyLogin(userName, actualPassword)) {
                            trace("Database authentication successful for: " + userName);
                            parentExt.clearFailedLoginAttempts(userName);

                            // Clear registration tracking BEFORE login to prevent race
                            parentExt.clearRecentRegistration(userName);

                            // Mark session to prevent duplicate login calls
                            session.setProperty("LOGIN_COMPLETED", true);

                            // Allow login - forceLogout zone setting will handle duplicates
                            getApi().login(session, userName, password, zoneName, loginParams);
                        } else {
                            trace("!!! Login failed for: " + userName + " - invalid password !!!");
                            parentExt.recordFailedLoginAttempt(userName);
                            throw new SFSLoginException("Invalid username or password");
                        }
                    } else {
                        // Normal login flow
                        if (dbManager.verifyLogin(userName, actualPassword)) {
                            trace("Database authentication successful for: " + userName);
                            parentExt.clearFailedLoginAttempts(userName);
                            getApi().login(session, userName, password, zoneName, loginParams);
                        } else {
                            trace("!!! Login failed for: " + userName + " - invalid password !!!");
                            parentExt.recordFailedLoginAttempt(userName);
                            throw new SFSLoginException("Invalid username or password");
                        }
                    }
                } else {
                    trace("!!! Login failed for: " + userName + " - unknown account !!!");
                    parentExt.recordFailedLoginAttempt(userName);
                    throw new SFSLoginException("Invalid username or password");
                }
            } else {
                trace("Database not available - rejecting registered login");
                throw new SFSLoginException("Login service is temporarily unavailable");
            }
        }
    }
}
