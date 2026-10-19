using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Media;

using Iciclecreek.Terminal;

namespace SourceGit.ViewModels
{
    /// <summary>
    /// App-level registry of all embedded terminal sessions. Sessions must survive repository
    /// tab and page switches, so this type — not the views — owns their lifetime:
    /// <list type="bullet">
    ///   <item>Create: arms BeginReparent() on the view, so any detach keeps the PTY alive.</item>
    ///   <item>Close: EndReparent() first, then detach+kill, so the library tears the PTY down.</item>
    /// </list>
    /// </summary>
    public static class TerminalService
    {
        /// <summary>All live sessions (exited ones stay visible until explicitly closed).</summary>
        public static AvaloniaList<TerminalSession> Sessions
        {
            get;
        } = [];

        /// <summary>Raised after Sessions changed (add/remove).</summary>
        public static event Action SessionsChanged;

        public static TerminalSession Create(Repository repo, string profile)
        {
            var view = new TerminalView
            {
                ShowCaretOnClick = true,
            };

            ApplyFont(view);
            view.BeginReparent(); // any detach is a UI move, not a shutdown, until Close() re-arms it

            var session = new TerminalSession(repo.FullPath, Path.GetFileName(repo.FullPath), profile, repo.FullPath, view);

            Sessions.Add(session);
            SessionsChanged?.Invoke();

            Start(session);
            return session;
        }

        /// <summary>
        /// Starts (or restarts) the process of a session using the session's profile.
        /// Any failure (missing executable, launch error) is reported via the
        /// notification bar instead of crashing.
        /// </summary>
        public static void Start(TerminalSession session)
        {
            if (session?.View is null)
                return;

            session.IsRunning = true;
            session.ExitCode = 0;
            session.Pid = 0;
            session.StartedAt = DateTime.Now;

            var view = session.View;

            // The emulator only exists after the view was initialized (first logical
            // attach): defer the launch, and never schedule the deferral twice.
            if (!view.IsInitialized)
            {
                if (_launchPending.Add(session.Id))
                    view.Initialized += (_, _) => LaunchInBackground(session);

                return;
            }

            LaunchInBackground(session);
        }

        private static async void LaunchInBackground(TerminalSession session)
        {
            // Closed while waiting for initialization — don't launch into the void.
            if (!Sessions.Contains(session))
                return;

            // One in-flight launch per session: a second Start while the first is still
            // launching is a no-op instead of racing the PTY connection.
            if (!_launchPending.Add(session.Id))
                return;

            try
            {
                var (process, args, displayName) = ResolveCommand(session.Profile);
                session.ProcessName = displayName;

                try
                {
                    session.View.BufferSize = Preferences.Instance.TerminalScrollback;
                }
                catch
                {
                    // scrollback is a nice-to-have; never block the launch on it
                }

                await session.View.LaunchProcess(session.Workdir, process, args);

                try
                {
                    session.Pid = session.View.Pid;
                }
                catch
                {
                    // Pid is information-only
                }
            }
            catch (Exception ex)
            {
                session.IsRunning = false;
                session.ExitCode = 0;
                Models.Notification.Send(session.RepositoryPath,
                    $"Failed to start '{session.ProcessName}' in the embedded terminal:\n{ex.Message}", true);
            }
            finally
            {
                _launchPending.Remove(session.Id);
            }
        }

        public static void Kill(TerminalSession session)
        {
            if (session is null)
                return;

            try
            {
                session.View.Kill();
            }
            catch
            {
                // already gone
            }
        }

        /// <summary>Kill + remove from the registry. Detaching the view runs the library cleanup.</summary>
        public static void Close(TerminalSession session)
        {
            if (session is null)
                return;

            Sessions.Remove(session);
            SessionsChanged?.Invoke();

            var view = session.View;
            if (view is null)
                return;

            var parent = view.Parent;

            // Disarm first: the following detach must run the library's CleanupProcess path.
            view.EndReparent();

            // The view may not be attached anywhere (dashboard visible, editor page, app closing):
            // kill explicitly, guarded because CleanupProcess may have nulled the connection already.
            try
            {
                view.Kill();
            }
            catch
            {
                // no PTY anymore
            }

            // If still hosted, detach so the library unsubscribes + cleans up too.
            switch (parent)
            {
                case ContentControl cc:
                    cc.Content = null;
                    break;
                case Decorator dec:
                    dec.Child = null;
                    break;
            }
        }

        /// <summary>Kill every session process in one go. Rows remain in 'exited' state.</summary>
        public static void KillAll()
        {
            foreach (var session in Sessions.ToArray())
                Kill(session);
        }

        public static void CloseAll()
        {
            foreach (var session in Sessions.ToArray())
                Close(session);
        }

        public static void CloseAllForRepository(string repositoryPath)
        {
            var normalized = repositoryPath.Replace('\\', '/').TrimEnd('/');
            foreach (var session in Sessions.ToArray())
            {
                if (session.RepositoryPath.Equals(normalized, StringComparison.Ordinal))
                    Close(session);
            }
        }

        private static void ApplyFont(TerminalView view)
        {
            view.FontSize = Preferences.Instance.TerminalFontSize;

            if (TryGetMonospaceFamily(out var family))
                view.FontFamily = family;
        }

        private static bool TryGetMonospaceFamily(out FontFamily family)
        {
            family = null;

            if (Application.Current is { } app &&
                app.TryGetResource("Fonts.Monospace", app.ActualThemeVariant, out var value) &&
                value is FontFamily parsed)
            {
                family = parsed;
                return true;
            }

            return false;
        }

        private static (string Process, string[] Args, string DisplayName) ResolveCommand(string profile)
        {
            if (profile.Equals(TerminalSession.OpenCodeProfile, StringComparison.Ordinal))
                return ResolveOpenCode();

            return ResolveShell();
        }

        private static (string Process, string[] Args, string DisplayName) ResolveShell()
        {
            var overridePath = Preferences.Instance.TerminalShellCommand;
            if (!string.IsNullOrWhiteSpace(overridePath))
                return (overridePath, [], Path.GetFileName(overridePath));

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var pwsh = FindOnPath("pwsh.exe") ?? FindPwshViaRegistry();
                if (pwsh is not null)
                    return (pwsh, ["-NoLogo"], "pwsh");

                var powershell = FindOnPath("powershell.exe") ?? @"C:\Windows\System32\powershell.exe";
                if (File.Exists(powershell))
                    return (powershell, ["-NoLogo"], "powershell");

                return (@"C:\Windows\System32\cmd.exe", [], "cmd");
            }

            var shell = Environment.GetEnvironmentVariable("SHELL");
            if (!string.IsNullOrEmpty(shell) && IsExecutableFile(shell))
                return (shell, [], Path.GetFileName(shell));

            var bash = FindOnPath("bash");
            if (bash is not null)
                return (bash, [], "bash");

            return ("/bin/sh", [], "sh");
        }

        private static (string Process, string[] Args, string DisplayName) ResolveOpenCode()
        {
            var overridePath = Preferences.Instance.TerminalOpenCodeCommand;
            if (!string.IsNullOrWhiteSpace(overridePath))
                return (overridePath, [], "opencode");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // 1) A native opencode.exe on PATH is the reliable way to boot the TUI under
                //    ConPTY (spike-validated: .cmd/.bat shims do not work without a wrapper).
                var exe = FindOnPath("opencode.exe");
                if (exe is not null)
                    return (exe, [], "opencode");

                // 2) npm installs a .cmd shim plus a native binary under
                //    <npm-global-root>/@opencode/cli/bin/opencode.exe — use that directly.
                var npmExe = FindOpenCodeViaNpmRoot();
                if (npmExe is not null)
                    return (npmExe, [], "opencode");

                // 3) Last resort: the shim via cmd.exe.
                var shim = FindOnPath("opencode.cmd") ?? FindOnPath("opencode.bat");
                if (shim is not null)
                    return (@"C:\Windows\System32\cmd.exe", ["/c", shim], "opencode (shim)");
            }
            else
            {
                var exe = FindOnPath("opencode");
                if (exe is not null)
                    return (exe, [], "opencode");
            }

            throw new InvalidOperationException("OpenCode executable not found. Set the executable path in Preferences → Tools.");
        }

        [SupportedOSPlatform("windows")]
        private static string FindPwshViaRegistry()
        {
            try
            {
                using var localMachine = Microsoft.Win32.RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.LocalMachine,
                    Microsoft.Win32.RegistryView.Registry64);

                using var key = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\pwsh.exe");
                if (key?.GetValue(null) is string value && File.Exists(value))
                    return value;
            }
            catch
            {
                // ignore registry issues
            }

            return null;
        }

        private static string FindOnPath(string fileName)
        {
            var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fullpath = Path.Combine(dir, fileName);
                if (IsExecutableFile(fullpath))
                    return fullpath;
            }

            return null;
        }

        private static bool IsExecutableFile(string path)
        {
            if (!File.Exists(path))
                return false;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return true;

            return HasUnixExecuteBit(path);
        }

        private static bool HasUnixExecuteBit(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                    return false;

                try
                {
                    var mode = info.UnixFileMode;
                    return mode != 0 &&
                           ((mode & UnixFileMode.OtherExecute) != 0 ||
                            (mode & UnixFileMode.GroupExecute) != 0 ||
                            (mode & UnixFileMode.UserExecute) != 0);
                }
                catch
                {
                    // File system does not expose the mode: treat as usable.
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string FindOpenCodeViaNpmRoot()
        {
            if (_openCodeNpmProbe is not null)
                return _openCodeNpmProbe.Length > 0 ? _openCodeNpmProbe : null;

            var root = TryReadNpmRoot();
            if (string.IsNullOrEmpty(root))
            {
                _openCodeNpmProbe = string.Empty;
                return null;
            }

            var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(root, "@opencode", "cli", "bin", "opencode.exe")
                : Path.Combine(root, "@opencode", "cli", "bin", "opencode");

            _openCodeNpmProbe = File.Exists(exe) ? exe : string.Empty;
            return _openCodeNpmProbe.Length > 0 ? _openCodeNpmProbe : null;
        }

        private static string TryReadNpmRoot()
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    info.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                    info.Arguments = "/c npm root -g";
                }
                else
                {
                    info.FileName = "/usr/bin/env";
                    info.Arguments = "npm root -g";
                }

                using var proc = Process.Start(info);
                if (proc is null)
                    return null;

                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(2000);
                return output.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static string _openCodeNpmProbe = null;
        private static readonly HashSet<string> _launchPending = new();
    }
}
