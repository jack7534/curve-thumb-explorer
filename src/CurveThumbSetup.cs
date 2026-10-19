using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class CurveThumbSetup
{
    private const string Clsid = "{7D4E4693-E78B-4D59-AAC7-6B7B2B2B8F10}";
    private const string ThumbShell = "{E357FCCD-A995-4576-B01F-234630154E96}";
    private const string ProgId = "CurveThumb.ShellThumbnailProvider";
    private const string Version = "1.1.0.0";
    private static readonly string[] Extensions = { ".stl", ".obj", ".3mf", ".ply", ".fbx" };

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("Administrator approval is required to register the thumbnail provider for Windows Explorer.");
            bool uninstall = args.Length > 0 && string.Equals(args[0], "/uninstall", StringComparison.OrdinalIgnoreCase);
            if (uninstall) Uninstall(); else Install();
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            MessageBox.Show(uninstall ? "CurveThumb Explorer was removed." :
                "CurveThumb Explorer is installed. Reopen File Explorer and choose Large icons. Cloud-only files must be downloaded first.",
                "CurveThumb Explorer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "CurveThumb Explorer setup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static RegistryKey Machine { get { return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); } }
    private static RegistryKey User { get { return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64); } }
    private static string InstallDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CurveThumb Explorer"); } }

    private static void Install()
    {
        string directory = InstallDirectory;
        Directory.CreateDirectory(directory);
        string dll = Path.Combine(directory, "CurveThumb.Provider.dll");
        using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream("CurveThumb.Provider.dll"))
        {
            if (input == null) throw new InvalidOperationException("The bundled thumbnail component is missing.");
            using (FileStream output = new FileStream(dll, FileMode.Create, FileAccess.Write)) input.CopyTo(output);
        }
        string setup = Path.Combine(directory, "CurveThumb.Setup.exe");
        if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(setup), StringComparison.OrdinalIgnoreCase))
            File.Copy(Application.ExecutablePath, setup, true);

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
                key.SetValue("DisplayVersion", "1.1.0");
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
        string dll = Path.Combine(InstallDirectory, "CurveThumb.Provider.dll");
        if (File.Exists(dll)) File.Delete(dll);
    }
}
