using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace vView.Core
{
    /// <summary>
    /// vView as the default app for its file types (Options > General > Default app; the installer runs --register-types).
    ///
    /// Windows doesn't let a program make itself the default (the UserChoice keys carry a hash only Windows writes), so this does what it
    /// allows: registers vView for every type it opens (three file types vView.Picture / vView.Video / vView.Sound, "Open with", and
    /// Capabilities + RegisteredApplications so it's listed in Settings > Default apps), then opens Settings on vView's own page, where
    /// "Set default" (or a type at a time) finishes it. Everything is under HKCU, pointing at the running vView.exe.
    ///
    /// VVIEW_TEST_REGROOT (tests only): a key under HKCU (e.g. Software\vViewTest) used instead of Software, so tests never touch the
    /// real file types.
    /// </summary>
    public static class DefaultApp
    {
        public const string AppName = "vView";
        static readonly string[] ProgIds = { "vView.Picture", "vView.Video", "vView.Sound" };

        static string Software
        {
            get
            {
                string t = Environment.GetEnvironmentVariable("VVIEW_TEST_REGROOT");
                return string.IsNullOrWhiteSpace(t) ? "Software" : t.Trim('\\');
            }
        }

        static string Classes { get { return Software + "\\Classes"; } }

        static string ProgIdFor(MediaKind k) { return k == MediaKind.Picture ? ProgIds[0] : k == MediaKind.Video ? ProgIds[1] : ProgIds[2]; }

        static string TypeName(MediaKind k) { return k == MediaKind.Picture ? "vView picture" : k == MediaKind.Video ? "vView video" : "vView sound"; }

        /// <summary>Register vView (the running exe) for every type it opens. Safe to run again (it follows the exe if it moved).</summary>
        public static void Register()
        {
            string exe = Environment.ProcessPath;
            string open = "\"" + exe + "\" \"%1\"", icon = "\"" + exe + "\",0";
            try
            {
                RegistryKey cu = Registry.CurrentUser;
                foreach (MediaKind k in new[] { MediaKind.Picture, MediaKind.Video, MediaKind.Audio })
                {
                    string id = ProgIdFor(k);
                    using (RegistryKey p = cu.CreateSubKey(Classes + "\\" + id))
                    {
                        p.SetValue("", TypeName(k));
                        p.SetValue("FriendlyTypeName", TypeName(k));
                    }
                    using (RegistryKey p = cu.CreateSubKey(Classes + "\\" + id + "\\DefaultIcon")) p.SetValue("", icon);
                    using (RegistryKey p = cu.CreateSubKey(Classes + "\\" + id + "\\shell\\open\\command")) p.SetValue("", open);
                }
                using (RegistryKey a = cu.CreateSubKey(Classes + "\\Applications\\vView.exe"))
                    a.SetValue("FriendlyAppName", AppName);
                using (RegistryKey a = cu.CreateSubKey(Classes + "\\Applications\\vView.exe\\shell\\open\\command")) a.SetValue("", open);
                using (RegistryKey cap = cu.CreateSubKey(Software + "\\vView\\Capabilities"))
                {
                    cap.SetValue("ApplicationName", AppName);
                    cap.SetValue("ApplicationDescription", "Pictures, videos and sounds in your vShell theme");
                    cap.SetValue("ApplicationIcon", icon);
                }
                using (RegistryKey fa = cu.CreateSubKey(Software + "\\vView\\Capabilities\\FileAssociations"))
                using (RegistryKey st = cu.CreateSubKey(Classes + "\\Applications\\vView.exe\\SupportedTypes"))
                    foreach (KeyValuePair<string, MediaKind> t in Media.AllTypes)
                    {
                        fa.SetValue(t.Key, ProgIdFor(t.Value));
                        st.SetValue(t.Key, "");
                        using (RegistryKey ow = cu.CreateSubKey(Classes + "\\" + t.Key + "\\OpenWithProgids"))
                        {
                            ow.SetValue(ProgIdFor(t.Value), new byte[0], RegistryValueKind.None);
                            if (ow.GetValue("vView.File") != null) ow.DeleteValue("vView.File", false);   // 0.1.1's installer used one type
                        }
                    }
                using (RegistryKey ra = cu.CreateSubKey(Software + "\\RegisteredApplications"))
                    ra.SetValue(AppName, Software + "\\vView\\Capabilities");
                cu.DeleteSubKeyTree(Classes + "\\vView.File", false);
                Changed();
                Log.Write("Default app: registered " + Media.AllTypes.Count() + " types for " + exe);
            }
            catch (Exception ex) { Log.Write("Default app: register failed: " + ex.Message); throw; }
        }

        /// <summary>Take vView's registration away again (the uninstaller). Types it was the default for go back to asking.</summary>
        public static void Unregister()
        {
            try
            {
                RegistryKey cu = Registry.CurrentUser;
                foreach (string id in ProgIds.Concat(new[] { "vView.File" })) cu.DeleteSubKeyTree(Classes + "\\" + id, false);
                cu.DeleteSubKeyTree(Classes + "\\Applications\\vView.exe", false);
                cu.DeleteSubKeyTree(Software + "\\vView\\Capabilities", false);
                using (RegistryKey v = cu.OpenSubKey(Software + "\\vView"))
                    if (v != null && v.SubKeyCount == 0 && v.ValueCount == 0) { v.Close(); cu.DeleteSubKey(Software + "\\vView", false); }
                using (RegistryKey ra = cu.OpenSubKey(Software + "\\RegisteredApplications", true)) if (ra != null) ra.DeleteValue(AppName, false);
                foreach (KeyValuePair<string, MediaKind> t in Media.AllTypes)
                    using (RegistryKey ow = cu.OpenSubKey(Classes + "\\" + t.Key + "\\OpenWithProgids", true))
                        if (ow != null)
                            foreach (string id in ProgIds.Concat(new[] { "vView.File" })) ow.DeleteValue(id, false);
                Changed();
                Log.Write("Default app: unregistered");
            }
            catch (Exception ex) { Log.Write("Default app: unregister failed: " + ex.Message); }
        }

        /// <summary>Is vView registered, pointing at this exe?</summary>
        public static bool IsRegistered
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Classes + "\\" + ProgIds[0] + "\\shell\\open\\command"))
                    {
                        string cmd = k == null ? null : k.GetValue("") as string;
                        return cmd != null && cmd.IndexOf(Environment.ProcessPath ?? "?", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                }
                catch { return false; }
            }
        }

        /// <summary>How many of vView's types Windows opens with vView now (the user's choice), out of how many.</summary>
        public static int CountDefault(out int total)
        {
            var types = Media.AllTypes.ToList();
            total = types.Count;
            int n = 0;
            foreach (KeyValuePair<string, MediaKind> t in types)
            {
                string id = UserChoice(t.Key);
                if (id != null && (id.StartsWith("vView.", StringComparison.OrdinalIgnoreCase) || id.Equals("Applications\\vView.exe", StringComparison.OrdinalIgnoreCase))) n++;
            }
            return n;
        }

        static string UserChoice(string ext)
        {
            string baseKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + ext;
            foreach (string sub in new[] { "\\UserChoiceLatest", "\\UserChoice" })   // newer Windows 11 builds keep it in UserChoiceLatest
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(baseKey + sub))
                    {
                        string id = k == null ? null : k.GetValue("ProgId") as string;
                        if (!string.IsNullOrEmpty(id)) return id;
                    }
                }
                catch { }
            return null;
        }

        /// <summary>Settings > Apps > Default apps, on vView's page (Windows 11), or the Default apps page.</summary>
        public static void OpenSettings()
        {
            foreach (string uri in new[] { "ms-settings:defaultapps?registeredAppUser=" + AppName, "ms-settings:defaultapps" })
                try
                {
                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                    return;
                }
                catch (Exception ex) { Log.Write("Default app: open settings: " + ex.Message); }
        }

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        /// <summary>Tell Explorer the file types changed (icons, Open with).</summary>
        static void Changed() { SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero); }
    }
}
