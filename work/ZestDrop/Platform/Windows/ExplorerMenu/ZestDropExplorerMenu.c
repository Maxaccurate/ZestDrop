// "Process with ZestDrop" in the top-level Windows 11 File Explorer context menu: a tiny in-process COM server
// (IClassFactory + IExplorerCommand) that Explorer loads through the ZestDrop sparse package.
// Runs inside explorer.exe without CRT dependencies (-nostdlib, kernel32/user32/ole32/shell32/shlwapi only).

#define WIN32_LEAN_AND_MEAN
#define COBJMACROS
#define CINTERFACE
#include <windows.h>
#include <shobjidl.h>
#include <shlobj.h>
#include <shlwapi.h>

// {E839185B-FDC2-4DE6-8E3A-95E052DE1CF4}; must match ExplorerMenuPackage.ClassId
static const CLSID kClsidCommand = {0xe839185b, 0xfdc2, 0x4de6, {0x8e, 0x3a, 0x95, 0xe0, 0x52, 0xde, 0x1c, 0xf4}};
static const IID kIidUnknown = {0x00000000, 0x0000, 0x0000, {0xc0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46}};
static const IID kIidClassFactory = {0x00000001, 0x0000, 0x0000, {0xc0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46}};
static const IID kIidExplorerCommand = {0xa08ce4d0, 0xfa25, 0x44ab, {0xb5, 0x7c, 0xc7, 0xb1, 0xc3, 0x23, 0xe0, 0xb9}};
static const KNOWNFOLDERID kFolderRoamingAppData = {0x3eb685db, 0x65f9, 0x4cf6, {0xa0, 0x3a, 0xe3, 0xef, 0x65, 0x72, 0x9f, 0x3d}};

#define DEFAULT_TITLE L"用 ZestDrop 处理…"
#define TITLE_FILE L"ZestDrop\\explorer-menu.txt"
#define APP_EXE L"ZestDrop.exe"
#define MAX_COMMAND_LINE 32000

static HMODULE g_module;
static volatile LONG g_locks;

static BOOL SameGuid(const GUID *a, const GUID *b)
{
    const BYTE *x = (const BYTE *)a;
    const BYTE *y = (const BYTE *)b;
    for (int i = 0; i < (int)sizeof(GUID); i++)
    {
        if (x[i] != y[i])
            return FALSE;
    }
    return TRUE;
}

typedef struct Text
{
    WCHAR *data;
    SIZE_T length;
    SIZE_T capacity;
    BOOL failed;
} Text;

static void TextAppendN(Text *text, const WCHAR *value, SIZE_T count)
{
    if (text->failed)
        return;
    if (text->length + count + 1 > text->capacity)
    {
        SIZE_T capacity = text->capacity ? text->capacity : 1024;
        while (text->length + count + 1 > capacity)
            capacity *= 2;
        WCHAR *grown = text->data
                           ? (WCHAR *)HeapReAlloc(GetProcessHeap(), 0, text->data, capacity * sizeof(WCHAR))
                           : (WCHAR *)HeapAlloc(GetProcessHeap(), 0, capacity * sizeof(WCHAR));
        if (!grown)
        {
            text->failed = TRUE;
            return;
        }
        text->data = grown;
        text->capacity = capacity;
    }
    for (SIZE_T i = 0; i < count; i++)
        text->data[text->length + i] = value[i];
    text->length += count;
    text->data[text->length] = 0;
}

static void TextAppend(Text *text, const WCHAR *value)
{
    TextAppendN(text, value, (SIZE_T)lstrlenW(value));
}

static void TextFree(Text *text)
{
    if (text->data)
        HeapFree(GetProcessHeap(), 0, text->data);
    text->data = NULL;
    text->length = text->capacity = 0;
}

static BOOL PathNextToModule(const WCHAR *file, WCHAR *buffer, DWORD bufferSize)
{
    DWORD length = GetModuleFileNameW(g_module, buffer, bufferSize);
    if (length == 0 || length >= bufferSize)
        return FALSE;
    PathRemoveFileSpecW(buffer);
    return PathAppendW(buffer, file);
}

static BOOL ReadTitle(WCHAR *buffer, DWORD bufferSize)
{
    PWSTR appData = NULL;
    if (FAILED(SHGetKnownFolderPath(&kFolderRoamingAppData, KF_FLAG_DEFAULT, NULL, &appData)) || !appData)
        return FALSE;

    WCHAR path[MAX_PATH];
    lstrcpynW(path, appData, ARRAYSIZE(path));
    CoTaskMemFree(appData);
    if (!PathAppendW(path, TITLE_FILE))
        return FALSE;

    HANDLE file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                              NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE)
        return FALSE;

    char utf8[256];
    DWORD read = 0;
    BOOL ok = ReadFile(file, utf8, sizeof(utf8) - 1, &read, NULL);
    CloseHandle(file);
    if (!ok || read == 0)
        return FALSE;
    utf8[read] = 0;

    for (DWORD i = 0; i < read; i++)
    {
        if (utf8[i] == '\r' || utf8[i] == '\n')
        {
            utf8[i] = 0;
            break;
        }
    }

    int chars = MultiByteToWideChar(CP_UTF8, 0, utf8, -1, buffer, (int)bufferSize);
    return chars > 1;
}

static HRESULT StartZestDrop(const WCHAR *exe, WCHAR *commandLine)
{
    STARTUPINFOW si = {sizeof(si)};
    PROCESS_INFORMATION pi = {0};
    if (!CreateProcessW(exe, commandLine, NULL, NULL, FALSE, 0, NULL, NULL, &si, &pi))
        return HRESULT_FROM_WIN32(GetLastError());
    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
    return S_OK;
}

static HRESULT STDMETHODCALLTYPE Command_QueryInterface(IExplorerCommand *self, REFIID riid, void **result)
{
    if (!result) return E_POINTER;
    if (SameGuid(riid, &kIidUnknown) || SameGuid(riid, &kIidExplorerCommand))
    {
        *result = self;
        IExplorerCommand_AddRef(self);
        return S_OK;
    }
    *result = NULL;
    return E_NOINTERFACE;
}

static ULONG STDMETHODCALLTYPE Command_AddRef(IExplorerCommand *self)
{
    (void)self;
    return (ULONG)InterlockedIncrement(&g_locks);
}

static ULONG STDMETHODCALLTYPE Command_Release(IExplorerCommand *self)
{
    (void)self;
    return (ULONG)InterlockedDecrement(&g_locks);
}

static HRESULT STDMETHODCALLTYPE Command_GetTitle(IExplorerCommand *self, IShellItemArray *items, LPWSTR *name)
{
    (void)self;
    (void)items;
    if (!name) return E_POINTER;
    WCHAR title[128];
    const WCHAR *use = ReadTitle(title, ARRAYSIZE(title)) ? title : DEFAULT_TITLE;
    SIZE_T bytes = (lstrlenW(use) + 1) * sizeof(WCHAR);
    *name = (LPWSTR)CoTaskMemAlloc(bytes);
    if (!*name) return E_OUTOFMEMORY;
    CopyMemory(*name, use, bytes);
    return S_OK;
}

static HRESULT STDMETHODCALLTYPE Command_GetIcon(IExplorerCommand *self, IShellItemArray *items, LPWSTR *icon)
{
    (void)self;
    (void)items;
    if (!icon) return E_POINTER;
    WCHAR path[MAX_PATH];
    if (!PathNextToModule(APP_EXE, path, ARRAYSIZE(path)))
        return E_FAIL;
    SIZE_T bytes = (lstrlenW(path) + 1) * sizeof(WCHAR);
    *icon = (LPWSTR)CoTaskMemAlloc(bytes);
    if (!*icon) return E_OUTOFMEMORY;
    CopyMemory(*icon, path, bytes);
    return S_OK;
}

static HRESULT STDMETHODCALLTYPE Command_GetToolTip(IExplorerCommand *self, IShellItemArray *items, LPWSTR *infoTip)
{
    (void)self;
    (void)items;
    if (infoTip) *infoTip = NULL;
    return E_NOTIMPL;
}

static HRESULT STDMETHODCALLTYPE Command_GetCanonicalName(IExplorerCommand *self, GUID *guidCommandName)
{
    (void)self;
    if (!guidCommandName) return E_POINTER;
    *guidCommandName = kClsidCommand;
    return S_OK;
}

static HRESULT STDMETHODCALLTYPE Command_GetState(IExplorerCommand *self, IShellItemArray *items, BOOL okToBeSlow, EXPCMDSTATE *state)
{
    (void)self;
    (void)okToBeSlow;
    if (!state) return E_POINTER;
    *state = ECS_HIDDEN;
    WCHAR title[128];
    if (!ReadTitle(title, ARRAYSIZE(title)))
        return S_OK;
    const SFGAOF wanted = SFGAO_FILESYSTEM;
    SFGAOF attributes = 0;
    if (items && SUCCEEDED(IShellItemArray_GetAttributes(items, SIATTRIBFLAGS_AND, wanted, &attributes)) && (attributes & wanted) == wanted)
        *state = ECS_ENABLED;
    return S_OK;
}

static HRESULT STDMETHODCALLTYPE Command_Invoke(IExplorerCommand *self, IShellItemArray *items, IBindCtx *context)
{
    (void)self;
    (void)context;
    if (!items) return E_INVALIDARG;
    WCHAR exe[MAX_PATH];
    if (!PathNextToModule(APP_EXE, exe, ARRAYSIZE(exe)))
        return E_FAIL;

    DWORD count = 0;
    HRESULT hr = IShellItemArray_GetCount(items, &count);
    if (FAILED(hr)) return hr;

    Text args = {0};
    TextAppend(&args, L"\"");
    TextAppend(&args, exe);
    TextAppend(&args, L"\" --open");
    DWORD added = 0;

    for (DWORD i = 0; i < count; i++)
    {
        IShellItem *item = NULL;
        if (FAILED(IShellItemArray_GetItemAt(items, i, &item)))
            continue;
        LPWSTR path = NULL;
        if (SUCCEEDED(IShellItem_GetDisplayName(item, SIGDN_FILESYSPATH, &path)) && path)
        {
            TextAppend(&args, L" \"");
            TextAppend(&args, path);
            TextAppend(&args, L"\"");
            added++;
            CoTaskMemFree(path);
        }
        IShellItem_Release(item);
    }

    if (args.failed)
        hr = E_OUTOFMEMORY;
    else if (added > 0 && args.length < MAX_COMMAND_LINE)
        hr = StartZestDrop(exe, args.data);
    else
        hr = S_FALSE;

    TextFree(&args);
    return hr;
}

static HRESULT STDMETHODCALLTYPE Command_GetFlags(IExplorerCommand *self, EXPCMDFLAGS *flags)
{
    (void)self;
    if (!flags) return E_POINTER;
    *flags = ECF_DEFAULT;
    return S_OK;
}

static HRESULT STDMETHODCALLTYPE Command_EnumSubCommands(IExplorerCommand *self, IEnumExplorerCommand **commands)
{
    (void)self;
    if (commands) *commands = NULL;
    return E_NOTIMPL;
}

static IExplorerCommandVtbl g_commandVtbl = {
    Command_QueryInterface,
    Command_AddRef,
    Command_Release,
    Command_GetTitle,
    Command_GetIcon,
    Command_GetToolTip,
    Command_GetCanonicalName,
    Command_GetState,
    Command_Invoke,
    Command_GetFlags,
    Command_EnumSubCommands,
};

static IExplorerCommand g_command = {&g_commandVtbl};

static HRESULT STDMETHODCALLTYPE Factory_QueryInterface(IClassFactory *self, REFIID riid, void **result)
{
    if (!result) return E_POINTER;
    if (SameGuid(riid, &kIidUnknown) || SameGuid(riid, &kIidClassFactory))
    {
        *result = self;
        IClassFactory_AddRef(self);
        return S_OK;
    }
    *result = NULL;
    return E_NOINTERFACE;
}

static ULONG STDMETHODCALLTYPE Factory_AddRef(IClassFactory *self)
{
    (void)self;
    return (ULONG)InterlockedIncrement(&g_locks);
}

static ULONG STDMETHODCALLTYPE Factory_Release(IClassFactory *self)
{
    (void)self;
    return (ULONG)InterlockedDecrement(&g_locks);
}

static HRESULT STDMETHODCALLTYPE Factory_CreateInstance(IClassFactory *self, IUnknown *outer, REFIID riid, void **result)
{
    (void)self;
    if (!result) return E_POINTER;
    *result = NULL;
    if (outer) return CLASS_E_NOAGGREGATION;
    return IExplorerCommand_QueryInterface(&g_command, riid, result);
}

static HRESULT STDMETHODCALLTYPE Factory_LockServer(IClassFactory *self, BOOL lock)
{
    (void)self;
    if (lock)
        InterlockedIncrement(&g_locks);
    else
        InterlockedDecrement(&g_locks);
    return S_OK;
}

static IClassFactoryVtbl g_factoryVtbl = {
    Factory_QueryInterface,
    Factory_AddRef,
    Factory_Release,
    Factory_CreateInstance,
    Factory_LockServer,
};

static IClassFactory g_factory = {&g_factoryVtbl};

__declspec(dllexport) HRESULT STDMETHODCALLTYPE DllGetClassObject(REFCLSID rclsid, REFIID riid, void **result)
{
    if (!result) return E_POINTER;
    *result = NULL;
    if (!SameGuid(rclsid, &kClsidCommand))
        return CLASS_E_CLASSNOTAVAILABLE;
    return IClassFactory_QueryInterface(&g_factory, riid, result);
}

__declspec(dllexport) HRESULT STDMETHODCALLTYPE DllCanUnloadNow(void)
{
    return g_locks == 0 ? S_OK : S_FALSE;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void *reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = instance;
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}
