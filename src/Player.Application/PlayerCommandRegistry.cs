namespace Player.Application;

public sealed class PlayerCommandRegistry : IPlayerCommandRegistry
{
    private readonly Dictionary<PlayerCommandId, Registration> _registrations = new();

    public IReadOnlyCollection<PlayerCommandDefinition> Commands =>
        _registrations.Values.Select(static registration => registration.Definition).ToArray();

    public void Register(
        PlayerCommandDefinition definition,
        Action<object?> execute,
        Func<object?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(execute);
        if (!_registrations.TryAdd(
                definition.Id,
                new Registration(definition, execute, canExecute)))
        {
            throw new InvalidOperationException(
                $"The {definition.Id} command is already registered.");
        }
    }

    public bool CanExecute(PlayerCommandId id, object? parameter = null) =>
        _registrations.TryGetValue(id, out Registration? registration) &&
        (registration.CanExecute?.Invoke(parameter) ?? true);

    public void Execute(PlayerCommandId id, object? parameter = null)
    {
        if (!_registrations.TryGetValue(id, out Registration? registration))
            throw new InvalidOperationException($"The {id} command is not registered.");
        if (!CanExecute(id, parameter))
            return;

        registration.Execute(parameter);
    }

    private sealed record Registration(
        PlayerCommandDefinition Definition,
        Action<object?> Execute,
        Func<object?, bool>? CanExecute);
}
