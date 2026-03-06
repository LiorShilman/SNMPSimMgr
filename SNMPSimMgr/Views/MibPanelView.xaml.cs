using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Views
{
    public partial class MibPanelView : UserControl
    {
        public MibPanelView()
        {
            InitializeComponent();
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
