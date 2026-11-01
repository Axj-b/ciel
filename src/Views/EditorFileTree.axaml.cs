using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

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

        private static async System.Threading.Tasks.Task ActivateAsync(ViewModels.EditorPage vm, ViewModels.EditorFileTreeNode node)
        {
            if (node.IsFolder)
                vm.ToggleNode(node);
            else
                await vm.OpenFileAsync(node.Path);
        }
    }
}
