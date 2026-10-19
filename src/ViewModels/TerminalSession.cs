using System;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using Iciclecreek.Terminal;

namespace SourceGit.ViewModels
{
    /// <summary>
    /// One persistent terminal session. The session (and its PTY + scrollback) is owned by
    /// <see cref="TerminalService"/> and outlives page switches; only explicit Close destroys it.
    /// </summary>
    public class TerminalSession : ObservableObject
    {
        public const string ShellProfile = "Shell";
        public const string OpenCodeProfile = "OpenCode";

        public TerminalSession(string repositoryPath, string repositoryName, string profile, string workdir, TerminalView view)
        {
            RepositoryPath = repositoryPath;
            RepositoryName = repositoryName;
            Profile = profile;
            Workdir = workdir;
            View = view;
        }

        public string Id
        {
            get;
        } = Guid.NewGuid().ToString("N");

        public string RepositoryPath
        {
            get;
        }

        public string RepositoryName
        {
            get;
        }

        /// <summary>
        /// 'Shell', 'OpenCode' or a single executable path configured by the user.
        /// </summary>
        public string Profile
        {
            get;
        }

        public string Workdir
        {
            get;
        }

        public TerminalView View
        {
            get => _view;
            private set
            {
                if (_view is not null)
                {
                    _view.ProcessExited -= OnViewProcessExited;
                    _view.TitleChanged -= OnViewTitleChanged;
                }

                _view = value;

                if (_view is not null)
                {
                    _view.ProcessExited += OnViewProcessExited;
                    _view.TitleChanged += OnViewTitleChanged;
                }
            }
        }

        public int Pid
        {
            get => _pid;
            set => SetProperty(ref _pid, value);
        }

        public string PidText
        {
            get => _pid > 0 ? _pid.ToString() : string.Empty;
        }

        /// <summary>
        /// Headline shown in session tabs and the sessions dashboard. Falls back to
        /// "profile · repo" until the program sets a terminal title.
        /// </summary>
        public string Title
        {
            get
            {
                var title = _title;
                if (string.IsNullOrWhiteSpace(title))
                    return $"{Profile} · {RepositoryName}";

                return title;
            }
        }

        /// <summary>Raw title coming from the running program (OSC 0/2). null until seen.</summary>
        public string RawTitle
        {
            get => _title;
            set
            {
                if (SetProperty(ref _title, value))
                    OnPropertyChanged(nameof(Title));
            }
        }

        /// <summary>Entry-point name of the launched process, for the dashboard row.</summary>
        public string ProcessName
        {
            get => _processName;
            set => SetProperty(ref _processName, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (SetProperty(ref _isRunning, value))
                    OnPropertyChanged(nameof(StateText));
            }
        }

        public int ExitCode
        {
            get => _exitCode;
            set
            {
                if (SetProperty(ref _exitCode, value))
                    OnPropertyChanged(nameof(StateText));
            }
        }

        public string StateText
        {
            get => IsRunning ? "running" : $"exited ({ExitCode})";
        }

        public DateTime StartedAt
        {
            get;
            set;
        } = DateTime.Now;

        private void OnViewProcessExited(object sender, ProcessExitedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsRunning = false;
                ExitCode = e.ExitCode;
            });
        }

        private void OnViewTitleChanged(object sender, TitleChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!string.IsNullOrWhiteSpace(e.Title))
                    RawTitle = e.Title;
            });
        }

        private TerminalView _view = null;
        private string _title = string.Empty;
        private string _processName = string.Empty;
        private bool _isRunning = true;
        private int _exitCode = 0;
        private int _pid = 0;
    }
}
