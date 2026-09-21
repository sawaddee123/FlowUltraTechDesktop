using System;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Interfaces;

public interface IInstanceLogManager
{
    IInstanceLogger GetLogger(StudioInstanceProfile profile);
    IInstanceLogger GetLogger(string profileFolderName, int cdpPort, string accountName = "");
    IInstanceLogger? TryGetLoggerByPort(int cdpPort);
    string LogsDirectory { get; }
    (bool Success, string Logs, string FileName) ReadLogs(string profileFolderName, int cdpPort = 0, int maxLines = 3000);
}
