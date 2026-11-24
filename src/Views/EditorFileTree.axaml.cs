using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class EditorFileTree : UserControl
    {
        public EditorFileTree()
        {
            InitializeComponent();
        }

        private void OnExpanderClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.EditorPage vm &&
                sender is ToggleButton { DataContext: ViewModels.EditorFileTreeNode node })
            {
                vm.ToggleNode(node);
            }

            e.Handled = true;
        }

        private void OnRowsContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (DataContext is not ViewModels.EditorPage vm)
                return;

            var node = (e.Source as Control)?.FindAncestorOfType<ListBoxItem>()?.DataContext as
                ViewModels.EditorFileTreeNode;
            if (node != null)
                vm.SelectedRow = node;

            CreateContextMenu(vm, node).Open(RowsList);
            e.Handled = true;
        }

        private async void OnRowDoubleTapped(object sender, TappedEventArgs e)
        {
            if (e.Source is ToggleButton)
                return;

            if (DataContext is ViewModels.EditorPage vm && RowsList.SelectedItem is ViewModels.EditorFileTreeNode node)
                await ActivateAsync(vm, node);

            e.Handled = true;
        }

        private async void OnRowKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not ViewModels.EditorPage vm ||
                RowsList.SelectedItem is not ViewModels.EditorFileTreeNode node)
                return;

            switch (e.Key)
            {
                case Key.Enter:
                    await ActivateAsync(vm, node);
                    e.Handled = true;
                    break;
                case Key.F2:
                    vm.BeginRenameItem(node);
                    e.Handled = true;
                    break;
                case Key.Delete:
                    await vm.DeleteItemAsync(node);
                    e.Handled = true;
                    break;
                case Key.Right when node.IsFolder && !node.IsExpanded:
                    vm.ToggleNode(node);
                    e.Handled = true;
                    break;
                case Key.Left when node.IsFolder && node.IsExpanded:
                    vm.ToggleNode(node);
                    e.Handled = true;
                    break;
            }
        }

        private ContextMenu CreateContextMenu(ViewModels.EditorPage vm, ViewModels.EditorFileTreeNode node)
        {
            var menu = new ContextMenu();
            if (node == null)
            {
                menu.Items.Add(CreateNewItemMenuItem(vm, null, false));
                menu.Items.Add(CreateNewItemMenuItem(vm, null, true));
                menu.Items.Add(new MenuItem() { Header = "-" });

                var terminal = new MenuItem();
                terminal.Header = App.Text("Repository.Terminal");
                terminal.Icon = this.CreateMenuIcon("Icons.Terminal");
                terminal.Click += (_, e) =>
                {
                    Native.OS.OpenTerminal(vm.RepositoryPath);
                    e.Handled = true;
                };
                menu.Items.Add(terminal);

                var refresh = new MenuItem();
                refresh.Header = App.Text("Editor.Refresh");
                refresh.Icon = this.CreateMenuIcon("Icons.Reset");
                refresh.Click += async (_, e) =>
                {
                    await vm.RefreshFilesAsync();
                    e.Handled = true;
                };
                menu.Items.Add(refresh);
                return menu;
            }

            if (!node.IsFolder)
            {
                var open = new MenuItem();
                open.Header = App.Text("Editor.Open");
                open.Icon = this.CreateMenuIcon("Icons.File");
                open.Click += async (_, e) =>
                {
                    await vm.OpenFileAsync(node.Path);
                    e.Handled = true;
                };
                menu.Items.Add(open);
                menu.Items.Add(new MenuItem() { Header = "-" });
            }

            menu.Items.Add(CreateNewItemMenuItem(vm, node, false));
            menu.Items.Add(CreateNewItemMenuItem(vm, node, true));
            menu.Items.Add(new MenuItem() { Header = "-" });

            var rename = new MenuItem();
            rename.Header = App.Text("Editor.Rename");
            rename.Icon = this.CreateMenuIcon("Icons.Rename");
            rename.Click += (_, e) =>
            {
                vm.BeginRenameItem(node);
                e.Handled = true;
            };

            var delete = new MenuItem();
            delete.Header = App.Text("Editor.Delete");
            delete.Icon = this.CreateMenuIcon("Icons.File.Remove");
            delete.Click += async (_, e) =>
            {
                await vm.DeleteItemAsync(node);
                e.Handled = true;
            };

            menu.Items.Add(rename);
            menu.Items.Add(delete);
            menu.Items.Add(new MenuItem() { Header = "-" });

            var fullPath = Native.OS.GetAbsPath(vm.RepositoryPath, node.Path);

            var reveal = new MenuItem();
            reveal.Header = App.Text("RevealFile");
            reveal.Icon = this.CreateMenuIcon("Icons.Explore");
            reveal.Click += (_, e) =>
            {
                Native.OS.OpenInFileManager(fullPath);
                e.Handled = true;
            };

            var copyPath = new MenuItem();
            copyPath.Header = App.Text("CopyPath");
            copyPath.Icon = this.CreateMenuIcon("Icons.Copy");
            copyPath.Click += async (_, e) =>
            {
                await this.CopyTextAsync(node.Path);
                e.Handled = true;
            };

            var copyFullPath = new MenuItem();
            copyFullPath.Header = App.Text("CopyFullPath");
            copyFullPath.Icon = this.CreateMenuIcon("Icons.Copy");
            copyFullPath.Click += async (_, e) =>
            {
                await this.CopyTextAsync(fullPath);
                e.Handled = true;
            };

            var openTerminal = new MenuItem();
            openTerminal.Header = App.Text("Repository.Terminal");
            openTerminal.Icon = this.CreateMenuIcon("Icons.Terminal");
            openTerminal.Click += (_, e) =>
            {
                var workdir = node.IsFolder ? fullPath : System.IO.Path.GetDirectoryName(fullPath);
                Native.OS.OpenTerminal(string.IsNullOrEmpty(workdir) ? vm.RepositoryPath : workdir);
                e.Handled = true;
            };

            menu.Items.Add(reveal);
            menu.Items.Add(openTerminal);
            menu.Items.Add(copyPath);
            menu.Items.Add(copyFullPath);
            return menu;
        }

        private MenuItem CreateNewItemMenuItem(ViewModels.EditorPage vm, ViewModels.EditorFileTreeNode node, bool isFolder)
        {
            var item = new MenuItem();
            item.Header = App.Text(isFolder ? "Editor.NewFolder" : "Editor.NewFile");
            item.Icon = this.CreateMenuIcon(isFolder ? "Icons.Folder.Add" : "Icons.File.Add");
            item.Click += (_, e) =>
            {
                vm.BeginCreateItem(node, isFolder);
                e.Handled = true;
            };
            return item;
        }

        private static async Task ActivateAsync(ViewModels.EditorPage vm, ViewModels.EditorFileTreeNode node)
        {
            if (node.IsFolder)
                vm.ToggleNode(node);
            else
                await vm.OpenFileAsync(node.Path);
        }
    }
}
