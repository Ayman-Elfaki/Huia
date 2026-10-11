namespace Huia.Stores;

/// <summary>Reports whether the persistence layer behind the Huia stores is reachable (readiness checks).</summary>
public interface IHuiaDatabaseProbe
{
    /// <summary>Returns <c>true</c> when the underlying database accepts a connection.</summary>
    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);
}
