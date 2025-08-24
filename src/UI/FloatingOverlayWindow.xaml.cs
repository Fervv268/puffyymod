using System.Windows;
using System.Windows.Controls.Primitives;
using GameBuddyBrain.Services;

namespace GameBuddyBrain.UI
{
    public partial class FloatingOverlayWindow : Window
    {
        private readonly BrainService? _brain;
        public FloatingOverlayWindow(BrainService? brain)
        {
            InitializeComponent();
            _brain = brain;
            // Page 1 visible by default when shown
            Page1.Visibility = Visibility.Visible;
            // Make window transparent background click-through (so it doesn't steal clicks from game)
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW);
        }

        private void PageBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && int.TryParse(b.Content.ToString(), out var idx))
            {
                TogglePage(idx);
            }
        }

        private void TogglePage(int idx)
        {
            // Hide all
            Page1.Visibility = Visibility.Collapsed; Page2.Visibility = Visibility.Collapsed; Page3.Visibility = Visibility.Collapsed; Page4.Visibility = Visibility.Collapsed;
            Page5.Visibility = Visibility.Collapsed; Page6.Visibility = Visibility.Collapsed; Page7.Visibility = Visibility.Collapsed; Page8.Visibility = Visibility.Collapsed;
            Page9.Visibility = Visibility.Collapsed; Page10.Visibility = Visibility.Collapsed;
            // Show selected
            switch (idx)
            {
                case 1: Page1.Visibility = Visibility.Visible; break;
                case 2: Page2.Visibility = Visibility.Visible; break;
                case 3: Page3.Visibility = Visibility.Visible; break;
                case 4: Page4.Visibility = Visibility.Visible; break;
                case 5: Page5.Visibility = Visibility.Visible; break;
                case 6: Page6.Visibility = Visibility.Visible; break;
                case 7: Page7.Visibility = Visibility.Visible; break;
                case 8: Page8.Visibility = Visibility.Visible; break;
                case 9: Page9.Visibility = Visibility.Visible; break;
                case 10: Page10.Visibility = Visibility.Visible; break;
            }
        }

        // Simple passthrough buttons to brain
        private void StartBot_Click(object sender, RoutedEventArgs e) { _brain?.EnableBot(); }
        private void StopBot_Click(object sender, RoutedEventArgs e) { _brain?.DisableBot(); }
        private void CollectAll_Click(object sender, RoutedEventArgs e) { _brain?.CollectAll(); }
        private void ForcePrestige_Click(object sender, RoutedEventArgs e) { _brain?.ForcePrestige(); }
        private void OpenChests_Click(object sender, RoutedEventArgs e) { if (_brain != null && _brain.TryGetAttachedRect(out var l, out var t, out var r, out var b)) { /* call brain chest logic by toggling AutoFarm briefly */ _brain.CollectAll(); } }
        private void BuyBest_Click(object sender, RoutedEventArgs e) { /* best-effort click; BrainService already has shop heuristics run in main loop */ }
        private void NextStage_Click(object sender, RoutedEventArgs e) { /* Force a next wave click by running routine directly */ _brain?.SetEnableNextWave(true); }
        private void ToggleSpeed_Click(object sender, RoutedEventArgs e) { _brain?.ToggleTurbo(); }
    }
}
