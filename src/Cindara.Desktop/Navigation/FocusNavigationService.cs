using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Cindara.Desktop.Navigation;

/// <summary>Owns focus within one visible screen or modal, independently of the input device.</summary>
public sealed class FocusNavigationService(TopLevel topLevel)
{
    private readonly Dictionary<object, FocusMemory> _memory = [];
    private Control? _scope;
    private Control? _initial;
    private object? _key;
    private bool _waitingForLayout;

    public Control? Scope => _scope;

    public void Forget(object key) => _memory.Remove(key);

    public void SetScope(Control scope, Control? initial = null, object? key = null)
    {
        Remember();
        _scope = scope;
        _initial = initial;
        _key = key ?? scope;
        _waitingForLayout = true;
        Restore();
    }

    public void Reset()
    {
        _memory.Clear();
        _scope = null;
        _initial = null;
        _key = null;
    }

    public void Remember()
    {
        if (!_waitingForLayout && _scope is not null && topLevel.FocusManager?.GetFocusedElement() is Control focused
            && Candidates().Contains(focused))
        {
            _memory[_key!] = new FocusMemory(focused, BoundsInWindow(focused));
        }
    }

    public void EnsureFocus()
    {
        if (_waitingForLayout)
        {
            Restore();
            return;
        }

        if (topLevel.FocusManager?.GetFocusedElement() is Control focused && Candidates().Contains(focused))
        {
            Remember();
            return;
        }

        Restore();
    }

    public bool Move(NavigationDirection direction)
    {
        var candidates = Candidates();
        if (candidates.Length == 0)
        {
            return false;
        }

        var current = topLevel.FocusManager?.GetFocusedElement() as Control;
        if (current is null || !candidates.Contains(current))
        {
            Restore();
            return true;
        }

        Control? target;
        if (direction is NavigationDirection.Next or NavigationDirection.Previous)
        {
            var index = Array.IndexOf(candidates, current);
            var step = direction == NavigationDirection.Next ? 1 : -1;
            target = candidates[(index + step + candidates.Length) % candidates.Length];
        }
        else
        {
            var origin = BoundsInWindow(current);
            target = candidates.Where(candidate => candidate != current)
                .Select(candidate => (Control: candidate, Bounds: BoundsInWindow(candidate)))
                .Where(candidate => IsInDirection(origin, candidate.Bounds, direction))
                .OrderBy(candidate => OrthogonalGap(origin, candidate.Bounds, direction) > 0)
                .ThenBy(candidate => PrimaryGap(origin, candidate.Bounds, direction))
                .ThenBy(candidate => Distance(origin, candidate.Bounds))
                .Select(candidate => candidate.Control)
                .FirstOrDefault();
        }

        if (target is not null)
        {
            target.Focus(NavigationMethod.Directional);
            target.BringIntoView();
            Remember();
        }

        return true;
    }

    public bool Focus(Control control)
    {
        if (!Candidates().Contains(control))
        {
            return false;
        }

        var result = control.Focus(NavigationMethod.Directional);
        control.BringIntoView();
        Remember();
        return result;
    }

    private void Restore()
    {
        var candidates = Candidates();
        Control? target = null;
        if (_scope is not null && _memory.TryGetValue(_key!, out var memory))
        {
            if (AwaitingLayout(memory.Control))
            {
                _waitingForLayout = true;
                return;
            }

            target = candidates.Contains(memory.Control)
                ? memory.Control
                : candidates.OrderBy(candidate => Distance(memory.Bounds, BoundsInWindow(candidate))).FirstOrDefault();
        }

        if (target is null && _initial is not null && AwaitingLayout(_initial))
        {
            _waitingForLayout = true;
            return;
        }

        target ??= _initial is not null && candidates.Contains(_initial) ? _initial : candidates.FirstOrDefault();
        _waitingForLayout = false;
        if (target is not null)
        {
            Focus(target);
        }
    }

    private bool AwaitingLayout(Control control) => control.IsEffectivelyVisible && control.IsEffectivelyEnabled
        && control.GetVisualAncestors().Contains(_scope)
        && (control.Bounds.Width <= 0 || control.Bounds.Height <= 0);

    private Control[] Candidates() => _scope is null ? [] : _scope.GetVisualDescendants()
        .Prepend(_scope)
        .OfType<Control>()
        .Where(control => control is Button or TextBox
            && control.Focusable && control.IsEffectivelyVisible && control.IsEffectivelyEnabled
            && control.Bounds.Width > 0 && control.Bounds.Height > 0
            && !control.GetVisualAncestors().TakeWhile(ancestor => ancestor != _scope)
                .Any(ancestor => ancestor is Button or TextBox))
        .ToArray();

    private Rect BoundsInWindow(Control control)
    {
        // Scope coordinates can be mirrored; directions must follow the on-screen position.
        var start = control.TranslatePoint(default, topLevel) ?? default;
        var end = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), topLevel) ?? start;
        return new Rect(start, end);
    }

    private static double Distance(Rect origin, Rect target) =>
        Math.Pow(origin.Center.X - target.Center.X, 2) + Math.Pow(origin.Center.Y - target.Center.Y, 2);

    private static bool IsInDirection(Rect origin, Rect target, NavigationDirection direction) => direction switch
    {
        NavigationDirection.Left => target.Center.X < origin.Center.X - 1,
        NavigationDirection.Right => target.Center.X > origin.Center.X + 1,
        NavigationDirection.Up => target.Center.Y < origin.Center.Y - 1,
        NavigationDirection.Down => target.Center.Y > origin.Center.Y + 1,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    private static double OrthogonalGap(Rect origin, Rect target, NavigationDirection direction) =>
        direction is NavigationDirection.Left or NavigationDirection.Right
            ? Math.Max(0, Math.Max(origin.Top - target.Bottom, target.Top - origin.Bottom))
            : Math.Max(0, Math.Max(origin.Left - target.Right, target.Left - origin.Right));

    private static double PrimaryGap(Rect origin, Rect target, NavigationDirection direction) => direction switch
    {
        NavigationDirection.Left => Math.Max(0, origin.Left - target.Right),
        NavigationDirection.Right => Math.Max(0, target.Left - origin.Right),
        NavigationDirection.Up => Math.Max(0, origin.Top - target.Bottom),
        NavigationDirection.Down => Math.Max(0, target.Top - origin.Bottom),
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    private sealed record FocusMemory(Control Control, Rect Bounds);
}
