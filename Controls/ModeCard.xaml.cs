using SmartLauncher.UI.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace SmartLauncher.UI.Controls
{
    public enum ModeCardAction
    {
        Launch,
        Stop,
        Edit,
        Duplicate,
        Delete
    }

    public sealed class ModeCardActionEventArgs : EventArgs
    {
        public ModeCardActionEventArgs(
            LauncherMode mode,
            ModeCardAction action)
        {
            Mode = mode;
            Action = action;
        }

        public LauncherMode Mode { get; }

        public ModeCardAction Action { get; }
    }

    public partial class ModeCard :
        System.Windows.Controls.UserControl
    {
        private LauncherMode? _mode;
        private bool _isLightTheme;

        public ModeCard()
        {
            InitializeComponent();
        }

        public event EventHandler<ModeCardActionEventArgs>?
            ActionRequested;

        public LauncherMode? Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                DataContext = value;
                RefreshDetails();
            }
        }

        public void SetRunning(bool isRunning)
        {
            LaunchButton.Visibility =
                isRunning
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            StopButton.Visibility =
                isRunning
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            RuntimeStatusText.Text =
                isRunning
                    ? "● Режим активен"
                    : "Готов к запуску";

            RuntimeStatusText.Foreground =
                new SolidColorBrush(
                    isRunning
                        ? Color.FromRgb(91, 157, 255)
                        : Color.FromRgb(98, 212, 154));
        }

        public void SetRuntimeState(
            bool startedByLauncher,
            int runningApplicationCount)
        {
            LaunchButton.Visibility =
                startedByLauncher
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            StopButton.Visibility =
                startedByLauncher
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (startedByLauncher)
            {
                RuntimeStatusText.Text =
                    "● Режим активен";
                RuntimeStatusText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(91, 157, 255));
            }
            else if (runningApplicationCount > 0)
            {
                RuntimeStatusText.Text =
                    $"Уже запущено: {runningApplicationCount}";
                RuntimeStatusText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(91, 157, 255));
            }
            else
            {
                RuntimeStatusText.Text =
                    "Готов к запуску";
                RuntimeStatusText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(98, 212, 154));
            }
        }

        public void SetLightTheme(bool isLightTheme)
        {
            _isLightTheme = isLightTheme;

            CardBackgroundBrush.Color =
                isLightTheme
                    ? Color.FromRgb(255, 255, 255)
                    : Color.FromRgb(23, 23, 23);

            CardBorderBrush.Color =
                isLightTheme
                    ? Color.FromRgb(216, 222, 233)
                    : Color.FromRgb(44, 44, 44);
        }

        private void RefreshDetails()
        {
            if (_mode == null
                || TargetsText == null)
            {
                return;
            }

            string[] names =
                _mode.Targets
                    .Where(target => target.IsEnabled)
                    .Select(target => target.DisplayName)
                    .Take(3)
                    .ToArray();

            TargetsText.Text =
                names.Length == 0
                    ? "Нет действий"
                    : string.Join("  •  ", names)
                      + (_mode.Targets.Count > 3 ? "  …" : string.Empty);
        }

        private void UserControl_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            var easing = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            };

            BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(
                    0,
                    1,
                    TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = easing
                });

            CardScaleTransform.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(
                    0.96,
                    1,
                    TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = easing
                });

            CardScaleTransform.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(
                    0.96,
                    1,
                    TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = easing
                });

            CardTranslateTransform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(
                    18,
                    0,
                    TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = easing
                });
        }

        private void UserControl_MouseEnter(
            object sender,
            MouseEventArgs e)
        {
            AnimateHover(
                1.025,
                Color.FromRgb(59, 123, 255),
                _isLightTheme
                    ? Color.FromRgb(242, 246, 255)
                    : Color.FromRgb(26, 29, 37),
                0.42,
                28);
        }

        private void UserControl_MouseLeave(
            object sender,
            MouseEventArgs e)
        {
            AnimateHover(
                1,
                _isLightTheme
                    ? Color.FromRgb(216, 222, 233)
                    : Color.FromRgb(44, 44, 44),
                _isLightTheme
                    ? Color.FromRgb(255, 255, 255)
                    : Color.FromRgb(23, 23, 23),
                0.22,
                18);
        }

        private void AnimateHover(
            double scale,
            Color borderColor,
            Color backgroundColor,
            double shadowOpacity,
            double shadowBlur)
        {
            var easing = new QuadraticEase
            {
                EasingMode = EasingMode.EaseOut
            };

            var scaleAnimation =
                new DoubleAnimation
                {
                    To = scale,
                    Duration =
                        TimeSpan.FromMilliseconds(180),
                    EasingFunction = easing
                };

            CardScaleTransform.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                scaleAnimation);

            CardScaleTransform.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                scaleAnimation);

            CardBorderBrush.BeginAnimation(
                SolidColorBrush.ColorProperty,
                new ColorAnimation(
                    borderColor,
                    TimeSpan.FromMilliseconds(180)));

            CardBackgroundBrush.BeginAnimation(
                SolidColorBrush.ColorProperty,
                new ColorAnimation(
                    backgroundColor,
                    TimeSpan.FromMilliseconds(180)));

            CardShadowEffect.BeginAnimation(
                DropShadowEffect.OpacityProperty,
                new DoubleAnimation(
                    shadowOpacity,
                    TimeSpan.FromMilliseconds(180)));

            CardShadowEffect.BeginAnimation(
                DropShadowEffect.BlurRadiusProperty,
                new DoubleAnimation(
                    shadowBlur,
                    TimeSpan.FromMilliseconds(180)));
        }

        private void LaunchButton_Click(
            object sender,
            RoutedEventArgs e) =>
            RaiseAction(ModeCardAction.Launch);

        private void MoreButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (MoreButton.ContextMenu != null)
            {
                MoreButton.ContextMenu.PlacementTarget =
                    MoreButton;
                MoreButton.ContextMenu.IsOpen = true;
            }
        }

        private void StopButton_Click(
            object sender,
            RoutedEventArgs e) =>
            RaiseAction(ModeCardAction.Stop);

        private void EditMenuItem_Click(
            object sender,
            RoutedEventArgs e) =>
            RaiseAction(ModeCardAction.Edit);

        private void DuplicateMenuItem_Click(
            object sender,
            RoutedEventArgs e) =>
            RaiseAction(ModeCardAction.Duplicate);

        private void DeleteMenuItem_Click(
            object sender,
            RoutedEventArgs e) =>
            RaiseAction(ModeCardAction.Delete);

        private void RaiseAction(ModeCardAction action)
        {
            if (_mode != null)
            {
                ActionRequested?.Invoke(
                    this,
                    new ModeCardActionEventArgs(_mode, action));
            }
        }
    }
}
