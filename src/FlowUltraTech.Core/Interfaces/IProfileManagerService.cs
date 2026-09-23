using System;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Interfaces;

public class UserProfileInfo
{
    public string? UserId { get; set; }
    public string? Nickname { get; set; }
    public string? DisplayId { get; set; }
    public string? AvatarUrl { get; set; }
    public string? SessionId { get; set; }
    public string? LocalAvatarPath { get; set; }
    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(UserId) && (!string.IsNullOrWhiteSpace(DisplayId) || !string.IsNullOrWhiteSpace(Nickname));
}

public interface IProfileManagerService
{
    string ResolveProfileDirectory(StudioInstanceProfile profile);
    UserProfileInfo? ReadProfileFromDirectory(string profileDirectory);
    UserProfileInfo? ReadProfile(StudioInstanceProfile profile);
    bool UpdateProfileInfo(StudioInstanceProfile profile);
    IDisposable WatchProfile(StudioInstanceProfile profile, Action<UserProfileInfo> onProfileChanged);
}
