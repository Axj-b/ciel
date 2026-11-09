using System;
using System.Diagnostics;
using System.IO;

namespace SourceGit.Models
{
    /// <summary>
    /// Runs detected scripts in a separate console window (fire-and-forget). The console is kept
    /// open after the script exits so its output stays readable; closing the window ends the process.
    /// </summary>
    public static class RepositoryScriptLauncher
    {
        public static void Run(string repositoryPath, RepositoryScriptAction script)
        {
            if (script == null)
                return;

            try
            {
                var workdir = repositoryPath.Replace('/', Path.DirectorySeparatorChar);
                if (OperatingSystem.IsWindows())
                    RunOnWindows(workdir, script);
                else
                    RunOnUnix(workdir, script);
            }
            catch (Exception e)
            {
                Native.OS.LogException(e);
                Notification.Send(repositoryPath, $"Failed to run '{script.RelativePath}': {e.Message}", true);
            }
        }

        private static void RunOnWindows(string workdir, RepositoryScriptAction script)
        {
            ProcessStartInfo start;
            switch (script.Kind)
            {
                case RepositoryScriptKind.Cmd:
                case RepositoryScriptKind.Batch:
                    start = new ProcessStartInfo("cmd.exe")
                    {
                        Arguments = $"/d /k call \"{script.FullPath}\"",
                        WorkingDirectory = workdir,
                        UseShellExecute = true,
                    };
                    break;
                case RepositoryScriptKind.PowerShell:
                    var powershell = FindPowerShell() ??
                        throw new InvalidOperationException("No PowerShell executable (pwsh.exe or powershell.exe) found in PATH.");
                    start = new ProcessStartInfo(powershell)
                    {
                        Arguments = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -NoExit -File \"{script.FullPath}\"",
                        WorkingDirectory = workdir,
                        UseShellExecute = true,
                    };
                    break;
                default:
                    throw new InvalidOperationException($"Script type '{Path.GetExtension(script.FullPath)}' can not be run on Windows.");
            }

            Process.Start(start);
        }

        private static void RunOnUnix(string workdir, RepositoryScriptAction script)
        {
            string command;
            switch (script.Kind)
            {
                case RepositoryScriptKind.Shell:
                    command = $"/bin/sh {QuoteForShell(script.FullPath)}; echo; printf 'Press Enter to close...'; read _";
                    break;
                case RepositoryScriptKind.PowerShell:
                    var pwsh = FindOnPath("pwsh") ??
                        throw new InvalidOperationException("PowerShell (pwsh) was not found in PATH.");
                    command = $"{QuoteForShell(pwsh)} -NoLogo -NoProfile -ExecutionPolicy Bypass -NoExit -File {QuoteForShell(script.FullPath)}";
                    break;
                default:
                    throw new InvalidOperationException($"Script type '{Path.GetExtension(script.FullPath)}' can not be run on this platform.");
            }

            OpenTerminal(workdir, command);
        }

        private static void OpenTerminal(string workdir, string command)
        {
            if (OperatingSystem.IsMacOS())
            {
                var escaped = command.Replace("\\", "\\\\").Replace("\"", "\\\"");
                var start = new ProcessStartInfo("osascript")
                {
                    WorkingDirectory = workdir,
                    UseShellExecute = false,
                };
                start.ArgumentList.Add("-e");
                start.ArgumentList.Add($"tell application \"Terminal\" to do script \"{escaped}\"");
                start.ArgumentList.Add("-e");
                start.ArgumentList.Add("tell application \"Terminal\" to activate");
                Process.Start(start);
                return;
            }

            (string Exec, string[] Prefix)[] candidates =
            [
                ("x-terminal-emulator", ["-e"]),
                ("gnome-terminal", ["--"]),
                ("konsole", ["-e"]),
                ("xfce4-terminal", ["-e"]),
                ("xterm", ["-e"]),
            ];

            foreach (var (exe, prefix) in candidates)
            {
                var resolved = FindOnPath(exe);
                if (resolved == null)
                    continue;

                var start = new ProcessStartInfo(resolved)
                {
                    WorkingDirectory = workdir,
                    UseShellExecute = false,
                };
                foreach (var arg in prefix)
                    start.ArgumentList.Add(arg);
                start.ArgumentList.Add("/bin/sh");
                start.ArgumentList.Add("-c");
                start.ArgumentList.Add(command);
                Process.Start(start);
                return;
            }

            throw new InvalidOperationException("No terminal emulator was found to run the script.");
        }

        private static string FindPowerShell()
        {
            var pwsh = FindOnPath("pwsh.exe");
            if (pwsh != null)
                return pwsh;

            var powershell = FindOnPath("powershell.exe");
            if (powershell != null)
                return powershell;

            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            return File.Exists(fallback) ? fallback : null;
        }

        private static string FindOnPath(string fileName)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fullpath = Path.Combine(dir, fileName);
                if (File.Exists(fullpath))
                    return fullpath;
            }

            return null;
        }

        private static string QuoteForShell(string value)
        {
            return "'" + value.Replace("'", "'\\''") + "'";
        }
    }
}
