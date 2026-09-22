using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyUx;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.UndoRedo;

namespace Ymm4NoHarmonyFolderLayoutProbe;

internal sealed class HandsOnController : IDisposable
{
    private const string MenuTag = "CNWL.P4.HandsOn";

    private readonly Window window;
    private readonly FrameworkElement labels;
    private readonly Host host;
    private readonly Timeline timeline;
    private readonly UndoRedoManager undo;
    private readonly FolderStateStore state;
    private readonly DirectDisplay display;
    private readonly StructuralFolderBridge structural;
    private readonly InputMapAdapter input;
    private readonly FileDropMapAdapter fileDrop;
    private readonly AdornerLayer adornerLayer;
    private readonly MouseButtonEventHandler mouseHandler;

    private FolderOverlayAdorner? adorner;
    private bool disposed;

    internal HandsOnController(
        Window window,
        FrameworkElement labels,
        Host host,
        UndoRedoManager undo,
        FolderStateStore state)
    {
        this.window = window;
        this.labels = labels;
        this.host = host;
        this.undo = undo;
        this.state = state;
        timeline = host.Timeline;

        display = new DirectDisplay(host, HandsOnRuntime.Diagnostic);
        structural = new StructuralFolderBridge(
            window,
            timeline,
            undo,
            state,
            HandsOnRuntime.Diagnostic);
        input = new InputMapAdapter(host, display, HandsOnRuntime.Diagnostic);
        fileDrop = new FileDropMapAdapter(host, display, HandsOnRuntime.Diagnostic);
        adornerLayer = AdornerLayer.GetAdornerLayer(labels)
            ?? throw new InvalidOperationException("LayerLabels has no AdornerLayer.");

        mouseHandler = OnPreviewMouseDown;
        window.AddHandler(Mouse.PreviewMouseDownEvent, mouseHandler, true);
        RefreshFromDocument();
        ScheduleS0IntegrationSmoke();
    }

    private void ScheduleS0IntegrationSmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P4_S0_INTEGRATION_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunS0IntegrationSmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunS0IntegrationSmokeAsync()
    {
        try
        {
            await Task.Delay(500);

            if (timeline.Items.Any())
            {
                throw new InvalidOperationException(
                    "S0 structural smoke must start from a resource-free Timeline.");
            }

            var baselineSettingsCount = timeline.LayerSettings.Items.Count;

            // Match the frozen P1 native gate: establish a clean history boundary
            // before measuring the host-owned structural command.
            undo.Record();

            var key = timeline.ID.ToString("D");
            var folderId = Guid.Parse("10101010-2020-3030-4040-505050505050");
            state.ReplaceDocument(FolderDocumentRules.ReplaceTimeline(
                state.Document,
                key,
                [
                    new PersistedFolder
                    {
                        Id = folderId,
                        Start = 1,
                        End = 2,
                        Name = "S0 Smoke",
                        IsCollapsed = true
                    }
                ]));

            await Task.Delay(350);
            ExecuteHostCommand(CommandType.AddLayer, 2);
            await Task.Delay(500);

            AssertS0State(
                "after_add",
                expectedStart: 1,
                expectedEnd: 3,
                expectedSettingsCount: baselineSettingsCount + 1);

            if (structural.PendingEdits != 1)
                throw new InvalidOperationException(
                    $"Expected one pending structural edit, got {structural.PendingEdits}.");

            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(500);

            AssertS0State(
                "after_undo",
                expectedStart: 1,
                expectedEnd: 2,
                expectedSettingsCount: baselineSettingsCount);

            if (structural.UndoCallbacks != 1)
                throw new InvalidOperationException(
                    $"Expected one folder Undo callback, got {structural.UndoCallbacks}.");

            ExecuteHostCommand(CommandType.Redo, null);
            await Task.Delay(500);

            AssertS0State(
                "after_redo",
                expectedStart: 1,
                expectedEnd: 3,
                expectedSettingsCount: baselineSettingsCount + 1);

            if (structural.RedoCallbacks != 1)
                throw new InvalidOperationException(
                    $"Expected one folder Redo callback, got {structural.RedoCallbacks}.");

            WriteS0Result(
                "PASS_S0_INTEGRATION\n" +
                $"timeline={key}\n" +
                $"baseline_settings={baselineSettingsCount}\n" +
                $"after_redo_settings={timeline.LayerSettings.Items.Count}\n" +
                $"pending={structural.PendingEdits}\n" +
                $"undo_callbacks={structural.UndoCallbacks}\n" +
                $"redo_callbacks={structural.RedoCallbacks}\n" +
                "folded_input_attached=true\n" +
                "filedrop_attached=true\n");
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic("s0_integration_smoke_error=" + ex);
            WriteS0Result("FAIL_S0_INTEGRATION\n" + ex + "\n");
        }
    }

    private void AssertS0State(
        string phase,
        int expectedStart,
        int expectedEnd,
        int expectedSettingsCount)
    {
        var key = timeline.ID.ToString("D");
        var folder = FolderDocumentRules.FindTimeline(state.Document, key)
            ?.Folders.SingleOrDefault();

        if (folder is null
            || folder.Start != expectedStart
            || folder.End != expectedEnd)
        {
            throw new InvalidOperationException(
                $"{phase}: folder range mismatch: " +
                (folder is null ? "<null>" : $"{folder.Start}-{folder.End}"));
        }

        var settingsCount = timeline.LayerSettings.Items.Count;
        if (settingsCount != expectedSettingsCount)
        {
            throw new InvalidOperationException(
                $"{phase}: LayerSettings count mismatch: " +
                $"{settingsCount} != {expectedSettingsCount}");
        }

        display.ThrowIfFailed();
        HandsOnRuntime.Diagnostic(
            $"s0_state phase={phase} folder={folder.Start}-{folder.End} " +
            $"layer_settings={settingsCount}");
    }

    private void ExecuteHostCommand(CommandType type, object? parameter)
    {
        ICommand command = CommandSettings.Default[type]
            ?? throw new InvalidOperationException("Command missing: " + type);

        var targets = new List<IInputElement>();
        if (Keyboard.FocusedElement is IInputElement focused)
            targets.Add(focused);
        targets.Add(host.Source);
        targets.Add(host.View);
        targets.Add(window);

        foreach (var target in targets)
        {
            if (command is RoutedCommand routed)
            {
                bool can;
                try
                {
                    can = routed.CanExecute(parameter, target);
                }
                catch
                {
                    continue;
                }

                if (!can)
                    continue;

                routed.Execute(parameter, target);
                HandsOnRuntime.Diagnostic(
                    $"s0_command type={type} target={target.GetType().Name} parameter={parameter ?? "<null>"}");
                return;
            }

            if (command.CanExecute(parameter))
            {
                command.Execute(parameter);
                HandsOnRuntime.Diagnostic(
                    $"s0_command type={type} target=<direct> parameter={parameter ?? "<null>"}");
                return;
            }
        }

        throw new InvalidOperationException(
            $"No executable route for {type} with parameter {parameter ?? "<null>"}.");
    }

    private static void WriteS0Result(string text)
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_P4_HANDS_ON_DIAG_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "s0-result.txt"), text);
    }

    internal bool Matches(Guid timelineId) =>
        !disposed && timeline.ID == timelineId;

    internal void RefreshFromDocument()
    {
        if (disposed)
            return;

        var timelineState = FolderDocumentRules.FindTimeline(
            state.Document,
            timeline.ID.ToString("D"));

        var spans = timelineState?.Folders
            .Where(x => x.IsCollapsed)
            .Select(x => new CollapsedSpan(x.Start, x.End))
            .ToArray() ?? [];

        display.SetSpans(spans);

        Application.Current.Dispatcher.BeginInvoke(
            new Action(RebuildOverlay),
            DispatcherPriority.ContextIdle);
    }

    private void RebuildOverlay()
    {
        if (disposed)
            return;

        if (adorner is not null)
        {
            adornerLayer.Remove(adorner);
            adorner = null;
        }

        var folders = FolderDocumentRules.FindTimeline(
                state.Document,
                timeline.ID.ToString("D"))
            ?.Folders
            .ToArray() ?? [];

        if (folders.Length == 0)
            return;

        adorner = new FolderOverlayAdorner(labels, display, folders);
        adornerLayer.Add(adorner);
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (disposed)
            return;

        var point = e.GetPosition(labels);
        if (point.X < 0 || point.X >= labels.ActualWidth
            || point.Y < 0 || point.Y >= labels.ActualHeight)
            return;

        if (e.ChangedButton == MouseButton.Left
            && adorner?.TryFolderAt(point, out var folderId) == true)
        {
            e.Handled = true;
            ExecuteDocumentChange(
                document => FolderUxCommands.ToggleCollapsed(
                    document,
                    timeline.ID.ToString("D"),
                    folderId));
            return;
        }

        if (e.ChangedButton != MouseButton.Right)
            return;

        try
        {
            var logical = display.Layout.DisplayYToLogical(point.Y, display.Height);
            var owner = HandsOnHostAccess.FindLayerContextOwner(labels, logical);
            var menu = owner.ContextMenu;
            if (menu is null)
                return;

            PrepareMenu(menu, logical);
            e.Handled = true;
            menu.PlacementTarget = owner;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;

            HandsOnRuntime.Diagnostic(
                $"folder_menu display={point} logical={logical} timeline={timeline.ID:D}");
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic("folder_menu_error=" + ex);
        }
    }

    private void PrepareMenu(ContextMenu menu, int logicalLayer)
    {
        RemoveTagged(menu);

        var root = new MenuItem
        {
            Header = "レイヤーフォルダ",
            Tag = MenuTag
        };

        if (state.IsRecoveryBlocked)
        {
            root.Items.Add(new MenuItem
            {
                Header = "保存済みフォルダ情報を読み込めません",
                IsEnabled = false
            });
            root.Items.Add(new MenuItem
            {
                Header = "元データは保持中です。新規編集は行いません。",
                IsEnabled = false
            });
        }
        else
        {
            AddCreateAction(root);
            AddExistingFolderActions(root, logicalLayer);
        }

        menu.Items.Add(new Separator { Tag = MenuTag });
        menu.Items.Add(root);

        RoutedEventHandler? closed = null;
        closed = (_, _) =>
        {
            menu.Closed -= closed;
            RemoveTagged(menu);
        };
        menu.Closed += closed;
    }

    private void AddCreateAction(MenuItem root)
    {
        var selected = timeline.LayerSelection.SelectedLayers
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        var candidateId = Guid.NewGuid();
        var decision = FolderUxCommands.EvaluateCreate(
            state.Document,
            timeline.ID.ToString("D"),
            selected,
            candidateId);

        var create = new MenuItem
        {
            Header = decision.Allowed
                && decision.Start is not null
                && decision.End is not null
                    ? $"L{decision.Start}–L{decision.End} をフォルダにまとめる..."
                    : "フォルダを作成できません（" + CreationReason(decision.Status) + "）",
            IsEnabled = decision.Allowed
        };

        create.Click += (_, _) =>
        {
            if (!decision.Allowed
                || decision.Start is null
                || decision.End is null)
                return;

            var count = FolderDocumentRules.FindTimeline(
                    state.Document,
                    timeline.ID.ToString("D"))
                ?.Folders.Count ?? 0;
            var fallback = $"フォルダ {count + 1}";
            var name = TextPrompt.Show("新しいフォルダ名", fallback);
            if (name is null)
                return;
            if (string.IsNullOrWhiteSpace(name))
                name = fallback;

            var captured = selected.ToArray();
            ExecuteDocumentChange(
                document => FolderUxCommands.CreateFolder(
                    document,
                    timeline.ID.ToString("D"),
                    captured,
                    candidateId,
                    name));
        };

        root.Items.Add(create);
    }

    private void AddExistingFolderActions(MenuItem root, int logicalLayer)
    {
        var timelineState = FolderDocumentRules.FindTimeline(
            state.Document,
            timeline.ID.ToString("D"));
        if (timelineState is null)
            return;

        var containing = timelineState.Folders
            .Where(x => x.Start <= logicalLayer && logicalLayer <= x.End)
            .OrderBy(x => x.End - x.Start)
            .ThenByDescending(x => x.Start)
            .ToArray();

        foreach (var folder in containing)
        {
            root.Items.Add(new Separator());

            var sub = new MenuItem
            {
                Header = $"「{folder.Name}」 (L{folder.Start}–L{folder.End})"
            };

            var toggle = new MenuItem
            {
                Header = folder.IsCollapsed ? "開く" : "畳む"
            };
            toggle.Click += (_, _) => ExecuteDocumentChange(
                document => FolderUxCommands.ToggleCollapsed(
                    document,
                    timeline.ID.ToString("D"),
                    folder.Id));
            sub.Items.Add(toggle);

            var rename = new MenuItem { Header = "名前を変更..." };
            rename.Click += (_, _) =>
            {
                var name = TextPrompt.Show("フォルダ名", folder.Name);
                if (name is null)
                    return;
                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show(
                        "フォルダ名を入力してください。",
                        FolderToolViewModel.DisplayTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                ExecuteDocumentChange(
                    document => FolderUxCommands.RenameFolder(
                        document,
                        timeline.ID.ToString("D"),
                        folder.Id,
                        name));
            };
            sub.Items.Add(rename);

            sub.Items.Add(new Separator());

            var ungroup = new MenuItem
            {
                Header = "フォルダを解除（レイヤーは残す）"
            };
            ungroup.Click += (_, _) => ExecuteDocumentChange(
                document => FolderUxCommands.Ungroup(
                    document,
                    timeline.ID.ToString("D"),
                    folder.Id));
            sub.Items.Add(ungroup);

            root.Items.Add(sub);
        }
    }

    private void ExecuteDocumentChange(
        Func<FolderDocument, FolderDocument> change)
    {
        if (state.IsRecoveryBlocked)
            return;

        try
        {
            var before = state.Document;
            var after = FolderDocumentRules.NormalizeAndValidate(change(before));

            if (FolderDocumentCodec.Save(before) == FolderDocumentCodec.Save(after))
                return;

            state.ReplaceDocument(after);

            undo.AddCommand(new UndoRedoActionCommand(
                () => state.ReplaceDocument(before),
                () => state.ReplaceDocument(after)));
            undo.Record();

            HandsOnRuntime.Diagnostic(
                $"folder_history_record timeline={timeline.ID:D}");
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic("folder_command_error=" + ex);
            MessageBox.Show(
                "フォルダ操作に失敗しました。\n" + ex.Message,
                FolderToolViewModel.DisplayTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string CreationReason(FolderCreationStatus status) =>
        status switch
        {
            FolderCreationStatus.EmptySelection => "レイヤーを選択してください",
            FolderCreationStatus.TooSmall => "2レイヤー以上を選択してください",
            FolderCreationStatus.InvalidSelection => "選択範囲が不正です",
            FolderCreationStatus.NonContiguous => "連続したレイヤーを選択してください",
            FolderCreationStatus.InvalidRange => "既存フォルダと交差しています",
            _ => "作成できません"
        };

    private static void RemoveTagged(ContextMenu menu)
    {
        foreach (var item in menu.Items
            .OfType<FrameworkElement>()
            .Where(x => Equals(x.Tag, MenuTag))
            .ToArray())
        {
            menu.Items.Remove(item);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        window.RemoveHandler(Mouse.PreviewMouseDownEvent, mouseHandler);

        if (adorner is not null)
        {
            adornerLayer.Remove(adorner);
            adorner = null;
        }

        fileDrop.Dispose();
        input.Dispose();
        structural.Dispose();
        display.Dispose();
    }

    private sealed class FolderOverlayAdorner : Adorner
    {
        private readonly Canvas canvas = new()
        {
            ClipToBounds = true,
            IsHitTestVisible = false
        };
        private readonly VisualCollection children;
        private readonly DirectDisplay display;
        private readonly PersistedFolder[] folders;
        private readonly Dictionary<Guid, Rect> hitRects = [];

        internal FolderOverlayAdorner(
            UIElement adornedElement,
            DirectDisplay display,
            PersistedFolder[] folders)
            : base(adornedElement)
        {
            this.display = display;
            this.folders = folders;
            children = new VisualCollection(this) { canvas };
            IsHitTestVisible = false;
            Rebuild();
        }

        internal bool TryFolderAt(Point point, out Guid folderId)
        {
            foreach (var pair in hitRects)
            {
                if (pair.Value.Contains(point))
                {
                    folderId = pair.Key;
                    return true;
                }
            }

            folderId = Guid.Empty;
            return false;
        }

        private void Rebuild()
        {
            canvas.Children.Clear();
            hitRects.Clear();

            foreach (var folder in folders
                .OrderBy(x => x.Start)
                .ThenByDescending(x => x.End))
            {
                if (display.Layout.IsHidden(folder.Start))
                    continue;

                var depth = folders.Count(parent =>
                    parent.Id != folder.Id
                    && parent.Start < folder.Start
                    && folder.End <= parent.End);

                var x = 2 + depth * 9;
                var y = display.Layout.VisualRowOfLogical(folder.Start)
                    * display.Height + 3;
                var width = Math.Max(
                    48,
                    Math.Min(
                        120,
                        Math.Max(48, AdornedElement.RenderSize.Width - x - 4)));
                var height = Math.Max(18, display.Height - 6);
                var rect = new Rect(x, y, width, height);
                hitRects[folder.Id] = rect;

                var border = new Border
                {
                    Width = width,
                    Height = height,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(
                        Color.FromArgb(215, 78, 78, 78)),
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = (folder.IsCollapsed ? "▶ " : "▼ ") + folder.Name,
                        Foreground = Brushes.White,
                        FontSize = 10,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 3, 0),
                        IsHitTestVisible = false
                    }
                };

                Canvas.SetLeft(border, rect.X);
                Canvas.SetTop(border, rect.Y);
                canvas.Children.Add(border);
            }
        }

        protected override int VisualChildrenCount => children.Count;
        protected override Visual GetVisualChild(int index) => children[index];

        protected override Size MeasureOverride(Size constraint)
        {
            canvas.Measure(constraint);
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            canvas.Arrange(new Rect(AdornedElement.RenderSize));
            return finalSize;
        }
    }
}

internal static class TextPrompt
{
    internal static string? Show(string title, string initial)
    {
        var box = new TextBox
        {
            Text = initial,
            MinWidth = 240,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var ok = new Button
        {
            Content = "OK",
            IsDefault = true,
            MinWidth = 72,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var cancel = new Button
        {
            Content = "キャンセル",
            IsCancel = true,
            MinWidth = 72
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { ok, cancel }
        };
        var window = new Window
        {
            Title = title,
            Content = new StackPanel
            {
                Margin = new Thickness(12),
                Children = { box, buttons }
            },
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            ShowInTaskbar = false
        };

        ok.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
            Keyboard.Focus(box);
        };

        return window.ShowDialog() == true ? box.Text : null;
    }
}
