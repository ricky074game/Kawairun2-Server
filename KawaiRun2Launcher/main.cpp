#include <windows.h>
#include <string>
#include <filesystem>

int WINAPI wWinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, PWSTR pCmdLine, int nCmdShow) {
    
    wchar_t buffer[MAX_PATH];
    GetModuleFileNameW(NULL, buffer, MAX_PATH);
    std::filesystem::path exePath(buffer);
    std::filesystem::path baseDir = exePath.parent_path();
    std::filesystem::path flashExe = baseDir / "flash.exe";
    std::filesystem::path gameSwf = baseDir / "game.swf";
    if (!std::filesystem::exists(flashExe) || !std::filesystem::exists(gameSwf)) {
        std::wstring msg = L"Missing files!\n\nI looked here:\n" + baseDir.wstring() + 
                           L"\n\nI need 'flash.exe' and 'game.swf' in this folder.";
        MessageBoxW(NULL, msg.c_str(), L"KawaiRun2 Launcher Error", MB_OK | MB_ICONERROR);
        return 1;
    }
    std::wstring cmdLine = L"\"" + flashExe.wstring() + L"\" \"" + gameSwf.wstring() + L"\"";
    std::vector<wchar_t> cmdBuffer(cmdLine.begin(), cmdLine.end());
    cmdBuffer.push_back(0); // Null terminator

    STARTUPINFOW si = { sizeof(si) };
    PROCESS_INFORMATION pi = { 0 };
    BOOL success = CreateProcessW(
        NULL,                   
        cmdBuffer.data(),       
        NULL,                  
        NULL,                  
        FALSE,                  
        0,                      
        NULL,                   
        baseDir.c_str(),       
        &si,
        &pi
    );

    if (!success) {
        std::wstring errorMsg = L"Failed to start flash.exe. Error Code: " + std::to_wstring(GetLastError());
        MessageBoxW(NULL, errorMsg.c_str(), L"Launcher Error", MB_OK | MB_ICONERROR);
        return 1;
    }
    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
    return 0;
}