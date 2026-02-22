using System.Windows.Controls;
using System.Windows.Input;
using SNMPSimMgr.Models;
using SNMPSimMgr.ViewModels;

namespace SNMPSimMgr.Views;

public partial class SimulatorView : UserControl
{
    public SimulatorView()
    {
        InitializeComponent();
    }

    private void TrafficLog_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.Content is string entry
            && DataContext is SimulatorViewModel vm)
        {
            vm.SelectTrafficEntryCommand.Execute(entry);
        }
    }

    private void QueryResults_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is QueryResultItem item
            && DataContext is SimulatorViewModel vm)
        {
            vm.SelectQueryResultCommand.Execute(item);
        }
    }
}
