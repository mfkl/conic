using System;
using System.Linq;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Player.Application;
using Conic.Controls;
using Conic.ViewModels;

namespace Conic;

// Keyboard shortcuts, drag-and-drop, and queue list interaction.
public partial class MainWindow
{
    private void OnPlayerKeyDown(object? sender, KeyEventArgs e)
    {
        WakeChrome();
        if (e.Key == Key.Escape && UrlPanel.IsVisible)
        {
            CloseUrlPanel();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && QueueDrawer.IsVisible)
        {
            CloseDrawer();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && LibraryPanel.IsVisible)
        {
            CloseLibrary();
            e.Handled = true;
            return;
        }
        if (_presentation.HasPendingResume)
        {
            if (e.Key == Key.Enter) _presentation.Registry.Execute(PlayerCommandId.AcceptResume);
            else if (e.Key == Key.Escape) _presentation.Registry.Execute(PlayerCommandId.DismissResume);
            else return;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && OpenMenu.IsVisible)
        {
            CloseOpenMenu();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && SettingsPanel.IsVisible)
        {
            CloseSettings();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && _presentation.IsPictureInPicture)
        {
            ExitPictureInPicture();
            e.Handled = true;
            return;
        }
        // As in VLC, Escape leaves fullscreen.
        if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        if (SettingsPanel.IsVisible || ShortcutFocusPolicy.Suppresses(e.Source)) return;
        string gesture = MenuShortcuts.FromKey(e.Key, e.KeyModifiers);
        IReadOnlyDictionary<PlayerCommandId, string> active =
            PlayerShortcutPolicy.Effective(_presentation.Registry, _settings.Current.ShortcutOverrides);
        if (!PlayerShortcutPolicy.TryFind(_presentation.Registry, active, gesture, out PlayerCommandId command)) return;
        // The feedback says what the shortcut does, before its result arrives from the engine.
        string? feedback = _presentation.ShortcutFeedbackText(command);
        _presentation.Registry.Execute(command);
        e.Handled = true;
        ShowShortcutFeedback(feedback);
    }

    private void OnMediaDragOver(object? sender, DragEventArgs e)
    {
        IStorageItem[]? items = e.DataTransfer.TryGetFiles();
        bool local = items is { Length: > 0 } && items.All(item => item.Path.IsFile);
        e.DragEffects = local ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnMediaDrop(object? sender, DragEventArgs e)
    {
        IStorageItem[]? items = e.DataTransfer.TryGetFiles();
        string[] localSources = items is { Length: > 0 } && items.All(item => item.Path.IsFile)
            ? items.Select(item => item.Path.LocalPath).ToArray()
            : [];
        _presentation.OpenDroppedSources(localSources);
        UpdateOpenErrorSurface();
        e.Handled = true;
    }

    private void OnQueueDrawerLayoutUpdated(object? sender, EventArgs e)
    {
        if (_focusQueueOnLayout && QueueDrawer.IsEffectivelyVisible && CloseQueueButton.Focus())
            _focusQueueOnLayout = false;
    }

    private void OnDrawerTabsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not Button current || e.KeyModifiers != KeyModifiers.None) return;
        // Only the open panel's tabs show; arrows wrap among those.
        Button[] tabs = DrawerTabs.Children.OfType<Button>().Where(tab => tab.IsVisible).ToArray();
        int index = Array.IndexOf(tabs, current);
        if (index < 0) return;
        int next = e.Key switch
        {
            Key.Left => (index + tabs.Length - 1) % tabs.Length,
            Key.Right => (index + 1) % tabs.Length,
            Key.Home => 0,
            Key.End => tabs.Length - 1,
            _ => -1
        };
        if (next < 0) return;
        Button target = tabs[next];
        target.Focus(NavigationMethod.Directional);
        if (target.Command?.CanExecute(target.CommandParameter) == true) target.Command.Execute(target.CommandParameter);
        e.Handled = true;
    }

    private void OnMoveQueueItemUp(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: QueueItem item } && item.Index > 0)
            _presentation.Registry.Execute(PlayerCommandId.QueueMove, new QueueMove(item.Token, item.Index - 1));
    }

    private void OnMoveQueueItemDown(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: QueueItem item } && item.Index + 1 < _presentation.QueueItems.Count)
            _presentation.Registry.Execute(PlayerCommandId.QueueMove, new QueueMove(item.Token, item.Index + 1));
    }
}
