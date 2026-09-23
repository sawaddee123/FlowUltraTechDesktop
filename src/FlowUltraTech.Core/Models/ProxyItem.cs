using System;

namespace FlowUltraTech.Core.Models;

public class ProxyItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Proxy";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 1080;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public ProxyProtocol Protocol { get; set; } = ProxyProtocol.Socks5;
    
    /// <summary>
    /// ID used inside Proxifier XML configuration (e.g. 100, 101, 102...)
    /// </summary>
    public int ProxifierId { get; set; } = 100;
    
    public bool IsActive { get; set; } = true;

    public override string ToString()
    {
        return $"{Name} ({Protocol}://{Host}:{Port})";
    }
}
