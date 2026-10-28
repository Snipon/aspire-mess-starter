namespace Api.Contracts;

/// <summary>A node in the site navigation tree.</summary>
public sealed record NavItem(string Id, string Name, string Path, NavItem[] Children);
