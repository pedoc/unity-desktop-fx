#include "desktop_snapshot_internal.h"

#include <windows.h>
#include <ole2.h>
#include <oaidl.h>
#include <ocidl.h>

#include <commctrl.h>
#include <commoncontrols.h>
#include <exdisp.h>
#include <servprov.h>
#include <shlguid.h>
#include <shellapi.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <array>
#include <chrono>
#include <cstddef>
#include <filesystem>
#include <iomanip>
#include <memory>
#include <system_error>
#include <sstream>
#include <stdexcept>
#include <string_view>

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
        shouldUninitialize_ = SUCCEEDED(result_);
    }

    ~ComApartment()
    {
        if (shouldUninitialize_)
        {
            CoUninitialize();
        }
    }

    ComApartment(const ComApartment&) = delete;
    ComApartment& operator=(const ComApartment&) = delete;

private:
    HRESULT result_ = E_FAIL;
    bool shouldUninitialize_ = false;
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

struct CoTaskMemStringDeleter
{
    void operator()(wchar_t* value) const noexcept
    {
        CoTaskMemFree(value);
    }
};

using UniqueCoString = std::unique_ptr<wchar_t, CoTaskMemStringDeleter>;

std::string WideToUtf8(std::wstring_view value)
{
    if (value.empty())
    {
        return {};
    }

    const int required = WideCharToMultiByte(
        CP_UTF8,
        WC_ERR_INVALID_CHARS,
        value.data(),
        static_cast<int>(value.size()),
        nullptr,
        0,
        nullptr,
        nullptr);
    if (required <= 0)
    {
        throw std::runtime_error("WideCharToMultiByte size query failed");
    }

    std::string result(static_cast<std::size_t>(required), '\0');
    if (WideCharToMultiByte(
            CP_UTF8,
            WC_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            result.data(),
            required,
            nullptr,
            nullptr) <= 0)
    {
        throw std::runtime_error("WideCharToMultiByte conversion failed");
    }
    return result;
}

std::string CurrentUtcTimestamp()
{
    SYSTEMTIME time{};
    GetSystemTime(&time);

    std::ostringstream stream;
    stream << std::setfill('0')
           << std::setw(4) << time.wYear << '-'
           << std::setw(2) << time.wMonth << '-'
           << std::setw(2) << time.wDay << 'T'
           << std::setw(2) << time.wHour << ':'
           << std::setw(2) << time.wMinute << ':'
           << std::setw(2) << time.wSecond << '.'
           << std::setw(3) << time.wMilliseconds << 'Z';
    return stream.str();
}

std::string HexEncode(const void* data, std::size_t size)
{
    constexpr char digits[] = "0123456789abcdef";
    const auto* bytes = static_cast<const std::byte*>(data);
    std::string result(size * 2, '0');
    for (std::size_t index = 0; index < size; ++index)
    {
        const auto value = std::to_integer<unsigned int>(bytes[index]);
        result[index * 2] = digits[(value >> 4U) & 0x0FU];
        result[index * 2 + 1] = digits[value & 0x0FU];
    }
    return result;
}

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

std::string GetDisplayName(IShellItem* item, SIGDN kind)
{
    PWSTR rawName = nullptr;
    const HRESULT result = item->GetDisplayName(kind, &rawName);
    if (FAILED(result) || rawName == nullptr)
    {
        return {};
    }
    UniqueCoString name(rawName);
    return WideToUtf8(name.get());
}

ComPtr<IFolderView> FindDesktopFolderView(long& explorerWindowHandle)
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

    ComPtr<IDispatch> dispatch;
    result = shellWindows->FindWindowSW(
        &location,
        &empty,
        SWC_DESKTOP,
        &explorerWindowHandle,
        SWFO_NEEDDISPATCH,
        &dispatch);
    VariantClear(&location);
    VariantClear(&empty);
    if (FAILED(result) || !dispatch)
    {
        throw std::runtime_error("IShellWindows::FindWindowSW(SWC_DESKTOP) failed: " + HResultMessage(result));
    }

    ComPtr<IServiceProvider> serviceProvider;
    result = dispatch.As(&serviceProvider);
    if (FAILED(result))
    {
        throw std::runtime_error("Desktop dispatch does not provide IServiceProvider: " + HResultMessage(result));
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
        throw std::runtime_error("IShellBrowser::QueryActiveShellView failed: " + HResultMessage(result));
    }

    ComPtr<IFolderView> folderView;
    result = shellView.As(&folderView);
    if (FAILED(result))
    {
        throw std::runtime_error("Desktop shell view does not provide IFolderView: " + HResultMessage(result));
    }
    return folderView;
}

ComPtr<IWICImagingFactory> CreateWicFactory()
{
    ComPtr<IWICImagingFactory> factory;
    HRESULT result = CoCreateInstance(
        CLSID_WICImagingFactory2,
        nullptr,
        CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(&factory));
    if (FAILED(result))
    {
        result = CoCreateInstance(
            CLSID_WICImagingFactory,
            nullptr,
            CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(&factory));
    }
    if (FAILED(result))
    {
        throw std::runtime_error("Unable to create WIC imaging factory: " + HResultMessage(result));
    }
    return factory;
}

void SaveWicSourceAsPng(
    IWICImagingFactory* factory,
    IWICBitmapSource* originalSource,
    std::uint32_t requestedSize,
    const std::filesystem::path& path)
{
    UINT width = 0;
    UINT height = 0;
    HRESULT result = originalSource->GetSize(&width, &height);
    if (FAILED(result))
    {
        throw std::runtime_error("IWICBitmapSource::GetSize failed: " + HResultMessage(result));
    }

    ComPtr<IWICBitmapSource> source = originalSource;
    ComPtr<IWICBitmapScaler> scaler;
    if (width != requestedSize || height != requestedSize)
    {
        result = factory->CreateBitmapScaler(&scaler);
        if (FAILED(result))
        {
            throw std::runtime_error("IWICImagingFactory::CreateBitmapScaler failed: " + HResultMessage(result));
        }
        result = scaler->Initialize(
            originalSource,
            requestedSize,
            requestedSize,
            WICBitmapInterpolationModeFant);
        if (FAILED(result))
        {
            throw std::runtime_error("IWICBitmapScaler::Initialize failed: " + HResultMessage(result));
        }
        source = scaler;
    }

    ComPtr<IWICStream> stream;
    result = factory->CreateStream(&stream);
    if (FAILED(result))
    {
        throw std::runtime_error("IWICImagingFactory::CreateStream failed: " + HResultMessage(result));
    }

    result = stream->InitializeFromFilename(path.c_str(), GENERIC_WRITE);
    if (FAILED(result))
    {
        throw std::runtime_error("IWICStream::InitializeFromFilename failed: " + HResultMessage(result));
    }

    ComPtr<IWICBitmapEncoder> encoder;
    result = factory->CreateEncoder(GUID_ContainerFormatPng, nullptr, &encoder);
    if (FAILED(result))
    {
        throw std::runtime_error("IWICImagingFactory::CreateEncoder(PNG) failed: " + HResultMessage(result));
    }

    result = encoder->Initialize(stream.Get(), WICBitmapEncoderNoCache);
    if (FAILED(result))
    {
        throw std::runtime_error("IWICBitmapEncoder::Initialize failed: " + HResultMessage(result));
    }

    ComPtr<IWICBitmapFrameEncode> frame;
    ComPtr<IPropertyBag2> properties;
    result = encoder->CreateNewFrame(&frame, &properties);
    if (FAILED(result))
    {
        throw std::runtime_error("IWICBitmapEncoder::CreateNewFrame failed: " + HResultMessage(result));
    }

    result = frame->Initialize(properties.Get());
    if (FAILED(result))
    {
        throw std::runtime_error("IWICBitmapFrameEncode::Initialize failed: " + HResultMessage(result));
    }

    result = frame->SetSize(requestedSize, requestedSize);
    if (FAILED(result))
    {
        throw std::runtime_error("IWICBitmapFrameEncode::SetSize failed: " + HResultMessage(result));
    }

    WICPixelFormatGUID format = GUID_WICPixelFormat32bppBGRA;
    result = frame->SetPixelFormat(&format);
    if (FAILED(result))
    {
        throw std::runtime_error("IWICBitmapFrameEncode::SetPixelFormat failed: " + HResultMessage(result));
    }

    result = frame->WriteSource(source.Get(), nullptr);
    if (FAILED(result))
    {
        throw std::runtime_error("IWICBitmapFrameEncode::WriteSource failed: " + HResultMessage(result));
    }

    result = frame->Commit();
    if (FAILED(result))
    {
        throw std::runtime_error("IWICBitmapFrameEncode::Commit failed: " + HResultMessage(result));
    }

    result = encoder->Commit();
    if (FAILED(result))
    {
        throw std::runtime_error("IWICBitmapEncoder::Commit failed: " + HResultMessage(result));
    }
}

IconAssetSnapshot ExportSystemIcon(
    std::int32_t systemIconIndex,
    std::string_view stableId,
    std::uint32_t requestedSize,
    const std::filesystem::path& directory)
{
    const std::filesystem::path path = directory /
        (std::string(stableId) + "-" + std::to_string(requestedSize) + ".png");
    std::error_code cacheError;
    if (std::filesystem::exists(path, cacheError) && !cacheError &&
        std::filesystem::file_size(path, cacheError) > 0 && !cacheError)
    {
        const auto modified = std::filesystem::last_write_time(path, cacheError);
        if (!cacheError &&
            std::filesystem::file_time_type::clock::now() - modified < std::chrono::hours(12))
        {
            IconAssetSnapshot cached;
            cached.size = requestedSize;
            cached.path = WideToUtf8(path.wstring());
            return cached;
        }
    }

    int imageListKind = SHIL_JUMBO;
    if (requestedSize <= 16)
    {
        imageListKind = SHIL_SMALL;
    }
    else if (requestedSize <= 32)
    {
        imageListKind = SHIL_LARGE;
    }
    else if (requestedSize <= 48)
    {
        imageListKind = SHIL_EXTRALARGE;
    }

    ComPtr<IImageList> imageList;
    HRESULT result = SHGetImageList(imageListKind, IID_PPV_ARGS(&imageList));
    if (FAILED(result))
    {
        throw std::runtime_error("SHGetImageList failed: " + HResultMessage(result));
    }

    HICON icon = nullptr;
    result = imageList->GetIcon(systemIconIndex, ILD_TRANSPARENT, &icon);
    if (FAILED(result) || icon == nullptr)
    {
        throw std::runtime_error("IImageList::GetIcon failed: " + HResultMessage(result));
    }

    struct IconGuard
    {
        HICON value;
        ~IconGuard() { DestroyIcon(value); }
    } guard{icon};

    ComPtr<IWICImagingFactory> factory = CreateWicFactory();
    ComPtr<IWICBitmap> source;
    result = factory->CreateBitmapFromHICON(icon, &source);
    if (FAILED(result))
    {
        throw std::runtime_error("IWICImagingFactory::CreateBitmapFromHICON failed: " + HResultMessage(result));
    }

    SaveWicSourceAsPng(factory.Get(), source.Get(), requestedSize, path);

    IconAssetSnapshot asset;
    asset.size = requestedSize;
    asset.path = WideToUtf8(path.wstring());
    return asset;
}

DesktopItemSnapshot CaptureItem(
    IFolderView* folderView,
    IShellFolder* folder,
    PCIDLIST_ABSOLUTE parentPidl,
    int itemIndex,
    const DesktopSnapshotOptions& options,
    std::vector<SnapshotWarning>& warnings)
{
    PITEMID_CHILD rawChild = nullptr;
    HRESULT result = folderView->Item(itemIndex, &rawChild);
    if (FAILED(result) || rawChild == nullptr)
    {
        throw std::runtime_error("IFolderView::Item failed: " + HResultMessage(result));
    }
    UniquePidl<PITEMID_CHILD> child(rawChild);

    ComPtr<IShellItem> item;
    result = SHCreateItemWithParent(parentPidl, folder, child.get(), IID_PPV_ARGS(&item));
    if (FAILED(result))
    {
        throw std::runtime_error("SHCreateItemWithParent failed: " + HResultMessage(result));
    }

    PIDLIST_ABSOLUTE rawAbsolute = nullptr;
    result = SHGetIDListFromObject(item.Get(), &rawAbsolute);
    if (FAILED(result) || rawAbsolute == nullptr)
    {
        throw std::runtime_error("SHGetIDListFromObject failed: " + HResultMessage(result));
    }
    UniquePidl<PIDLIST_ABSOLUTE> absolute(rawAbsolute);

    POINT position{};
    result = folderView->GetItemPosition(child.get(), &position);
    if (FAILED(result))
    {
        throw std::runtime_error("IFolderView::GetItemPosition failed: " + HResultMessage(result));
    }

    SFGAOF requested = SFGAO_FILESYSTEM | SFGAO_FOLDER | SFGAO_LINK |
                       SFGAO_CANRENAME | SFGAO_CANDELETE;
    SFGAOF attributes = requested;
    if (FAILED(item->GetAttributes(requested, &attributes)))
    {
        attributes = 0;
    }

    SHFILEINFOW fileInfo{};
    const DWORD_PTR imageList = SHGetFileInfoW(
        reinterpret_cast<LPCWSTR>(absolute.get()),
        0,
        &fileInfo,
        sizeof(fileInfo),
        SHGFI_PIDL | SHGFI_SYSICONINDEX | SHGFI_LARGEICON | SHGFI_TYPENAME);

    DesktopItemSnapshot snapshot;
    snapshot.stableId = StableIdFromPidl(absolute.get());
    snapshot.displayName = GetDisplayName(item.Get(), SIGDN_NORMALDISPLAY);
    snapshot.parsingName = GetDisplayName(item.Get(), SIGDN_DESKTOPABSOLUTEPARSING);
    if (snapshot.parsingName.empty())
    {
        snapshot.parsingName = GetDisplayName(item.Get(), SIGDN_PARENTRELATIVEPARSING);
    }
    snapshot.pidlHex = HexEncode(absolute.get(), ILGetSize(absolute.get()));
    snapshot.x = position.x;
    snapshot.y = position.y;
    snapshot.attributes = static_cast<std::uint32_t>(attributes);
    snapshot.isFileSystem = (attributes & SFGAO_FILESYSTEM) != 0;
    snapshot.isFolder = (attributes & SFGAO_FOLDER) != 0;
    snapshot.isLink = (attributes & SFGAO_LINK) != 0;
    snapshot.canRename = (attributes & SFGAO_CANRENAME) != 0;
    snapshot.canDelete = (attributes & SFGAO_CANDELETE) != 0;
    if (imageList != 0)
    {
        snapshot.systemIconIndex = fileInfo.iIcon;
        snapshot.typeName = WideToUtf8(fileInfo.szTypeName);
    }

    for (const std::uint32_t iconSize : options.iconSizes)
    {
        try
        {
            if (snapshot.systemIconIndex < 0)
            {
                throw std::runtime_error("Shell item does not have a system image list index");
            }
            snapshot.icons.push_back(ExportSystemIcon(
                snapshot.systemIconIndex,
                snapshot.stableId,
                iconSize,
                options.iconDirectory));
        }
        catch (const std::exception& error)
        {
            SnapshotWarning warning;
            warning.itemIndex = itemIndex;
            warning.hresult = E_FAIL;
            warning.operation = "export-icon-" + std::to_string(iconSize) + ": " + error.what();
            warnings.push_back(std::move(warning));
        }
    }
    return snapshot;
}
} // namespace

DesktopSnapshot CaptureDesktopSnapshot(const DesktopSnapshotOptions& options)
{
    ComApartment apartment;

    DesktopSnapshot snapshot;
    snapshot.capturedAtUtc = CurrentUtcTimestamp();
    snapshot.virtualDesktopX = GetSystemMetrics(SM_XVIRTUALSCREEN);
    snapshot.virtualDesktopY = GetSystemMetrics(SM_YVIRTUALSCREEN);
    snapshot.virtualDesktopWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
    snapshot.virtualDesktopHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

    if (!options.iconSizes.empty())
    {
        if (options.iconDirectory.empty())
        {
            throw std::invalid_argument("iconDirectory is required when iconSizes are requested");
        }
        std::error_code directoryError;
        std::filesystem::create_directories(options.iconDirectory, directoryError);
        if (directoryError)
        {
            throw std::runtime_error("Unable to create icon directory: " + directoryError.message());
        }
    }

    long explorerWindowHandle = 0;
    ComPtr<IFolderView> folderView = FindDesktopFolderView(explorerWindowHandle);

    {
        std::ostringstream stream;
        stream << "0x" << std::hex << static_cast<std::uint64_t>(
            static_cast<std::uintptr_t>(explorerWindowHandle));
        snapshot.explorerWindowHandle = stream.str();
    }

    UINT viewMode = 0;
    if (SUCCEEDED(folderView->GetCurrentViewMode(&viewMode)))
    {
        snapshot.viewMode = static_cast<std::int32_t>(viewMode);
    }

    ComPtr<IFolderView2> folderView2;
    if (SUCCEEDED(folderView.As(&folderView2)))
    {
        FOLDERVIEWMODE detailedViewMode = FVM_AUTO;
        int iconSize = 0;
        if (SUCCEEDED(folderView2->GetViewModeAndIconSize(&detailedViewMode, &iconSize)))
        {
            snapshot.viewMode = static_cast<std::int32_t>(detailedViewMode);
            snapshot.iconSize = iconSize;
        }
    }

    POINT spacing{};
    if (SUCCEEDED(folderView->GetSpacing(&spacing)))
    {
        snapshot.spacingX = spacing.x;
        snapshot.spacingY = spacing.y;
    }

    snapshot.autoArrange = folderView->GetAutoArrange() == S_OK;

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
        throw std::runtime_error("Desktop folder does not provide IPersistFolder2: " + HResultMessage(result));
    }

    PIDLIST_ABSOLUTE rawParentPidl = nullptr;
    result = persistFolder->GetCurFolder(&rawParentPidl);
    if (FAILED(result) || rawParentPidl == nullptr)
    {
        throw std::runtime_error("IPersistFolder2::GetCurFolder failed: " + HResultMessage(result));
    }
    UniquePidl<PIDLIST_ABSOLUTE> parentPidl(rawParentPidl);

    int itemCount = 0;
    result = folderView->ItemCount(SVGIO_ALLVIEW, &itemCount);
    if (FAILED(result))
    {
        throw std::runtime_error("IFolderView::ItemCount failed: " + HResultMessage(result));
    }

    snapshot.items.reserve(static_cast<std::size_t>(itemCount));
    for (int index = 0; index < itemCount; ++index)
    {
        try
        {
            snapshot.items.push_back(CaptureItem(
                folderView.Get(),
                folder.Get(),
                parentPidl.get(),
                index,
                options,
                snapshot.warnings));
        }
        catch (const std::exception& error)
        {
            SnapshotWarning warning;
            warning.itemIndex = index;
            warning.hresult = E_FAIL;
            warning.operation = error.what();
            snapshot.warnings.push_back(std::move(warning));
        }
    }

    return snapshot;
}

std::string HResultMessage(long hresult)
{
    wchar_t* rawMessage = nullptr;
    const DWORD size = FormatMessageW(
        FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
        nullptr,
        static_cast<DWORD>(hresult),
        MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
        reinterpret_cast<LPWSTR>(&rawMessage),
        0,
        nullptr);

    std::string message;
    if (size > 0 && rawMessage != nullptr)
    {
        message = WideToUtf8(std::wstring_view(rawMessage, size));
        while (!message.empty() && (message.back() == '\r' || message.back() == '\n' || message.back() == ' '))
        {
            message.pop_back();
        }
        LocalFree(rawMessage);
    }

    std::ostringstream stream;
    stream << "0x" << std::hex << std::uppercase << static_cast<std::uint32_t>(hresult);
    if (!message.empty())
    {
        stream << " (" << message << ')';
    }
    return stream.str();
}
} // namespace interactive_wallpaper::shell
