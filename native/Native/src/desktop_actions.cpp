#include "desktop_actions_internal.h"
#include "desktop_snapshot_internal.h"

#include <windows.h>
#include <ole2.h>
#include <oaidl.h>
#include <ocidl.h>

#include <exdisp.h>
#include <servprov.h>
#include <shellapi.h>
#include <shlguid.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <wrl/client.h>

#include <cstdint>
#include <iomanip>
#include <memory>
#include <sstream>
#include <stdexcept>
#include <string>

using Microsoft::WRL::ComPtr;

namespace interactive_wallpaper::shell
{
namespace
{
class ComApartment final
{
public:
    ComApartment()
    {
        result_ = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
        if (FAILED(result_) && result_ != RPC_E_CHANGED_MODE)
        {
            throw std::runtime_error("CoInitializeEx failed: " + HResultMessage(result_));
        }
        uninitialize_ = SUCCEEDED(result_);
    }

    ~ComApartment()
    {
        if (uninitialize_)
        {
            CoUninitialize();
        }
    }

private:
    HRESULT result_ = E_FAIL;
    bool uninitialize_ = false;
};

template <typename Pointer>
class UniquePidl final
{
public:
    explicit UniquePidl(Pointer value = nullptr) noexcept : value_(value) {}
    ~UniquePidl() { CoTaskMemFree(value_); }
    UniquePidl(const UniquePidl&) = delete;
    UniquePidl& operator=(const UniquePidl&) = delete;
    Pointer get() const noexcept { return value_; }

private:
    Pointer value_ = nullptr;
};

std::string StableIdFromPidl(PCUIDLIST_ABSOLUTE pidl)
{
    const auto size = static_cast<std::size_t>(ILGetSize(pidl));
    const auto* bytes = reinterpret_cast<const std::uint8_t*>(pidl);
    std::uint64_t hash = 14695981039346656037ULL;
    for (std::size_t index = 0; index < size; ++index)
    {
        hash ^= bytes[index];
        hash *= 1099511628211ULL;
    }

    std::ostringstream stream;
    stream << "desktop-" << std::hex << std::setfill('0') << std::setw(16) << hash;
    return stream.str();
}

ComPtr<IFolderView> FindDesktopFolderView()
{
    ComPtr<IShellWindows> shellWindows;
    HRESULT result = CoCreateInstance(
        CLSID_ShellWindows,
        nullptr,
        CLSCTX_LOCAL_SERVER,
        IID_PPV_ARGS(&shellWindows));
    if (FAILED(result))
    {
        throw std::runtime_error("CoCreateInstance(CLSID_ShellWindows) failed: " + HResultMessage(result));
    }

    VARIANT location{};
    VariantInit(&location);
    location.vt = VT_I4;
    location.lVal = CSIDL_DESKTOP;
    VARIANT empty{};
    VariantInit(&empty);

    long windowHandle = 0;
    ComPtr<IDispatch> dispatch;
    result = shellWindows->FindWindowSW(
        &location,
        &empty,
        SWC_DESKTOP,
        &windowHandle,
        SWFO_NEEDDISPATCH,
        &dispatch);
    VariantClear(&location);
    VariantClear(&empty);
    if (FAILED(result) || !dispatch)
    {
        throw std::runtime_error("FindWindowSW(SWC_DESKTOP) failed: " + HResultMessage(result));
    }

    ComPtr<IServiceProvider> serviceProvider;
    if (FAILED(dispatch.As(&serviceProvider)))
    {
        throw std::runtime_error("desktop dispatch does not provide IServiceProvider");
    }
    ComPtr<IShellBrowser> browser;
    result = serviceProvider->QueryService(SID_STopLevelBrowser, IID_PPV_ARGS(&browser));
    if (FAILED(result))
    {
        throw std::runtime_error("QueryService(SID_STopLevelBrowser) failed: " + HResultMessage(result));
    }
    ComPtr<IShellView> shellView;
    result = browser->QueryActiveShellView(&shellView);
    if (FAILED(result))
    {
        throw std::runtime_error("QueryActiveShellView failed: " + HResultMessage(result));
    }
    ComPtr<IFolderView> folderView;
    result = shellView.As(&folderView);
    if (FAILED(result))
    {
        throw std::runtime_error("desktop view does not provide IFolderView: " + HResultMessage(result));
    }
    return folderView;
}
} // namespace

bool OpenDesktopItemByStableId(std::string_view stableId)
{
    if (stableId.size() != 24 || !stableId.starts_with("desktop-"))
    {
        return false;
    }
    for (std::size_t index = 8; index < stableId.size(); ++index)
    {
        const char character = stableId[index];
        if (!((character >= '0' && character <= '9') ||
              (character >= 'a' && character <= 'f')))
        {
            return false;
        }
    }

    ComApartment apartment;
    ComPtr<IFolderView> folderView = FindDesktopFolderView();
    ComPtr<IShellFolder> folder;
    HRESULT result = folderView->GetFolder(IID_PPV_ARGS(&folder));
    if (FAILED(result))
    {
        throw std::runtime_error("IFolderView::GetFolder failed: " + HResultMessage(result));
    }

    ComPtr<IPersistFolder2> persistFolder;
    result = folder.As(&persistFolder);
    if (FAILED(result))
    {
        throw std::runtime_error("desktop folder does not provide IPersistFolder2");
    }
    PIDLIST_ABSOLUTE rawParent = nullptr;
    result = persistFolder->GetCurFolder(&rawParent);
    if (FAILED(result) || rawParent == nullptr)
    {
        throw std::runtime_error("IPersistFolder2::GetCurFolder failed: " + HResultMessage(result));
    }
    UniquePidl<PIDLIST_ABSOLUTE> parent(rawParent);

    int itemCount = 0;
    result = folderView->ItemCount(SVGIO_ALLVIEW, &itemCount);
    if (FAILED(result))
    {
        throw std::runtime_error("IFolderView::ItemCount failed: " + HResultMessage(result));
    }

    for (int index = 0; index < itemCount; ++index)
    {
        PITEMID_CHILD rawChild = nullptr;
        if (FAILED(folderView->Item(index, &rawChild)) || rawChild == nullptr)
        {
            continue;
        }
        UniquePidl<PITEMID_CHILD> child(rawChild);
        ComPtr<IShellItem> item;
        if (FAILED(SHCreateItemWithParent(parent.get(), folder.Get(), child.get(), IID_PPV_ARGS(&item))))
        {
            continue;
        }

        PIDLIST_ABSOLUTE rawAbsolute = nullptr;
        if (FAILED(SHGetIDListFromObject(item.Get(), &rawAbsolute)) || rawAbsolute == nullptr)
        {
            continue;
        }
        UniquePidl<PIDLIST_ABSOLUTE> absolute(rawAbsolute);
        if (StableIdFromPidl(absolute.get()) != stableId)
        {
            continue;
        }

        SHELLEXECUTEINFOW execute{};
        execute.cbSize = sizeof(execute);
        execute.fMask = SEE_MASK_IDLIST | SEE_MASK_FLAG_NO_UI;
        execute.lpVerb = L"open";
        execute.lpIDList = absolute.get();
        execute.nShow = SW_SHOWNORMAL;
        if (!ShellExecuteExW(&execute))
        {
            throw std::runtime_error("ShellExecuteExW failed with Win32 error " +
                std::to_string(GetLastError()));
        }
        return true;
    }
    return false;
}
} // namespace interactive_wallpaper::shell
