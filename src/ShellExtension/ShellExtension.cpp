#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <shellapi.h>
#include <objbase.h>
#include <atomic>
#include <array>
#include <string>
#include <vector>
#include <algorithm>

// Must match packaging/AppxManifest.xml.
const CLSID CLSID_LocalConverterCommand =
{ 0xe58a8821, 0x9315, 0x4e15, { 0xa8, 0x47, 0x84, 0x6d, 0x68, 0x96, 0x3c, 0x42 } };

static HMODULE g_module = nullptr;
static std::atomic<long> g_objects{0};

enum class MediaKind { Unknown, Image, Video, Audio };

struct CommandDefinition
{
    const wchar_t* title;
    const wchar_t* target;
    MediaKind kind;
    bool custom;
};

static constexpr std::array<CommandDefinition, 17> Commands{{
    {L"JPG", L"jpg", MediaKind::Image, false},
    {L"PNG", L"png", MediaKind::Image, false},
    {L"WEBP", L"webp", MediaKind::Image, false},
    {L"AVIF", L"avif", MediaKind::Image, false},
    {L"BMP", L"bmp", MediaKind::Image, false},
    {L"TIFF", L"tiff", MediaKind::Image, false},
    {L"MP4", L"mp4", MediaKind::Video, false},
    {L"MKV", L"mkv", MediaKind::Video, false},
    {L"MOV", L"mov", MediaKind::Video, false},
    {L"WEBM", L"webm", MediaKind::Video, false},
    {L"MP3 — извлечь аудио", L"mp3", MediaKind::Video, false},
    {L"MP3", L"mp3", MediaKind::Audio, false},
    {L"WAV", L"wav", MediaKind::Audio, false},
    {L"FLAC", L"flac", MediaKind::Audio, false},
    {L"AAC", L"aac", MediaKind::Audio, false},
    {L"OGG", L"ogg", MediaKind::Audio, false},
    {L"Другой формат…", L"custom", MediaKind::Unknown, true}
}};

struct SelectionInfo
{
    bool valid = false;
    MediaKind kind = MediaKind::Unknown;
    std::vector<std::wstring> extensions;
};

static std::wstring ToLower(std::wstring value)
{
    std::transform(value.begin(), value.end(), value.begin(), towlower);
    return value;
}

static std::wstring NormalizeExtension(std::wstring extension)
{
    extension = ToLower(extension);
    if (!extension.empty() && extension.front() == L'.') extension.erase(extension.begin());
    if (extension == L"jpeg") return L"jpg";
    if (extension == L"tif") return L"tiff";
    if (extension == L"m4a") return L"aac";
    if (extension == L"opus") return L"ogg";
    return extension;
}

static MediaKind DetectKind(const std::wstring& extension)
{
    const auto value = NormalizeExtension(extension);
    if (value == L"png" || value == L"jpg" || value == L"webp" || value == L"avif" || value == L"bmp" || value == L"tiff") return MediaKind::Image;
    if (value == L"mp4" || value == L"mkv" || value == L"mov" || value == L"webm" || value == L"avi") return MediaKind::Video;
    if (value == L"mp3" || value == L"wav" || value == L"flac" || value == L"aac" || value == L"ogg") return MediaKind::Audio;
    return MediaKind::Unknown;
}

static SelectionInfo AnalyzeSelection(IShellItemArray* items)
{
    SelectionInfo result;
    if (!items) return result;

    DWORD count = 0;
    if (FAILED(items->GetCount(&count)) || count == 0) return result;

    result.extensions.reserve(count);
    for (DWORD index = 0; index < count; ++index)
    {
        IShellItem* item = nullptr;
        if (FAILED(items->GetItemAt(index, &item)) || !item) return {};

        PWSTR rawPath = nullptr;
        const HRESULT pathResult = item->GetDisplayName(SIGDN_FILESYSPATH, &rawPath);
        item->Release();
        if (FAILED(pathResult) || !rawPath) return {};

        const std::wstring path(rawPath);
        CoTaskMemFree(rawPath);
        const auto dot = path.find_last_of(L'.');
        const auto slash = path.find_last_of(L"\\/");
        if (dot == std::wstring::npos || (slash != std::wstring::npos && dot < slash)) return {};

        const auto extension = NormalizeExtension(path.substr(dot + 1));
        const auto kind = DetectKind(extension);
        if (kind == MediaKind::Unknown) return {};
        if (result.kind != MediaKind::Unknown && result.kind != kind) return {};
        result.kind = kind;
        result.extensions.push_back(extension);
    }

    result.valid = true;
    return result;
}

static HRESULT DuplicateString(const wchar_t* source, PWSTR* value)
{
    if (!value) return E_POINTER;
    *value = nullptr;
    const size_t bytes = (wcslen(source) + 1) * sizeof(wchar_t);
    auto copy = static_cast<PWSTR>(CoTaskMemAlloc(bytes));
    if (!copy) return E_OUTOFMEMORY;
    memcpy(copy, source, bytes);
    *value = copy;
    return S_OK;
}

static std::wstring JsonEscape(const std::wstring& value)
{
    std::wstring escaped;
    escaped.reserve(value.size() + 8);
    for (const auto ch : value)
    {
        switch (ch)
        {
            case L'\\': escaped += L"\\\\"; break;
            case L'\"': escaped += L"\\\""; break;
            case L'\b': escaped += L"\\b"; break;
            case L'\f': escaped += L"\\f"; break;
            case L'\n': escaped += L"\\n"; break;
            case L'\r': escaped += L"\\r"; break;
            case L'\t': escaped += L"\\t"; break;
            default:
                if (ch < 0x20)
                {
                    wchar_t buffer[7]{};
                    swprintf(buffer, 7, L"\\u%04x", static_cast<unsigned>(ch));
                    escaped += buffer;
                }
                else
                {
                    escaped += ch;
                }
        }
    }
    return escaped;
}

static bool ToUtf8(const std::wstring& value, std::string& result)
{
    const int size = WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (size <= 0) return false;
    result.resize(size);
    return WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), size, nullptr, nullptr) == size;
}

static HRESULT GetSelectedPaths(IShellItemArray* items, std::vector<std::wstring>& paths)
{
    DWORD count = 0;
    HRESULT hr = items ? items->GetCount(&count) : E_POINTER;
    if (FAILED(hr) || count == 0) return FAILED(hr) ? hr : E_INVALIDARG;
    paths.reserve(count);
    for (DWORD index = 0; index < count; ++index)
    {
        IShellItem* item = nullptr;
        hr = items->GetItemAt(index, &item);
        if (FAILED(hr)) return hr;
        PWSTR rawPath = nullptr;
        hr = item->GetDisplayName(SIGDN_FILESYSPATH, &rawPath);
        item->Release();
        if (FAILED(hr)) return hr;
        paths.emplace_back(rawPath);
        CoTaskMemFree(rawPath);
    }
    return S_OK;
}

static HRESULT LaunchWorker(IShellItemArray* items, const wchar_t* target, bool custom)
{
    std::vector<std::wstring> paths;
    HRESULT hr = GetSelectedPaths(items, paths);
    if (FAILED(hr)) return hr;

    wchar_t tempDirectory[MAX_PATH + 1]{};
    if (!GetTempPathW(MAX_PATH, tempDirectory)) return HRESULT_FROM_WIN32(GetLastError());
    GUID jobId{};
    if (FAILED(CoCreateGuid(&jobId))) return E_FAIL;
    wchar_t guidText[40]{};
    StringFromGUID2(jobId, guidText, 40);
    std::wstring jobPath = std::wstring(tempDirectory) + L"LocalConverter-" + guidText + L".json";

    std::wstring json = L"{\"targetFormat\":\"" + JsonEscape(target) + L"\",\"files\":[";
    for (size_t index = 0; index < paths.size(); ++index)
    {
        if (index) json += L",";
        json += L"\"" + JsonEscape(paths[index]) + L"\"";
    }
    json += L"]}";

    std::string utf8;
    if (!ToUtf8(json, utf8)) return E_FAIL;
    HANDLE file = CreateFileW(jobPath.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_TEMPORARY, nullptr);
    if (file == INVALID_HANDLE_VALUE) return HRESULT_FROM_WIN32(GetLastError());
    DWORD written = 0;
    const BOOL wrote = WriteFile(file, utf8.data(), static_cast<DWORD>(utf8.size()), &written, nullptr);
    CloseHandle(file);
    if (!wrote || written != utf8.size())
    {
        DeleteFileW(jobPath.c_str());
        return HRESULT_FROM_WIN32(GetLastError());
    }

    std::wstring modulePath(32768, L'\0');
    const DWORD moduleLength = GetModuleFileNameW(g_module, modulePath.data(), static_cast<DWORD>(modulePath.size()));
    if (!moduleLength || moduleLength == modulePath.size())
    {
        DeleteFileW(jobPath.c_str());
        return HRESULT_FROM_WIN32(GetLastError());
    }
    modulePath.resize(moduleLength);
    const auto separator = modulePath.find_last_of(L"\\/");
    const std::wstring executable = modulePath.substr(0, separator + 1) + L"Converter.App.exe";
    std::wstring commandLine = L"\"" + executable + L"\" --job \"" + jobPath + L"\"";
    if (custom) commandLine += L" --custom";

    STARTUPINFOW startup{sizeof(startup)};
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(executable.c_str(), commandLine.data(), nullptr, nullptr, FALSE,
        CREATE_NO_WINDOW | DETACHED_PROCESS, nullptr, nullptr, &startup, &process))
    {
        const DWORD error = GetLastError();
        DeleteFileW(jobPath.c_str());
        return HRESULT_FROM_WIN32(error);
    }
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return S_OK;
}

class ExplorerCommand final : public IExplorerCommand
{
public:
    explicit ExplorerCommand(const CommandDefinition* definition = nullptr) : definition_(definition)
    {
        ++g_objects;
    }

    ~ExplorerCommand() { --g_objects; }

    IFACEMETHODIMP QueryInterface(REFIID iid, void** object) override
    {
        if (!object) return E_POINTER;
        *object = nullptr;
        if (IsEqualIID(iid, IID_IUnknown) || IsEqualIID(iid, IID_IExplorerCommand))
        {
            *object = static_cast<IExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override { return static_cast<ULONG>(InterlockedIncrement(&references_)); }
    IFACEMETHODIMP_(ULONG) Release() override
    {
        const auto value = static_cast<ULONG>(InterlockedDecrement(&references_));
        if (!value) delete this;
        return value;
    }

    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* title) override
    {
        return DuplicateString(definition_ ? definition_->title : L"Конвертировать", title);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override
    {
        if (icon) *icon = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tooltip) override
    {
        if (tooltip) *tooltip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(GUID* canonicalName) override
    {
        if (!canonicalName) return E_POINTER;
        *canonicalName = definition_ ? GUID_NULL : CLSID_LocalConverterCommand;
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
    {
        if (!state) return E_POINTER;
        const auto selection = AnalyzeSelection(items);
        if (!selection.valid)
        {
            *state = ECS_HIDDEN;
            return S_OK;
        }

        if (!definition_ || definition_->custom)
        {
            *state = ECS_ENABLED;
            return S_OK;
        }

        if (definition_->kind != selection.kind)
        {
            *state = ECS_HIDDEN;
            return S_OK;
        }

        const std::wstring target(definition_->target);
        const bool sameAsAnySource = std::any_of(selection.extensions.begin(), selection.extensions.end(),
            [&target](const std::wstring& value) { return value == target; });
        *state = sameAsAnySource ? ECS_HIDDEN : ECS_ENABLED;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        if (!definition_) return E_NOTIMPL;
        return LaunchWorker(items, definition_->target, definition_->custom);
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
    {
        if (!flags) return E_POINTER;
        *flags = definition_ ? ECF_DEFAULT : ECF_HASSUBCOMMANDS;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override;

private:
    long references_ = 1;
    const CommandDefinition* definition_;
};

class CommandEnumerator final : public IEnumExplorerCommand
{
public:
    explicit CommandEnumerator(ULONG index = 0) : index_(index) { ++g_objects; }
    ~CommandEnumerator() { --g_objects; }

    IFACEMETHODIMP QueryInterface(REFIID iid, void** object) override
    {
        if (!object) return E_POINTER;
        *object = nullptr;
        if (IsEqualIID(iid, IID_IUnknown) || IsEqualIID(iid, IID_IEnumExplorerCommand))
        {
            *object = static_cast<IEnumExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override { return static_cast<ULONG>(InterlockedIncrement(&references_)); }
    IFACEMETHODIMP_(ULONG) Release() override
    {
        const auto value = static_cast<ULONG>(InterlockedDecrement(&references_));
        if (!value) delete this;
        return value;
    }

    IFACEMETHODIMP Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) override
    {
        if (!commands || (count > 1 && !fetched)) return E_POINTER;
        ULONG produced = 0;
        while (produced < count && index_ < Commands.size())
        {
            auto command = new (std::nothrow) ExplorerCommand(&Commands[index_++]);
            if (!command) return E_OUTOFMEMORY;
            commands[produced++] = command;
        }
        if (fetched) *fetched = produced;
        return produced == count ? S_OK : S_FALSE;
    }

    IFACEMETHODIMP Skip(ULONG count) override
    {
        index_ = static_cast<ULONG>(std::min<size_t>(Commands.size(), index_ + count));
        return index_ < Commands.size() ? S_OK : S_FALSE;
    }

    IFACEMETHODIMP Reset() override { index_ = 0; return S_OK; }

    IFACEMETHODIMP Clone(IEnumExplorerCommand** result) override
    {
        if (!result) return E_POINTER;
        *result = new (std::nothrow) CommandEnumerator(index_);
        return *result ? S_OK : E_OUTOFMEMORY;
    }

private:
    long references_ = 1;
    ULONG index_ = 0;
};

HRESULT ExplorerCommand::EnumSubCommands(IEnumExplorerCommand** commands)
{
    if (!commands) return E_POINTER;
    *commands = nullptr;
    if (definition_) return E_NOTIMPL;
    *commands = new (std::nothrow) CommandEnumerator();
    return *commands ? S_OK : E_OUTOFMEMORY;
}

class CommandFactory final : public IClassFactory
{
public:
    CommandFactory() { ++g_objects; }
    ~CommandFactory() { --g_objects; }

    IFACEMETHODIMP QueryInterface(REFIID iid, void** object) override
    {
        if (!object) return E_POINTER;
        *object = nullptr;
        if (IsEqualIID(iid, IID_IUnknown) || IsEqualIID(iid, IID_IClassFactory))
        {
            *object = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override { return static_cast<ULONG>(InterlockedIncrement(&references_)); }
    IFACEMETHODIMP_(ULONG) Release() override
    {
        const auto value = static_cast<ULONG>(InterlockedDecrement(&references_));
        if (!value) delete this;
        return value;
    }

    IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID iid, void** object) override
    {
        if (outer) return CLASS_E_NOAGGREGATION;
        auto command = new (std::nothrow) ExplorerCommand();
        if (!command) return E_OUTOFMEMORY;
        const HRESULT hr = command->QueryInterface(iid, object);
        command->Release();
        return hr;
    }

    IFACEMETHODIMP LockServer(BOOL lock) override
    {
        if (lock) ++g_objects; else --g_objects;
        return S_OK;
    }

private:
    long references_ = 1;
};

extern "C" __declspec(dllexport) HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID iid, void** object)
{
    if (!IsEqualCLSID(clsid, CLSID_LocalConverterCommand)) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = new (std::nothrow) CommandFactory();
    if (!factory) return E_OUTOFMEMORY;
    const HRESULT hr = factory->QueryInterface(iid, object);
    factory->Release();
    return hr;
}

extern "C" __declspec(dllexport) HRESULT __stdcall DllCanUnloadNow()
{
    return g_objects.load() == 0 ? S_OK : S_FALSE;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = instance;
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}
