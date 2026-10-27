// ImgConv entry for the Windows 11 Explorer context menu ("Convert to" → JPG / PNG / ...).
// Explorer loads this DLL through the ImgConv MSIX package (see AppxManifest.xml).
// It only builds the menu; the actual work is done by ImgConv.exe next to this DLL.
#include <windows.h>
#include <shobjidl_core.h>
#include <shlwapi.h>
#include <wrl/module.h>
#include <wrl/implements.h>
#include <wrl/client.h>
#include <string>
#include <vector>

using namespace Microsoft::WRL;

static HMODULE g_module;
static const wchar_t* kFormats[] = { L"jpg", L"png", L"bmp", L"gif", L"tiff" };

static std::wstring ExePath()
{
    wchar_t p[MAX_PATH];
    GetModuleFileNameW(g_module, p, MAX_PATH);
    PathRemoveFileSpecW(p);
    return std::wstring(p) + L"\\ImgConv.exe";
}

// Reads a DWORD flag from HKCU\Software\ImgConv (set from the app's settings).
static bool Flag(const wchar_t* name, DWORD def)
{
    DWORD v = def, size = sizeof(v);
    RegGetValueW(HKEY_CURRENT_USER, L"Software\\ImgConv", name, RRF_RT_REG_DWORD, nullptr, &v, &size);
    return v != 0;
}

static void Launch(std::wstring cmd)
{
    STARTUPINFOW si = { sizeof(si) };
    PROCESS_INFORMATION pi = {};
    if (CreateProcessW(nullptr, &cmd[0], nullptr, nullptr, FALSE, 0, nullptr, nullptr, &si, &pi))
    {
        AllowSetForegroundWindow(pi.dwProcessId); // let the size/quality dialog come to the front
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
    }
}

class SubCommand : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
{
    std::wstring fmt_;
public:
    HRESULT RuntimeClassInitialize(PCWSTR fmt) { fmt_ = fmt; return S_OK; }

    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* name) override
    {
        std::wstring t = fmt_;
        for (auto& c : t) c = towupper(c);
        if (Flag(L"Ask", 1)) t += L"…"; // a dialog follows
        return SHStrDupW(t.c_str(), name);
    }
    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override { *icon = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tip) override { *tip = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetCanonicalName(GUID* guid) override { *guid = GUID_NULL; return S_OK; }
    IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override { *state = ECS_ENABLED; return S_OK; }
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override { *flags = ECF_DEFAULT; return S_OK; }
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** e) override { *e = nullptr; return E_NOTIMPL; }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        if (!items) return S_OK;
        DWORD count = 0;
        items->GetCount(&count);
        const std::wstring base = L"\"" + ExePath() + L"\" /to " + fmt_;
        std::wstring cmd = base;
        for (DWORD i = 0; i < count; i++)
        {
            ComPtr<IShellItem> item;
            PWSTR path = nullptr;
            if (FAILED(items->GetItemAt(i, &item)) || FAILED(item->GetDisplayName(SIGDN_FILESYSPATH, &path))) continue;
            std::wstring arg = L" \"" + std::wstring(path) + L"\"";
            CoTaskMemFree(path);
            // Command lines max out at 32K chars; ImgConv.exe merges split batches into one dialog.
            if (cmd.size() + arg.size() > 30000) { Launch(cmd); cmd = base; }
            cmd += arg;
        }
        if (cmd.size() > base.size()) Launch(cmd);
        return S_OK;
    }
};

class SubCommands : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IEnumExplorerCommand>
{
    std::vector<ComPtr<IExplorerCommand>> cmds_;
    size_t pos_ = 0;
public:
    HRESULT RuntimeClassInitialize()
    {
        for (auto f : kFormats)
        {
            ComPtr<SubCommand> c;
            HRESULT hr = MakeAndInitialize<SubCommand>(&c, f);
            if (FAILED(hr)) return hr;
            cmds_.push_back(c);
        }
        return S_OK;
    }

    IFACEMETHODIMP Next(ULONG celt, IExplorerCommand** out, ULONG* fetched) override
    {
        ULONG n = 0;
        while (n < celt && pos_ < cmds_.size()) cmds_[pos_++].CopyTo(&out[n++]);
        if (fetched) *fetched = n;
        return n == celt ? S_OK : S_FALSE;
    }
    IFACEMETHODIMP Skip(ULONG celt) override { pos_ = min(pos_ + celt, cmds_.size()); return S_OK; }
    IFACEMETHODIMP Reset() override { pos_ = 0; return S_OK; }
    IFACEMETHODIMP Clone(IEnumExplorerCommand** e) override { *e = nullptr; return E_NOTIMPL; }
};

class __declspec(uuid("5C437DD0-633E-434E-B0BC-044A972C2B4D")) RootCommand
    : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
{
public:
    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* name) override { return SHStrDupW(L"Convert to", name); }
    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override { return SHStrDupW((ExePath() + L",0").c_str(), icon); }
    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tip) override { *tip = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetCanonicalName(GUID* guid) override { *guid = __uuidof(RootCommand); return S_OK; }
    IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override
    {
        // "Add Convert to to the Explorer menu" switch in the app's settings.
        *state = Flag(L"Menu", 1) ? ECS_ENABLED : ECS_HIDDEN;
        return S_OK;
    }
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override { *flags = ECF_HASSUBCOMMANDS; return S_OK; }
    IFACEMETHODIMP Invoke(IShellItemArray*, IBindCtx*) override { return E_NOTIMPL; }
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** e) override
    {
        *e = nullptr;
        ComPtr<SubCommands> list;
        HRESULT hr = MakeAndInitialize<SubCommands>(&list);
        return SUCCEEDED(hr) ? list.CopyTo(e) : hr;
    }
};

CoCreatableClass(RootCommand)

STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** ppv)
{
    return Module<InProc>::GetModule().GetClassObject(clsid, iid, ppv);
}

STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().Terminate() ? S_OK : S_FALSE;
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}
