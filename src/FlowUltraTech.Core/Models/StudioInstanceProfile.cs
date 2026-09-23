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
    public string StreamerUsername { get; set; } = string.Empty;
    public string CustomChromiumFlags { get; set; } = string.Empty;

    // Runtime states
    public InstanceStatus Status { get; set; } = InstanceStatus.Stopped;
    public bool IsWindowHidden { get; set; } = false;
    public int? ProcessId { get; set; }
    public DateTime? StartedAt { get; set; }
    public string StatusMessage { get; set; } = "Idle";
    public string? ActiveLeaseId { get; set; }

    // Profile & Authentication Information
    public string? LoggedInUserId { get; set; }
    public string? LoggedInNickname { get; set; }
    public string? LoggedInDisplayId { get; set; }
    public string? LoggedInAvatarUrl { get; set; }
    public bool IsLoggedIn { get; set; } = false;
    public DateTime? LastProfileSync { get; set; }
}
