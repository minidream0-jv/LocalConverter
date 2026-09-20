#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shobjidl.h>
#include <shellapi.h>
#include <iostream>
#include <string>

const CLSID CLSID_LocalConverterCommand =
{ 0xe58a8821, 0x9315, 0x4e15, { 0xa8, 0x47, 0x84, 0x6d, 0x68, 0x96, 0x3c, 0x42 } };

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2)
    {
        std::wcerr << L"Usage: ShellInvokeTest.exe <image-path>\n";
        return 2;
    }

    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(hr)) return 3;

    IShellItem* item = nullptr;
    IShellItemArray* items = nullptr;
    IExplorerCommand* root = nullptr;
    IEnumExplorerCommand* children = nullptr;
    IExplorerCommand* command = nullptr;
    IExplorerCommand* webpCommand = nullptr;
    int result = 1;
    bool sawWebpEnabled = false;
    bool sawPngHidden = false;
    bool sawVideoHidden = false;
    bool sawCustomEnabled = false;

    hr = SHCreateItemFromParsingName(argv[1], nullptr, IID_PPV_ARGS(&item));
    if (SUCCEEDED(hr)) hr = SHCreateShellItemArrayFromShellItem(item, IID_PPV_ARGS(&items));
    if (SUCCEEDED(hr)) hr = CoCreateInstance(CLSID_LocalConverterCommand, nullptr, CLSCTX_ALL, IID_PPV_ARGS(&root));
    if (SUCCEEDED(hr))
    {
        PWSTR rootTitle = nullptr;
        EXPCMDSTATE rootState = ECS_DISABLED;
        EXPCMDFLAGS rootFlags = ECF_DEFAULT;
        root->GetTitle(items, &rootTitle);
        root->GetState(items, TRUE, &rootState);
        root->GetFlags(&rootFlags);
        const bool rootValid = rootTitle && _wcsicmp(rootTitle, L"Конвертировать") == 0 &&
            rootState == ECS_ENABLED && (rootFlags & ECF_HASSUBCOMMANDS) != 0;
        CoTaskMemFree(rootTitle);
        if (!rootValid) hr = E_FAIL;
    }
    if (SUCCEEDED(hr)) hr = root->EnumSubCommands(&children);

    while (SUCCEEDED(hr) && children->Next(1, &command, nullptr) == S_OK)
    {
        PWSTR title = nullptr;
        EXPCMDSTATE state = ECS_DISABLED;
        command->GetTitle(items, &title);
        command->GetState(items, TRUE, &state);
        const bool isWebp = title && _wcsicmp(title, L"WEBP") == 0;
        if (title && _wcsicmp(title, L"PNG") == 0 && state == ECS_HIDDEN) sawPngHidden = true;
        if (title && _wcsicmp(title, L"MP4") == 0 && state == ECS_HIDDEN) sawVideoHidden = true;
        if (title && _wcsicmp(title, L"Другой формат…") == 0 && state == ECS_ENABLED) sawCustomEnabled = true;
        if (isWebp && state == ECS_ENABLED) sawWebpEnabled = true;
        CoTaskMemFree(title);
        if (isWebp && state == ECS_ENABLED)
        {
            webpCommand = command;
            command = nullptr;
            continue;
        }
        command->Release();
        command = nullptr;
    }

    if (webpCommand && sawWebpEnabled && sawPngHidden && sawVideoHidden && sawCustomEnabled)
    {
        hr = webpCommand->Invoke(items, nullptr);
        if (SUCCEEDED(hr)) result = 0;
    }

    if (command) command->Release();
    if (webpCommand) webpCommand->Release();
    if (children) children->Release();
    if (root) root->Release();
    if (items) items->Release();
    if (item) item->Release();
    CoUninitialize();
    return result;
}
