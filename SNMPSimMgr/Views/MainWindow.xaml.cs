using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SNMPSimMgr.ViewModels;

namespace SNMPSimMgr.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != MainTabs) return;

            var tab = MainTabs.SelectedItem as TabItem;
            if (tab == null) return;

            var header = tab.Header as string;
            if (header == null) return;

            var vm = DataContext as MainViewModel;
            if (vm == null) return;

            if (header == "MIB BROWSER")
            {
                await vm.MibBrowser.RefreshIfNeeded();
            }
        }
    }
}
