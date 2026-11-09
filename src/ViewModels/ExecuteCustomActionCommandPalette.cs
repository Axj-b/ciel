using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class ExecuteCustomActionCommandPaletteCmd
    {
        public Models.CustomAction Action { get; set; }
        public Models.RepositoryScriptAction Script { get; set; }
        public bool IsGlobal { get; set; }
        public bool IsAutoDetected { get => Script != null; }
        public string Name { get => Script?.Name ?? Action.Name; }
        public string Path { get => Script?.RelativePath; }

        public ExecuteCustomActionCommandPaletteCmd(Models.CustomAction action, bool isGlobal)
        {
            Action = action;
            IsGlobal = isGlobal;
        }

        public ExecuteCustomActionCommandPaletteCmd(Models.RepositoryScriptAction script)
        {
            Script = script;
        }
    }

    public class ExecuteCustomActionCommandPalette : ICommandPalette
    {
        public List<ExecuteCustomActionCommandPaletteCmd> VisibleActions
        {
            get => _visibleActions;
            private set => SetProperty(ref _visibleActions, value);
        }

        public ExecuteCustomActionCommandPaletteCmd Selected
        {
            get => _selected;
            set => SetProperty(ref _selected, value);
        }

        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                    UpdateVisibleActions();
            }
        }

        public ExecuteCustomActionCommandPalette(Repository repo)
        {
            _repo = repo;

            var actions = repo.GetCustomActions(Models.CustomActionScope.Repository);
            foreach (var (action, menu) in actions)
                _actions.Add(new(action, menu.IsGlobal));

            foreach (var script in Models.RepositoryScriptDetector.Discover(repo.FullPath))
                _actions.Add(new(script));

            if (_actions.Count > 0)
            {
                _actions.Sort((l, r) =>
                {
                    if (l.IsGlobal != r.IsGlobal)
                        return l.IsGlobal ? -1 : 1;

                    if (l.IsAutoDetected != r.IsAutoDetected)
                        return l.IsAutoDetected ? 1 : -1;

                    return l.Name.CompareTo(r.Name, StringComparison.OrdinalIgnoreCase);
                });

                _visibleActions = _actions;
                _selected = _actions[0];
            }
        }

        public void ClearFilter()
        {
            Filter = string.Empty;
        }

        public async Task ExecAsync()
        {
            _actions.Clear();
            _visibleActions.Clear();
            Close();

            if (_selected?.Script != null)
                Models.RepositoryScriptLauncher.Run(_repo.FullPath, _selected.Script);
            else if (_selected != null)
                await _repo.ExecCustomActionAsync(_selected.Action, null);
        }

        private void UpdateVisibleActions()
        {
            var filter = _filter?.Trim();
            if (string.IsNullOrEmpty(filter))
            {
                VisibleActions = _actions;
                return;
            }

            var visible = new List<ExecuteCustomActionCommandPaletteCmd>();
            foreach (var act in _actions)
            {
                if (act.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    visible.Add(act);
            }

            var autoSelected = _selected;
            if (visible.Count == 0)
                autoSelected = null;
            else if (_selected == null || !visible.Contains(_selected))
                autoSelected = visible[0];

            VisibleActions = visible;
            Selected = autoSelected;
        }

        private Repository _repo;
        private List<ExecuteCustomActionCommandPaletteCmd> _actions = [];
        private List<ExecuteCustomActionCommandPaletteCmd> _visibleActions = [];
        private ExecuteCustomActionCommandPaletteCmd _selected = null;
        private string _filter;
    }
}
