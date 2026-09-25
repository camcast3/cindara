using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cindara.Desktop.Localization;
using Cindara.Desktop.ViewModels;

namespace Cindara.Desktop.Views;

public partial class MainWindow
{
    private void ShowLibraryLayout()
    {
        if (_viewModel?.LibraryLayout is not { } layout || ModalOverlay.IsVisible)
        {
            return;
        }

        BeginFullScreenModal(Loc.Get("LibraryLayout.Title"));
        ModalActions.Children.Add(new TextBlock { Text = Loc.Get("LibraryLayout.Scope"), TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(layout.Status))
        {
            ModalActions.Children.Add(new TextBlock { Text = layout.Status, TextWrapping = TextWrapping.Wrap });
        }

        var sidebar = AddModalButton(Loc.Get("LibraryLayout.Sidebar"), () =>
            ShowLibraryOrder(layout, LibraryLayoutSurface.Sidebar, layout.CreateDraft(LibraryLayoutSurface.Sidebar)));
        AutomationProperties.SetAutomationId(sidebar, "ConfigureSidebarLibraries");
        var home = AddModalButton(Loc.Get("LibraryLayout.Home"), () =>
            ShowLibraryOrder(layout, LibraryLayoutSurface.Home, layout.CreateDraft(LibraryLayoutSurface.Home)));
        AutomationProperties.SetAutomationId(home, "ConfigureHomeLibraries");
        AddModalButton(Loc.Get("Action.Back"), DismissModal);
        FocusModal();
    }

    private void ShowLibraryOrder(LibraryLayoutViewModel layout, LibraryLayoutSurface surface,
        List<LibraryLayoutChoice> draft, string? focusId = null, string focusAction = "toggle")
    {
        ModalActions.Children.Clear();
        _navigation.Forget("modal");
        ModalTitle.Text = Loc.Get(surface == LibraryLayoutSurface.Sidebar ? "LibraryLayout.Sidebar" : "LibraryLayout.Home");
        AutomationProperties.SetName(ModalOverlay, ModalTitle.Text);
        ModalActions.Children.Add(new TextBlock
        {
            Text = Loc.Get("LibraryLayout.Instructions"),
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrEmpty(layout.Status))
        {
            var status = new TextBlock { Text = layout.Status, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            ModalActions.Children.Add(status);
        }

        Control? focus = null;
        foreach (var choice in draft)
        {
            var row = new StackPanel { Spacing = 8 };
            var name = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new TextBlock { Text = choice.Library.Name, TextWrapping = TextWrapping.Wrap });
            name.Children.Add(new TextBlock
            {
                Text = Loc.Get(choice.Included ? "LibraryLayout.Shown" : "LibraryLayout.Hidden"),
                Classes = { "caption" },
            });
            row.Children.Add(name);
            var actions = new WrapPanel();
            row.Children.Add(actions);
            var toggle = ChoiceButton(Loc.Get(choice.Included ? "LibraryLayout.Hide" : "LibraryLayout.Show"),
                $"library-{surface}-{choice.Library.Id}-toggle",
                Loc.Format(choice.Included ? "LibraryLayout.HideFor" : "LibraryLayout.ShowFor", choice.Library.Name));
            toggle.MinWidth = 140;
            toggle.Margin = new Avalonia.Thickness(0, 0, 12, 8);
            AutomationProperties.SetItemStatus(toggle, Loc.Get(choice.Included ? "LibraryLayout.Shown" : "LibraryLayout.Hidden"));
            toggle.Click += (_, _) =>
            {
                choice.Included = !choice.Included;
                ShowLibraryOrder(layout, surface, draft, choice.Library.Id);
            };
            actions.Children.Add(toggle);
            var index = draft.IndexOf(choice);
            var up = ChoiceButton(Loc.Get("LibraryLayout.Up"), $"library-{surface}-{choice.Library.Id}-up",
                Loc.Format("LibraryLayout.UpFor", choice.Library.Name));
            up.MinWidth = 140;
            up.Margin = new Avalonia.Thickness(0, 0, 12, 8);
            up.IsEnabled = index > 0;
            up.Click += (_, _) => Move(choice, -1, "up");
            actions.Children.Add(up);
            var down = ChoiceButton(Loc.Get("LibraryLayout.Down"), $"library-{surface}-{choice.Library.Id}-down",
                Loc.Format("LibraryLayout.DownFor", choice.Library.Name));
            down.MinWidth = 140;
            down.Margin = new Avalonia.Thickness(0, 0, 0, 8);
            down.IsEnabled = index < draft.Count - 1;
            down.Click += (_, _) => Move(choice, 1, "down");
            actions.Children.Add(down);
            ModalActions.Children.Add(row);
            if (choice.Library.Id == focusId)
            {
                focus = focusAction == "up" && up.IsEnabled ? up
                    : focusAction == "down" && down.IsEnabled ? down : toggle;
            }
        }

        if (draft.Count == 0)
        {
            ModalActions.Children.Add(new TextBlock { Text = Loc.Get("Library.NoLibraries"), TextWrapping = TextWrapping.Wrap });
        }

        var save = AddModalButton(Loc.Get(layout.HasLoadError ? "LibraryLayout.Replace" : "LibraryLayout.Save"), () =>
        {
            if (layout.Save(surface, draft))
            {
                DismissModal();
            }
            else
            {
                ShowLibraryOrder(layout, surface, draft, focusAction: "save");
            }
        });
        AutomationProperties.SetAutomationId(save, "SaveLibraryLayout");
        var cancel = AddModalButton(Loc.Get("Action.Cancel"), DismissModal);
        AutomationProperties.SetAutomationId(cancel, "CancelLibraryLayout");
        FocusModal(focusAction == "save" ? save : focus);

        void Move(LibraryLayoutChoice choice, int direction, string action)
        {
            var index = draft.IndexOf(choice);
            var target = Math.Clamp(index + direction, 0, draft.Count - 1);
            draft.RemoveAt(index);
            draft.Insert(target, choice);
            ShowLibraryOrder(layout, surface, draft, choice.Library.Id, action);
        }
    }

    private static Button ChoiceButton(string label, string automationId, string? accessibleName = null)
    {
        var button = new Button { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap } };
        AutomationProperties.SetName(button, accessibleName ?? label);
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }
}
