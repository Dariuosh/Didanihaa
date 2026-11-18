namespace Didanihaa.Core;

public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int Offset, int Limit);
