using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using LocalDesktopStore.ViewModels;

namespace LocalDesktopStore;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            HookLogAutoScroll();
            SyncTokenBoxFromViewModel();
            SetSettingsDrawerOpen(false);
        };
        KeyDown += OnWindowKeyDown;
    }

    private void ToggleSettings_Click(object sender, RoutedEventArgs e)
    {
        SetSettingsDrawerOpen(SettingsDrawer.Visibility != Visibility.Visible);
    }

    private void GitHubTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.GitHubTokenInput = GitHubTokenBox.Password;
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SettingsDrawer.Visibility == Visibility.Visible)
        {
            SetSettingsDrawerOpen(false);
            e.Handled = true;
        }
    }

    private void HookLogAutoScroll()
    {
        if (DataContext is not MainViewModel vm) return;
        ((INotifyCollectionChanged)vm.LogLines).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
            {
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            }
        };
    }

    private void SyncTokenBoxFromViewModel()
    {
        if (DataContext is MainViewModel vm && GitHubTokenBox.Password != vm.GitHubTokenInput)
            GitHubTokenBox.Password = vm.GitHubTokenInput;
    }

    private void SetSettingsDrawerOpen(bool isOpen)
    {
        SettingsDrawer.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        SettingsToggleButton.Content = isOpen ? "Hide settings" : "Settings";
        SettingsToggleButton.IsDefault = isOpen;
        if (isOpen)
            GitHubUserBox.Focus();
    }
}
