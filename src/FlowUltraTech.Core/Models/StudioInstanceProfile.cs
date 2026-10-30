using System;

namespace FlowUltraTech.Core.Models;

public class StudioInstanceProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Account 1";
    public string ProfileFolderName { get; set; } = "Acc01";

    public string SandboxBoxName
    {
        get => ProfileFolderName;
        set => ProfileFolderName = value;
    }
    public int CdpPort { get; set; } = 9221;
    public string UserDataDir { get; set; } = string.Empty;
    public InstanceLaunchMode LaunchMode { get; set; } = InstanceLaunchMode.DirectProfile;
    
    public Guid? ProxyId { get; set; }

    // Per-Instance Native Proxy Configuration (No Proxifier Required)
    public bool UseCustomProxy { get; set; } = false;
    public ProxyProtocol ProxyProtocol { get; set; } = ProxyProtocol.Http;
    public string ProxyHost { get; set; } = string.Empty;
    public int ProxyPort { get; set; } = 8080;
    public string ProxyUsername { get; set; } = string.Empty;
    public string ProxyPassword { get; set; } = string.Empty;

    public string StreamerUsername { get; set; } = string.Empty;
    public string CustomChromiumFlags { get; set; } = string.Empty;

    // Runtime states
    public InstanceStatus Status { get; set; } = InstanceStatus.Stopped;
    public bool IsWindowHidden { get; set; } = false;
    public int? ProcessId { get; set; }
    public DateTime? StartedAt { get; set; }
    public string StatusMessage { get; set; } = "Idle";
    public string? ActiveLeaseId { get; set; }

    /// <summary>Set after a stalled launch so this session retries with GPU off. Not saved: the next app run tries GPU again.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool UseSafeRendering { get; set; }

    // Profile & Authentication Information
    public string? LoggedInUserId { get; set; }
    public string? LoggedInNickname { get; set; }
    public string? LoggedInDisplayId { get; set; }
    public string? LoggedInAvatarUrl { get; set; }

    /// <summary>Last channel auto-filled from the Studio login; used to detect an account switch.</summary>
    public string? AutoDetectedChannel { get; set; }
    public bool IsLoggedIn { get; set; } = false;
    public DateTime? LastProfileSync { get; set; }

    // Live Proof & Realtime Clock Overlay Configuration
    public OverlayConfig Overlay { get; set; } = new();

    // 5-Mode Auto-Reply & Live Comment Studio Configuration
    public InstanceAutoReplyConfig AutoReply { get; set; } = new();
}
