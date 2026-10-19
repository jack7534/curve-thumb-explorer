using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class CurveThumbSetup
{
    private const string Clsid = "{7D4E4693-E78B-4D59-AAC7-6B7B2B2B8F10}";
    private const string ThumbShell = "{E357FCCD-A995-4576-B01F-234630154E96}";
    private const string ProgId = "CurveThumb.ShellThumbnailProvider";
    private const string Version = "1.2.0.0";
    private static readonly string[] Extensions = { ".stl", ".obj", ".3mf", ".ply", ".fbx" };

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existing, string replacement, int flags);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("Administrator approval is required to register the thumbnail provider for Windows Explorer.");
            bool uninstall = args.Length > 0 && string.Equals(args[0], "/uninstall", StringComparison.OrdinalIgnoreCase);
            bool refresh = args.Length > 0 && string.Equals(args[0], "/refresh-cache", StringComparison.OrdinalIgnoreCase);
            bool quiet = Array.Exists(args, arg => string.Equals(arg, "/quiet", StringComparison.OrdinalIgnoreCase));
            string activeDll = null;
            string activeSetup = null;
            if (uninstall) Uninstall();
            else if (!refresh) Install(out activeDll, out activeSetup);
            // Wait for the association notification before deleting the old
            // thumbnail databases. Windows will build new entries on demand.
            SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
            int removed = RefreshThumbnailCache();
            if (!uninstall && !refresh) CleanupOldInstallFiles(activeDll, activeSetup);
            if (!quiet)
                MessageBox.Show(uninstall ? "CurveThumb Explorer was removed." : refresh ?
                    "The Windows thumbnail cache was refreshed. Reopen your model folder." :
                    "CurveThumb Explorer is installed. The old thumbnail cache was refreshed (" + removed + " cache files). Reopen your model folder and choose Large icons. Cloud-only files must be downloaded first.",
                    "CurveThumb Explorer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "CurveThumb-setup-error.txt"), ex.ToString()); }
            catch { }
            MessageBox.Show(ex.Message, "CurveThumb Explorer setup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static RegistryKey Machine { get { return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); } }
    private static RegistryKey User { get { return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64); } }
    private static string InstallDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CurveThumb Explorer"); } }

    private static void Install(out string activeDll, out string activeSetup)
    {
        string directory = InstallDirectory;
        Directory.CreateDirectory(directory);
        byte[] bundled;
        using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream("CurveThumb.Provider.dll"))
        {
            if (input == null) throw new InvalidOperationException("The bundled thumbnail component is missing.");
            using (MemoryStream output = new MemoryStream()) { input.CopyTo(output); bundled = output.ToArray(); }
        }
        string suffix;
        using (SHA256 sha = SHA256.Create()) suffix = BitConverter.ToString(sha.ComputeHash(bundled)).Replace("-", "").Substring(0, 12);
        // A loaded Shell DLL cannot be overwritten. Each build gets a stable,
        // content-addressed path so upgrades do not depend on Explorer exiting.
        string dll = Path.Combine(directory, "CurveThumb.Provider." + suffix + ".dll");
        if (!File.Exists(dll)) File.WriteAllBytes(dll, bundled);
        string setup = Path.Combine(directory, "CurveThumb.Setup." + suffix + ".exe");
        if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(setup), StringComparison.OrdinalIgnoreCase))
        {
            try { File.Copy(Application.ExecutablePath, setup, true); }
            catch (IOException)
            {
                setup = Path.Combine(directory, "CurveThumb.Setup." + suffix + "." + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(Application.ExecutablePath, setup, false);
            }
        }

        using (RegistryKey machine = Machine)
        {
            string root = @"SOFTWARE\Classes\CLSID\" + Clsid;
            using (RegistryKey clsid = machine.CreateSubKey(root)) clsid.SetValue("", ProgId);
            // RegAsm stores literal spaces in CodeBase. Escaped spaces prevent
            // the .NET COM loader from locating the assembly.
            string codeBase = Uri.UnescapeDataString(new Uri(dll).AbsoluteUri);
            foreach (string keyName in new[] { root + @"\InprocServer32", root + @"\InprocServer32\" + Version })
            {
                using (RegistryKey key = machine.CreateSubKey(keyName))
                {
                    key.SetValue("", "mscoree.dll");
                    key.SetValue("ThreadingModel", "Both");
                    key.SetValue("Class", "CurveThumb.ShellThumbnailProvider");
                    key.SetValue("Assembly", "CurveThumb.Provider, Version=" + Version + ", Culture=neutral, PublicKeyToken=null");
                    key.SetValue("RuntimeVersion", "v4.0.30319");
                    key.SetValue("CodeBase", codeBase);
                }
            }
            using (RegistryKey key = machine.CreateSubKey(root + @"\ProgId")) key.SetValue("", ProgId);
            using (RegistryKey key = machine.CreateSubKey(root + @"\Implemented Categories\{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}")) { }
            using (RegistryKey key = machine.CreateSubKey(@"SOFTWARE\Classes\" + ProgId + @"\CLSID")) key.SetValue("", Clsid);

            foreach (string ext in Extensions)
            {
                string association = @"SOFTWARE\Classes\" + ext + @"\ShellEx\" + ThumbShell;
                string backup = @"SOFTWARE\CurveThumb\PreviousHandlers\" + ext.TrimStart('.');
                using (RegistryKey old = machine.OpenSubKey(association, false))
                using (RegistryKey saved = machine.OpenSubKey(backup, false))
                {
                    string previous = old == null ? null : old.GetValue("") as string;
                    if (saved == null && !string.IsNullOrEmpty(previous) && !string.Equals(previous, Clsid, StringComparison.OrdinalIgnoreCase))
                        using (RegistryKey write = machine.CreateSubKey(backup)) write.SetValue("PreviousCLSID", previous);
                }
                using (RegistryKey key = machine.CreateSubKey(association)) key.SetValue("", Clsid);
            }
            using (RegistryKey key = machine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\CurveThumbExplorer"))
            {
                key.SetValue("DisplayName", "CurveThumb Explorer");
                key.SetValue("DisplayVersion", "1.2.0");
                key.SetValue("Publisher", "CurveThumb Explorer");
                key.SetValue("InstallLocation", directory);
                key.SetValue("UninstallString", "\"" + setup + "\" /uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }
        using (RegistryKey user = User)
        {
            foreach (string ext in Extensions)
            {
                string association = @"SOFTWARE\Classes\" + ext + @"\ShellEx\" + ThumbShell;
                bool remove;
                using (RegistryKey key = user.OpenSubKey(association, false))
                    remove = key != null && string.Equals(key.GetValue("") as string, Clsid, StringComparison.OrdinalIgnoreCase);
                if (remove) user.DeleteSubKey(association, false);
            }
            user.DeleteSubKeyTree(@"SOFTWARE\Classes\CLSID\" + Clsid, false);
            user.DeleteSubKeyTree(@"SOFTWARE\Classes\" + ProgId, false);
        }
        activeDll = dll;
        activeSetup = setup;
    }

    private static int RefreshThumbnailCache()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string cacheDir = Path.GetFullPath(Path.Combine(local, @"Microsoft\Windows\Explorer"));
        // Never enumerate or remove files outside this exact Windows cache folder.
        if (!Directory.Exists(cacheDir)) return 0;
        int currentSession = Process.GetCurrentProcess().SessionId;
        bool stoppedExplorer = false;
        foreach (Process process in Process.GetProcessesByName("explorer"))
        {
            using (process)
            {
                if (process.SessionId != currentSession) continue;
                try { process.Kill(); process.WaitForExit(5000); stoppedExplorer = true; }
                catch { }
            }
        }
        int removed = 0;
        try
        {
            foreach (string file in Directory.GetFiles(cacheDir, "thumbcache_*.db", SearchOption.TopDirectoryOnly))
            {
                // A fixed prefix and extension exclude model files and user data.
                try
                {
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        try { File.Delete(file); removed++; break; }
                        catch (IOException) { Thread.Sleep(250); }
                    }
                }
                catch (UnauthorizedAccessException) { }
            }
        }
        finally
        {
            if (stoppedExplorer)
            {
                try { Process.Start("explorer.exe"); }
                catch { }
            }
        }
        return removed;
    }

    private static void CleanupOldInstallFiles(string activeDll, string activeSetup)
    {
        if (!Directory.Exists(InstallDirectory)) return;
        foreach (string pattern in new[] { "CurveThumb.Provider*.dll", "CurveThumb.Setup*.exe" })
        foreach (string file in Directory.GetFiles(InstallDirectory, pattern, SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(file, activeDll, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(file, activeSetup, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(file, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(file); }
            catch (IOException) { MoveFileEx(file, null, 0x4); }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void Uninstall()
    {
        using (RegistryKey machine = Machine)
        {
            foreach (string ext in Extensions)
            {
                string association = @"SOFTWARE\Classes\" + ext + @"\ShellEx\" + ThumbShell;
                string backup = @"SOFTWARE\CurveThumb\PreviousHandlers\" + ext.TrimStart('.');
                bool remove = false;
                using (RegistryKey key = machine.OpenSubKey(association, true))
                {
                    if (key != null && string.Equals(key.GetValue("") as string, Clsid, StringComparison.OrdinalIgnoreCase))
                    {
                        using (RegistryKey saved = machine.OpenSubKey(backup, false))
                        {
                            string previous = saved == null ? null : saved.GetValue("PreviousCLSID") as string;
                            if (string.IsNullOrEmpty(previous)) remove = true;
                            else key.SetValue("", previous);
                        }
                    }
                }
                if (remove) machine.DeleteSubKey(association, false);
            }
            machine.DeleteSubKeyTree(@"SOFTWARE\Classes\CLSID\" + Clsid, false);
            machine.DeleteSubKeyTree(@"SOFTWARE\Classes\" + ProgId, false);
            machine.DeleteSubKeyTree(@"SOFTWARE\CurveThumb\PreviousHandlers", false);
            machine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\CurveThumbExplorer", false);
        }
        // Shell may still hold a provider loaded before uninstallation. The
        // registration is gone immediately; locked binaries can be reclaimed
        // after Explorer releases them.
        foreach (string dll in Directory.GetFiles(InstallDirectory, "CurveThumb.Provider.*.dll"))
            try { File.Delete(dll); } catch (IOException) { }
    }
}

