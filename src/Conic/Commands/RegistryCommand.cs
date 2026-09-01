using System.Windows.Input;
using Player.Application;

namespace Conic.Commands;

internal sealed class RegistryCommand : ICommand
{
    private readonly IPlayerCommandRegistry _registry;
    private readonly PlayerCommandId _id;

    public RegistryCommand(IPlayerCommandRegistry registry, PlayerCommandId id)
    {
        _registry = registry;
        _id = id;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _registry.CanExecute(_id, parameter);

    public void Execute(object? parameter) => _registry.Execute(_id, parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
