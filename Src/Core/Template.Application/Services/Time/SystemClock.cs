#nullable enable
using Template.Application.Interfaces;

namespace Template.Application.Services.Time;

/// <summary>Default <see cref="IClock"/> backed by the system clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
