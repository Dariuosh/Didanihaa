namespace Didanihaa.Core;

public sealed record ServiceState(bool Supported, bool Installed, string State, string? ExecutablePath = null);
