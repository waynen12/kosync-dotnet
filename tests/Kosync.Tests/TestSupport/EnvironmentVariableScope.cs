namespace Kosync.Tests.TestSupport;

// Temporarily overrides a process environment variable, restoring the
// previous value on dispose. The app reads several settings (ADMIN_PASSWORD,
// REGISTRATION_DISABLED) directly from the process environment rather than
// configuration, so this is how tests exercise those paths deterministically.
public sealed class EnvironmentVariableScope : IDisposable
{
    private readonly string _name;
    private readonly string? _previousValue;

    public EnvironmentVariableScope(string name, string? value)
    {
        _name = name;
        _previousValue = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_name, _previousValue);
    }
}
