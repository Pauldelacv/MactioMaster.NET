namespace RatioMaster.Core.Net;

public enum ProxyKind
{
    None = 0,
    Http = 1,
    Socks4 = 2,
    Socks4a = 3,
    Socks5 = 4,
}

public sealed record ProxySettings
{
    public static readonly ProxySettings Direct = new();

    public ProxyKind Kind { get; init; } = ProxyKind.None;

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; }

    public string User { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public bool IsEnabled => this.Kind != ProxyKind.None && this.Host.Length > 0 && this.Port > 0;

    public override string ToString() =>
        this.IsEnabled ? $"{this.Kind} {this.Host}:{this.Port}" : "None";
}
