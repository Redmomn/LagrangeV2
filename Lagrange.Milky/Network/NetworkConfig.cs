namespace Lagrange.Milky.Network;

public class NetworkConfig
{
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 8080;

    public string CommonPrefix { get; set; } = "/";

    public string? AccessToken { get; set; } = null;
}