using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SNMPSimMgr.Helpers
{
    public static class ScrollHelper
    {
        public static readonly DependencyProperty BubbleMouseWheelProperty =
            DependencyProperty.RegisterAttached(
                "BubbleMouseWheel", typeof(bool), typeof(ScrollHelper),
                new PropertyMetadata(false, OnBubbleMouseWheelChanged));

        public static bool GetBubbleMouseWheel(DependencyObject d) => (bool)d.GetValue(BubbleMouseWheelProperty);
        public static void SetBubbleMouseWheel(DependencyObject d, bool value) => d.SetValue(BubbleMouseWheelProperty, value);

        private static void OnBubbleMouseWheelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
            {
                if ((bool)e.NewValue)
                    sv.PreviewMouseWheel += OnPreviewMouseWheel;
                else
                    sv.PreviewMouseWheel -= OnPreviewMouseWheel;
            }
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = (ScrollViewer)sender;
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }
}
