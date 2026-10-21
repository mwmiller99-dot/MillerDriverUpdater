// Win32 pieces: hidden pseudo-console (for winget's live progress), process trees, Authenticode, window helpers,
// and the COM callback object Windows Update's BeginDownload/BeginInstall require.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Mdu
{
    // Runs a command attached to a hidden pseudo-console (ConPTY) and collects everything it prints.
    // winget only draws its progress bar when it sees a console, so this is how we get live download numbers.
    public class PtyProcess : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] public struct COORD { public short X; public short Y; }
        [StructLayout(LayoutKind.Sequential)] public struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public int bInheritHandle; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb; public IntPtr lpReserved; public IntPtr lpDesktop; public IntPtr lpTitle;
            public int dwX; public int dwY; public int dwXSize; public int dwYSize; public int dwXCountChars; public int dwYCountChars; public int dwFillAttribute; public int dwFlags;
            public short wShowWindow; public short cbReserved2; public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError;
        }
        [StructLayout(LayoutKind.Sequential)] public struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }
        [StructLayout(LayoutKind.Sequential)] public struct PROCESS_INFORMATION { public IntPtr hProcess; public IntPtr hThread; public int dwProcessId; public int dwThreadId; }

        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, int nSize);
        [DllImport("kernel32.dll", SetLastError = true)] static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);
        [DllImport("kernel32.dll", SetLastError = true)] static extern void ClosePseudoConsole(IntPtr hPC);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr Attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);
        [DllImport("kernel32.dll", SetLastError = true)] static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool CreateProcessW(string lpApplicationName, StringBuilder lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);
        [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr hObject);

        const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        static readonly IntPtr PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = (IntPtr)0x00020016;

        IntPtr _pty = IntPtr.Zero;
        IntPtr _attr = IntPtr.Zero;
        IntPtr _process = IntPtr.Zero;
        IntPtr _thread = IntPtr.Zero;
        SafeFileHandle _inWrite;
        SafeFileHandle _outRead;
        Thread _reader;
        readonly StringBuilder _buffer = new StringBuilder();
        readonly object _lock = new object();
        bool _eof;
        int _exitCode = -1;

        public static PtyProcess Start(string commandLine, short cols, short rows)
        {
            PtyProcess p = new PtyProcess();
            p.Launch(commandLine, cols, rows);
            return p;
        }

        void Launch(string commandLine, short cols, short rows)
        {
            SECURITY_ATTRIBUTES sa = new SECURITY_ATTRIBUTES();
            sa.nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES));
            SafeFileHandle inRead; SafeFileHandle outWrite;
            if (!CreatePipe(out inRead, out _inWrite, ref sa, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe(in)");
            if (!CreatePipe(out _outRead, out outWrite, ref sa, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe(out)");
            COORD size; size.X = cols; size.Y = rows;
            int hr = CreatePseudoConsole(size, inRead, outWrite, 0, out _pty);
            if (hr != 0) throw new Win32Exception(hr, "CreatePseudoConsole");
            inRead.Dispose(); outWrite.Dispose();   // the pseudo-console keeps its own references

            IntPtr attrSize = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
            _attr = Marshal.AllocHGlobal(attrSize);
            if (!InitializeProcThreadAttributeList(_attr, 1, 0, ref attrSize)) throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList");
            if (!UpdateProcThreadAttribute(_attr, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, _pty, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute");

            STARTUPINFOEX six = new STARTUPINFOEX();
            six.StartupInfo.cb = Marshal.SizeOf(typeof(STARTUPINFOEX));
            // STARTF_USESTDHANDLES with null handles: the child must take its handles from the pseudo-console, not from
            // whatever console or pipes this process has (without it, a child of a console process prints to that console).
            six.StartupInfo.dwFlags = 0x00000100;
            six.lpAttributeList = _attr;
            PROCESS_INFORMATION pi;
            StringBuilder cmd = new StringBuilder(commandLine);
            if (!CreateProcessW(null, cmd, IntPtr.Zero, IntPtr.Zero, false, EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT, IntPtr.Zero, null, ref six, out pi))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess");
            _process = pi.hProcess; _thread = pi.hThread; ProcessId = pi.dwProcessId;

            _reader = new Thread(ReadLoop);
            _reader.IsBackground = true;
            _reader.Start();
        }

        void ReadLoop()
        {
            try
            {
                using (FileStream fs = new FileStream(_outRead, FileAccess.Read, 4096, false))
                {
                    byte[] buf = new byte[4096];
                    Decoder dec = new UTF8Encoding(false).GetDecoder();
                    char[] chars = new char[8192];
                    while (true)
                    {
                        int n = fs.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        int c = dec.GetChars(buf, 0, n, chars, 0);
                        lock (_lock) { _buffer.Append(chars, 0, c); }
                    }
                }
            }
            catch (Exception) { }
            lock (_lock) { _eof = true; }
        }

        public int ProcessId { get; private set; }
        public bool HasExited { get { return _process == IntPtr.Zero || WaitForSingleObject(_process, 0) == 0; } }
        public bool OutputClosed { get { lock (_lock) { return _eof; } } }
        public int ExitCode { get { uint code; if (_process != IntPtr.Zero && GetExitCodeProcess(_process, out code)) { _exitCode = (int)code; } return _exitCode; } }
        public void Kill() { if (_process != IntPtr.Zero) TerminateProcess(_process, 1); }

        public string ReadNew()
        {
            lock (_lock)
            {
                if (_buffer.Length == 0) return string.Empty;
                string s = _buffer.ToString();
                _buffer.Length = 0;
                return s;
            }
        }

        public void Dispose()
        {
            int unused = ExitCode;
            if (_pty != IntPtr.Zero) { ClosePseudoConsole(_pty); _pty = IntPtr.Zero; }   // reader sees EOF once the child is gone
            if (_reader != null) { _reader.Join(5000); _reader = null; }
            if (_attr != IntPtr.Zero) { DeleteProcThreadAttributeList(_attr); Marshal.FreeHGlobal(_attr); _attr = IntPtr.Zero; }
            if (_thread != IntPtr.Zero) { CloseHandle(_thread); _thread = IntPtr.Zero; }
            if (_process != IntPtr.Zero) { CloseHandle(_process); _process = IntPtr.Zero; }
            if (_inWrite != null) { _inWrite.Dispose(); _inWrite = null; }
            if (_outRead != null) { _outRead.Dispose(); _outRead = null; }
        }
    }

    // Reads a redirected stdout on its own thread so a caller can poll it (the pipe fallback for winget).
    public class PipeReader
    {
        readonly StringBuilder _buffer = new StringBuilder();
        readonly object _lock = new object();
        readonly Thread _thread;
        bool _eof;

        public PipeReader(StreamReader reader)
        {
            _thread = new Thread(() =>
            {
                try
                {
                    char[] buf = new char[4096];
                    int n;
                    while ((n = reader.Read(buf, 0, buf.Length)) > 0) { lock (_lock) _buffer.Append(buf, 0, n); }
                }
                catch { }
                lock (_lock) _eof = true;
            });
            _thread.IsBackground = true;
            _thread.Start();
        }

        public bool Eof { get { lock (_lock) return _eof && _buffer.Length == 0; } }
        public void Join(int ms) { _thread.Join(ms); }

        public string ReadNew()
        {
            lock (_lock)
            {
                if (_buffer.Length == 0) return string.Empty;
                string s = _buffer.ToString();
                _buffer.Length = 0;
                return s;
            }
        }
    }

    public static class ProcessTree
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PROCESSENTRY32
        {
            public uint dwSize; public uint cntUsage; public uint th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID;
            public uint cntThreads; public uint th32ParentProcessID; public int pcPriClassBase; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        // Process ids under a parent, recursively. A Toolhelp snapshot: milliseconds, unlike a WMI query.
        public static List<int> Descendants(int rootPid)
        {
            List<KeyValuePair<int, int>> all = new List<KeyValuePair<int, int>>();
            IntPtr snap = CreateToolhelp32Snapshot(2 /* TH32CS_SNAPPROCESS */, 0);
            List<int> found = new List<int>();
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return found;
            try
            {
                PROCESSENTRY32 e = new PROCESSENTRY32();
                e.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
                if (Process32FirstW(snap, ref e))
                {
                    do { all.Add(new KeyValuePair<int, int>((int)e.th32ProcessID, (int)e.th32ParentProcessID)); }
                    while (Process32NextW(snap, ref e));
                }
            }
            finally { CloseHandle(snap); }
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(rootPid);
            while (queue.Count > 0)
            {
                int parent = queue.Dequeue();
                foreach (KeyValuePair<int, int> kv in all)
                {
                    if (kv.Value != parent || kv.Key == parent || kv.Key == rootPid || found.Contains(kv.Key)) continue;
                    found.Add(kv.Key);
                    queue.Enqueue(kv.Key);
                }
            }
            return found;
        }

        // True when any of these processes has a top-level window: an installer that ignored --silent and is waiting.
        public static bool AnyHasWindow(IEnumerable<int> pids)
        {
            foreach (int id in pids)
            {
                try { using (Process p = Process.GetProcessById(id)) { if (p.MainWindowHandle != IntPtr.Zero) return true; } }
                catch { }
            }
            return false;
        }

        // Ends a process and everything it started (winget + the installer it launched).
        public static void Kill(int pid)
        {
            List<int> tree = Descendants(pid);
            try { Util.Run(Util.SystemTool("taskkill.exe"), "/PID " + pid + " /T /F", 15000); } catch { }
            tree.Add(pid);
            foreach (int id in tree)
            {
                try { using (Process p = Process.GetProcessById(id)) { p.Kill(); } } catch { }
            }
        }
    }

    // WinVerifyTrust: the check behind Get-AuthenticodeSignature's "Valid".
    public static class Authenticode
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO { public uint cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }
        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public uint cbStruct; public IntPtr pPolicyCallbackData; public IntPtr pSIPClientData; public uint dwUIChoice; public uint fdwRevocationChecks;
            public uint dwUnionChoice; public IntPtr pFile; public uint dwStateAction; public IntPtr hWVTStateData; public IntPtr pwszURLReference;
            public uint dwProvFlags; public uint dwUIContext; public IntPtr pSignatureSettings;
        }
        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)] static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

        public static bool Verify(string path, out string subject, out int code)
        {
            subject = null;
            code = -1;
            WINTRUST_FILE_INFO fi = new WINTRUST_FILE_INFO();
            fi.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
            fi.pcwszFilePath = path;
            IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            Marshal.StructureToPtr(fi, pFile, false);
            WINTRUST_DATA data = new WINTRUST_DATA();
            data.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
            data.dwUIChoice = 2;          // WTD_UI_NONE
            data.fdwRevocationChecks = 0; // WTD_REVOKE_NONE
            data.dwUnionChoice = 1;       // WTD_CHOICE_FILE
            data.pFile = pFile;
            data.dwStateAction = 1;       // WTD_STATEACTION_VERIFY
            Guid action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");   // WINTRUST_ACTION_GENERIC_VERIFY_V2
            try { code = WinVerifyTrust(IntPtr.Zero, ref action, ref data); }
            finally
            {
                data.dwStateAction = 2;   // WTD_STATEACTION_CLOSE
                try { WinVerifyTrust(IntPtr.Zero, ref action, ref data); } catch { }
                Marshal.DestroyStructure(pFile, typeof(WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(pFile);
            }
            try { subject = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)).Subject; } catch { }
            return code == 0;
        }
    }

    // Device properties Windows keeps but WMI does not show, read through SetupAPI.
    public static class DeviceProps
    {
        [StructLayout(LayoutKind.Sequential)] struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] struct DEVPROPKEY { public Guid fmtid; public uint pid; }
        [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr classGuid, IntPtr hwndParent);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiOpenDeviceInfoW(IntPtr set, string instanceId, IntPtr hwnd, uint flags, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out uint type, byte[] buffer, uint size, out uint required, uint flags);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        // The product name the device itself reports over USB (e.g. "CORSAIR K70 RGB PRO Mechanical Gaming Keyboard"),
        // where Windows' own name is just "USB Composite Device". Null when there is none.
        public static string BusReportedName(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            IntPtr set = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return null;
            try
            {
                SP_DEVINFO_DATA d = new SP_DEVINFO_DATA();
                d.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                if (!SetupDiOpenDeviceInfoW(set, instanceId, IntPtr.Zero, 0, ref d)) return null;
                DEVPROPKEY key = new DEVPROPKEY();
                key.fmtid = new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2");   // DEVPKEY_Device_BusReportedDeviceDesc
                key.pid = 4;
                uint type, required;
                byte[] buf = new byte[1024];
                if (!SetupDiGetDevicePropertyW(set, ref d, ref key, out type, buf, (uint)buf.Length, out required, 0)) return null;
                if (type != 0x12 /* DEVPROP_TYPE_STRING */ || required < 2) return null;
                string s = Encoding.Unicode.GetString(buf, 0, (int)Math.Min(required, (uint)buf.Length)).TrimEnd('\0').Trim();
                return s.Length > 0 ? s : null;
            }
            catch { return null; }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }
    }

    public static class NativeUi
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool FlashWindow(IntPtr hwnd, bool invert);

        public static void DarkTitleBar(IntPtr hwnd, bool dark)
        {
            try
            {
                int v = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref v, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref v, 4);   // DWMWA_USE_IMMERSIVE_DARK_MODE (19 before 20H1)
            }
            catch { }
        }

        // Brings an already-open window to the front (second launch of the app).
        public static bool ActivateExisting(string title)
        {
            try
            {
                IntPtr h = FindWindow(null, title);
                if (h == IntPtr.Zero) return false;
                if (IsIconic(h)) ShowWindow(h, 9 /* SW_RESTORE */);
                SetForegroundWindow(h);
                return true;
            }
            catch { return false; }
        }

        public static void Flash(IntPtr hwnd) { try { FlashWindow(hwnd, true); } catch { } }
    }

    // ---- Windows Update callbacks. WUA wants these on BeginDownload/BeginInstall; the IIDs are the fixed ones from wuapi.idl
    // (verified against HKCR\Interface on Windows 11). WUA also accepts "an Automation object with a dispatch identifier of 0",
    // so the class exposes Invoke as its default member too.
    [ComImport, ComVisible(true), Guid("8C3F1CDD-6173-4591-AEBD-A56A53CA77C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDownloadProgressChangedCallback { void Invoke([MarshalAs(UnmanagedType.IUnknown)] object job, [MarshalAs(UnmanagedType.IUnknown)] object args); }
    [ComImport, ComVisible(true), Guid("77254866-9F5B-4C8E-B9E2-C77A8530D64B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDownloadCompletedCallback { void Invoke([MarshalAs(UnmanagedType.IUnknown)] object job, [MarshalAs(UnmanagedType.IUnknown)] object args); }
    [ComImport, ComVisible(true), Guid("E01402D5-F8DA-43BA-A012-38894BD048F1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInstallationProgressChangedCallback { void Invoke([MarshalAs(UnmanagedType.IUnknown)] object job, [MarshalAs(UnmanagedType.IUnknown)] object args); }
    [ComImport, ComVisible(true), Guid("45F4F6F3-D602-4F98-9A8A-3EFA152AD2D3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInstallationCompletedCallback { void Invoke([MarshalAs(UnmanagedType.IUnknown)] object job, [MarshalAs(UnmanagedType.IUnknown)] object args); }

    [ComVisible(true), ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class WuaSink : IDownloadProgressChangedCallback, IDownloadCompletedCallback, IInstallationProgressChangedCallback, IInstallationCompletedCallback
    {
        public int Calls;
        [DispId(0)] public void Invoke(object job, object args) { Interlocked.Increment(ref Calls); }
        void IDownloadProgressChangedCallback.Invoke(object job, object args) { Interlocked.Increment(ref Calls); }
        void IDownloadCompletedCallback.Invoke(object job, object args) { Interlocked.Increment(ref Calls); }
        void IInstallationProgressChangedCallback.Invoke(object job, object args) { Interlocked.Increment(ref Calls); }
        void IInstallationCompletedCallback.Invoke(object job, object args) { Interlocked.Increment(ref Calls); }
    }
}
