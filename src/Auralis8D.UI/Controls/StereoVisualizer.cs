using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Auralis8D.UI.Controls
{
    /// <summary>
    /// Custom lightweight visualizer displaying real-time 8D stereo rotation movement.
    /// Draws an acoustic sound stage with left/right channel indicators and a smooth moving sound orb.
    /// Implemented via OnRender for ultra-low CPU utilization and 60fps responsiveness.
    /// </summary>
    public class StereoVisualizer : FrameworkElement
    {
        public static readonly DependencyProperty PanPositionProperty =
            DependencyProperty.Register(
                nameof(PanPosition),
                typeof(float),
                typeof(StereoVisualizer),
                new FrameworkPropertyMetadata(0.0f, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AudioPeakProperty =
            DependencyProperty.Register(
                nameof(AudioPeak),
                typeof(float),
                typeof(StereoVisualizer),
                new FrameworkPropertyMetadata(0.0f, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(
                nameof(IsActive),
                typeof(bool),
                typeof(StereoVisualizer),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Current normalized pan position [-1.0 = left, 0.0 = center, 1.0 = right].
        /// </summary>
        public float PanPosition
        {
            get => (float)GetValue(PanPositionProperty);
            set => SetValue(PanPositionProperty, value);
        }

        /// <summary>
        /// Instantaneous peak audio level [0.0 to 1.0].
        /// </summary>
        public float AudioPeak
        {
            get => (float)GetValue(AudioPeakProperty);
            set => SetValue(AudioPeakProperty, value);
        }

        /// <summary>
        /// Whether 8D audio processing is actively running.
        /// </summary>
        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        private static readonly Pen StageArcPen = new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)), 2.0);
        private static readonly Pen StageCenterPen = new Pen(new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)), 1.5) { DashStyle = DashStyles.Dash };
        private static readonly Brush TextSilverBrush = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
        private static readonly Brush TextBrightBrush = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
        private static readonly Typeface ChannelTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        static StereoVisualizer()
        {
            StageArcPen.Freeze();
            StageCenterPen.Freeze();
            TextSilverBrush.Freeze();
            TextBrightBrush.Freeze();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double width = ActualWidth;
            double height = ActualHeight;
            if (width < 20 || height < 20) return;

            double centerX = width / 2.0;
            double centerY = height / 2.0;
            double trackRadiusX = (width - 60.0) / 2.0;
            if (trackRadiusX < 10) trackRadiusX = 10;

            // 1. Draw horizontal stereo track line
            double trackY = centerY;
            Point leftEnd = new Point(centerX - trackRadiusX, trackY);
            Point rightEnd = new Point(centerX + trackRadiusX, trackY);
            dc.DrawLine(StageArcPen, leftEnd, rightEnd);

            // 2. Draw center reference mark
            dc.DrawLine(StageCenterPen, new Point(centerX, trackY - 14), new Point(centerX, trackY + 14));

            // 3. Draw Channel Labels: "L" and "R"
            var leftText = new FormattedText(
                "L",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                ChannelTypeface,
                14,
                (PanPosition < -0.2f && IsActive) ? TextBrightBrush : TextSilverBrush,
                1.0);

            var rightText = new FormattedText(
                "R",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                ChannelTypeface,
                14,
                (PanPosition > 0.2f && IsActive) ? TextBrightBrush : TextSilverBrush,
                1.0);

            dc.DrawText(leftText, new Point(leftEnd.X - 22, trackY - (leftText.Height / 2.0)));
            dc.DrawText(rightText, new Point(rightEnd.X + 12, trackY - (rightText.Height / 2.0)));

            // 4. Calculate instantaneous sound orb position along stereo trajectory
            double clampedPan = Math.Clamp((double)PanPosition, -1.0, 1.0);
            double orbX = centerX + (clampedPan * trackRadiusX);
            double orbY = trackY;

            // 5. Compute orb glow and size proportional to audio peak and activity
            double baseRadius = 8.0;
            double peakOffset = IsActive ? (Math.Clamp((double)AudioPeak, 0.0, 1.0) * 6.0) : 0.0;
            double currentRadius = baseRadius + peakOffset;

            if (IsActive)
            {
                // Outer subtle glow circle
                Color glowColor = Color.FromArgb(0x40, 0x06, 0xB6, 0xD4);
                Brush glowBrush = new SolidColorBrush(glowColor);
                dc.DrawEllipse(glowBrush, null, new Point(orbX, orbY), currentRadius + 8.0, currentRadius + 8.0);

                // Main vibrant cyan sound orb
                Brush orbBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE));
                dc.DrawEllipse(orbBrush, null, new Point(orbX, orbY), currentRadius, currentRadius);

                // Inner bright white core
                Brush coreBrush = new SolidColorBrush(Colors.White);
                dc.DrawEllipse(coreBrush, null, new Point(orbX, orbY), 3.0, 3.0);
            }
            else
            {
                // Inactive / standby state: cool silver orb at center
                Brush standbyBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
                dc.DrawEllipse(standbyBrush, null, new Point(orbX, orbY), baseRadius, baseRadius);
            }
        }
    }
}
