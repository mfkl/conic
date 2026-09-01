using System.Windows.Input;
namespace Conic.Commands;
internal sealed class RelayCommand(Action<object?> action) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action(parameter);
}
