using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using GameBuddyBrain.Services;

namespace GameBuddyBrain.UI
{
    public partial class OverlayWindow : Window
    {
        public OverlayWindow()
        {
            InitializeComponent();
        }

        // Draw last-detected rects from BrainService (absolute screen coords)
        public void ShowRects(BrainService brain)
        {
            try
            {
                if (brain.TryGetLastNextRect(out var nr))
                {
                    ShowRect((Rectangle)FindName("NextRect")!, nr);
                }
                else { var rr = (Rectangle)FindName("NextRect")!; rr.Visibility = Visibility.Collapsed; }

                if (brain.TryGetLastClaimRect(out var cr))
                {
                    ShowRect((Rectangle)FindName("ClaimRect")!, cr);
                }
                else { var rr2 = (Rectangle)FindName("ClaimRect")!; rr2.Visibility = Visibility.Collapsed; }
            }
            catch { }
        }

        private static void ShowRect(Rectangle rect, GameDetector.RECT r)
        {
            if (r.Right <= r.Left || r.Bottom <= r.Top) { rect.Visibility = Visibility.Collapsed; return; }
            Canvas.SetLeft(rect, r.Left);
            Canvas.SetTop(rect, r.Top);
            rect.Width = Math.Max(1, r.Right - r.Left);
            rect.Height = Math.Max(1, r.Bottom - r.Top);
            rect.Visibility = Visibility.Visible;
        }
    }
}
