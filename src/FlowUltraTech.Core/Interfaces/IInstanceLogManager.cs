using System;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Interfaces;

public interface IInstanceLogManager
{
    IInstanceLogger GetLogger(StudioInstanceProfile profile);
    IInstanceLogger GetLogger(string profileFolderName, int cdpPort, string accountName = "");
    IInstanceLogger? TryGetLoggerByPort(int cdpPort);
    string LogsDirectory { get; }
    (bool Success, string Logs, string FileName) ReadLogs(string profileFolderName, int cdpPort = 0, int maxLines = 3000, string accountName = "");
    void RemoveLogger(string profileFolderName, int cdpPort = 0);
    void Clear();
}
