using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;
using SNMPSimMgr.Models;
using SNMPSimMgr.ViewModels;

namespace SNMPSimMgr.Views
{
    public partial class SimulatorView : UserControl
    {
        public SimulatorView()
        {
            InitializeComponent();
            Loaded += (s, e) => SetupAutoScroll();
        }

        private void SetupAutoScroll()
        {
            var vm = DataContext as SimulatorViewModel;
            if (vm == null) return;

            // Items are inserted at index 0 (newest first) — scroll to top
            vm.TrafficLog.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add && TrafficListBox.Items.Count > 0)
                    TrafficListBox.ScrollIntoView(TrafficListBox.Items[0]);
            };

            vm.LogEntries.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add && SystemLogListBox.Items.Count > 0)
                    SystemLogListBox.ScrollIntoView(SystemLogListBox.Items[0]);
            };
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
}
