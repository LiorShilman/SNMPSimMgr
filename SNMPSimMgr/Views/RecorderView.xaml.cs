using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using SNMPSimMgr.Models;
using SNMPSimMgr.ViewModels;

namespace SNMPSimMgr.Views
{
    public partial class RecorderView : UserControl
    {
        public RecorderView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Double-click on a results row → populate QueryOid (and SetValue/SetValueType) for GET/SET.
        /// </summary>
        private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var dg = sender as DataGrid;
            if (dg?.SelectedItem == null) return;

            var item = dg.SelectedItem as QueryResultItem;
            if (item == null) return;

            var vm = DataContext as RecorderViewModel;
            if (vm == null) return;

            vm.QueryOid = item.Oid;
            vm.SetValue = item.Value ?? "";
            if (!string.IsNullOrEmpty(item.ValueType))
                vm.SetValueType = item.ValueType;
        }
    }
}
