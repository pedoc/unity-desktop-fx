#include "desktop_window_internal.h"

#include <windows.h>

#include <stdexcept>
#include <string>

namespace interactive_wallpaper::desktop
{
namespace
{
struct ProcessWindowSearch
{
    DWORD processId = 0;
    HWND window = nullptr;
};

BOOL CALLBACK FindProcessWindow(HWND window, LPARAM parameter)
{
    auto& search = *reinterpret_cast<ProcessWindowSearch*>(parameter);
    DWORD processId = 0;
    GetWindowThreadProcessId(window, &processId);
    if (processId != search.processId || !IsWindowVisible(window) || GetWindow(window, GW_OWNER) != nullptr)
    {
        return TRUE;
    }

    wchar_t className[128]{};
    GetClassNameW(window, className, static_cast<int>(std::size(className)));
    if (wcscmp(className, L"UnityWndClass") == 0 || wcscmp(className, L"UnityContainerWndClass") == 0)
    {
        search.window = window;
        return FALSE;
    }

    if (search.window == nullptr)
    {
        search.window = window;
    }
    return TRUE;
}

BOOL CALLBACK FindExplorerDesktopWorker(HWND window, LPARAM parameter)
{
    const HWND view = FindWindowExW(window, nullptr, L"SHELLDLL_DefView", nullptr);
    if (view == nullptr)
    {
        return TRUE;
    }

    *reinterpret_cast<HWND*>(parameter) = window;
    return FALSE;
}

HWND FindExplorerDesktopContainer()
{
    const HWND programManager = FindWindowW(L"Progman", nullptr);
    if (programManager != nullptr &&
        FindWindowExW(programManager, nullptr, L"SHELLDLL_DefView", nullptr) != nullptr)
    {
        return programManager;
    }

    HWND worker = nullptr;
    EnumWindows(FindExplorerDesktopWorker, reinterpret_cast<LPARAM>(&worker));
    return worker;
}

RECT PrimaryMonitorBounds()
{
    const POINT primaryOrigin{0, 0};
    const HMONITOR monitor = MonitorFromPoint(primaryOrigin, MONITOR_DEFAULTTOPRIMARY);
    MONITORINFO info{};
    info.cbSize = sizeof(info);
    if (monitor == nullptr || !GetMonitorInfoW(monitor, &info))
    {
        throw std::runtime_error("GetMonitorInfoW failed for the primary monitor");
    }
    return info.rcMonitor;
}

std::runtime_error Win32Error(const char* operation)
{
    return std::runtime_error(std::string(operation) + " failed with Win32 error " + std::to_string(GetLastError()));
}
} // namespace

WindowAttachmentResult AttachCurrentProcessWindow()
{
    WindowAttachmentResult result;

    ProcessWindowSearch search{GetCurrentProcessId(), nullptr};
    EnumWindows(FindProcessWindow, reinterpret_cast<LPARAM>(&search));
    if (search.window == nullptr)
    {
        return result;
    }
    result.windowFound = true;

    const HWND desktopContainer = FindExplorerDesktopContainer();
    if (desktopContainer == nullptr)
    {
        return result;
    }
    result.explorerDesktopFound = true;

    const RECT primaryBounds = PrimaryMonitorBounds();
    POINT primaryOrigin{primaryBounds.left, primaryBounds.top};
    if (!ScreenToClient(desktopContainer, &primaryOrigin))
    {
        throw Win32Error("ScreenToClient");
    }

    LONG_PTR style = GetWindowLongPtrW(search.window, GWL_STYLE);
    style &= ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU | WS_POPUP);
    style |= WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN;
    SetLastError(ERROR_SUCCESS);
    if (SetWindowLongPtrW(search.window, GWL_STYLE, style) == 0 && GetLastError() != ERROR_SUCCESS)
    {
        throw Win32Error("SetWindowLongPtrW(GWL_STYLE)");
    }

    LONG_PTR extendedStyle = GetWindowLongPtrW(search.window, GWL_EXSTYLE);
    extendedStyle &= ~WS_EX_APPWINDOW;
    extendedStyle |= WS_EX_TOOLWINDOW;
    SetLastError(ERROR_SUCCESS);
    if (SetWindowLongPtrW(search.window, GWL_EXSTYLE, extendedStyle) == 0 && GetLastError() != ERROR_SUCCESS)
    {
        throw Win32Error("SetWindowLongPtrW(GWL_EXSTYLE)");
    }

    SetLastError(ERROR_SUCCESS);
    if (SetParent(search.window, desktopContainer) == nullptr && GetLastError() != ERROR_SUCCESS)
    {
        throw Win32Error("SetParent");
    }

    result.width = primaryBounds.right - primaryBounds.left;
    result.height = primaryBounds.bottom - primaryBounds.top;
    if (!SetWindowPos(
            search.window,
            HWND_TOP,
            primaryOrigin.x,
            primaryOrigin.y,
            result.width,
            result.height,
            SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOACTIVATE))
    {
        throw Win32Error("SetWindowPos");
    }

    result.attached = true;
    return result;
}
} // namespace interactive_wallpaper::desktop