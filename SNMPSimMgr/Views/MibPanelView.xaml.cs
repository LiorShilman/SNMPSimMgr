using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SNMPSimMgr.Models;
using SNMPSimMgr.ViewModels;

namespace SNMPSimMgr.Views
{
    public partial class MibPanelView : UserControl
    {
        public MibPanelView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Double-click on a table cell opens the SET dialog for that specific cell
        /// (column OID + row index).
        /// </summary>
        private void TableDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var dg = sender as DataGrid;
            if (dg == null) return;

            var panelTableItem = dg.Tag as PanelTableItem;
            if (panelTableItem?.SourceTable == null) return;

            // Find the clicked cell
            var cell = FindVisualParent<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell == null) return;

            var row = DataGridRow.GetRowContainingElement(cell);
            if (row == null) return;

            // Get column index (skip the "Name" column at index 0)
            int colIndex = cell.Column.DisplayIndex;
            if (colIndex <= 0) return; // Name column — not editable

            var table = panelTableItem.SourceTable;
            if (colIndex - 1 >= table.Columns.Count) return;

            var column = table.Columns[colIndex - 1];

            // Get the row data to find the index
            var rowView = row.Item as DataRowView;
            if (rowView == null) return;

            // Find matching table row by label/name
            var nameValue = rowView["Name"]?.ToString();
            var tableRow = table.Rows.FirstOrDefault(r =>
                (r.Label ?? r.Index) == nameValue);
            if (tableRow == null) return;

            // Invoke PanelTableSetCellCommand with (table, columnOid, rowIndex)
            var vm = DataContext as MibBrowserViewModel;
            if (vm?.PanelTableSetCellCommand != null)
            {
                vm.PanelTableSetCellCommand.Execute(new object[] { table, column.Oid, tableRow.Index });
            }
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent)
                    return parent;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }
    }

    public class ConfigFieldTemplateSelector : DataTemplateSelector
    {
        public DataTemplate TextTemplate { get; set; }
        public DataTemplate EnumTemplate { get; set; }
        public DataTemplate ToggleTemplate { get; set; }
        public DataTemplate NumberTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is MibFieldSchema field)
            {
                switch (field.InputType)
                {
                    case "enum": return EnumTemplate;
                    case "toggle": return ToggleTemplate;
                    case "number":
                    case "gauge":
                    case "counter":
                        return NumberTemplate;
                    default: return TextTemplate;
                }
            }
            return TextTemplate;
        }
    }
}
