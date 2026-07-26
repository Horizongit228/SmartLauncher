using Microsoft.Win32;
using SmartLauncher.UI.Controls;
using SmartLauncher.UI.Dialogs;
using SmartLauncher.UI.Models;
using SmartLauncher.UI.Services;
using SmartLauncher.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace SmartLauncher.UI
{
    public partial class MainWindow : Window
    {
        private readonly AppCatalogService _catalogService;
        private readonly LauncherDataService _dataService;
        private readonly IconExtractionService _iconService;
        private readonly LauncherService _launcherService;
        private readonly UpdateService _updateService;
        private readonly MainViewModel _viewModel;
        private readonly DispatcherTimer _statusTimer;

        private AppCatalog _appCatalog;
        private LauncherData _launcherData;
        private AppSettings _settings;
        private LauncherMode? _editorDraft;
        private string? _editingModeId;
        private string? _editingTargetId;
        private readonly List<ProjectFileSet>
            _pendingProjectFileSets = new();
        private bool _updatingProjectFileSets;
        private bool _updateCheckRunning;
        private readonly List<ModeIconOption> _modeIconOptions = new();
        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private System.Drawing.Icon? _trayApplicationIcon;
        private GlobalHotKeyService? _globalHotKeyService;
        private bool _isExiting;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            _catalogService = new AppCatalogService();
            _dataService = new LauncherDataService();
            _iconService = new IconExtractionService();
            _launcherService = new LauncherService();
            _updateService = new UpdateService();

            _appCatalog = _catalogService.LoadOrScan();
            _iconService.PopulateIcons(_appCatalog);
            _catalogService.Save(_appCatalog);

            _launcherData =
                _dataService.LoadOrCreate(_appCatalog);

            _dataService.UpgradeKnownApplications(
                _launcherData,
                _appCatalog);

            _dataService.UpgradeYouTubeWebApplication(
                _launcherData,
                _appCatalog);

            _settings = _dataService.LoadSettings();
            DesktopShortcutService.RefreshIconIfCreated();

            TargetTypeCombo.ItemsSource =
                CreateTargetTypeOptions();
            TargetTypeCombo.DisplayMemberPath = "Name";
            TargetTypeCombo.SelectedValuePath = "Type";
            TargetTypeCombo.SelectedIndex = 0;

            RefreshApplicationSelectors();
            RefreshModeIconOptions();

            Loaded += MainWindow_Loaded;
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;
            SourceInitialized +=
                (_, _) =>
                {
                    _globalHotKeyService =
                        new GlobalHotKeyService(
                            this,
                            RestoreAndActivate);
                    AppLogService.Info(
                        _globalHotKeyService.IsRegistered
                            ? "Горячая клавиша Ctrl+L зарегистрирована."
                            : "Ctrl+L занята другим приложением.");
                };
            PreviewKeyDown +=
                (_, eventArgs) =>
                {
                    if (eventArgs.Key == Key.L
                        && Keyboard.Modifiers
                            .HasFlag(
                                ModifierKeys.Control))
                    {
                        eventArgs.Handled = true;
                        RestoreAndActivate();
                    }
                };

            _statusTimer =
                new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };

            _statusTimer.Tick += (_, _) =>
                RefreshRuntimeStates();

            InitializeTrayIcon();
            ApplySettingsToControls();
            ApplyTheme(_settings.Theme);
            ApplySidebarState(
                _settings.IsSidebarCollapsed,
                animate: false);

            RefreshModes();
            RefreshCatalog();
            UpdateGreeting();
            ShowPage(HomePage, HomeNavButton, animate: false);
            UpdateWindowStateAppearance();
        }

        private void MainWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(
                    0,
                    1,
                    TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = new QuadraticEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                });

            _statusTimer.Start();

            if (_catalogService.PerformedInitialScan)
            {
                int found =
                    _appCatalog.Applications.Count(
                        application => application.IsFound);

                ShowNotification(
                    "Первичное сканирование завершено",
                    $"Найдено приложений: {found} из "
                    + _appCatalog.Applications.Count);
            }

            if (_settings.CheckUpdatesAutomatically
                && !string.IsNullOrWhiteSpace(
                    _settings.UpdateManifestUrl))
            {
                _ = CheckForUpdatesAsync(
                    showUpToDateMessage: false);
            }
        }

        private void RefreshModes()
        {
            ModesPanel.Children.Clear();
            ModeCountText.Text =
                $"{_launcherData.Modes.Count} режимов";

            foreach (LauncherMode mode
                     in _launcherData.Modes)
            {
                var card = new ModeCard
                {
                    Mode = mode,
                    Margin = new Thickness(10)
                };

                card.ActionRequested +=
                    ModeCard_ActionRequested;

                card.SetRuntimeState(
                    _launcherService.HasTrackedProcesses(mode.Id),
                    _launcherService.GetRunningApplicationCount(mode));

                card.SetLightTheme(
                    _settings.Theme == AppTheme.Light);

                ModesPanel.Children.Add(card);
            }

            _viewModel.SetModes(
                _launcherData.Modes);
        }

        private void RefreshRuntimeStates()
        {
            foreach (ModeCard card
                     in ModesPanel.Children.OfType<ModeCard>())
            {
                if (card.Mode != null)
                {
                    card.SetRuntimeState(
                        _launcherService.HasTrackedProcesses(
                            card.Mode.Id),
                        _launcherService.GetRunningApplicationCount(
                            card.Mode));
                }
            }
        }

        private async void ModeCard_ActionRequested(
            object? sender,
            ModeCardActionEventArgs e)
        {
            switch (e.Action)
            {
                case ModeCardAction.Launch:
                    await LaunchModeAsync(e.Mode);
                    break;

                case ModeCardAction.Stop:
                    _launcherService.StopMode(e.Mode.Id);
                    OperationStatusText.Text =
                        $"Режим «{e.Mode.Name}» остановлен";
                    ShowNotification(
                        "Режим остановлен",
                        e.Mode.Name);
                    RefreshRuntimeStates();
                    break;

                case ModeCardAction.Edit:
                    OpenModeEditor(e.Mode);
                    break;

                case ModeCardAction.Duplicate:
                    DuplicateMode(e.Mode);
                    break;

                case ModeCardAction.Delete:
                    DeleteMode(e.Mode);
                    break;
            }
        }

        private async Task LaunchModeAsync(
            LauncherMode mode)
        {
            if (!mode.Targets.Any(target =>
                    target.IsEnabled
                    && !string.IsNullOrWhiteSpace(target.Value)))
            {
                MessageBox.Show(
                    "В режиме пока нет доступных действий.",
                    mode.Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            List<LaunchTarget> untrustedCommands =
                mode.Targets
                    .Where(target =>
                        target.IsEnabled
                        && target.Type
                            == LaunchTargetType.Command
                        && !target.IsTrusted)
                    .ToList();

            if (untrustedCommands.Count > 0)
            {
                string commandText =
                    string.Join(
                        Environment.NewLine
                        + Environment.NewLine,
                        untrustedCommands.Select(
                            (target, index) =>
                                $"{index + 1}. {target.DisplayName}"
                                + Environment.NewLine
                                + target.Value));

                MessageBoxResult confirmation =
                    MessageBox.Show(
                        "Сценарий содержит команды из импортированного файла."
                        + Environment.NewLine
                        + "Проверьте полный текст перед запуском:"
                        + Environment.NewLine
                        + Environment.NewLine
                        + commandText
                        + Environment.NewLine
                        + Environment.NewLine
                        + "Выполнить эти команды?",
                        "Проверка потенциально опасных команд",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                if (confirmation
                    != MessageBoxResult.Yes)
                {
                    return;
                }

                foreach (LaunchTarget command
                         in untrustedCommands)
                {
                    command.IsTrusted = true;
                }

                _dataService.Save(_launcherData);
            }

            OperationStatusText.Text =
                $"Запускается «{mode.Name}»…";

            ModeLaunchResult result =
                await _launcherService.StartModeAsync(
                    mode,
                    _settings.LaunchDelayMilliseconds);

            string summary =
                $"Запущено: {result.LaunchedCount}";

            if (result.SkippedCount > 0)
            {
                summary +=
                    $", уже работало: {result.SkippedCount}";
            }

            if (result.Errors.Count > 0)
            {
                summary +=
                    $", ошибок: {result.Errors.Count}";

                MessageBox.Show(
                    string.Join(
                        Environment.NewLine,
                        result.Errors),
                    "Не все действия выполнены",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            OperationStatusText.Text =
                $"{mode.Name}: {summary}";

            ShowNotification(mode.Name, summary);
            RefreshRuntimeStates();
        }

        private void DuplicateMode(LauncherMode source)
        {
            LauncherMode copy = source.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name += " — копия";

            foreach (LaunchTarget target in copy.Targets)
            {
                target.Id = Guid.NewGuid().ToString("N");
            }

            _launcherData.Modes.Add(copy);
            SaveModesAndRefresh();
            OpenModeEditor(copy);
        }

        private void DeleteMode(LauncherMode mode)
        {
            MessageBoxResult answer =
                MessageBox.Show(
                    $"Удалить режим «{mode.Name}»?",
                    "Smart Launcher",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            _launcherService.StopMode(mode.Id);
            _launcherData.Modes.Remove(mode);
            SaveModesAndRefresh();

            if (_editingModeId == mode.Id)
            {
                ClearEditor();
            }
        }

        private void SaveModesAndRefresh()
        {
            _dataService.Save(_launcherData);
            RefreshModes();
        }

        private void NewModeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _editingModeId = null;
            _editorDraft =
                new LauncherMode
                {
                    Name = "Новый режим",
                    Description = "Описание режима",
                    Icon = "/Assets/Icons/Settings.png",
                    AccentColor = "#2F6DF4"
                };

            LoadDraftIntoEditor();
            ShowPage(ModesPage, ModesNavButton);
            ModesList.SelectedItem = null;
            ModeNameBox.Focus();
            ModeNameBox.SelectAll();
        }

        private void OpenModeEditor(LauncherMode mode)
        {
            _editingModeId = mode.Id;
            _editorDraft = mode.Clone();
            LoadDraftIntoEditor();
            ShowPage(ModesPage, ModesNavButton);
            ModesList.SelectedItem = mode;
        }

        private void ModesList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (ModesList.SelectedItem
                is LauncherMode mode
                && mode.Id != _editingModeId)
            {
                _editingModeId = mode.Id;
                _editorDraft = mode.Clone();
                LoadDraftIntoEditor();
            }
        }

        private void LoadDraftIntoEditor()
        {
            if (_editorDraft == null)
            {
                return;
            }

            EditorTitle.Text =
                _editingModeId == null
                    ? "Новый режим"
                    : "Редактор режима";

            ModeNameBox.Text = _editorDraft.Name;
            ModeDescriptionBox.Text = _editorDraft.Description;
            SelectModeIcon(_editorDraft.Icon);
            ModeAccentBox.Text = _editorDraft.AccentColor;
            UpdateModePreview();
            ResetTargetEditor();
            RefreshTargetsList();
        }

        private void ClearEditor()
        {
            _editingModeId = null;
            _editorDraft = null;
            ModeNameBox.Clear();
            ModeDescriptionBox.Clear();
            ModeIconCombo.SelectedItem = null;
            ModeAccentBox.Text = "#2F6DF4";
            ResetTargetEditor();
            TargetsList.ItemsSource = null;
        }

        private void SaveModeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_editorDraft == null)
            {
                return;
            }

            string name = ModeNameBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(
                    "Укажите название режима.",
                    "Smart Launcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!TryNormalizeColor(
                    ModeAccentBox.Text,
                    out string accentColor))
            {
                MessageBox.Show(
                    "Цвет должен быть указан в формате #RRGGBB.",
                    "Smart Launcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _editorDraft.Name = name;
            _editorDraft.Description =
                ModeDescriptionBox.Text.Trim();
            _editorDraft.Icon =
                (ModeIconCombo.SelectedItem as ModeIconOption)?.Path
                ?? string.Empty;
            _editorDraft.AccentColor = accentColor;

            LauncherMode? existing =
                _launcherData.Modes.FirstOrDefault(
                    mode => mode.Id == _editingModeId);

            if (existing == null)
            {
                _launcherData.Modes.Add(_editorDraft);
                _editingModeId = _editorDraft.Id;
            }
            else
            {
                int index =
                    _launcherData.Modes.IndexOf(existing);
                _launcherData.Modes[index] = _editorDraft;
            }

            _dataService.Save(_launcherData);
            RefreshModes();
            OperationStatusText.Text =
                $"Режим «{_editorDraft.Name}» сохранён";
            ShowPage(HomePage, HomeNavButton);
        }

        private void DeleteEditedModeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LauncherMode? existing =
                _launcherData.Modes.FirstOrDefault(
                    mode => mode.Id == _editingModeId);

            if (existing != null)
            {
                DeleteMode(existing);
            }
        }

        private void ChooseModeIconButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog =
                new OpenFileDialog
                {
                    Title = "Выберите изображение или приложение",
                    Filter =
                        "Иконки и приложения|*.png;*.jpg;*.jpeg;*.ico;*.exe|"
                        + "Все файлы|*.*"
                };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            if (string.Equals(
                    Path.GetExtension(dialog.FileName),
                    ".exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                string extracted =
                    _iconService.ExtractIcon(
                        dialog.FileName,
                        "mode-" + Guid.NewGuid().ToString("N"));

                SelectModeIcon(
                    string.IsNullOrWhiteSpace(extracted)
                        ? dialog.FileName
                        : extracted,
                    "Пользовательская иконка");
            }
            else
            {
                SelectModeIcon(
                    dialog.FileName,
                    "Пользовательская иконка");
            }

            UpdateModePreview();
        }

        private void RefreshModeIconOptions()
        {
            string selectedPath =
                (ModeIconCombo.SelectedItem as ModeIconOption)?.Path
                ?? _editorDraft?.Icon
                ?? string.Empty;

            _modeIconOptions.Clear();
            _modeIconOptions.AddRange(
                new[]
                {
                    new ModeIconOption
                    {
                        Name = "Работа",
                        Path = "/Assets/Icons/Work.png",
                        SourceText = "Smart Launcher"
                    },
                    new ModeIconOption
                    {
                        Name = "Игры",
                        Path = "/Assets/Icons/Gaming.png",
                        SourceText = "Smart Launcher"
                    },
                    new ModeIconOption
                    {
                        Name = "Отдых",
                        Path = "/Assets/Icons/Relax.png",
                        SourceText = "Smart Launcher"
                    },
                    new ModeIconOption
                    {
                        Name = "Приложения",
                        Path = "/Assets/Icons/Apps.png",
                        SourceText = "Smart Launcher"
                    },
                    new ModeIconOption
                    {
                        Name = "Настройки",
                        Path = "/Assets/Icons/Settings.png",
                        SourceText = "Smart Launcher"
                    }
                });

            foreach (InstalledApplication application
                     in _appCatalog.Applications
                         .Where(application =>
                             application.IsFound
                             && File.Exists(
                                 application.IconPath))
                         .OrderBy(application =>
                             application.Name))
            {
                if (_modeIconOptions.Any(option =>
                        string.Equals(
                            option.Path,
                            application.IconPath,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                _modeIconOptions.Add(
                    new ModeIconOption
                    {
                        Name = application.Name,
                        Path = application.IconPath,
                        SourceText = "Из каталога приложений"
                    });
            }

            _viewModel.ModeEditor.SetIconOptions(
                _modeIconOptions);

            SelectModeIcon(selectedPath);
        }

        private void SelectModeIcon(
            string path,
            string sourceText = "Текущий режим")
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                ModeIconCombo.SelectedItem = null;
                return;
            }

            ModeIconOption? option =
                _modeIconOptions.FirstOrDefault(item =>
                    string.Equals(
                        item.Path,
                        path,
                        StringComparison.OrdinalIgnoreCase));

            if (option == null)
            {
                option =
                    new ModeIconOption
                    {
                        Name = "Пользовательская",
                        Path = path,
                        SourceText = sourceText
                    };
                _modeIconOptions.Add(option);
                _viewModel.ModeEditor.SetIconOptions(
                    _modeIconOptions);
            }

            ModeIconCombo.SelectedItem = option;
        }

        private void ModeIconCombo_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e) =>
            UpdateModePreview();

        private void ModePreviewField_Changed(
            object sender,
            TextChangedEventArgs e) =>
            UpdateModePreview();

        private void UpdateModePreview()
        {
            if (ModePreviewBorder == null)
            {
                return;
            }

            string name = ModeNameBox.Text.Trim();
            string description =
                ModeDescriptionBox.Text.Trim();

            ModePreviewName.Text =
                string.IsNullOrWhiteSpace(name)
                    ? "Новый режим"
                    : name;
            ModePreviewDescription.Text =
                string.IsNullOrWhiteSpace(description)
                    ? "Предварительный просмотр карточки"
                    : description;

            string iconPath =
                (ModeIconCombo.SelectedItem
                    as ModeIconOption)?.Path
                ?? string.Empty;

            try
            {
                ModePreviewIcon.Source =
                    string.IsNullOrWhiteSpace(iconPath)
                        ? null
                        : new BitmapImage(
                            new Uri(
                                iconPath,
                                UriKind.RelativeOrAbsolute));
            }
            catch
            {
                ModePreviewIcon.Source = null;
            }

            string accentText =
                ModeAccentBox.Text.Trim();

            if (!TryNormalizeColor(
                    accentText,
                    out string accentColor))
            {
                accentColor = "#2F6DF4";
            }

            Color accent =
                (Color)ColorConverter.ConvertFromString(
                    accentColor);
            ModePreviewBorder.Background =
                new LinearGradientBrush(
                    accent,
                    Color.FromRgb(
                        (byte)Math.Max(0, accent.R - 38),
                        (byte)Math.Max(0, accent.G - 38),
                        (byte)Math.Max(0, accent.B - 38)),
                    0);
        }

        private void TargetApplicationCombo_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (TargetApplicationCombo.SelectedItem
                    is InstalledApplication application
                && string.IsNullOrWhiteSpace(
                    TargetNameBox.Text))
            {
                TargetNameBox.Text = application.Name;
            }
        }

        private void AddTargetButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_editorDraft == null)
            {
                return;
            }

            LaunchTargetType type =
                TargetTypeCombo.SelectedValue
                    is LaunchTargetType selectedType
                        ? selectedType
                        : LaunchTargetType.Application;

            InstalledApplication? selectedApplication =
                type == LaunchTargetType.Application
                    ? TargetApplicationCombo.SelectedItem
                        as InstalledApplication
                    : null;

            string value =
                selectedApplication?.EffectiveLaunchValue
                ?? TargetValueBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(value))
            {
                MessageBox.Show(
                    type == LaunchTargetType.Application
                        ? "Выберите приложение из каталога."
                        : "Укажите путь, адрес или команду.",
                    "Smart Launcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (type == LaunchTargetType.Project)
            {
                if (!Directory.Exists(value))
                {
                    MessageBox.Show(
                        "Укажите существующую папку проекта.",
                        "Smart Launcher",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                int selectedFileCount =
                    _pendingProjectFileSets
                        .Where(set => set.IsEnabled)
                        .Sum(set => set.Files.Count);

                if (selectedFileCount == 0
                    && OpenProjectFolderCheck.IsChecked
                        != true)
                {
                    MessageBox.Show(
                        "Выберите файлы или включите открытие всей папки проекта.",
                        "Smart Launcher",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
            }

            LaunchTarget? existingTarget =
                _editorDraft.Targets.FirstOrDefault(
                    target =>
                        target.Id == _editingTargetId);

            var updatedTarget =
                new LaunchTarget
                {
                    Id =
                        existingTarget?.Id
                        ?? Guid.NewGuid().ToString("N"),
                    Name = TargetNameBox.Text.Trim(),
                    Type = type,
                    Value = value,
                    ApplicationId =
                        selectedApplication?.Id
                        ?? existingTarget?.ApplicationId
                        ?? string.Empty,
                    IsEnabled =
                        existingTarget?.IsEnabled
                        ?? true,
                    ProjectFiles =
                        type == LaunchTargetType.Project
                            ? CreateProjectFileReferences(
                                value,
                                _pendingProjectFileSets
                                    .Where(set =>
                                        set.IsEnabled)
                                    .SelectMany(set =>
                                        set.Files))
                            : new List<string>(),
                    ProjectFileSets =
                        type == LaunchTargetType.Project
                            ? CreateProjectFileSetReferences(
                                value,
                                _pendingProjectFileSets)
                            : new List<ProjectFileSet>(),
                    OpenProjectFolder =
                        type == LaunchTargetType.Project
                        && OpenProjectFolderCheck.IsChecked
                            == true
                };

            if (existingTarget == null)
            {
                _editorDraft.Targets.Add(updatedTarget);
            }
            else
            {
                int targetIndex =
                    _editorDraft.Targets.IndexOf(
                        existingTarget);

                _editorDraft.Targets[targetIndex] =
                    updatedTarget;
            }

            ResetTargetEditor();
            RefreshTargetsList();
            TargetsList.SelectedItem = updatedTarget;
        }

        private void RemoveTargetButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_editorDraft != null
                && TargetsList.SelectedItem
                is LaunchTarget target)
            {
                _editorDraft.Targets.Remove(target);
                if (_editingTargetId == target.Id)
                {
                    ResetTargetEditor();
                }

                RefreshTargetsList();
            }
        }

        private void EditTargetButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (TargetsList.SelectedItem
                is not LaunchTarget target)
            {
                MessageBox.Show(
                    "Сначала выберите действие в списке.",
                    "Smart Launcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            LoadTargetIntoEditor(target);
        }

        private void TargetsList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            if (TargetsList.SelectedItem
                is LaunchTarget target)
            {
                LoadTargetIntoEditor(target);
            }
        }

        private void LoadTargetIntoEditor(
            LaunchTarget target)
        {
            _editingTargetId = target.Id;
            TargetNameBox.Text = target.Name;
            TargetTypeCombo.SelectedValue = target.Type;
            TargetValueBox.Text = target.Value;
            if (target.Type == LaunchTargetType.Application)
            {
                TargetApplicationCombo.SelectedItem =
                    _appCatalog.Applications.FirstOrDefault(
                        application =>
                            (!string.IsNullOrWhiteSpace(
                                target.ApplicationId)
                             && application.Id
                                 == target.ApplicationId)
                            || string.Equals(
                                application.EffectiveLaunchValue,
                                target.Value,
                                StringComparison.OrdinalIgnoreCase));
            }

            _pendingProjectFileSets.Clear();

            if (target.Type == LaunchTargetType.Project)
            {
                IEnumerable<ProjectFileSet> sourceSets =
                    target.ProjectFileSets.Count > 0
                        ? target.ProjectFileSets
                        : new[]
                        {
                            new ProjectFileSet
                            {
                                Name = "Основной набор",
                                Files =
                                    target.ProjectFiles.ToList()
                            }
                        };

                foreach (ProjectFileSet sourceSet
                         in sourceSets)
                {
                    var editorSet =
                        new ProjectFileSet
                        {
                            Id = sourceSet.Id,
                            Name = sourceSet.Name,
                            IsEnabled =
                                sourceSet.IsEnabled,
                            Files =
                                sourceSet.Files
                                    .Select(fileReference =>
                                        ResolveProjectFileReference(
                                            target.Value,
                                            fileReference))
                                    .ToList()
                        };
                    _pendingProjectFileSets.Add(
                        editorSet);
                }

                OpenProjectFolderCheck.IsChecked =
                    target.OpenProjectFolder;
            }

            AddTargetButton.Content =
                "Сохранить изменения";
            TargetEditorStatusText.Text =
                $"Редактирование: {target.DisplayName}";
            CancelTargetEditButton.Visibility =
                Visibility.Visible;
            UpdateProjectEditorState();
            TargetNameBox.Focus();
        }

        private void CancelTargetEditButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ResetTargetEditor();
            TargetsList.SelectedItem = null;
        }

        private void MoveTargetUpButton_Click(
            object sender,
            RoutedEventArgs e) =>
            MoveSelectedTarget(-1);

        private void MoveTargetDownButton_Click(
            object sender,
            RoutedEventArgs e) =>
            MoveSelectedTarget(1);

        private void MoveSelectedTarget(int offset)
        {
            if (_editorDraft == null
                || TargetsList.SelectedItem
                is not LaunchTarget target)
            {
                return;
            }

            int oldIndex =
                _editorDraft.Targets.IndexOf(target);
            int newIndex = oldIndex + offset;

            if (newIndex < 0
                || newIndex >= _editorDraft.Targets.Count)
            {
                return;
            }

            _editorDraft.Targets.RemoveAt(oldIndex);
            _editorDraft.Targets.Insert(newIndex, target);
            RefreshTargetsList();
            TargetsList.SelectedItem = target;
        }

        private void RefreshTargetsList()
        {
            TargetsList.ItemsSource = null;
            TargetsList.ItemsSource = _editorDraft?.Targets;
        }

        private void BrowseTargetButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LaunchTargetType type =
                TargetTypeCombo.SelectedValue
                    is LaunchTargetType selectedType
                        ? selectedType
                        : LaunchTargetType.Application;

            if (type == LaunchTargetType.Application)
            {
                ShowPage(CatalogPage, CatalogNavButton);
                CatalogSearchBox.Focus();
                return;
            }

            if (type == LaunchTargetType.Folder)
            {
                using var dialog =
                    new System.Windows.Forms.FolderBrowserDialog
                    {
                        Description =
                            "Выберите папку для запуска",
                        UseDescriptionForTitle = true
                    };

                if (dialog.ShowDialog()
                    == System.Windows.Forms.DialogResult.OK)
                {
                    TargetValueBox.Text =
                        dialog.SelectedPath;
                }

                return;
            }

            if (type == LaunchTargetType.Project)
            {
                using var dialog =
                    new System.Windows.Forms.FolderBrowserDialog
                    {
                        Description =
                            "Выберите корневую папку проекта",
                        UseDescriptionForTitle = true,
                        SelectedPath =
                            Directory.Exists(
                                TargetValueBox.Text.Trim())
                                ? TargetValueBox.Text.Trim()
                                : string.Empty
                    };

                if (dialog.ShowDialog()
                    == System.Windows.Forms.DialogResult.OK)
                {
                    TargetValueBox.Text =
                        dialog.SelectedPath;

                    if (string.IsNullOrWhiteSpace(
                            TargetNameBox.Text))
                    {
                        TargetNameBox.Text =
                            new DirectoryInfo(
                                dialog.SelectedPath).Name;
                    }

                    ResetProjectFileSets();
                    UpdateProjectFilesSummary();
                }

                return;
            }

            if (type is LaunchTargetType.Website
                or LaunchTargetType.Steam
                or LaunchTargetType.Command)
            {
                TargetValueBox.Focus();
                return;
            }

            var fileDialog =
                new OpenFileDialog
                {
                    Title = "Выберите файл",
                    Filter =
                        type == LaunchTargetType.Application
                            ? "Приложения|*.exe|Все файлы|*.*"
                            : "Все файлы|*.*"
                };

            if (fileDialog.ShowDialog(this) == true)
            {
                TargetValueBox.Text =
                    fileDialog.FileName;

                if (string.IsNullOrWhiteSpace(
                        TargetNameBox.Text))
                {
                    TargetNameBox.Text =
                        Path.GetFileNameWithoutExtension(
                            fileDialog.FileName);
                }
            }
        }

        private void TargetTypeCombo_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            UpdateProjectEditorState();
        }

        private void SelectProjectFilesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string projectDirectory =
                TargetValueBox.Text.Trim();

            if (!Directory.Exists(projectDirectory))
            {
                MessageBox.Show(
                    "Сначала выберите корневую папку проекта.",
                    "Smart Launcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            ChooseProjectFiles(projectDirectory);
        }

        private void ClearProjectFilesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            CurrentProjectFileSet()?.Files.Clear();
            UpdateProjectFilesSummary();
        }

        private void RemoveProjectFilesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            List<string> selectedFiles =
                ProjectFilesList.SelectedItems
                    .Cast<string>()
                    .ToList();

            foreach (string selectedFile
                     in selectedFiles)
            {
                CurrentProjectFileSet()?.Files.Remove(
                    selectedFile);
            }

            UpdateProjectFilesSummary();
        }

        private void ChooseProjectFiles(
            string projectDirectory)
        {
            var dialog =
                new OpenFileDialog
                {
                    Title =
                        "Выберите все файлы, которые нужно открыть",
                    InitialDirectory =
                        projectDirectory,
                    Multiselect = true,
                    CheckFileExists = true,
                    Filter =
                        "Файлы проекта|*.sln;*.slnx;*.csproj;*.fsproj;*.vbproj;*.code-workspace;"
                        + "*.cs;*.fs;*.vb;*.xaml;*.json;*.xml;*.md;*.txt;*.html;*.css;*.js;*.ts;*.tsx;*.jsx|"
                        + "Все файлы|*.*"
                };

            if (dialog.ShowDialog(this) != true)
            {
                UpdateProjectFilesSummary();
                return;
            }

            foreach (string fileName
                     in dialog.FileNames)
            {
                ProjectFileSet? currentSet =
                    CurrentProjectFileSet();

                if (currentSet != null
                    && !currentSet.Files.Contains(
                        fileName,
                        StringComparer.OrdinalIgnoreCase))
                {
                    currentSet.Files.Add(
                        fileName);
                }
            }

            UpdateProjectFilesSummary();
        }

        private ProjectFileSet? CurrentProjectFileSet()
        {
            return ProjectFileSetsCombo.SelectedItem
                    as ProjectFileSet
                ?? _pendingProjectFileSets.FirstOrDefault();
        }

        private void ResetProjectFileSets()
        {
            _pendingProjectFileSets.Clear();
            _pendingProjectFileSets.Add(
                new ProjectFileSet
                {
                    Name = "Основной набор"
                });
        }

        private void ProjectFileSetsCombo_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!_updatingProjectFileSets)
            {
                UpdateProjectFilesSummary();
            }
        }

        private void ProjectFileSetEnabledCheck_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (_updatingProjectFileSets)
            {
                return;
            }

            ProjectFileSet? currentSet =
                CurrentProjectFileSet();
            if (currentSet != null)
            {
                currentSet.IsEnabled =
                    ProjectFileSetEnabledCheck.IsChecked
                    == true;
                UpdateProjectFilesSummary();
            }
        }

        private void AddProjectFileSetButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog =
                new TextInputDialog(
                    "Новый набор файлов",
                    "Как назвать этот набор?",
                    $"Набор {_pendingProjectFileSets.Count + 1}")
                {
                    Owner = this
                };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var fileSet =
                new ProjectFileSet
                {
                    Name = dialog.Value
                };
            _pendingProjectFileSets.Add(fileSet);
            UpdateProjectFilesSummary();
            ProjectFileSetsCombo.SelectedItem = fileSet;
        }

        private void RenameProjectFileSetButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ProjectFileSet? fileSet =
                CurrentProjectFileSet();
            if (fileSet == null)
            {
                return;
            }

            var dialog =
                new TextInputDialog(
                    "Переименование набора",
                    "Новое название набора:",
                    fileSet.Name)
                {
                    Owner = this
                };

            if (dialog.ShowDialog() == true)
            {
                fileSet.Name = dialog.Value;
                UpdateProjectFilesSummary();
                ProjectFileSetsCombo.SelectedItem =
                    fileSet;
            }
        }

        private void DeleteProjectFileSetButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ProjectFileSet? fileSet =
                CurrentProjectFileSet();
            if (fileSet == null)
            {
                return;
            }

            if (_pendingProjectFileSets.Count == 1)
            {
                fileSet.Files.Clear();
                fileSet.Name = "Основной набор";
                fileSet.IsEnabled = true;
            }
            else
            {
                _pendingProjectFileSets.Remove(fileSet);
            }

            UpdateProjectFilesSummary();
        }

        private void ProjectFilesList_PreviewDragOver(
            object sender,
            System.Windows.DragEventArgs e)
        {
            e.Effects =
                e.Data.GetDataPresent(
                    System.Windows.DataFormats.FileDrop)
                    ? System.Windows.DragDropEffects.Copy
                    : System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void ProjectFilesList_Drop(
            object sender,
            System.Windows.DragEventArgs e)
        {
            if (e.Data.GetData(
                    System.Windows.DataFormats.FileDrop)
                is not string[] paths)
            {
                return;
            }

            ProjectFileSet? fileSet =
                CurrentProjectFileSet();
            if (fileSet == null)
            {
                return;
            }

            foreach (string path
                     in paths.Where(File.Exists))
            {
                if (!fileSet.Files.Contains(
                        path,
                        StringComparer.OrdinalIgnoreCase))
                {
                    fileSet.Files.Add(path);
                }
            }

            UpdateProjectFilesSummary();
        }

        private void ResetTargetEditor()
        {
            _editingTargetId = null;
            ResetProjectFileSets();

            if (TargetNameBox == null)
            {
                return;
            }

            TargetNameBox.Clear();
            TargetValueBox.Clear();
            TargetApplicationCombo.SelectedItem = null;
            TargetTypeCombo.SelectedIndex = 0;
            OpenProjectFolderCheck.IsChecked = false;
            AddTargetButton.Content =
                "+  Добавить в сценарий";
            TargetEditorStatusText.Text =
                "Новое действие";
            CancelTargetEditButton.Visibility =
                Visibility.Collapsed;
            UpdateProjectEditorState();
        }

        private void UpdateProjectEditorState()
        {
            if (ProjectFilesPanel == null)
            {
                return;
            }

            LaunchTargetType type =
                TargetTypeCombo.SelectedValue
                    is LaunchTargetType selectedType
                        ? selectedType
                        : LaunchTargetType.Application;

            bool isProject =
                type == LaunchTargetType.Project;

            bool isApplication =
                type == LaunchTargetType.Application;

            TargetApplicationCombo.Visibility =
                isApplication
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            TargetValueBox.Visibility =
                isApplication
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            ProjectFilesPanel.Visibility =
                isProject
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            bool canBrowse =
                type is LaunchTargetType.Application
                    or LaunchTargetType.File
                    or LaunchTargetType.Folder
                    or LaunchTargetType.Project;

            BrowseTargetButton.Visibility =
                canBrowse
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            BrowseTargetButton.Content =
                type == LaunchTargetType.Application
                    ? "Каталог"
                    :
                type is LaunchTargetType.Folder
                    or LaunchTargetType.Project
                    ? "Папка"
                    : "Обзор";

            TargetValueLabel.Text = type switch
            {
                LaunchTargetType.Application =>
                    "Приложение",
                LaunchTargetType.Website =>
                    "Адрес сайта",
                LaunchTargetType.File =>
                    "Путь к файлу",
                LaunchTargetType.Folder =>
                    "Путь к папке",
                LaunchTargetType.Steam =>
                    "Steam ID или ссылка",
                LaunchTargetType.Command =>
                    "Команда Windows",
                LaunchTargetType.Project =>
                    "Корневая папка проекта",
                _ => "Значение"
            };

            TargetHelpText.Text = type switch
            {
                LaunchTargetType.Application =>
                    "Выберите найденное приложение. Кнопка «Каталог» позволяет добавить своё.",
                LaunchTargetType.Website =>
                    "Введите полный адрес. Например: https://calendar.google.com.",
                LaunchTargetType.File =>
                    "Выберите документ, таблицу, проект или другой файл.",
                LaunchTargetType.Folder =>
                    "Выберите папку, которая должна открыться в Проводнике.",
                LaunchTargetType.Steam =>
                    "Введите числовой ID игры или ссылку вида steam://rungameid/...",
                LaunchTargetType.Command =>
                    "Введите команду так, как запускали бы её в cmd.exe.",
                LaunchTargetType.Project =>
                    "Сначала выберите папку проекта, затем добавьте все нужные файлы ниже.",
                _ => string.Empty
            };

            TargetValueBox.ToolTip =
                TargetHelpText.Text;

            UpdateProjectFilesSummary();
        }

        private void UpdateProjectFilesSummary()
        {
            if (ProjectFilesSummaryText == null)
            {
                return;
            }

            ProjectFileSet? currentSet =
                CurrentProjectFileSet();

            _updatingProjectFileSets = true;
            try
            {
                string selectedSetId =
                    (ProjectFileSetsCombo.SelectedItem
                        as ProjectFileSet)?.Id
                    ?? currentSet?.Id
                    ?? string.Empty;

                ProjectFileSetsCombo.ItemsSource = null;
                ProjectFileSetsCombo.ItemsSource =
                    _pendingProjectFileSets.ToList();
                ProjectFileSetsCombo.SelectedItem =
                    _pendingProjectFileSets.FirstOrDefault(
                        set => set.Id == selectedSetId)
                    ?? _pendingProjectFileSets.FirstOrDefault();

                currentSet = CurrentProjectFileSet();
                ProjectFileSetEnabledCheck.IsChecked =
                    currentSet?.IsEnabled ?? true;
            }
            finally
            {
                _updatingProjectFileSets = false;
            }

            List<string> currentFiles =
                currentSet?.Files
                ?? new List<string>();

            ProjectFilesList.ItemsSource = null;
            ProjectFilesList.ItemsSource =
                currentFiles.ToList();

            int totalFiles =
                _pendingProjectFileSets.Sum(
                    set => set.Files.Count);

            if (totalFiles == 0)
            {
                ProjectFilesSummaryText.Text =
                    "Перетащите файлы сюда или нажмите «Добавить файлы»";
                return;
            }

            string preview =
                string.Join(
                    ", ",
                    currentFiles
                        .Take(3)
                        .Select(Path.GetFileName));

            if (currentFiles.Count > 3)
            {
                preview +=
                    $" и ещё {currentFiles.Count - 3}";
            }

            ProjectFilesSummaryText.Text =
                $"{_pendingProjectFileSets.Count} наборов, "
                + $"{totalFiles} файлов всего"
                + (currentFiles.Count > 0
                    ? $" • выбранный: {preview}"
                    : " • выбранный набор пуст");
        }

        private static List<string>
            CreateProjectFileReferences(
                string projectDirectory,
                IEnumerable<string> filePaths)
        {
            string rootPath =
                Path.GetFullPath(projectDirectory);

            return filePaths
                .Select(filePath =>
                {
                    string fullPath =
                        Path.GetFullPath(filePath);
                    string relativePath =
                        Path.GetRelativePath(
                            rootPath,
                            fullPath);

                    return relativePath == ".."
                           || relativePath.StartsWith(
                               ".." + Path.DirectorySeparatorChar,
                               StringComparison.Ordinal)
                        ? fullPath
                        : relativePath;
                })
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<ProjectFileSet>
            CreateProjectFileSetReferences(
                string projectDirectory,
                IEnumerable<ProjectFileSet> fileSets)
        {
            return fileSets
                .Select(fileSet =>
                    new ProjectFileSet
                    {
                        Id = fileSet.Id,
                        Name = fileSet.Name,
                        IsEnabled =
                            fileSet.IsEnabled,
                        Files =
                            CreateProjectFileReferences(
                                projectDirectory,
                                fileSet.Files)
                    })
                .ToList();
        }

        private static string ResolveProjectFileReference(
            string projectDirectory,
            string fileReference)
        {
            if (Path.IsPathFullyQualified(fileReference))
            {
                return fileReference;
            }

            try
            {
                return Path.GetFullPath(
                    Path.Combine(
                        projectDirectory,
                        fileReference));
            }
            catch
            {
                return fileReference;
            }
        }

        private void RefreshCatalog()
        {
            _viewModel.SetApplications(
                _appCatalog.Applications);

            int found =
                _appCatalog.Applications.Count(
                    app => app.IsFound);

            ScanStatusText.Text =
                $"Найдено: {found} из "
                + _appCatalog.Applications.Count
                + $" • обновлено {_appCatalog.ScannedAtUtc.ToLocalTime():g}";

            FoundAppsCountText.Text =
                $"{found} приложений";
        }

        private void CatalogFilter_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (CatalogList != null)
            {
                RefreshCatalog();
            }
        }

        private void RefreshApplicationSelectors()
        {
            string selectedId =
                (TargetApplicationCombo.SelectedItem
                    as InstalledApplication)?.Id
                ?? string.Empty;

            _viewModel.SetApplications(
                _appCatalog.Applications);
            TargetApplicationCombo.SelectedItem =
                _viewModel.ApplicationOptions
                    .FirstOrDefault(application =>
                        application.Id == selectedId);
        }

        private async void RescanButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ScanProgress.Visibility = Visibility.Visible;
            ScanStatusText.Text =
                "Сканирование приложений…";

            try
            {
                AppCatalog refreshed =
                    await Task.Run(() =>
                        _catalogService.Refresh(_appCatalog));

                await Task.Run(() =>
                    _iconService.PopulateIcons(refreshed));

                _appCatalog = refreshed;
                _catalogService.Save(_appCatalog);
                RefreshApplicationSelectors();
                RefreshModeIconOptions();

                if (_dataService.UpgradeKnownApplications(
                        _launcherData,
                        _appCatalog))
                {
                    RefreshModes();
                }

                if (_dataService
                    .UpgradeYouTubeWebApplication(
                        _launcherData,
                        _appCatalog))
                {
                    RefreshModes();
                }

                RefreshCatalog();

                ShowNotification(
                    "Сканирование завершено",
                    ScanStatusText.Text);
            }
            catch (Exception exception)
            {
                ScanStatusText.Text =
                    "Ошибка сканирования";

                MessageBox.Show(
                    exception.Message,
                    "Smart Launcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ScanProgress.Visibility =
                    Visibility.Collapsed;
            }
        }

        private void ChoosePathButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Button button
                || button.Tag
                is not InstalledApplication application)
            {
                return;
            }

            var dialog =
                new OpenFileDialog
                {
                    Title =
                        $"Укажите путь к {application.Name}",
                    Filter = "Приложения|*.exe"
                };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                _catalogService.SetManualPath(
                    _appCatalog,
                    application.Id,
                    application.Name,
                    dialog.FileName);

                application.IconPath =
                    _iconService.ExtractIcon(
                        dialog.FileName,
                        application.Id);

                _catalogService.Save(_appCatalog);
                RefreshCatalog();
                RefreshApplicationSelectors();
                RefreshModeIconOptions();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    exception.Message,
                    "Smart Launcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void TestAppButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is Button button
                && button.Tag
                is InstalledApplication application)
            {
                if (!_launcherService.Open(
                        application.EffectiveLaunchValue))
                {
                    MessageBox.Show(
                        "Не удалось запустить приложение.",
                        application.Name,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        private void AddCatalogApplicationButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog =
                new AddApplicationDialog
                {
                    Owner = this
                };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                InstalledApplication application =
                    _catalogService.AddManualApplication(
                        _appCatalog,
                        dialog.ApplicationName,
                        dialog.ExecutablePath,
                        dialog.Category);

                application.IconPath =
                    _iconService.ExtractIcon(
                        application.ExecutablePath,
                        application.Id);

                _catalogService.Save(_appCatalog);
                RefreshCatalog();
                RefreshApplicationSelectors();
                RefreshModeIconOptions();
                CatalogSearchBox.Text =
                    application.Name;
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    exception.Message,
                    "Не удалось добавить приложение",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void DeleteCatalogApplicationButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Button button
                || button.Tag
                    is not InstalledApplication application
                || !application.IsUserAdded)
            {
                return;
            }

            MessageBoxResult result =
                MessageBox.Show(
                    $"Удалить пользовательскую запись «{application.Name}»?\n"
                    + "Само приложение и его файлы удалены не будут.",
                    "Удаление из каталога",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            if (_catalogService.RemoveUserApplication(
                    _appCatalog,
                    application.Id))
            {
                _catalogService.Save(_appCatalog);
                RefreshCatalog();
                RefreshApplicationSelectors();
                RefreshModeIconOptions();
            }
        }

        private void OpenCatalogButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!File.Exists(
                    _catalogService.CatalogFilePath))
            {
                return;
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments =
                        "/select,\""
                        + _catalogService.CatalogFilePath
                        + "\"",
                    UseShellExecute = true
                });
        }

        private void ApplySettingsToControls()
        {
            ThemeCombo.SelectedIndex =
                _settings.Theme == AppTheme.Dark
                    ? 0
                    : 1;

            AutoStartCheck.IsChecked =
                _settings.StartWithWindows;
            CloseToTrayCheck.IsChecked =
                _settings.CloseToTray;
            DesktopShortcutStatusText.Text =
                DesktopShortcutService.IsCreated
                    ? "Ярлык установлен и использует актуальную иконку."
                    : "Обычно создаётся установщиком; здесь его можно восстановить.";
            DelaySlider.Value =
                _settings.LaunchDelayMilliseconds;
            TransparencySlider.Value =
                _settings.WindowTransparency;
            AutomaticUpdatesCheck.IsChecked =
                _settings.CheckUpdatesAutomatically;
            UpdateManifestUrlBox.Text =
                _settings.UpdateManifestUrl;
            UpdateStatusText.Text =
                string.IsNullOrWhiteSpace(
                    _settings.UpdateManifestUrl)
                    ? "Укажите адрес после публикации установщика."
                    : "Готово к проверке.";
            DataPathText.Text =
                "Режимы: "
                + _dataService.DataFilePath
                + Environment.NewLine
                + "Настройки: "
                + _dataService.SettingsFilePath
                + Environment.NewLine
                + "Журнал: "
                + AppLogService.CurrentLogPath;
        }

        private void SaveSettingsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _settings.Theme =
                ThemeCombo.SelectedIndex == 1
                    ? AppTheme.Light
                    : AppTheme.Dark;

            _settings.StartWithWindows =
                AutoStartCheck.IsChecked == true;
            _settings.CloseToTray =
                CloseToTrayCheck.IsChecked == true;
            _settings.LaunchDelayMilliseconds =
                (int)DelaySlider.Value;
            _settings.WindowTransparency =
                TransparencySlider.Value;
            _settings.CheckUpdatesAutomatically =
                AutomaticUpdatesCheck.IsChecked == true;
            _settings.UpdateManifestUrl =
                UpdateManifestUrlBox.Text.Trim();
            _settings.IsSidebarCollapsed =
                SidebarColumn.Width.Value < 100;

            try
            {
                AutoStartService.SetEnabled(
                    _settings.StartWithWindows);

                _dataService.SaveSettings(_settings);
                ApplyTheme(_settings.Theme);

                ShowNotification(
                    "Smart Launcher",
                    "Настройки сохранены");
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    exception.Message,
                    "Не удалось сохранить настройки",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void RestoreDesktopShortcutButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                DesktopShortcutService.SetEnabled(true);
                DesktopShortcutStatusText.Text =
                    "Ярлык восстановлен с актуальной иконкой.";
                ShowNotification(
                    "Smart Launcher",
                    "Ярлык на рабочем столе восстановлен");
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    exception.Message,
                    "Не удалось восстановить ярлык",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void DelaySlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (DelayValueText != null)
            {
                DelayValueText.Text =
                    $"{(int)e.NewValue} мс";
            }
        }

        private void TransparencySlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (TransparencyValueText != null)
            {
                TransparencyValueText.Text =
                    $"{e.NewValue:P0}";
            }

            if (IsLoaded)
            {
                _settings.WindowTransparency =
                    e.NewValue;
                ApplyTheme(_settings.Theme);
            }
        }

        private async void CheckUpdatesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _settings.UpdateManifestUrl =
                UpdateManifestUrlBox.Text.Trim();
            await CheckForUpdatesAsync(
                showUpToDateMessage: true);
        }

        private async Task CheckForUpdatesAsync(
            bool showUpToDateMessage)
        {
            if (_updateCheckRunning)
            {
                return;
            }

            string manifestUrl =
                UpdateManifestUrlBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                if (showUpToDateMessage)
                {
                    MessageBox.Show(
                        "Сначала укажите HTTPS-адрес манифеста обновления.",
                        "Обновления",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            _updateCheckRunning = true;
            UpdateStatusText.Text =
                "Проверка обновлений…";

            try
            {
                Version currentVersion =
                    typeof(MainWindow)
                        .Assembly
                        .GetName()
                        .Version
                    ?? new Version(0, 0);

                UpdateCheckResult result =
                    await _updateService.CheckAsync(
                        manifestUrl,
                        currentVersion);

                if (!result.IsUpdateAvailable)
                {
                    UpdateStatusText.Text =
                        $"Установлена актуальная версия {currentVersion.ToString(3)}.";
                    if (showUpToDateMessage)
                    {
                        ShowNotification(
                            "Smart Launcher",
                            "Установлена актуальная версия");
                    }

                    return;
                }

                UpdateStatusText.Text =
                    $"Доступна версия {result.Manifest.Version}.";
                string notes =
                    string.IsNullOrWhiteSpace(
                        result.Manifest.ReleaseNotes)
                        ? "Описание изменений не указано."
                        : result.Manifest.ReleaseNotes;

                MessageBoxResult answer =
                    MessageBox.Show(
                        $"Доступна версия {result.Manifest.Version}."
                        + Environment.NewLine
                        + Environment.NewLine
                        + notes
                        + Environment.NewLine
                        + Environment.NewLine
                        + "Скачать и запустить установщик?",
                        "Обновление Smart Launcher",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }

                var progress =
                    new Progress<double>(value =>
                    {
                        UpdateStatusText.Text =
                            $"Загрузка обновления: {value:P0}";
                    });

                string installerPath =
                    await _updateService
                        .DownloadInstallerAsync(
                            result.Manifest,
                            progress);

                UpdateStatusText.Text =
                    "Запуск установщика…";
                UpdateService.StartInstaller(
                    installerPath);
                _isExiting = true;
                Close();
            }
            catch (Exception exception)
            {
                AppLogService.Error(
                    "Не удалось проверить или загрузить обновление.",
                    exception);
                UpdateStatusText.Text =
                    "Не удалось проверить обновления.";

                if (showUpToDateMessage)
                {
                    MessageBox.Show(
                        exception.Message,
                        "Ошибка обновления",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            finally
            {
                _updateCheckRunning = false;
            }
        }

        private void ImportModesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog =
                new OpenFileDialog
                {
                    Title = "Импорт режимов",
                    Filter =
                        "Smart Launcher JSON|*.json|Все файлы|*.*"
                };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                _launcherData =
                    _dataService.Import(dialog.FileName);

                ClearEditor();
                RefreshModes();
                ShowPage(HomePage, HomeNavButton);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    exception.Message,
                    "Ошибка импорта",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ExportModesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog =
                new SaveFileDialog
                {
                    Title = "Экспорт режимов",
                    Filter = "Smart Launcher JSON|*.json",
                    FileName = "smart-launcher-modes.json"
                };

            if (dialog.ShowDialog(this) == true)
            {
                _dataService.Export(
                    dialog.FileName,
                    _launcherData);

                ShowNotification(
                    "Экспорт завершён",
                    dialog.FileName);
            }
        }

        private void OpenLogButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string logPath =
                AppLogService.CurrentLogPath;

            if (!File.Exists(logPath))
            {
                AppLogService.Info(
                    "Журнал открыт пользователем.");
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments =
                        "/select,\"" + logPath + "\"",
                    UseShellExecute = true
                });
        }

        private void ApplyTheme(AppTheme theme)
        {
            bool light = theme == AppTheme.Light;
            byte surfaceAlpha =
                (byte)Math.Clamp(
                    (int)Math.Round(
                        _settings.WindowTransparency
                        * 255),
                    0,
                    255);

            SetBrush("WindowBrush",
                light ? "#F2F4F8" : "#101010",
                surfaceAlpha);
            SetBrush("SidebarBrush",
                light ? "#FFFFFF" : "#181818",
                (byte)Math.Min(
                    255,
                    surfaceAlpha + 8));
            SetBrush("PanelBrush",
                light ? "#FFFFFF" : "#171717",
                (byte)Math.Min(
                    255,
                    surfaceAlpha + 4));
            SetBrush("InputBrush",
                light ? "#EEF1F6" : "#202020");
            SetBrush("BorderBrush",
                light ? "#D8DEE9" : "#2B2B2B");
            SetBrush("PrimaryTextBrush",
                light ? "#172033" : "#FFFFFF");
            SetBrush("SecondaryTextBrush",
                light ? "#465168" : "#C8C8C8");
            SetBrush("MutedTextBrush",
                light ? "#788398" : "#8D8D8D");
            SetBrush("SecondaryButtonBrush",
                light ? "#E8EDF5" : "#252A35");
            SetBrush("InputFocusBrush",
                light ? "#FFFFFF" : "#242A38");

            foreach (ModeCard card
                     in ModesPanel.Children.OfType<ModeCard>())
            {
                card.SetLightTheme(light);
            }
        }

        private void SetBrush(
            string resourceKey,
            string colorText,
            byte alpha = 255)
        {
            Color color =
                (Color)ColorConverter.ConvertFromString(
                    colorText);
            color.A = alpha;

            Resources[resourceKey] =
                new SolidColorBrush(color);
        }

        private void CollapseSidebarButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            bool collapse =
                SidebarColumn.Width.Value >= 100;

            ApplySidebarState(collapse, animate: true);
            _settings.IsSidebarCollapsed = collapse;
            _dataService.SaveSettings(_settings);
        }

        private void ApplySidebarState(
            bool collapsed,
            bool animate)
        {
            double width = collapsed ? 82 : 230;

            if (animate)
            {
                var animation =
                    new GridLengthAnimation
                    {
                        From = SidebarColumn.Width,
                        To = new GridLength(width),
                        Duration =
                            TimeSpan.FromMilliseconds(300),
                        EasingFunction =
                            new QuadraticEase
                            {
                                EasingMode =
                                    EasingMode.EaseOut
                            }
                    };

                animation.Completed += (_, _) =>
                {
                    SidebarColumn.BeginAnimation(
                        ColumnDefinition.WidthProperty,
                        null);

                    SidebarColumn.Width =
                        new GridLength(width);
                };

                SidebarColumn.BeginAnimation(
                    ColumnDefinition.WidthProperty,
                    animation);
            }
            else
            {
                SidebarColumn.Width =
                    new GridLength(width);
            }

            ApplySidebarTextAnimation(
                collapsed,
                animate);

            SidebarToggleButton.Content =
                collapsed
                    ? "»"
                    : "‹    Свернуть";

            SidebarToggleButton.ToolTip =
                collapsed
                    ? "Развернуть боковое меню"
                    : "Свернуть боковое меню";

            HomeNavButton.HorizontalContentAlignment =
                collapsed
                    ? System.Windows.HorizontalAlignment.Center
                    : System.Windows.HorizontalAlignment.Left;
            ModesNavButton.HorizontalContentAlignment =
                HomeNavButton.HorizontalContentAlignment;
            CatalogNavButton.HorizontalContentAlignment =
                HomeNavButton.HorizontalContentAlignment;
            SettingsNavButton.HorizontalContentAlignment =
                HomeNavButton.HorizontalContentAlignment;
        }

        private void ApplySidebarTextAnimation(
            bool collapsed,
            bool animate)
        {
            FrameworkElement[] textElements =
            {
                SidebarTitle,
                SidebarVersionText,
                HomeNavText,
                ModesNavText,
                CatalogNavText,
                SettingsNavText
            };

            if (!animate)
            {
                foreach (FrameworkElement element
                         in textElements)
                {
                    element.BeginAnimation(
                        OpacityProperty,
                        null);
                    element.Opacity =
                        collapsed ? 0 : 1;
                    element.Visibility =
                        collapsed
                            ? Visibility.Collapsed
                            : Visibility.Visible;
                    element.RenderTransform =
                        new TranslateTransform();
                }

                return;
            }

            foreach (FrameworkElement element
                     in textElements)
            {
                element.Visibility = Visibility.Visible;

                var translate =
                    new TranslateTransform(
                        collapsed ? 0 : -10,
                        0);
                element.RenderTransform = translate;

                var opacityAnimation =
                    new DoubleAnimation
                    {
                        From = collapsed ? 1 : 0,
                        To = collapsed ? 0 : 1,
                        Duration =
                            TimeSpan.FromMilliseconds(
                                collapsed ? 130 : 210),
                        BeginTime =
                            collapsed
                                ? TimeSpan.Zero
                                : TimeSpan.FromMilliseconds(75),
                        EasingFunction =
                            new QuadraticEase
                            {
                                EasingMode =
                                    EasingMode.EaseOut
                            }
                    };

                var slideAnimation =
                    new DoubleAnimation
                    {
                        From = collapsed ? 0 : -10,
                        To = collapsed ? -10 : 0,
                        Duration =
                            TimeSpan.FromMilliseconds(
                                collapsed ? 150 : 230),
                        BeginTime =
                            collapsed
                                ? TimeSpan.Zero
                                : TimeSpan.FromMilliseconds(60),
                        EasingFunction =
                            new QuadraticEase
                            {
                                EasingMode =
                                    EasingMode.EaseOut
                            }
                    };

                if (collapsed)
                {
                    opacityAnimation.Completed +=
                        (_, _) =>
                        {
                            element.Visibility =
                                Visibility.Collapsed;
                        };
                }

                element.BeginAnimation(
                    OpacityProperty,
                    opacityAnimation);
                translate.BeginAnimation(
                    TranslateTransform.XProperty,
                    slideAnimation);
            }
        }

        private void HomeNavButton_Click(
            object sender,
            RoutedEventArgs e) =>
            ShowPage(HomePage, HomeNavButton);

        private void ModesNavButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_editorDraft == null)
            {
                LauncherMode? first =
                    _launcherData.Modes.FirstOrDefault();

                if (first != null)
                {
                    _editingModeId = first.Id;
                    _editorDraft = first.Clone();
                    LoadDraftIntoEditor();
                    ModesList.SelectedItem = first;
                }
            }

            ShowPage(ModesPage, ModesNavButton);
        }

        private void CatalogNavButton_Click(
            object sender,
            RoutedEventArgs e) =>
            ShowPage(CatalogPage, CatalogNavButton);

        private void SettingsNavButton_Click(
            object sender,
            RoutedEventArgs e) =>
            ShowPage(SettingsPage, SettingsNavButton);

        private void ShowPage(
            FrameworkElement page,
            Button activeButton,
            bool animate = true)
        {
            FrameworkElement[] pages =
            {
                HomePage,
                ModesPage,
                CatalogPage,
                SettingsPage
            };

            foreach (FrameworkElement candidate in pages)
            {
                candidate.Visibility =
                    candidate == page
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }

            Button[] buttons =
            {
                HomeNavButton,
                ModesNavButton,
                CatalogNavButton,
                SettingsNavButton
            };

            foreach (Button button in buttons)
            {
                button.Tag =
                    button == activeButton;

                button.ClearValue(
                    Button.BackgroundProperty);
                button.ClearValue(
                    Button.ForegroundProperty);
            }

            if (!animate)
            {
                page.Opacity = 1;
                return;
            }

            page.RenderTransform =
                new TranslateTransform(18, 0);

            page.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(
                    0,
                    1,
                    TimeSpan.FromMilliseconds(230)));

            ((TranslateTransform)page.RenderTransform)
                .BeginAnimation(
                    TranslateTransform.XProperty,
                    new DoubleAnimation(
                        18,
                        0,
                        TimeSpan.FromMilliseconds(230))
                    {
                        EasingFunction =
                            new QuadraticEase
                            {
                                EasingMode =
                                    EasingMode.EaseOut
                            }
                    });
        }

        private void UpdateGreeting()
        {
            int hour = DateTime.Now.Hour;
            string greeting =
                hour < 6
                    ? "Доброй ночи"
                    : hour < 12
                        ? "Доброе утро"
                        : hour < 18
                            ? "Добрый день"
                            : "Добрый вечер";

            GreetingText.Text =
                greeting + " 👋";
        }

        private void InitializeTrayIcon()
        {
            _trayApplicationIcon =
                LoadApplicationIcon();

            _trayIcon =
                new System.Windows.Forms.NotifyIcon
                {
                    Text = "Smart Launcher",
                    Icon = _trayApplicationIcon,
                    Visible = true
                };

            var menu =
                new System.Windows.Forms.ContextMenuStrip();

            menu.Items.Add(
                "Открыть Smart Launcher",
                null,
                (_, _) => RestoreFromTray());

            var quickLaunch =
                new System.Windows.Forms.ToolStripMenuItem(
                    "Быстрый запуск");

            quickLaunch.DropDownOpening += (_, _) =>
            {
                quickLaunch.DropDownItems.Clear();

                foreach (LauncherMode mode
                         in _launcherData.Modes)
                {
                    LauncherMode capturedMode = mode;

                    quickLaunch.DropDownItems.Add(
                        mode.Name,
                        null,
                        (_, _) =>
                            Dispatcher.BeginInvoke(
                                async () =>
                                    await LaunchModeAsync(
                                        capturedMode)));
                }

                if (_launcherData.Modes.Count == 0)
                {
                    quickLaunch.DropDownItems.Add(
                        "Нет режимов")
                        .Enabled = false;
                }
            };

            menu.Items.Add(quickLaunch);

            menu.Items.Add(
                "Выход",
                null,
                (_, _) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _isExiting = true;
                        Close();
                    });
                });

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick +=
                (_, _) => RestoreFromTray();
        }

        private static System.Drawing.Icon LoadApplicationIcon()
        {
            string? executablePath =
                Environment.ProcessPath;

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                try
                {
                    using System.Drawing.Icon? extractedIcon =
                        System.Drawing.Icon.ExtractAssociatedIcon(
                            executablePath);

                    if (extractedIcon != null)
                    {
                        return
                            (System.Drawing.Icon)
                            extractedIcon.Clone();
                    }
                }
                catch (ArgumentException)
                {
                    // Fall back to the standard Windows icon.
                }
            }

            return
                (System.Drawing.Icon)
                System.Drawing.SystemIcons.Application.Clone();
        }

        public void RestoreAndActivate()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    new Action(
                        RestoreAndActivate));
                return;
            }

            ShowInTaskbar = true;

            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            Focus();
        }

        private void RestoreFromTray()
        {
            RestoreAndActivate();
        }

        private void ShowNotification(
            string title,
            string message)
        {
            _trayIcon?.ShowBalloonTip(
                2500,
                title,
                message,
                System.Windows.Forms.ToolTipIcon.Info);
        }

        private void MainWindow_Closing(
            object? sender,
            CancelEventArgs e)
        {
            if (!_isExiting
                && _settings.CloseToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            _statusTimer.Stop();
            _globalHotKeyService?.Dispose();
            _globalHotKeyService = null;
            _launcherService.Dispose();

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            _trayApplicationIcon?.Dispose();
            _trayApplicationIcon = null;
        }

        private void HideToTray()
        {
            ShowInTaskbar = false;
            Hide();
            ShowNotification(
                "Smart Launcher",
                "Окно закрыто в трей. Дважды нажмите на значок, чтобы вернуться.");
        }

        private void TopBar_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // Состояние окна могло измениться во время жеста.
            }
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        private void MinimizeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowInTaskbar = true;
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(
            object sender,
            RoutedEventArgs e) =>
            ToggleMaximize();

        private void ToggleMaximize()
        {
            WindowState =
                WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
        }

        private void MainWindow_StateChanged(
            object? sender,
            EventArgs e)
        {
            UpdateWindowStateAppearance();
        }

        private void UpdateWindowStateAppearance()
        {
            if (MaximizeButton == null
                || WindowFrame == null)
            {
                return;
            }

            bool maximized =
                WindowState == WindowState.Maximized;

            MaximizeButton.Content =
                maximized ? "\uE923" : "\uE922";
            MaximizeButton.ToolTip =
                maximized ? "Восстановить" : "Развернуть";
            WindowFrame.CornerRadius =
                maximized
                    ? new CornerRadius(0)
                    : new CornerRadius(20);
        }

        private static bool TryNormalizeColor(
            string value,
            out string normalized)
        {
            normalized = string.Empty;

            try
            {
                Color color =
                    (Color)ColorConverter.ConvertFromString(
                        value.Trim());

                normalized =
                    $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static List<TargetTypeOption>
            CreateTargetTypeOptions()
        {
            return new List<TargetTypeOption>
            {
                new(LaunchTargetType.Application, "Программа"),
                new(LaunchTargetType.Website, "Сайт"),
                new(LaunchTargetType.File, "Файл"),
                new(LaunchTargetType.Folder, "Папка"),
                new(LaunchTargetType.Steam, "Steam-игра"),
                new(LaunchTargetType.Command, "Команда"),
                new(LaunchTargetType.Project, "Проект")
            };
        }

        private sealed record TargetTypeOption(
            LaunchTargetType Type,
            string Name);
    }

    public class GridLengthAnimation :
        AnimationTimeline
    {
        public GridLength From { get; set; }

        public GridLength To { get; set; }

        public IEasingFunction? EasingFunction { get; set; }

        public override Type TargetPropertyType =>
            typeof(GridLength);

        protected override Freezable CreateInstanceCore() =>
            new GridLengthAnimation();

        public override object GetCurrentValue(
            object defaultOriginValue,
            object defaultDestinationValue,
            AnimationClock animationClock)
        {
            double progress =
                animationClock.CurrentProgress ?? 0;

            if (EasingFunction != null)
            {
                progress =
                    EasingFunction.Ease(progress);
            }

            return new GridLength(
                From.Value
                + (To.Value - From.Value) * progress,
                GridUnitType.Pixel);
        }
    }
}
