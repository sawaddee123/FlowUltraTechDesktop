using System;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Interfaces;

public interface IInstanceLogManager
{
    IInstanceLogger GetLogger(StudioInstanceProfile profile);
    IInstanceLogger GetLogger(string profileFolderName, int cdpPort, string accountName = "");
    IInstanceLogger? TryGetLoggerByPort(int cdpPort);
    string LogsDirectory { get; }
}
