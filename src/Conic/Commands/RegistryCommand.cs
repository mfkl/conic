using System.Windows.Input;
using Player.Application;

namespace Conic.Commands;

internal sealed class RegistryCommand : ICommand
{
    private readonly IPlayerCommandRegistry _registry;
    private readonly PlayerCommandId _id;
    private readonly object? _defaultParameter;

    public RegistryCommand(IPlayerCommandRegistry registry, PlayerCommandId id, object? defaultParameter = null)
    {
        _registry = registry;
        _id = id;
        _defaultParameter = defaultParameter;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _registry.CanExecute(_id, parameter ?? _defaultParameter);

    public void Execute(object? parameter) => _registry.Execute(_id, parameter ?? _defaultParameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
