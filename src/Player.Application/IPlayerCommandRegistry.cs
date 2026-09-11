namespace Player.Application;

public interface IPlayerCommandRegistry
{
    IReadOnlyCollection<PlayerCommandDefinition> Commands { get; }

    bool CanExecute(PlayerCommandId id, object? parameter = null);

    void Execute(PlayerCommandId id, object? parameter = null);
}
